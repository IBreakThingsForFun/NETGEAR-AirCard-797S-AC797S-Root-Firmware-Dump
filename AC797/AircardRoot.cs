using System.Threading;

namespace AC797
{
    /// <summary>
    /// Automates "get a root shell on the AC797" so the one-click flows don't depend
    /// on the operator having run README Phase 3–4 by hand.
    ///
    /// Ground truth for success is always the same: does <c>telnet 23</c> answer with
    /// <c>uid=0(root)</c>? EnsureRoot checks that first and only touches the AT port /
    /// reboots the device when root is NOT already reachable.
    /// </summary>
    internal static class AircardRoot
    {
        /// <summary>
        /// Guarantees a root telnet shell on <paramref name="ip"/>, enabling it via the
        /// AT!OPENLOCK keygen (TELNETENABLE/ADBENABLE + reboot) only if it isn't already
        /// up. Returns true once a root shell answers. Safe to call from a background
        /// thread — it blocks (root-enable reboots the device, ~60–90 s).
        /// </summary>
        public static bool EnsureRoot(string ip, Action<string> log)
        {
            if (IsRoot(ip))
            {
                log("    Root telnet already open (uid=0).");
                return true;
            }

            log("    Root telnet not reachable — enabling it over the AT port (this reboots the AirCard) ...");
            if (!EnableRootViaAt(ip, log)) return false;

            log("    Waiting for the AirCard to reboot and telnet to come back (up to 120 s) ...");
            if (WaitForRoot(ip, log, maxSeconds: 120)) { log("    Root shell is up."); return true; }

            log("    STOP: telnet did not come up after the reboot. Check the AT transcript above,");
            log("          or enable it manually (aircard_dump/README.md Phase 4) and re-run.");
            return false;
        }

        /// <summary>True if telnet 23 answers with a uid=0 root shell.</summary>
        public static bool IsRoot(string ip)
        {
            try { return new RootShell(ip).RunCmd("id", timeoutMs: 4000, idleGapMs: 400).Contains("uid=0"); }
            catch { return false; }
        }

        /// <summary>
        /// Over the AT port (5510): unlock, set TELNETENABLE=1 + ADBENABLE=1 (verify),
        /// then unlock again and AT!RESET to apply. Returns false only if the unlock or
        /// the flag write clearly failed (the reboot itself is confirmed by WaitForRoot).
        /// </summary>
        public static bool EnableRootViaAt(string ip, Action<string> log)
        {
            var at = new AtPort(ip);
            string set;
            try
            {
                set = at.UnlockAndRun(
                    "AT!CUSTOM=\"TELNETENABLE\",1",
                    "AT!CUSTOM=\"ADBENABLE\",1",
                    "AT!CUSTOM?");
            }
            catch (Exception ex) { log("    STOP: AT port (5510) unreachable — " + ex.Message); return false; }
            log("    " + set.Replace("\n", "\n    "));
            if (set.Contains("skipped") || set.Contains("no challenge"))
            {
                log("    STOP: could not unlock the OEM lock (keygen self-test PASSes, so this is usually");
                log("          a wrong/unreachable AT port). Enable telnet manually and re-run.");
                return false;
            }

            try
            {
                string reset = at.UnlockAndRun("AT!RESET");
                log("    " + reset.Replace("\n", "\n    "));
            }
            catch (Exception ex)
            {
                // AT!RESET commonly drops the socket as the device reboots — not a failure.
                log("    (reset issued; socket dropped as expected: " + ex.Message + ")");
            }
            return true;
        }

        /// <summary>Poll telnet 23 until a root shell answers or the budget runs out.</summary>
        public static bool WaitForRoot(string ip, Action<string> log, int maxSeconds)
        {
            var end = DateTime.UtcNow.AddSeconds(maxSeconds);
            Thread.Sleep(20000);                       // give the reboot a head start before polling
            while (DateTime.UtcNow < end)
            {
                if (IsRoot(ip)) return true;
                Thread.Sleep(5000);
            }
            return false;
        }
    }

    /// <summary>
    /// The REAL Wi-Fi offload / station path (README Phase 7). The device's
    /// <c>Wlan.Offload.*</c> dx keys and <c>/Forms/offloadNetwork</c> are OEM-stubbed
    /// dead ends — they silently persist nothing. The working path is driving QCMAP's
    /// own <c>wpa_supplicant</c> on wlan0 with <c>wpa_cli</c>. This joins an upstream AP
    /// (open or WPA2-PSK) as a station, keeping the radio awake so it survives on USB.
    /// </summary>
    internal static class OffloadStation
    {
        // Parametric join script (SSID $1, passphrase $2). Pushed via a QUOTED heredoc,
        // so $1/$2/$SSID expand at run time on the device, not at push time. Mirrors the
        // proven multidrone.sh slot-1 join but adds WPA2-PSK when a passphrase is given.
        private const string OffloadSh =
"""
#!/bin/sh
# offload-connect.sh <ssid> [passphrase]  — join an upstream AP on wlan0 as a station.
# Uses QCMAP's own wpa_supplicant (the dx Wlan.Offload list is an OEM-dead end).
SSID="$1"; PASS="$2"; IF=wlan0
[ -z "$SSID" ] && { echo "offload: no SSID"; exit 1; }

# keep the whole radio subsystem awake so the STA survives on USB / screen-off
dx -c Wlan.WifiEnabled 1              >/dev/null 2>&1
dx -c Power.StandbyOnUsbIdleTimer  0  >/dev/null 2>&1
dx -c Power.StandbyOnAcIdleTimer   0  >/dev/null 2>&1
dx -c Power.StandbyOnBattIdleTimer 0  >/dev/null 2>&1

ID=$(wpa_cli -i $IF list_networks 2>/dev/null | awk -v s="$SSID" '$0 ~ s {print $1; exit}')
if [ -z "$ID" ]; then
  ID=$(wpa_cli -i $IF add_network | tail -1)
  wpa_cli -i $IF set_network $ID ssid "\"$SSID\""
  if [ -n "$PASS" ]; then
    wpa_cli -i $IF set_network $ID psk "\"$PASS\""
  else
    wpa_cli -i $IF set_network $ID key_mgmt NONE
  fi
fi
wpa_cli -i $IF enable_network $ID
wpa_cli -i $IF select_network $ID
sleep 6
udhcpc -i $IF -n -q -t 6 >/dev/null 2>&1
STATE=$(wpa_cli -i $IF status 2>/dev/null | sed -n 's/^wpa_state=//p')
IP=$(ifconfig $IF | sed -n 's/.*inet addr:\([0-9.]*\).*/\1/p')
echo "offload: ssid=\"$SSID\" wpa_state=$STATE ip=${IP:-none}"
""";

        /// <summary>
        /// One-click offload: ensure root, then either join <paramref name="ssid"/> on
        /// wlan0 (station mode) or, when <paramref name="enable"/> is false, disable the
        /// station networks. Call from a background thread. Returns true on a completed
        /// association attempt (COMPLETED wpa_state or an assigned IP).
        /// </summary>
        public static bool Connect(string ip, string ssid, string pass, bool enable, Action<string> log)
        {
            ssid = (ssid ?? "").Trim();

            log(">>> [1/3] Ensuring root shell on the AirCard ...");
            if (!AircardRoot.EnsureRoot(ip, log)) return false;

            var sh = new RootShell(ip);

            if (!enable)
            {
                log(">>> Offload disabled — dropping wlan0 station networks ...");
                try { log("    " + Flat(sh.RunCmd("wpa_cli -i wlan0 disable_network all 2>&1"))); }
                catch (Exception ex) { log("    error: " + ex.Message); return false; }
                return true;
            }

            if (ssid.Length == 0) { log("STOP: enable checked but no offload SSID entered."); return false; }

            log(">>> [2/3] Installing offload join script to /tmp/offload-connect.sh ...");
            try
            {
                sh.PushFile("/tmp/offload-connect.sh", OffloadSh);
                sh.RunCmd("chmod +x /tmp/offload-connect.sh");
            }
            catch (Exception ex) { log("STOP: could not write script — " + ex.Message); return false; }

            // strip double-quotes so the shell arg can't break out of its quoting
            string safeSsid = ssid.Replace("\"", "");
            string safePass = (pass ?? "").Replace("\"", "");
            log($">>> [3/3] Joining '{safeSsid}' on wlan0 as a station ({(safePass.Length > 0 ? "WPA2-PSK" : "open")}, ~10-15s) ...");
            string res;
            try
            {
                res = sh.RunCmd($"sh /tmp/offload-connect.sh \"{safeSsid}\" \"{safePass}\"",
                                timeoutMs: 22000, idleGapMs: 9000);
            }
            catch (Exception ex) { log("STOP at join — " + ex.Message); return false; }
            log("    " + Flat(res));

            bool ok = res.Contains("wpa_state=COMPLETED") || (res.Contains("ip=") && !res.Contains("ip=none"));
            if (ok)
            {
                log("OFFLOAD UP. The AirCard is now a station on '" + safeSsid + "'.");
                log("(For the drone-subnet remap use the One-Click Bridge; this button just joins the AP.)");
                return true;
            }
            log("Join issued but no association yet. Likely: AP not powered / wrong passphrase /");
            log("     target on 5 GHz (the wlan0 STA is 2.4 GHz). Fix and re-run, or click Refresh.");
            return false;
        }

        private static string Flat(string s) =>
            string.Join(" | ", s.Replace("\r", "").Split('\n', StringSplitOptions.RemoveEmptyEntries));
    }
}

using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;

namespace AC797
{
    /// <summary>
    /// One-click "bridge the PC to the drone" routine for the AirCard 797.
    ///
    /// Drives the PROVEN slot-1 path (aircard_dump/README.md Phase 7-8, verified
    /// end-to-end): the AirCard joins the drone's open AP on wlan0 (reusing QCMAP's
    /// supplicant) and NETMAP-remaps it to 10.9.1.1 so the PC reaches it without the
    /// 192.168.100.1 subnet collision. The PC-side route (10.9.0.0/16 -> 192.168.1.1)
    /// is already persistent via aircard_dump/tools/fix-ac797-route.ps1.
    ///
    /// Everything here touches ONLY the AirCard over its root telnet; the drone is
    /// never modified. The whole setup is RAM/QCMAP-owned (ATTENTION.md B4) so this
    /// is meant to be re-run after any reboot or drone-sleep.
    /// </summary>
    internal static class DroneBridge
    {
        // Verbatim copy of aircard_dump/tools/multidrone.sh, spliced at build time so
        // it cannot drift from the proven original. Pushed to /tmp on the AirCard.
        private const string MultiDroneSh =
"""
#!/bin/sh
# multidrone.sh - connect the AirCard to multiple Holy Stone drones at once,
# each remapped to a private, non-colliding subnet. DRONES ARE NEVER MODIFIED.
#
#   add <slot> <ssid> [iface]   provision a drone in slot N (1..3) -> 10.9.N.1
#   del <slot>                  tear down slot N
#   status                      show slots / links / rules
#
# slot 1 defaults to wlan0 (QCMAP's existing STA); slots 2..3 make new VAPs.
# On the PC add one route per slot:  route add 10.9.N.0 mask 255.255.255.0 192.168.1.1
#
# Design: PC hits 10.9.N.1 -> mangle marks it N -> NETMAP rewrites dst to the
# drone's real 192.168.100.x -> fwmark policy-routes it out that drone's radio ->
# MASQUERADE so the drone replies to us. conntrack reverses the whole path.
# Safety: touches ONLY the AirCard. Recover over USB. Nothing persists a reboot.

DRONE_NET=192.168.100.0/24
DRONE_GW=192.168.100.1

iface_for() { case "$1" in 1) echo wlan0;; 2) echo wlan2;; 3) echo wlan3;; *) echo "";; esac; }

add() {
  SLOT=$1; SSID=$2; IF=${3:-$(iface_for $SLOT)}
  [ -z "$SLOT" ] || [ -z "$SSID" ] || [ -z "$IF" ] && { echo "usage: add <slot 1-3> <ssid> [iface]"; return 1; }
  TABLE=$((100+SLOT)); MARK=$TABLE; RM=10.9.$SLOT

  # 1. bring up the radio + associate (open network)
  if [ "$IF" = wlan0 ]; then
    # reuse QCMAP's supplicant on wlan0
    ID=$(wpa_cli -i $IF list_networks 2>/dev/null | awk -v s="$SSID" '$0 ~ s {print $1; exit}')
    if [ -z "$ID" ]; then
      ID=$(wpa_cli -i $IF add_network | tail -1)
      wpa_cli -i $IF set_network $ID ssid "\"$SSID\""
      wpa_cli -i $IF set_network $ID key_mgmt NONE
    fi
    wpa_cli -i $IF enable_network $ID; wpa_cli -i $IF select_network $ID
    sleep 6
    udhcpc -i $IF -n -q -t 6 >/dev/null 2>&1
    LOCALIP=$(ifconfig $IF | sed -n 's/.*inet addr:\([0-9.]*\).*/\1/p')
  else
    # dedicated VAP + our own supplicant
    iw dev $IF del 2>/dev/null
    iw dev wlan0 interface add $IF type managed 2>/dev/null
    CFG=/tmp/wpa_$IF.conf
    printf 'ctrl_interface=/var/run/wpa_%s\nnetwork={\n ssid="%s"\n key_mgmt=NONE\n}\n' "$IF" "$SSID" > $CFG
    killall -q wpa_supplicant_$IF 2>/dev/null
    wpa_supplicant -i $IF -Dnl80211 -c $CFG -B >/dev/null 2>&1
    sleep 6
    LOCALIP=192.168.100.$((100+SLOT))
    ifconfig $IF $LOCALIP netmask 255.255.255.0 up
    ip route del $DRONE_NET dev $IF 2>/dev/null   # keep this subnet OUT of the main table
  fi
  [ -z "$LOCALIP" ] && { echo "slot $SLOT: $IF got no IP - is the drone on?"; return 1; }

  # 2. per-slot routing table -> out this drone's radio
  ip route flush table $TABLE 2>/dev/null
  ip route add $DRONE_NET dev $IF src $LOCALIP table $TABLE
  ip route add default via $DRONE_GW dev $IF table $TABLE
  ip rule del fwmark $MARK table $TABLE 2>/dev/null
  ip rule add fwmark $MARK table $TABLE

  # 3. remap RM.0/24 <-> drone 192.168.100.0/24, mark, and SNAT out the radio
  iptables -t mangle -C PREROUTING -d $RM.0/24 -j MARK --set-mark $MARK 2>/dev/null || \
    iptables -t mangle -A PREROUTING -d $RM.0/24 -j MARK --set-mark $MARK
  iptables -t nat -C PREROUTING -d $RM.0/24 -j NETMAP --to $DRONE_NET 2>/dev/null || \
    iptables -t nat -A PREROUTING -d $RM.0/24 -j NETMAP --to $DRONE_NET
  iptables -t nat -C POSTROUTING -o $IF -j MASQUERADE 2>/dev/null || \
    iptables -t nat -A POSTROUTING -o $IF -j MASQUERADE

  echo "slot $SLOT ready: $SSID on $IF ($LOCALIP) -> PC reaches it at $RM.1"
}

del() {
  SLOT=$1; IF=$(iface_for $SLOT); TABLE=$((100+SLOT)); MARK=$TABLE; RM=10.9.$SLOT
  iptables -t mangle -D PREROUTING -d $RM.0/24 -j MARK --set-mark $MARK 2>/dev/null
  iptables -t nat -D PREROUTING -d $RM.0/24 -j NETMAP --to $DRONE_NET 2>/dev/null
  iptables -t nat -D POSTROUTING -o $IF -j MASQUERADE 2>/dev/null
  ip rule del fwmark $MARK table $TABLE 2>/dev/null
  ip route flush table $TABLE 2>/dev/null
  [ "$IF" != wlan0 ] && { killall -q wpa_supplicant 2>/dev/null; iw dev $IF del 2>/dev/null; }
  echo "slot $SLOT torn down"
}

status() {
  echo "== station links =="; iw dev 2>/dev/null | grep -E 'Interface|type|ssid'
  echo "== remap rules =="; iptables -t nat -L PREROUTING -n 2>/dev/null | grep -E 'NETMAP|10.9'
  echo "== policy rules =="; ip rule list 2>/dev/null | grep fwmark
  echo "== connected drones =="
  for s in 1 2 3; do IF=$(iface_for $s); S=$(wpa_cli -i $IF status 2>/dev/null | sed -n 's/^ssid=//p'); [ -n "$S" ] && echo "  slot $s ($IF): $S  -> 10.9.$s.1"; done
}

CMD=$1; shift 2>/dev/null
case "$CMD" in
  add) add "$@";;
  del) del "$@";;
  status|"") status;;
  *) echo "usage: multidrone.sh add|del|status";;
esac
""";

        /// <summary>
        /// Runs the bridge stage-by-stage, logging each step. Stops at the first
        /// failed stage (so you can see exactly where it stopped) and returns false.
        /// Call from a background thread — RootShell does blocking socket I/O.
        /// </summary>
        public static bool Run(string aircardIp, string droneSsid, Action<string> log)
        {
            var sh = new RootShell(aircardIp);
            droneSsid = droneSsid.Trim();
            if (droneSsid.Length == 0) { log("STOP: no drone SSID entered."); return false; }

            // [0/7] preflight — before you click, these must be true. Also fail fast if
            // the AirCard itself isn't reachable, so we never plow ahead blind (or pop a
            // UAC prompt) when the USB cable is out or the device is off.
            log(">>> [0/7] Preflight — what must already be ON:");
            log("      • AC797 powered on + USB-connected (PC has a 192.168.1.x adapter).");
            log("      • Drone powered on, PROPS OFF, in Wi-Fi range, on 2.4 GHz.");
            log("      • Drone Wi-Fi SSID entered ('" + droneSsid + "').");
            log("    Checking the AC797 is reachable at " + aircardIp + " ...");
            if (!TcpReachable(aircardIp, 23) && !TcpReachable(aircardIp, 80))
            {
                log("STOP: AC797 not reachable at " + aircardIp + ". Is it powered on and connected");
                log("      by USB? The PC should show a 192.168.1.x RNDIS network adapter.");
                return false;
            }
            log("    AC797 reachable.");

            // [1/7] root shell — auto-enable telnet (AT!OPENLOCK -> TELNETENABLE -> reset)
            // if it isn't already up, so this is genuinely one-click even from a stock unit.
            log(">>> [1/7] Ensuring AirCard root shell (telnet 23 @ " + aircardIp + ") ...");
            if (!AircardRoot.EnsureRoot(aircardIp, log))
            {
                log("STOP: no root shell (see the AT transcript above).");
                return false;
            }

            // [2/7] keep the Wi-Fi radio awake (best-effort; non-fatal). Zero ALL THREE
            // idle-standby timers (USB / AC / battery) + WifiEnabled — any one left at
            // 300 lets the radio sleep and the station drop (README Phase 7).
            log(">>> [2/7] Keeping Wi-Fi radio up (all standby timers -> 0) ...");
            try
            {
                log("    " + OneLine(sh.RunCmd(
                    "dx -c Wlan.WifiEnabled 1 2>&1; " +
                    "dx -c Power.StandbyOnUsbIdleTimer 0 2>&1; " +
                    "dx -c Power.StandbyOnAcIdleTimer 0 2>&1; " +
                    "dx -c Power.StandbyOnBattIdleTimer 0 2>&1")));
            }
            catch (Exception ex) { log("    (non-fatal) " + ex.Message); }

            // [3/7] install the proven bridge script to /tmp (streamed, paced — see PushFile)
            log(">>> [3/7] Installing bridge script to /tmp/multidrone.sh ...");
            int expected = MultiDroneSh.Replace("\r", "").Length + 1;   // +1 for heredoc's trailing newline
            string wc;
            try
            {
                wc = sh.PushFile("/tmp/multidrone.sh", MultiDroneSh);
                sh.RunCmd("chmod +x /tmp/multidrone.sh");
            }
            catch (Exception ex) { log("STOP: could not write script — " + ex.Message); return false; }
            log("    wrote " + OneLine(wc) + " (expected ~" + expected + " bytes)");
            // wc output looks like "1234 /tmp/multidrone.sh"; require it to be close to expected
            int got = 0;
            foreach (var tok in wc.Split(new[] { ' ', '\t', '\n' }, StringSplitOptions.RemoveEmptyEntries))
                if (int.TryParse(tok, out got)) break;
            if (got < expected - 40 || got > expected + 40)
            {
                log("STOP: script did not transfer intact (" + got + " vs ~" + expected + " bytes). Re-run.");
                return false;
            }

            // [4/7] join the drone (slot 1 / wlan0). Script sleeps ~6s + DHCP, so wait it out.
            // Cold association can take longer than the script's one-shot wait, so on a
            // "got no IP" (association still settling) retry once — it persists between tries.
            log(">>> [4/7] Joining drone '" + droneSsid + "' on wlan0 (~10-15s) ...");
            string Join()
            {
                try { return sh.RunCmd("sh /tmp/multidrone.sh add 1 \"" + droneSsid + "\"", timeoutMs: 20000, idleGapMs: 9000); }
                catch (Exception ex) { return "ERROR: " + ex.Message; }
            }
            string res = Join();
            log(res);
            if (!res.Contains("slot 1 ready") && res.Contains("got no IP"))
            {
                log("    association still settling — retrying once ...");
                res = Join();
                log(res);
            }
            if (!res.Contains("slot 1 ready"))
            {
                log("STOP at join. Likely causes: drone not powered / not awake, SSID wrong,");
                log("      or drone still on 5 GHz (the AirCard STA joins 2.4 GHz). Fix and re-run.");
                return false;
            }

            // [5/7] verify the drone LINK from the AirCard by pinging the REAL drone
            // gateway (192.168.100.1) out wlan0. NOTE: do NOT ping 10.9.1.1 here — the
            // NETMAP remap lives in nat PREROUTING, which locally-generated packets skip
            // (they go via OUTPUT), so 10.9.1.1 is only reachable from the PC (stage 7).
            // A DHCP lease at [4] already proves association; this is a soft confirm, so
            // a missed local ICMP does not abort the run.
            log(">>> [5/7] Confirming the drone link from the AirCard (ping 192.168.100.1) ...");
            string ping;
            try { ping = sh.RunCmd("ping -c 2 -W 2 192.168.100.1 2>&1", timeoutMs: 9000, idleGapMs: 3000); }
            catch (Exception ex) { ping = "(ping failed: " + ex.Message + ")"; }
            log(OneLine(ping));
            if (ping.Contains("bytes from") || ping.Contains(" 0% packet loss"))
                log("    Drone link alive (192.168.100.1 answers on wlan0).");
            else
                log("    (drone didn't answer ICMP locally, but DHCP already associated — continuing.)");

            // [6/7] PC side — static RNDIS IP + 10.9.0.0/16 route (elevated, only if not
            // already set). This is the half that used to be a manual fix-ac797-route.ps1.
            log(">>> [6/7] Configuring the PC side (RNDIS static IP + route) ...");
            if (!PcNet.Ensure(log))
            {
                log("STOP: the AirCard→drone bridge is up, but the PC route/adapter is not set,");
                log("      so this laptop can't reach 10.9.1.1 yet. Fix the PC side and re-run.");
                return false;
            }

            // [7/7] the real success test: can THIS PC reach the drone at 10.9.1.1?
            log(">>> [7/7] Verifying the drone at 10.9.1.1 from this PC ...");
            if (PcReachable("10.9.1.1"))
            {
                log("BRIDGE UP ✅  This PC reaches the drone at 10.9.1.1.");
                log("Point the drone app / RTSP at 10.9.1.1 (e.g. rtsp://hy_syma:ls155633@10.9.1.1:19200/HYSymaH264VideoSMS).");
                return true;
            }
            log("STOP: PC route is set but 10.9.1.1 did not answer from this PC. If the drone");
            log("      just associated, give it a few seconds and click again (it's idempotent).");
            return false;
        }

        // TCP connect test with a short timeout (device liveness without needing root).
        private static bool TcpReachable(string ip, int port, int timeoutMs = 2500)
        {
            try
            {
                using var c = new TcpClient();
                var task = c.ConnectAsync(ip, port);
                return task.Wait(timeoutMs) && c.Connected;
            }
            catch { return false; }
        }

        // ICMP echo from the PC (proves the RNDIS route actually carries traffic).
        private static bool PcReachable(string ip)
        {
            try
            {
                using var p = new Ping();
                for (int i = 0; i < 3; i++)
                {
                    var r = p.Send(ip, 2000);
                    if (r.Status == IPStatus.Success) return true;
                }
                return false;
            }
            catch { return false; }
        }

        private static string OneLine(string s) =>
            string.Join(" | ", s.Replace("\r", "").Split('\n', StringSplitOptions.RemoveEmptyEntries));
    }
}

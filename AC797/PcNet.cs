using System.Diagnostics;

namespace AC797
{
    /// <summary>
    /// The PC (Windows) half of the one-click bridge. The AirCard side only makes the
    /// drone reachable at <c>10.9.1.1</c> from the AirCard itself; the laptop reaches it
    /// only if the USB-RNDIS adapter is static <c>192.168.1.50/24</c> (no gateway/DNS,
    /// so it can't steal the default route) and a <c>10.9.0.0/16 → 192.168.1.1</c> route
    /// exists. That is exactly what <c>aircard_dump/tools/fix-ac797-route.ps1</c> does.
    ///
    /// This class mirrors that script's logic (embedded as a const, same convention as
    /// <see cref="DroneBridge"/> embedding <c>multidrone.sh</c>) so the app can apply it
    /// itself. <see cref="IsConfigured"/> is a read-only check that needs no admin; only
    /// <see cref="Ensure"/> elevates (one UAC prompt), and only when the check fails —
    /// so a re-run of an already-configured PC does not prompt.
    /// </summary>
    internal static class PcNet
    {
        // The AC797 RNDIS adapter's fixed MAC (alias shifts between "Ethernet 2/3/…"
        // across replugs, so match by MAC — same as fix-ac797-route.ps1).
        private const string Mac = "00-A0-C6-00-00-00";

        // Read-only probe: prints OK when the adapter has a 192.168.1.x IP AND the
        // 10.9.0.0/16 route is present, else MISSING. No admin required.
        private const string CheckPs =
"""
$ErrorActionPreference = 'SilentlyContinue'
$mac = '00-A0-C6-00-00-00'
$a = Get-NetAdapter | Where-Object { $_.MacAddress -eq $mac }
$ipok = $false; $rtok = $false
if ($a) {
  $ip = Get-NetIPAddress -InterfaceIndex $a.ifIndex -AddressFamily IPv4 |
        Where-Object { $_.IPAddress -like '192.168.1.*' }
  if ($ip) { $ipok = $true }
}
if (Get-NetRoute -DestinationPrefix '10.9.0.0/16') { $rtok = $true }
if ($ipok -and $rtok) { 'OK' } else { 'MISSING' }
""";

        // Elevated apply: verbatim behavior of fix-ac797-route.ps1 (static IP + route).
        // Run via -File from a temp path under a "runas" verb.
        private const string ApplyPs =
"""
$ErrorActionPreference = 'Stop'
$mac = '00-A0-C6-00-00-00'
$adapter = Get-NetAdapter | Where-Object { $_.MacAddress -eq $mac }
if (-not $adapter) { Write-Error "AC797 RNDIS adapter (MAC $mac) not found."; exit 1 }
$alias = $adapter.InterfaceAlias
Set-NetIPInterface -InterfaceAlias $alias -Dhcp Disabled -Confirm:$false
Get-NetIPAddress -InterfaceAlias $alias -AddressFamily IPv4 -ErrorAction SilentlyContinue |
    Remove-NetIPAddress -Confirm:$false -ErrorAction SilentlyContinue
New-NetIPAddress -InterfaceAlias $alias -IPAddress 192.168.1.50 -PrefixLength 24 -Confirm:$false | Out-Null
Set-DnsClientServerAddress -InterfaceAlias $alias -ServerAddresses @() -Confirm:$false
route -p add 10.9.0.0 mask 255.255.0.0 192.168.1.1 | Out-Null
exit 0
""";

        /// <summary>
        /// True if the RNDIS adapter already has a 192.168.1.x address and the
        /// 10.9.0.0/16 route exists. Read-only; needs no elevation.
        /// </summary>
        public static bool IsConfigured(Action<string> log)
        {
            try
            {
                string outp = RunCapture(CheckPs, out _);
                return outp.Contains("OK") && !outp.Contains("MISSING");
            }
            catch (Exception ex)
            {
                log("    (PC-side check could not run: " + ex.Message + ")");
                return false;
            }
        }

        /// <summary>
        /// Guarantees the PC side is set up: if <see cref="IsConfigured"/> is already
        /// true, does nothing (no UAC). Otherwise runs the apply script elevated and
        /// re-checks. Returns true when the PC side is confirmed configured.
        /// </summary>
        public static bool Ensure(Action<string> log)
        {
            if (IsConfigured(log))
            {
                log("    PC route + RNDIS static IP already in place (192.168.1.50, 10.9.0.0/16).");
                return true;
            }

            log("    Configuring the PC side (static 192.168.1.50/24 + route 10.9.0.0/16) — approve the UAC prompt ...");
            try
            {
                string path = Path.Combine(Path.GetTempPath(), "ac797-pcfix.ps1");
                File.WriteAllText(path, ApplyPs);
                var psi = new ProcessStartInfo
                {
                    FileName = "powershell.exe",
                    Arguments = "-NoProfile -ExecutionPolicy Bypass -File \"" + path + "\"",
                    UseShellExecute = true,     // required for the runas verb
                    Verb = "runas",             // elevate
                    WindowStyle = ProcessWindowStyle.Hidden,
                    CreateNoWindow = true
                };
                using var p = Process.Start(psi);
                if (p == null) { log("    STOP: could not launch the elevated helper."); return false; }
                p.WaitForExit(30000);
            }
            catch (Exception ex)
            {
                // The most common case: user dismissed the UAC prompt (Win32 1223).
                log("    STOP: PC-side setup did not run — " + ex.Message);
                log("          Approve the UAC prompt, or run tools/fix-ac797-route.ps1 as admin once.");
                return false;
            }

            if (IsConfigured(log))
            {
                log("    PC side configured (192.168.1.50/24, route 10.9.0.0/16 → 192.168.1.1).");
                return true;
            }
            log("    STOP: PC side still not configured after the elevated run. Is the AC797 RNDIS");
            log("          adapter present (USB plugged in)? Otherwise run fix-ac797-route.ps1 by hand.");
            return false;
        }

        // Run a PowerShell snippet non-elevated, capturing stdout. Written to a temp
        // file and executed with -File to avoid command-line quoting pitfalls.
        private static string RunCapture(string script, out int exitCode)
        {
            string path = Path.Combine(Path.GetTempPath(), "ac797-check.ps1");
            File.WriteAllText(path, script);
            var psi = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = "-NoProfile -ExecutionPolicy Bypass -File \"" + path + "\"",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
            using var p = Process.Start(psi)!;
            string outp = p.StandardOutput.ReadToEnd();
            p.WaitForExit(15000);
            exitCode = p.HasExited ? p.ExitCode : -1;
            return outp;
        }
    }
}

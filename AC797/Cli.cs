using System.Net.Sockets;

namespace AC797
{
    /// <summary>
    /// Headless command-line driver for the same backend the GUI uses.
    /// Lets every feature be scripted / tested. Run AC797.exe with no args for the GUI.
    /// </summary>
    internal static class Cli
    {
        private const string Usage = """
        AC797S Control Panel — CLI

        Usage: AC797 [--ip <ip>] [--pass <pw>] <command> [args]
          defaults: --ip 192.168.1.1  --pass 7bLAKi2P

        Read / diagnostics (safe):
          selftest                 OPENLOCK keygen self-test (no device)
          ports                    check TCP 80/23/5510 reachability
          probe                    full end-to-end self-check (PASS/FAIL table)
          status                   HTTP login + parsed model.json summary
          model                    dump raw model.json
          get <group.item>         dx read (root telnet)  e.g. get Wlan.Offload
          at <cmd...>              send an AT command to :5510  e.g. at ATI

        Write / control:
          webset <key> <value>     POST to /Forms/config  e.g. webset custom.hiddenMenuEnabled 1
          set <group.item> <val>   dx write (root telnet)
          unlock                   OEM OPENLOCK unlock (keygen challenge/response)
          ensureroot                guarantee root telnet (enables TELNETENABLE/ADBENABLE + reboot if needed)

        Wi-Fi station (the AirCard joining ANOTHER access point, e.g. a drone's AP):
          offload-connect <ssid> [pass]   ensure root, join <ssid> on wlan0 as a station (open AP if pass omitted)
          offload-disable                 drop wlan0 station networks
          offload-status                  read live wpa_cli station state (wlan0)
          bridge <ssid>                    one-click: root + join + NAT-remap to 10.9.1.1 + fix the PC route

        Browser dashboard (every feature above, from a web page on the PC):
          webui [--webport <port>]         start the control panel at http://127.0.0.1:<port>/ (default 8787)
        """;

        private static string _ip = "192.168.1.1";
        private static string _pass = "7bLAKi2P";
        private static int _webPort = 8787;

        public static async Task<int> Run(string[] argv)
        {
            var args = new List<string>();
            for (int i = 0; i < argv.Length; i++)
            {
                if (argv[i] == "--ip" && i + 1 < argv.Length) _ip = argv[++i];
                else if (argv[i] == "--pass" && i + 1 < argv.Length) _pass = argv[++i];
                else if (argv[i] == "--webport" && i + 1 < argv.Length) _webPort = int.Parse(argv[++i]);
                else args.Add(argv[i]);
            }
            if (args.Count == 0) { Console.WriteLine(Usage); return 1; }

            string cmd = args[0].ToLowerInvariant();
            var rest = args.Skip(1).ToList();

            switch (cmd)
            {
                case "help" or "-h" or "--help": Console.WriteLine(Usage); return 0;
                case "selftest": return Ok($"keygen self-test: {(OpenLockKeygen.SelfTest() ? "PASS" : "FAIL")}", OpenLockKeygen.SelfTest());
                case "ports": return Ports();
                case "probe": return await Probe();
                case "status": return await Status();
                case "model": Console.WriteLine(await Web(async w => await w.GetModelJsonAsync())); return 0;
                case "get": return NeedArgs(rest, 1) ?? Print(new RootShell(_ip).DxGet(rest[0]));
                case "set": return NeedArgs(rest, 2) ?? Print(new RootShell(_ip).DxSet(rest[0], rest[1]));
                case "at": return NeedArgs(rest, 1) ?? Print(new AtPort(_ip).Send(string.Join(' ', rest)));
                case "unlock": return Print(new AtPort(_ip).Unlock());
                case "webset": return NeedArgs(rest, 2) ?? Print(await Web(async w => await w.SetAsync(rest[0], rest[1])));
                case "ensureroot": return Ok("root shell ready", AircardRoot.EnsureRoot(_ip, Console.WriteLine));
                case "offload-status": return Print(new RootShell(_ip).RunCmd("wpa_cli -i wlan0 status 2>&1"));
                case "offload-disable": return Ok("offload disabled", OffloadStation.Connect(_ip, "", "", false, Console.WriteLine));
                case "offload-connect": return NeedArgs(rest, 1) ?? Ok("offload connect finished",
                    OffloadStation.Connect(_ip, rest[0], rest.Count >= 2 ? rest[1] : "", true, Console.WriteLine));
                case "bridge": return NeedArgs(rest, 1) ?? Ok("bridge finished", DroneBridge.Run(_ip, rest[0], Console.WriteLine));
                case "sh": return NeedArgs(rest, 1) ?? Print(new RootShell(_ip).RunCmd(string.Join(' ', rest)));
                case "webui": WebUi.Run(_ip, _pass, _webPort, Console.WriteLine); return 0;
                case "atunlock": return NeedArgs(rest, 1) ?? Print(new AtShell(_ip).UnlockAndRun(rest.ToArray()));
                case "pushbin": return NeedArgs(rest, 2) ?? PushBin(rest[0], rest[1]);
                default: Console.WriteLine($"unknown command '{cmd}'\n\n{Usage}"); return 1;
            }
        }

        // one-off: push a local binary file to the device via a base64 heredoc + `base64 -d`,
        // since PushFile's line-paced heredoc is only safe for text. Verifies size + md5 match
        // before leaving it at the destination path.
        private static int PushBin(string localPath, string remotePath)
        {
            byte[] data = File.ReadAllBytes(localPath);
            string expectedMd5 = Convert.ToHexString(System.Security.Cryptography.MD5.HashData(data)).ToLowerInvariant();
            string b64 = Convert.ToBase64String(data);
            var lines = new List<string>();
            for (int i = 0; i < b64.Length; i += 1000) lines.Add(b64.Substring(i, Math.Min(1000, b64.Length - i)));
            string b64Path = remotePath + ".b64";
            var rs = new RootShell(_ip);
            Console.WriteLine($"pushing {data.Length} bytes as {lines.Count} base64 lines...");
            rs.PushFile(b64Path, string.Join('\n', lines));
            string decodeOut = rs.RunCmd($"base64 -d {b64Path} > {remotePath} && rm -f {b64Path} && wc -c {remotePath} && md5sum {remotePath} 2>&1", timeoutMs: 30000, idleGapMs: 3000);
            Console.WriteLine(decodeOut);
            bool sizeOk = decodeOut.Contains(data.Length.ToString());
            bool md5Ok = decodeOut.Contains(expectedMd5, StringComparison.OrdinalIgnoreCase);
            Console.WriteLine($"expected: {data.Length} bytes, md5 {expectedMd5} -> {(sizeOk && md5Ok ? "MATCH" : "MISMATCH")}");
            return sizeOk && md5Ok ? 0 : 1;
        }

        // ── helpers ──────────────────────────────────────────────────
        private static int Ok(string msg, bool ok) { Console.WriteLine(msg); return ok ? 0 : 1; }
        private static int Print(string s) { Console.WriteLine(s); return 0; }
        private static int? NeedArgs(List<string> r, int n)
        {
            if (r.Count >= n) return null;
            Console.Error.WriteLine($"error: expected {n} argument(s)"); return 3;
        }

        private static async Task<T> Web<T>(Func<WebApi, Task<T>> f)
        {
            var w = new WebApi(_ip);
            await w.LoginAsync(_pass);
            return await f(w);
        }

        private static bool PortOpen(int port)
        {
            try { using var c = new TcpClient(); var t = c.ConnectAsync(_ip, port); return t.Wait(1500) && c.Connected; }
            catch { return false; }
        }

        private static int Ports()
        {
            foreach (var (p, name) in new[] { (80, "web"), (23, "telnet-root"), (5510, "AT") })
                Console.WriteLine($"  {p,-5} {name,-12} {(PortOpen(p) ? "OPEN" : "closed")}");
            return 0;
        }

        private static async Task<int> Status()
        {
            var w = new WebApi(_ip);
            Console.WriteLine(await w.LoginAsync(_pass));
            Console.WriteLine(WebApi.Summarize(await w.GetModelJsonAsync()));
            return 0;
        }

        // ── probe: exercise every channel, print PASS/FAIL ───────────
        private static async Task<int> Probe()
        {
            int fails = 0;
            void Check(string name, bool ok, string detail = "")
            {
                Console.WriteLine($"  [{(ok ? "PASS" : "FAIL")}] {name,-22} {detail}");
                if (!ok) fails++;
            }

            Check("keygen self-test", OpenLockKeygen.SelfTest());
            bool p80 = PortOpen(80), p23 = PortOpen(23), p5510 = PortOpen(5510);
            Check("port 80 (web)", p80);
            Check("port 23 (telnet)", p23);
            Check("port 5510 (AT)", p5510);

            try
            {
                var w = new WebApi(_ip);
                await w.LoginAsync(_pass);
                Check("HTTP login (Admin)", w.Role.Equals("Admin", StringComparison.OrdinalIgnoreCase), $"role={w.Role}");
                string model = await w.GetModelJsonAsync();
                Check("model.json read", model.Contains("secToken"), $"{model.Length} bytes");
            }
            catch (Exception ex) { Check("HTTP login (Admin)", false, ex.Message); }

            try { string r = new AtPort(_ip).Send("ATI"); Check("AT port responds", r.Contains("OK"), r.Contains("AirCard") ? "AirCard 797S" : ""); }
            catch (Exception ex) { Check("AT port responds", false, ex.Message); }

            try { string r = new RootShell(_ip).DxGet("General.Versions"); Check("dx engine (root)", r.Length > 0, r.Split('\n')[0]); }
            catch (Exception ex) { Check("dx engine (root)", false, ex.Message); }

            Console.WriteLine(fails == 0 ? "\nALL CHECKS PASSED" : $"\n{fails} CHECK(S) FAILED");
            return fails == 0 ? 0 : 1;
        }

    }
}

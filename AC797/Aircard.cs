using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace AC797
{
    /// <summary>
    /// Sierra Wireless MDM9x40 AT!OPENLOCK keygen — clean port of the algorithm used
    /// to unlock the AC797S engineering commands. Self-validates against a known vector.
    /// </summary>
    internal sealed class OpenLockKeygen
    {
        // keytable index 11 (NTG9X40C / AC815s / MR1100 OpenLock key)
        private static readonly byte[] Key =
        {
            0x8A, 0x56, 0x03, 0xF0, 0xBB, 0x9C, 0x13, 0xD2,
            0x4E, 0xB2, 0x45, 0xAD, 0xC4, 0x0A, 0xE7, 0x52
        };
        private static readonly int[] Init = { 7, 3, 0, 1, 5 };

        private readonly int[] _tbl = new int[256];
        private readonly int[] _rtbl = new int[20];

        private (int, int, int) PreInit(int counter, int keylen, int challengelen, int mcount)
        {
            if (counter != 0)
            {
                int tmp2 = 0, i = 1;
                while (i < counter) i = 2 * i + 1;
                while (true)
                {
                    int tmp = mcount;
                    mcount = tmp + 1;
                    challengelen = (Key[tmp & 0xFF] + _tbl[challengelen & 0xFF]) & 0xFF;
                    if (mcount >= keylen)
                    {
                        mcount = 0;
                        challengelen = ((challengelen & 0xFF) + keylen) & 0xFF;
                    }
                    tmp2++;
                    int tmp3 = ((challengelen & 0xFF) & i) & 0xFF;
                    if (tmp2 >= 0xB && tmp3 != 0) tmp3 = counter % tmp3;
                    if (tmp3 <= counter) { counter = tmp3 & 0xFF; break; }
                }
            }
            return (counter, challengelen, mcount);
        }

        private void InitState(int keylen)
        {
            for (int i = 0; i < 256; i++) _tbl[i] = i & 0xFF;
            int mcount = 0;
            int cl = keylen & unchecked((int)0xffffff00);
            for (int i = 0xFF; i > -1; i--)
            {
                (int t, cl, mcount) = PreInit(i, keylen, cl, mcount);
                int m = _tbl[i];
                _tbl[i] = _tbl[t & 0xFF];
                _tbl[t & 0xFF] = m;
            }
            for (int k = 0; k < 5; k++)
                _rtbl[k] = Init[k] != 0 ? _tbl[Init[k]] : _tbl[cl & 0xFF];
        }

        private int Algo(int challenge, int a, int b, int c, int d, int e, int ret, int ret2, int flag)
        {
            int v6 = _rtbl[e];
            int v0 = (v6 + 1) & 0xFF;
            _rtbl[e] = v0;
            _rtbl[c] = (_tbl[(v6 + flag) & 0xFF] + _rtbl[c]) & 0xFF;
            int v4 = _rtbl[c] & 0xFF;
            int v2 = _rtbl[b] & 0xFF;
            int v1 = _tbl[v2 & 0xFF];
            _tbl[v2 & 0xFF] = _tbl[v4 & 0xFF];
            int v5 = _rtbl[d] & 0xFF;
            _tbl[v4 & 0xFF] = _tbl[v5 & 0xFF];
            _tbl[v5 & 0xFF] = _tbl[v0 & 0xFF];
            _tbl[v0] = v1 & 0xFF;
            int u = _tbl[_tbl[(_tbl[(_rtbl[a] + _tbl[v1 & 0xFF]) & 0xFF] + _tbl[v5 & 0xFF] + _tbl[v2 & 0xFF]) & 0xFF] & 0xFF];
            int v = _tbl[(_tbl[v4 & 0xFF] + v1) & 0xFF];
            _rtbl[ret] = u ^ v ^ challenge;
            _rtbl[a] = (_tbl[v1 & 0xFF] + _rtbl[a]) & 0xFF;
            _rtbl[ret2] = challenge & 0xFF;
            return _rtbl[ret] & 0xFF;
        }

        public string Response(string challengeHex)
        {
            byte[] challenge = Convert.FromHexString(challengeHex);
            InitState(16);
            var outp = new byte[8];
            for (int i = 0; i < 8; i++)
                outp[i] = (byte)Algo(challenge[i], 4, 2, 1, 0, 3, 2, 0, 0); // MDM9x40 run params
            return Convert.ToHexString(outp).ToUpperInvariant();
        }

        public static bool SelfTest() =>
            new OpenLockKeygen().Response("BE96CBBEE0829BCA") == "1033773720F6EE66";
    }

    /// <summary>HTTP admin API: session login, model.json, and /Forms/config writes.</summary>
    internal sealed class WebApi
    {
        private readonly HttpClient _http;
        public string Ip { get; }
        public string? Token { get; private set; }
        public string Role { get; private set; } = "Guest";

        public WebApi(string ip)
        {
            Ip = ip;
            var handler = new HttpClientHandler
            {
                CookieContainer = new CookieContainer(),
                UseCookies = true,
                AllowAutoRedirect = true
            };
            _http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(10) };
        }

        private string Base => $"http://{Ip}";

        public async Task<string> LoginAsync(string password)
        {
            string home = await _http.GetStringAsync($"{Base}/");
            string token = Match(home, "\"secToken\"\\s*:\\s*\"([^\"]+)\"");
            var body = new Dictionary<string, string>
            {
                ["session.password"] = password,
                ["session.lang"] = "en",
                ["token"] = token
            };
            var resp = await _http.PostAsync($"{Base}/Forms/config", new FormUrlEncodedContent(body));
            await RefreshTokenAsync();
            return $"login -> HTTP {(int)resp.StatusCode}, role={Role}";
        }

        public async Task<string> GetModelJsonAsync() => await _http.GetStringAsync($"{Base}/model.json");

        public async Task RefreshTokenAsync()
        {
            string m = await GetModelJsonAsync();
            string t = Match(m, "\"secToken\"\\s*:\\s*\"([^\"]+)\"");
            if (t.Length > 0) Token = t;
            string r = Match(m, "\"userRole\"\\s*:\\s*\"([^\"]+)\"");
            if (r.Length > 0) Role = r;
        }

        public async Task<string> SetManyAsync(Dictionary<string, string> fields)
        {
            var body = new Dictionary<string, string>(fields)
            {
                ["token"] = Token ?? "",
                ["session.lang"] = "en"
            };
            var resp = await _http.PostAsync($"{Base}/Forms/config", new FormUrlEncodedContent(body));
            await RefreshTokenAsync();
            string keys = string.Join(", ", fields.Select(kv => $"{kv.Key}={kv.Value}"));
            return $"POST {{ {keys} }} -> HTTP {(int)resp.StatusCode}";
        }

        public Task<string> SetAsync(string key, string val) =>
            SetManyAsync(new Dictionary<string, string> { [key] = val });

        /// <summary>GET a device-relative URL (e.g. /dui/export.cfg?save=export.cfg) using the logged-in session's cookies.</summary>
        public async Task<byte[]> GetBytesAsync(string relativeUrl) => await _http.GetByteArrayAsync($"{Base}{relativeUrl}");

        /// <summary>
        /// POSTs a multipart/form-data form the same way the portal's own Import/firmware-update
        /// forms do: hidden token + redirect fields, plus one file field. Returns the final
        /// response URL (after redirect) and status, which is how those forms report success/failure.
        /// </summary>
        public async Task<(int status, string finalUrl)> PostMultipartAsync(
            string relativeUrl, string fileField, byte[] fileBytes, string fileName, string okQuery, string errQuery)
        {
            using var content = new MultipartFormDataContent
            {
                { new StringContent(Token ?? ""), "token" },
                { new StringContent($"/index.html?{okQuery}"), "ok_redirect" },
                { new StringContent($"/index.html?{errQuery}"), "err_redirect" }
            };
            var fileContent = new ByteArrayContent(fileBytes);
            content.Add(fileContent, fileField, fileName);
            var resp = await _http.PostAsync($"{Base}{relativeUrl}", content);
            return ((int)resp.StatusCode, resp.RequestMessage?.RequestUri?.ToString() ?? "");
        }

        /// <summary>Curated human-readable summary of model.json (falls back to raw on parse error).</summary>
        public static string Summarize(string json)
        {
            var sb = new StringBuilder();
            try
            {
                using var doc = JsonDocument.Parse(json,
                    new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip });
                var root = doc.RootElement;
                void Line(string label, params string[] path)
                {
                    JsonElement e = root;
                    foreach (var p in path)
                        if (e.ValueKind != JsonValueKind.Object || !e.TryGetProperty(p, out e)) { return; }
                    sb.AppendLine($"  {label,-20}: {e}");
                }
                sb.AppendLine("── Device ──");
                Line("Model", "general", "model");
                Line("Firmware", "general", "FWversion");
                Line("IMEI", "general", "IMEI");
                Line("Up time (s)", "general", "upTime");
                Line("Temperature", "general", "devTemperature");
                Line("Session role", "session", "userRole");
                sb.AppendLine("── Wi-Fi ──");
                Line("Status", "wifi", "status");
                Line("SSID", "wifi", "SSID");
                Line("Passphrase", "wifi", "passPhrase");
                Line("Channel", "wifi", "channel");
                sb.AppendLine("── Router ──");
                Line("Gateway IP", "router", "gatewayIP");
                Line("USB mode", "router", "usbMode");
                Line("Hidden menu", "custom", "hiddenMenuEnabled");
            }
            catch (Exception ex)
            {
                sb.AppendLine($"[parse failed: {ex.Message} — raw JSON below]");
            }
            return sb.ToString();
        }

        private static string Match(string s, string pattern)
        {
            var m = Regex.Match(s, pattern);
            return m.Success ? m.Groups[1].Value : "";
        }
    }

    /// <summary>Raw AT-command port over TCP 5510, including OEM OPENLOCK unlock.</summary>
    internal sealed class AtPort
    {
        public string Ip { get; }
        public AtPort(string ip) => Ip = ip;

        public string Send(string cmd, int timeoutMs = 1500)
        {
            using var c = new TcpClient();
            c.Connect(Ip, 5510);
            using var s = c.GetStream();
            Write(s, cmd);
            return Read(s, timeoutMs, 300).Trim();
        }

        /// <summary>Query the challenge and answer it in one session; returns log text.</summary>
        public string Unlock()
        {
            using var c = new TcpClient();
            c.Connect(Ip, 5510);
            using var s = c.GetStream();
            Write(s, "AT!OPENLOCK?");
            string r = Read(s, 1500, 300);
            var m = Regex.Match(r, "\\b([0-9A-Fa-f]{16})\\b");
            if (!m.Success) return "no challenge in reply:\n" + r;
            string challenge = m.Groups[1].Value.ToUpperInvariant();
            string resp = new OpenLockKeygen().Response(challenge);
            Write(s, $"AT!OPENLOCK=\"{resp}\"");
            string r2 = Read(s, 1500, 300);
            return $"challenge = {challenge}\nresponse  = {resp}\n{r2.Trim()}";
        }

        /// <summary>
        /// Unlock the OEM engineering lock, then run one or more privileged AT commands
        /// (e.g. <c>AT!CUSTOM="TELNETENABLE",1</c>, <c>AT!RESET</c>) in the SAME session.
        /// The lock resets when the socket closes, so the unlock and every privileged
        /// command it gates must share one connection (see README Phase 4 / setflags.py).
        /// Returns a transcript; the first line reports the unlock result. When the
        /// unlock does not return OK the privileged commands are not sent.
        /// </summary>
        public string UnlockAndRun(params string[] privilegedCmds)
        {
            using var c = new TcpClient();
            c.Connect(Ip, 5510);
            using var s = c.GetStream();

            Write(s, "AT!OPENLOCK?");
            string r = Read(s, 1500, 300);
            var m = Regex.Match(r, "\\b([0-9A-Fa-f]{16})\\b");
            if (!m.Success) return "unlock: no challenge in reply:\n" + r.Trim();
            string challenge = m.Groups[1].Value.ToUpperInvariant();
            string resp = new OpenLockKeygen().Response(challenge);
            Write(s, $"AT!OPENLOCK=\"{resp}\"");
            string unlock = Read(s, 1500, 300).Trim();

            var log = new StringBuilder();
            log.AppendLine($"AT!OPENLOCK=\"{resp}\" -> {OneLine(unlock)}");
            if (!unlock.Contains("OK", StringComparison.OrdinalIgnoreCase))
            {
                log.Append("unlock did not return OK — privileged commands skipped.");
                return log.ToString();
            }
            foreach (var cmd in privilegedCmds)
            {
                Write(s, cmd);
                // AT!RESET reboots immediately, so its reply may just be the socket dropping.
                string rr = Read(s, 2500, 400).Trim();
                log.AppendLine($"$ {cmd} -> {OneLine(rr)}");
            }
            return log.ToString().TrimEnd();
        }

        private static string OneLine(string s) =>
            string.Join(" ", s.Replace("\r", "").Split('\n', StringSplitOptions.RemoveEmptyEntries));

        private static void Write(NetworkStream s, string cmd)
        {
            byte[] b = Encoding.ASCII.GetBytes(cmd + "\r\n");
            s.Write(b, 0, b.Length);
        }

        private static string Read(NetworkStream s, int firstMs, int gapMs)
        {
            var mem = new MemoryStream();
            var buf = new byte[4096];
            s.ReadTimeout = firstMs;
            while (true)
            {
                try
                {
                    int n = s.Read(buf, 0, buf.Length);
                    if (n <= 0) break;
                    mem.Write(buf, 0, n);
                    s.ReadTimeout = gapMs;
                }
                catch (IOException) { break; }
            }
            return Encoding.ASCII.GetString(mem.ToArray());
        }
    }

    /// <summary>Root telnet channel (port 23) for driving the `dx` config engine.</summary>
    internal sealed class RootShell
    {
        private const byte IAC = 255, DONT = 254, DO = 253, WONT = 252, WILL = 251;
        public string Ip { get; }
        public RootShell(string ip) => Ip = ip;

        public string DxGet(string key) => RunCmd($"dx {key} 2>/dev/null");
        public string DxSet(string key, string val) => RunCmd($"dx -c {key} {val} 2>&1");

        // idleGapMs = how long to wait for more output after the last byte before
        // returning. Bump it well past any in-script sleep (e.g. the bridge's `sleep 6`
        // + DHCP) so a mid-command pause doesn't truncate the result.
        public string RunCmd(string cmd, int timeoutMs = 6000, int idleGapMs = 400)
        {
            using var c = new TcpClient();
            c.Connect(Ip, 23);
            using var s = c.GetStream();
            ReadProc(s, 2500, 400);                 // consume banner + prompt
            byte[] b = Encoding.ASCII.GetBytes(cmd + "\n");
            s.Write(b, 0, b.Length);
            string outp = ReadProc(s, timeoutMs, idleGapMs);
            return Clean(outp, cmd);
        }

        /// <summary>
        /// Write <paramref name="content"/> to a file on the device, robustly.
        /// Streams a quoted heredoc ONE LINE AT A TIME over a single connection,
        /// draining the echo after each line. The per-line pacing is essential:
        /// blasting a multi-KB heredoc in one write overflows the telnet pty's input
        /// buffer and silently truncates lines. Returns `wc -c` of the written file.
        /// </summary>
        public string PushFile(string remotePath, string content)
        {
            using var c = new TcpClient();
            c.Connect(Ip, 23);
            using var s = c.GetStream();
            ReadProc(s, 2500, 400);                          // banner + prompt

            void Send(string line)
            {
                byte[] b = Encoding.ASCII.GetBytes(line + "\n");
                s.Write(b, 0, b.Length);
                ReadProc(s, 300, 120);                       // drain echo → paces the stream
            }

            Send("cat > " + remotePath + " <<'HSEOF'");
            foreach (string line in content.Replace("\r", "").Split('\n'))
                Send(line);
            Send("HSEOF");
            return Clean(RunLine(s, "wc -c " + remotePath), "wc -c " + remotePath);
        }

        // send one command on an already-open stream and read its reply
        private static string RunLine(NetworkStream s, string cmd)
        {
            byte[] b = Encoding.ASCII.GetBytes(cmd + "\n");
            s.Write(b, 0, b.Length);
            return ReadProc(s, 3000, 400);
        }

        private static string Clean(string outp, string cmd)
        {
            var lines = outp.Replace("\r", "").Replace("\0", "").Split('\n');
            string first = cmd.Split('\n')[0];
            var keep = new List<string>();
            foreach (var l in lines)
            {
                string t = l.TrimEnd();
                if (t.Length == 0) continue;
                // Drop the echoed command only when the line IS that echo (optionally
                // prompt-prefixed), not merely when it contains the text — otherwise a
                // short cmd like "id" would eat its own output line "uid=0(root)".
                if (keep.Count == 0 && IsEcho(t, first)) continue;
                if (t == "/ #" || t.EndsWith("/ #")) continue;                    // drop prompt
                keep.Add(t);
            }
            return string.Join("\n", keep).Trim();
        }

        // true if line t is the terminal echo of command `first` (bare or after a prompt)
        private static bool IsEcho(string t, string first)
        {
            string s = t.TrimStart();
            int hash = s.LastIndexOf("# ", StringComparison.Ordinal);   // strip a "/ # " prompt prefix
            if (hash >= 0) s = s[(hash + 2)..].TrimStart();
            return s == first;
        }

        /// <summary>Reads raw bytes, answering telnet IAC negotiation, until idle.</summary>
        private static string ReadProc(NetworkStream s, int firstMs, int gapMs)
        {
            var outp = new MemoryStream();
            var buf = new byte[4096];
            s.ReadTimeout = firstMs;
            while (true)
            {
                int n;
                try { n = s.Read(buf, 0, buf.Length); }
                catch (IOException) { break; }
                if (n <= 0) break;
                for (int i = 0; i < n; i++)
                {
                    if (buf[i] == IAC && i + 2 < n)
                    {
                        byte verb = buf[i + 1], opt = buf[i + 2];
                        byte reply = verb is DO or DONT ? WONT : DONT;
                        s.Write(new byte[] { IAC, reply, opt }, 0, 3);
                        i += 2;
                    }
                    else outp.WriteByte(buf[i]);
                }
                s.ReadTimeout = gapMs;
            }
            return Encoding.ASCII.GetString(outp.ToArray());
        }
    }

    /// <summary>
    /// Direct AT access over the root shell, via the modem's own AT channel
    /// (/dev/smd7, per /usr/bin/runatotcp.sh) instead of the TCP 5510 bridge —
    /// which the OEM's `nc` loop restarts on its own schedule and isn't always up.
    /// Uses a single telnet session per call; a command's full reply (OK/ERROR)
    /// is captured by holding the device fd open for a short read window.
    /// </summary>
    internal sealed class AtShell
    {
        private readonly string _ip;
        public AtShell(string ip) => _ip = ip;

        public string Send(string atCmd, int timeoutSec = 3)
        {
            string script = "exec 3<>/dev/smd7; printf '" + Esc(atCmd) + "\\r' >&3; " +
                             $"timeout -t {timeoutSec} cat <&3; exec 3<&-";
            return new RootShell(_ip).RunCmd(script, timeoutMs: (timeoutSec + 4) * 1000, idleGapMs: 500);
        }

        /// <summary>
        /// Unlock the OEM engineering lock, then run one or more privileged AT
        /// commands, over the direct /dev/smd7 channel. Unlike AtPort (TCP 5510,
        /// which needs the unlock + privileged command on one socket because
        /// closing it hangs up the serial line), smd7 is a shared-memory channel
        /// with no such hangup semantics, so the challenge and the response/command
        /// can safely go in two separate root-shell round trips.
        /// </summary>
        public string UnlockAndRun(params string[] privilegedCmds)
        {
            string challengeReply = Send("AT!OPENLOCK?", timeoutSec: 2);
            var m = Regex.Match(challengeReply, @"\b([0-9A-Fa-f]{16})\b");
            if (!m.Success) return "unlock: no challenge in reply:\n" + challengeReply;
            string challenge = m.Groups[1].Value.ToUpperInvariant();
            string resp = new OpenLockKeygen().Response(challenge);

            string script = "exec 3<>/dev/smd7; printf '" + Esc($"AT!OPENLOCK=\"{resp}\"") + "\\r' >&3; sleep 1; " +
                             string.Join(" ", privilegedCmds.Select(c => "printf '" + Esc(c) + "\\r' >&3; sleep 1; ")) +
                             "timeout -t 3 cat <&3; exec 3<&-";
            string result = new RootShell(_ip).RunCmd(script, timeoutMs: 9000, idleGapMs: 4000);
            return $"challenge = {challenge}\nresponse  = {resp}\n{result}";
        }

        /// <summary>
        /// Writes an SMS to local storage via AT+CMGW (text mode), the same path the
        /// device's own web portal reads from for the Messaging page. Needs a SIM —
        /// without one the modem answers every CMGx command with ERROR / +CME ERROR.
        /// </summary>
        public string WriteSms(string number, string body)
        {
            string script =
                "exec 3<>/dev/smd7; printf 'AT+CMGF=1\\r' >&3; sleep 1; " +
                "printf 'AT+CPMS=\"ME\",\"ME\",\"ME\"\\r' >&3; sleep 1; " +
                $"printf 'AT+CMGW=\"{Esc(number)}\"\\r' >&3; sleep 1; " +
                "printf '" + Esc(body) + "\\x1a' >&3; sleep 2; " +
                "timeout -t 4 cat <&3; exec 3<&-";
            // idleGapMs must clear the 1+1+1+2s of in-script sleeps between printfs —
            // RunCmd's gap timer only measures silence AFTER the first byte (the
            // telnet-echoed command line), and every printf above runs before any
            // AT reply is read, so a short gap here truncates the result to nothing.
            return new RootShell(_ip).RunCmd(script, timeoutMs: 9000, idleGapMs: 6000);
        }

        public string ListSms(string mem = "ME") =>
            Send($"AT+CMGF=1;+CPMS=\"{mem}\";+CMGL=\"ALL\"", timeoutSec: 4);

        // escape for embedding inside a single-quoted shell printf format string
        private static string Esc(string s) => s.Replace("'", "'\\''");
    }
}

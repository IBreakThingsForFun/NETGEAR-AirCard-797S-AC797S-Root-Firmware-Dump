using System.Linq;
using System.Net;
using System.Text;
using System.Text.Json;

namespace AC797
{
    /// <summary>
    /// A browser-based control panel for the AirCard, mirroring every feature the
    /// WinForms GUI and CLI expose (root, dx console, AT console, Wi-Fi offload,
    /// multi-AP bridge, SMS) behind a small local HTTP API, under /_tool. Runs
    /// entirely on the PC — it talks to the AirCard the same way AC797.exe always
    /// has (HTTP/telnet/AT). Every OTHER path is transparently reverse-proxied to
    /// the device's own stock admin portal at http://{ip}/, so the real portal — every
    /// form, table and dialog it has — is reachable at this same local port, under
    /// this app's own address bar entry, without ever navigating to the device's IP
    /// directly. See ProxyToDevice.
    /// </summary>
    internal static class WebUi
    {
        public static void Run(string ip, string webPass, int port, Action<string> log)
        {
            var listener = new HttpListener();
            listener.Prefixes.Add($"http://127.0.0.1:{port}/");
            listener.Start();
            log($"AC797 dashboard running at http://127.0.0.1:{port}/  (Ctrl+C to stop)");
            while (true)
            {
                HttpListenerContext ctx;
                try { ctx = listener.GetContext(); } catch (HttpListenerException) { break; }
                var local = ctx;
                ThreadPool.QueueUserWorkItem(_ => Handle(local, ip, webPass, log));
            }
        }

        private static void Handle(HttpListenerContext ctx, string ip, string webPass, Action<string> log)
        {
            try
            {
                string path = ctx.Request.Url!.AbsolutePath;
                if (ctx.Request.HttpMethod == "GET" && (path == "/_tool" || path == "/_tool/")) { WriteHtml(ctx, IndexHtml); return; }
                if (ctx.Request.HttpMethod == "GET" && path == "/_tool/api/fs/image")
                {
                    try { var (bytes, mime) = FsImage(ip, ctx.Request.QueryString["path"] ?? ""); WriteBinary(ctx, bytes, mime); }
                    catch (Exception ex) { ctx.Response.StatusCode = 500; WriteHtml(ctx, ex.Message); }
                    return;
                }
                if (ctx.Request.HttpMethod == "GET" && path == "/_tool/api/fs/download")
                {
                    try
                    {
                        var (bytes, filename) = FsDownload(ip, ctx.Request.QueryString["path"] ?? "/");
                        ctx.Response.Headers.Add("Content-Disposition", $"attachment; filename=\"{filename}\"");
                        WriteBinary(ctx, bytes, "application/gzip");
                    }
                    catch (Exception ex) { ctx.Response.StatusCode = 500; WriteHtml(ctx, ex.Message); }
                    return;
                }
                if (ctx.Request.HttpMethod == "GET" && path == "/_tool/api/sw/export")
                {
                    try
                    {
                        byte[] bytes = SwExport(ip, webPass).GetAwaiter().GetResult();
                        ctx.Response.Headers.Add("Content-Disposition", $"attachment; filename=\"ac797_settings_{DateTime.Now:yyyyMMdd_HHmmss}.cfg\"");
                        WriteBinary(ctx, bytes, "application/octet-stream");
                    }
                    catch (Exception ex) { ctx.Response.StatusCode = 500; WriteHtml(ctx, ex.Message); }
                    return;
                }
                if (ctx.Request.HttpMethod == "POST" && (path == "/_tool/api/sw/import" || path == "/_tool/api/sw/fwupdate"))
                {
                    try
                    {
                        byte[] fileBytes = ReadAllBytes(ctx.Request.InputStream);
                        string filename = ctx.Request.QueryString["filename"] ?? "upload.bin";
                        object uploadResult = path == "/_tool/api/sw/import"
                            ? SwImport(ip, webPass, fileBytes, filename)
                            : SwFwUpdate(ip, webPass, fileBytes, filename);
                        WriteJson(ctx, new { ok = true, result = uploadResult });
                    }
                    catch (Exception ex) { WriteJson(ctx, new { ok = false, error = ex.Message }); }
                    return;
                }
                if (ctx.Request.HttpMethod == "GET" && path == "/_tool/api/fw/partition")
                {
                    try
                    {
                        int mtd = int.Parse(ctx.Request.QueryString["mtd"] ?? "-1");
                        var (bytes, filename) = FwDownloadOne(ip, mtd);
                        ctx.Response.Headers.Add("Content-Disposition", $"attachment; filename=\"{filename}\"");
                        WriteBinary(ctx, bytes, "application/gzip");
                    }
                    catch (Exception ex) { ctx.Response.StatusCode = 500; WriteHtml(ctx, ex.Message); }
                    return;
                }
                if (ctx.Request.HttpMethod == "GET" && path == "/_tool/api/fw/zip")
                {
                    try
                    {
                        var mtds = (ctx.Request.QueryString["mtds"] ?? "")
                            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                            .Select(int.Parse).ToArray();
                        var (bytes, filename) = FwDownloadZip(ip, mtds);
                        ctx.Response.Headers.Add("Content-Disposition", $"attachment; filename=\"{filename}\"");
                        WriteBinary(ctx, bytes, "application/zip");
                    }
                    catch (Exception ex) { ctx.Response.StatusCode = 500; WriteHtml(ctx, ex.Message); }
                    return;
                }
                if (!path.StartsWith("/_tool/api/")) { ProxyToDevice(ctx, ip); return; }

                var body = ReadJson(ctx);
                log($"[web] {path}");

                object result;
                try
                {
                    result = path switch
                    {
                        "/_tool/api/ports" => Ports(ip),
                        "/_tool/api/status" => Status(ip, webPass).GetAwaiter().GetResult(),
                        "/_tool/api/dx/get" => new RootShell(ip).DxGet(Str(body, "key")),
                        "/_tool/api/dx/set" => new RootShell(ip).DxSet(Str(body, "key"), Str(body, "val")),
                        "/_tool/api/at" => new AtShell(ip).Send(Str(body, "cmd")),
                        "/_tool/api/unlock" => new AtPort(ip).Unlock(),
                        "/_tool/api/ensureroot" => RunLog(l => AircardRoot.EnsureRoot(ip, l)),
                        "/_tool/api/offload/status" => new RootShell(ip).RunCmd("wpa_cli -i wlan0 status 2>&1"),
                        "/_tool/api/offload/connect" => RunLog(l => OffloadStation.Connect(ip, Str(body, "ssid"), Str(body, "pass"), true, l)),
                        "/_tool/api/offload/disable" => RunLog(l => OffloadStation.Connect(ip, "", "", false, l)),
                        "/_tool/api/bridge" => RunLog(l => DroneBridge.Run(ip, Str(body, "ssid"), l)),
                        "/_tool/api/sms/write" => new AtShell(ip).WriteSms(Str(body, "number"), Str(body, "body")),
                        "/_tool/api/sms/list" => new AtShell(ip).ListSms(),
                        "/_tool/api/messages" => Messages(ip, webPass).GetAwaiter().GetResult(),
                        "/_tool/api/fs/list" => FsList(ip, Str(body, "path", "/")),
                        "/_tool/api/fs/read" => FsRead(ip, Str(body, "path", "")),
                        "/_tool/api/fw/list" => FwList(ip),
                        "/_tool/api/sw/status" => SwStatus(ip, webPass).GetAwaiter().GetResult(),
                        "/_tool/api/sw/checkupdate" => SwAction(ip, webPass, new() { ["fota.fwupdater.chknow"] = "1" }).GetAwaiter().GetResult(),
                        "/_tool/api/sw/startupdate" => SwAction(ip, webPass, new() { ["fota.fwupdater.start"] = "1" }).GetAwaiter().GetResult(),
                        "/_tool/api/sw/postpone" => SwAction(ip, webPass, new() { ["fota.fwupdater.postponeinstall"] = "1", ["fota.fwupdater.postponetime"] = Str(body, "hours", "14400") }).GetAwaiter().GetResult(),
                        "/_tool/api/sw/factoryreset" => SwAction(ip, webPass, new() { ["general.factoryReset"] = "1" }).GetAwaiter().GetResult(),
                        _ => null!
                    };
                    if (result is null) { ctx.Response.StatusCode = 404; ctx.Response.Close(); return; }
                    WriteJson(ctx, new { ok = true, result });
                }
                catch (Exception ex)
                {
                    WriteJson(ctx, new { ok = false, error = ex.Message });
                }
            }
            catch { /* client disconnected etc — nothing to do */ }
        }

        private static readonly HttpClient ProxyHttp = new(new HttpClientHandler { UseCookies = false, AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(30) };

        // Headers HttpListenerResponse manages itself (via ContentLength64 etc.) — setting
        // these directly via the Headers collection throws or is simply wrong to forward
        // verbatim from an upstream response we're fully buffering (no chunked passthrough).
        private static readonly HashSet<string> HopByHopHeaders = new(StringComparer.OrdinalIgnoreCase)
        { "Content-Length", "Transfer-Encoding", "Connection", "Keep-Alive", "Upgrade" };

        /// <summary>
        /// Transparently reverse-proxies everything that isn't one of our own /_tool routes to the
        /// device's own stock admin portal, so the real portal — every form, table and dialog it
        /// has — is reachable at this same local port/origin instead of navigating to the device's
        /// IP directly. Cookies are passed through byte-for-byte in both directions (not managed by
        /// HttpClient's own jar) since the device only checks the cookie VALUE against its session
        /// table — it doesn't care what origin the browser associates it with.
        /// For the top-level page response specifically, injects a new "Tools" nav entry + panel
        /// into the portal's own nav (which is driven by a generic, already-present hash-to-element-id
        /// router — adding a matching &lt;li&gt;/&lt;div id="tools"&gt; pair is enough for their own
        /// script.js to show/hide it exactly like Home/WiFi/Security/Settings), embedding this app's
        /// existing dashboard via an iframe so nothing needs reimplementing or restyling to match.
        /// </summary>
        private static void ProxyToDevice(HttpListenerContext ctx, string ip)
        {
            try
            {
                string targetUrl = $"http://{ip}{ctx.Request.Url!.PathAndQuery}";
                var req = new HttpRequestMessage(new HttpMethod(ctx.Request.HttpMethod), targetUrl);
                if (ctx.Request.Headers["Cookie"] is string cookie) req.Headers.TryAddWithoutValidation("Cookie", cookie);
                if (ctx.Request.HasEntityBody)
                {
                    byte[] reqBody = ReadAllBytes(ctx.Request.InputStream);
                    req.Content = new ByteArrayContent(reqBody);
                    if (ctx.Request.ContentType != null) req.Content.Headers.TryAddWithoutValidation("Content-Type", ctx.Request.ContentType);
                }
                var resp = ProxyHttp.SendAsync(req).GetAwaiter().GetResult();
                byte[] respBody = resp.Content.ReadAsByteArrayAsync().GetAwaiter().GetResult();

                string contentType = resp.Content.Headers.ContentType?.ToString() ?? "";
                if (contentType.Contains("text/html", StringComparison.OrdinalIgnoreCase))
                {
                    string html = Encoding.UTF8.GetString(respBody);
                    string injected = InjectToolsTab(html);
                    respBody = Encoding.UTF8.GetBytes(injected);
                }

                ctx.Response.StatusCode = (int)resp.StatusCode;
                string ourOrigin = $"{ctx.Request.Url!.Scheme}://{ctx.Request.Url.Authority}";
                foreach (var h in resp.Headers)
                {
                    if (HopByHopHeaders.Contains(h.Key)) continue;
                    var values = h.Value;
                    // The device's own redirects (e.g. its sessionId-cookie handshake on first
                    // hit) use an ABSOLUTE Location pointing at itself — passed through verbatim
                    // that sends the browser straight to the device, out of the proxy entirely.
                    if (string.Equals(h.Key, "Location", StringComparison.OrdinalIgnoreCase))
                        values = values.Select(v => v.Replace($"http://{ip}", ourOrigin).Replace($"https://{ip}", ourOrigin));
                    try { ctx.Response.Headers[h.Key] = string.Join(",", values); } catch { }
                }
                foreach (var h in resp.Content.Headers)
                    if (!HopByHopHeaders.Contains(h.Key))
                        try { ctx.Response.Headers[h.Key] = string.Join(",", h.Value); } catch { }
                ctx.Response.ContentLength64 = respBody.Length;
                ctx.Response.OutputStream.Write(respBody, 0, respBody.Length);
                ctx.Response.OutputStream.Close();
            }
            catch (Exception ex)
            {
                ctx.Response.StatusCode = 502;
                WriteHtml(ctx, "proxy error talking to the device: " + ex.Message);
            }
        }

        private sealed record NavItem(string Id, string Label, string IconSvg, string PanelHtml);

        /// <summary>
        /// One top-level entry per custom capability, each a full peer of Home/WiFi/Security/
        /// Settings rather than nested under one umbrella tab — matches the stock portal's own
        /// flat top-level nav instead of adding another layer of sub-navigation. Content reuses
        /// the portal's own markup idioms (dl/dt/dd, fieldset, .block, button.section_submit) so
        /// it inherits the portal's own CSS and looks like a native page, not an embedded widget.
        /// Settings backup and firmware-file install are deliberately NOT duplicated here — those
        /// already exist on the proxied Settings → General → Software and Reset page.
        /// </summary>
        private static readonly NavItem[] CustomNavItems =
        {
            new("ac797status", "Status",
                "<path d='M3 12h4l2-7 4 14 2-7h6'/>",
                """
                <h2>Status</h2>
                <div class="block">
                <p class="pageDescription">Reachability checks this app uses: port 80 (web admin), port 23 (root telnet,
                unlocked by this app), port 5510 (OEM AT-over-TCP bridge — firewalled off Wi-Fi clients; see AT Console
                for the path that actually works).</p>
                <button type="button" class="section_submit" onclick="ac797StatusLoad()">Check</button>
                <pre class="ac797out" id="ac797_statusOut">—</pre>
                </div>
                """),
            new("ac797dx", "dx Console",
                "<rect x='2' y='4' width='20' height='16' rx='2'/><path d='M6 9l4 3-4 3M13 15h5'/>",
                """
                <h2>dx Console</h2>
                <div class="block">
                <p class="pageDescription">Direct read/write access to the device's own dx config engine over root
                telnet — the same engine the firmware uses internally for almost everything.</p>
                <fieldset><dl>
                <dt>Key</dt><dd><input type="text" id="ac797_dxKey" placeholder="group.item  e.g. General.Versions"></dd>
                <dt>Value</dt><dd><input type="text" id="ac797_dxVal" placeholder="new value (for Set)"></dd>
                </dl>
                <div class="ac797row">
                <button type="button" class="section_submit" onclick="ac797DxGet()">Get</button>
                <button type="button" class="section_submit" onclick="ac797DxSet()">Set</button>
                </div></fieldset>
                <pre class="ac797out" id="ac797_dxOut">—</pre>
                </div>
                """),
            new("ac797at", "AT Console",
                "<path d='M21 11.5a8.38 8.38 0 0 1-8.5 8.5 8.5 8.5 0 0 1-4-1L3 20l1-5.5A8.5 8.5 0 1 1 21 11.5z'/>",
                """
                <h2>AT Console</h2>
                <div class="block">
                <p class="pageDescription">Talks to the modem's AT command parser directly over the root-telnet-visible
                <code>/dev/smd7</code> channel (the TCP :5510 bridge is firewalled off Wi-Fi). Engineering commands
                need the OPENLOCK keygen unlock first.</p>
                <fieldset><dl>
                <dt>Command</dt><dd><input type="text" id="ac797_atCmd" value="ATI"></dd>
                </dl>
                <div class="ac797row">
                <button type="button" class="section_submit" onclick="ac797AtSend()">Send</button>
                <button type="button" class="section_submit" onclick="ac797AtUnlock()">AT!OPENLOCK (keygen)</button>
                </div></fieldset>
                <pre class="ac797out" id="ac797_atOut">—</pre>
                </div>
                """),
            new("ac797root", "Root",
                "<path d='M12 2l8 4v6c0 5-3.5 8.5-8 10-4.5-1.5-8-5-8-10V6l8-4z'/>",
                """
                <h2>Root</h2>
                <div class="block">
                <p class="pageDescription">Runs the OPENLOCK keygen challenge/response over AT, then sets the
                TELNETENABLE/ADBENABLE NV flags and reboots if telnet isn't already up. Safe to run repeatedly —
                a no-op once root telnet is already reachable.</p>
                <button type="button" class="section_submit" onclick="ac797RootEnsure()">Ensure root</button>
                <pre class="ac797out" id="ac797_rootOut">—</pre>
                </div>
                """),
            new("ac797offload", "Wi-Fi Offload",
                "<path d='M5 13a10 10 0 0 1 14 0M8.5 16.5a5 5 0 0 1 7 0M12 20h.01'/><path d='M12 9V2m0 0L9 5m3-3l3 3'/>",
                """
                <h2>Wi-Fi Offload</h2>
                <div class="block">
                <p class="pageDescription">Drives <code>wpa_cli -i wlan0</code> directly so the AirCard itself joins
                another access point as a client, since the stock portal's own offload fields are inert (QCMAP never
                acts on them).</p>
                <fieldset><dl>
                <dt>SSID</dt><dd><input type="text" id="ac797_offSsid"></dd>
                <dt>Password</dt><dd><input type="text" id="ac797_offPass" placeholder="blank = open"></dd>
                </dl>
                <div class="ac797row">
                <button type="button" class="section_submit" onclick="ac797OffConnect()">Connect</button>
                <button type="button" class="section_submit" onclick="ac797OffDisable()">Disable</button>
                <button type="button" class="section_submit" onclick="ac797OffStatus()">Status</button>
                </div></fieldset>
                <pre class="ac797out" id="ac797_offOut">—</pre>
                </div>
                """),
            new("ac797bridge", "Drone Bridge",
                "<path d='M10 14a5 5 0 0 0 7 0l3-3a5 5 0 0 0-7-7l-1 1'/><path d='M14 10a5 5 0 0 0-7 0l-3 3a5 5 0 0 0 7 7l1-1'/>",
                """
                <h2>Drone Bridge</h2>
                <div class="block">
                <p class="pageDescription">One-click PC &rarr; AirCard &rarr; target-AP bridge with NAT-remap to
                10.9.1.1 (handles subnet collisions a plain station join can't). Takes a bit — stages root, Wi-Fi
                standby disable, NAT push, join, and the PC-side route.</p>
                <fieldset><dl>
                <dt>Target AP SSID</dt><dd><input type="text" id="ac797_brSsid"></dd>
                </dl>
                <div class="ac797row">
                <button type="button" class="section_submit" onclick="ac797BridgeRun()">Bridge</button>
                </div></fieldset>
                <pre class="ac797out" id="ac797_brOut">—</pre>
                </div>
                """),
            new("ac797files", "Files",
                "<path d='M3 7a2 2 0 0 1 2-2h4l2 2h8a2 2 0 0 1 2 2v8a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2V7z'/>",
                """
                <h2>Files</h2>
                <div class="block">
                <p class="pageDescription">Root-telnet file browser — lists with <code>ls -la</code>, reads with
                <code>head -c 200000</code>. "Download" tars + gzips the path exactly as it sits on the device,
                capped at 200MB.</p>
                <fieldset><dl>
                <dt>Path</dt><dd><input type="text" id="ac797_fsPath" value="/"></dd>
                </dl>
                <div class="ac797row">
                <button type="button" class="section_submit" onclick="ac797FsList()">List</button>
                <button type="button" class="section_submit" onclick="ac797FsDownload()">Download (.tar.gz)</button>
                </div></fieldset>
                <div id="ac797_fsList"></div>
                <pre class="ac797out" id="ac797_fsOut">—</pre>
                </div>
                """),
            new("ac797firmware", "Firmware Backup",
                "<rect x='6' y='6' width='12' height='12' rx='1'/><path d='M9 2v3M15 2v3M9 19v3M15 19v3M2 9h3M2 15h3M19 9h3M19 15h3'/>",
                """
                <h2>Firmware Backup</h2>
                <div class="block">
                <p class="pageDescription">Reads raw NAND partitions via their read-only <code>/dev/mtdNro</code>
                nodes (never the rw node — no erase risk), gzipped on-device. Large partitions can take minutes.</p>
                <div class="ac797row">
                <button type="button" class="section_submit" onclick="ac797FwLoad()">Refresh partitions</button>
                <button type="button" class="section_submit" onclick="ac797FwZip()">Download selected (.zip)</button>
                </div>
                <div id="ac797_fwList">—</div>
                <pre class="ac797out" id="ac797_fwOut">—</pre>
                </div>
                """),
            new("ac797sms", "SMS",
                "<rect x='2' y='4' width='20' height='16' rx='2'/><path d='M2 6l10 7 10-7'/>",
                """
                <h2>SMS (AT+CMGW)</h2>
                <div class="block">
                <p class="pageDescription">Writes a message into the modem's SMS storage over AT, the same mechanism
                a real network text uses. Needs a SIM inserted — the gate is in the baseband firmware, not Linux.</p>
                <fieldset><dl>
                <dt>From / sender</dt><dd><input type="text" id="ac797_smsNum" value="12345550100"></dd>
                <dt>Message</dt><dd><input type="text" id="ac797_smsBody"></dd>
                </dl>
                <div class="ac797row">
                <button type="button" class="section_submit" onclick="ac797SmsWrite()">Write to inbox</button>
                <button type="button" class="section_submit" onclick="ac797SmsList()">List messages</button>
                </div></fieldset>
                <pre class="ac797out" id="ac797_smsOut">—</pre>
                </div>
                """),
        };

        // The stock icon font recolors itself via `color` for free (inactive vs active just
        // changes a text color); a background-image can't do that, so we bake two variants —
        // measured directly off the live portal: inactive icons render ~rgb(80,75,81), active
        // ones ~rgb(245,248,249).
        private static string IconDataUri(string svgPathsInner, string stroke) =>
            "data:image/svg+xml;base64," + Convert.ToBase64String(Encoding.UTF8.GetBytes(
                $"<svg xmlns='http://www.w3.org/2000/svg' viewBox='0 0 24 24' fill='none' stroke='{stroke}' stroke-width='2' stroke-linecap='round' stroke-linejoin='round'>{svgPathsInner}</svg>"));

        // Octicons' "mark-github" glyph (MIT-licensed), inlined so it recolors via `fill`/`currentColor`
        // like any other text instead of needing light/dark data-URI variants.
        private const string GithubMarkSvg = """
            <svg viewBox='0 0 16 16' width='20' height='20' fill='currentColor' aria-hidden='true'><path d='M8 0C3.58 0 0 3.58 0 8c0 3.54 2.29 6.53 5.47 7.59.4.07.55-.17.55-.38 0-.19-.01-.82-.01-1.49-2.01.37-2.53-.49-2.69-.94-.09-.23-.48-.94-.82-1.13-.28-.15-.68-.52-.01-.53.63-.01 1.08.58 1.23.82.72 1.21 1.87.87 2.33.66.07-.52.28-.87.51-1.07-1.78-.2-3.64-.89-3.64-3.95 0-.87.31-1.59.82-2.15-.08-.2-.36-1.02.08-2.12 0 0 .67-.21 2.2.82.64-.18 1.32-.27 2-.27.68 0 1.36.09 2 .27 1.53-1.04 2.2-.82 2.2-.82.44 1.1.16 1.92.08 2.12.51.56.82 1.27.82 2.15 0 3.07-1.87 3.75-3.65 3.95.29.25.54.73.54 1.48 0 1.07-.01 1.93-.01 2.2 0 .21.15.46.55.38A8.013 8.013 0 0 0 16 8c0-4.42-3.58-8-8-8z'/></svg>
            """;

        /// <summary>
        /// Adds one top-level nav entry + panel per <see cref="CustomNavItems"/> to the portal's
        /// own &lt;ul id="main_nav"&gt; (right after Settings) and the panel list (right before
        /// &lt;div class="footer"&gt;), each with a matching icon rule in the SAME visual "icon
        /// before label" slot the stock items use (#main_nav li a:before, 32px wide) — just a CSS
        /// background-image instead of their proprietary icon font, since that font has no glyphs
        /// for these concepts. The portal's router looks panels up by plain document.getElementById
        /// keyed off the URL hash, so a matching &lt;li&gt;/&lt;div id&gt; pair is all its existing
        /// script.js needs to show/hide these exactly like Home/WiFi/Security/Settings — no changes
        /// to their JS. No-ops per-anchor (leaves the real portal page untouched) if the expected
        /// anchor text isn't found, e.g. if the device ever changes its template.
        /// </summary>
        private static string InjectToolsTab(string html)
        {
            const string navAnchor = "<a id='nav_settings' href='#settings' role='tab' aria-selected='false' aria-controls='settings' ><span>Settings</span></a></li>";
            int navIdx = html.IndexOf(navAnchor, StringComparison.Ordinal);
            if (navIdx >= 0)
            {
                var navLis = new StringBuilder();
                foreach (var item in CustomNavItems)
                    navLis.Append($"\n\t<li role='presentation'><a id='nav_{item.Id}' href='#{item.Id}' role='tab' aria-selected='false' aria-controls='{item.Id}' ><span>{item.Label}</span></a></li>");
                html = html.Insert(navIdx + navAnchor.Length, navLis.ToString());
            }

            // Move the Wi-Fi client-count notification badge out of the WiFi sidebar item and into
            // the header bar instead, labeled (the alert-count badge was dropped per feedback —
            // redundant with this one). It's driven by the portal's own generic model-binding
            // system (elements carrying m_x classes get kept in sync by key, c_x classes similarly),
            // which indexes the WHOLE DOM by class name exactly once, right after the initial page
            // load — not per-navigation and not via a live class query. That means a duplicate bound
            // element only works if it's present in the HTML from the very first paint (as this
            // server-side injection makes it, unlike adding it via JS afterwards, which is too late
            // to be picked up). Reusing the exact c_wifi_clientCountClass/wifi_users_N tokens the
            // original's hide-when-zero convention looks for (see the matching CSS below) keeps the
            // same hide-when-empty behavior as the original.
            const string headerAnchor = "<li><a id=\"nav_power\">icon_power</a></li>";
            int headerIdx = html.IndexOf(headerAnchor, StringComparison.Ordinal);
            if (headerIdx >= 0)
            {
                const string headerBadges = """
                <li class='c_wifi_clientCountClass wifi_users_0' id='ac797_wifiBadgeLi'><span id='ac797_wifiBadgeLabel'>Wi-Fi Clients</span><span class='m_wifi_clientCount count'></span></li>
                """;
                html = html.Insert(headerIdx, headerBadges.Trim());
            }

            // Brand mark + "view the source" link, in the empty spot next to the portal's own
            // (unused on this device) logo slot. #header is the positioning context for both this
            // and the Wi-Fi badge above.
            const string logoAnchor = "<a href=\"#\"><h1></h1></a>";
            int logoIdx = html.IndexOf(logoAnchor, StringComparison.Ordinal);
            if (logoIdx >= 0)
            {
                string brand = $"""
                <div id='ac797_brand'><span id='ac797_brandText'>IBreakThingsForFun</span><a id='ac797_brandRepo' href='https://github.com/IBreakThingsForFun/NETGEAR-AirCard-797S-AC797S-Root-Firmware-Dump' target='_blank' rel='noopener noreferrer' title='View this project on GitHub'>{GithubMarkSvg}</a></div>
                """;
                html = html.Insert(logoIdx + logoAnchor.Length, brand.Trim());
            }

            // A second GitHub link, to the account's profile rather than this one repo, dropped
            // into the portal's own stock footer (kept alongside NETGEAR's own boilerplate there).
            const string footerAnchor = "<div class=\"footer\">";
            int footerIdx = html.IndexOf(footerAnchor, StringComparison.Ordinal);
            if (footerIdx >= 0)
            {
                string footerLink = $"""
                <a id='ac797_footerGithub' href='https://github.com/IBreakThingsForFun' target='_blank' rel='noopener noreferrer' title='IBreakThingsForFun on GitHub'>{GithubMarkSvg}</a>
                """;
                html = html.Insert(footerIdx + footerAnchor.Length, footerLink.Trim());
            }

            // Panels must land INSIDE #content as actual siblings of #main/#wifi/#security/etc —
            // the portal's CSS positions .panel elements relative to #content, so a panel placed
            // outside it (e.g. near the footer) is still found/toggled by the router but renders
            // in the wrong place entirely. #content's own class value is dynamic (connection
            // state), so anchor on the stable id prefix and insert right after its opening tag.
            const string panelAnchor = "<div id=\"content\"";
            int contentTagIdx = html.IndexOf(panelAnchor, StringComparison.Ordinal);
            if (contentTagIdx < 0) return html;
            int panelIdx = html.IndexOf('>', contentTagIdx);
            if (panelIdx < 0) return html;
            panelIdx += 1;

            var style = new StringBuilder("\n<style>\n.ac797out{background:#f5f5f5;border:1px solid #ccc;border-radius:4px;padding:10px;white-space:pre-wrap;word-break:break-word;max-height:360px;overflow:auto;font:12px/1.4 monospace;}\n.ac797row{margin:8px 0;display:flex;gap:8px;flex-wrap:wrap;align-items:center;}\n" +
                "#nav_wifi .count{display:none!important;}\n" +
                "#ac797_wifiBadgeLi{display:inline-block;list-style:none;vertical-align:middle;margin:14px 28px 0 0;}\n" +
                "#ac797_wifiBadgeLabel{color:#fff;font-size:13px;margin-right:6px;vertical-align:middle;}\n" +
                "#ac797_wifiBadgeLi .count{position:static!important;display:inline-block!important;width:auto!important;min-width:22px!important;height:20px!important;line-height:20px!important;text-align:center;color:#fff!important;background-color:#b30a3e!important;font-size:12px!important;font-weight:bold;border:none!important;border-radius:6px!important;box-sizing:border-box;padding:0 4px;vertical-align:middle;}\n" +
                "#ac797_wifiBadgeLi.wifi_users_0 .count{display:none;}\n" +
                "#ac797_brand{position:absolute;left:20px;top:0;height:100%;display:flex;align-items:center;gap:10px;z-index:1001;}\n" +
                "#ac797_brandText{color:#fff;font-weight:bold;font-size:15px;letter-spacing:.3px;white-space:nowrap;}\n" +
                "#ac797_brandRepo{color:#fff;opacity:.8;display:inline-flex;}\n" +
                "#ac797_brandRepo:hover{opacity:1;}\n" +
                "#ac797_footerGithub{display:inline-block;vertical-align:middle;margin-left:10px;color:#666;}\n" +
                "#ac797_footerGithub:hover{color:#333;}\n" +
                "@media only screen and (max-width:900px){#ac797_brand{left:50px;}#ac797_brandText{font-size:13px;}}\n");
            var panels = new StringBuilder();
            foreach (var item in CustomNavItems)
            {
                style.Append($"#nav_{item.Id}{{background-image:none;}}\n#nav_{item.Id}:before{{content:\"\";display:inline-block;width:32px;height:20px;margin-right:15px;vertical-align:middle;background:no-repeat center/20px url('{IconDataUri(item.IconSvg, "rgb(80,75,81)")}');}}\n#nav_{item.Id}:hover:before,\n#nav_{item.Id}.active:before{{background-image:url('{IconDataUri(item.IconSvg, "rgb(245,248,249)")}');}}\n");
                panels.Append($"\n<div id=\"{item.Id}\" class=\"panel container\" role=\"tabpanel\" aria-labelledby=\"nav_{item.Id}\">\n{item.PanelHtml}\n</div>\n");
            }
            style.Append("</style>\n");

            html = html.Insert(panelIdx, style.ToString() + panels + CustomNavScript);
            return html;
        }

        private const string CustomNavScript = """

        <script>
        async function ac797Api(path, body) {
          const r = await fetch(path, { method: body !== undefined ? 'POST' : 'GET', headers: {'Content-Type':'application/json'}, body: body !== undefined ? JSON.stringify(body) : undefined });
          return r.json();
        }
        function ac797Show(el, obj) { el.textContent = typeof obj === 'string' ? obj : JSON.stringify(obj, null, 2); }

        async function ac797StatusLoad(){
          const r = await ac797Api('/_tool/api/ports');
          ac797Show(document.getElementById('ac797_statusOut'), r.ok ? r.result : r.error);
        }

        async function ac797DxGet(){
          const r = await ac797Api('/_tool/api/dx/get', {key: document.getElementById('ac797_dxKey').value});
          ac797Show(document.getElementById('ac797_dxOut'), r.ok ? r.result : r.error);
        }
        async function ac797DxSet(){
          const r = await ac797Api('/_tool/api/dx/set', {key: document.getElementById('ac797_dxKey').value, val: document.getElementById('ac797_dxVal').value});
          ac797Show(document.getElementById('ac797_dxOut'), r.ok ? r.result : r.error);
        }

        async function ac797AtSend(){
          const r = await ac797Api('/_tool/api/at', {cmd: document.getElementById('ac797_atCmd').value});
          ac797Show(document.getElementById('ac797_atOut'), r.ok ? r.result : r.error);
        }
        async function ac797AtUnlock(){
          const r = await ac797Api('/_tool/api/unlock', {});
          ac797Show(document.getElementById('ac797_atOut'), r.ok ? r.result : r.error);
        }

        async function ac797RootEnsure(){
          document.getElementById('ac797_rootOut').textContent = 'working…';
          const r = await ac797Api('/_tool/api/ensureroot', {});
          ac797Show(document.getElementById('ac797_rootOut'), r.ok ? r.result : r.error);
        }

        async function ac797OffConnect(){
          document.getElementById('ac797_offOut').textContent = 'working…';
          const r = await ac797Api('/_tool/api/offload/connect', {ssid: document.getElementById('ac797_offSsid').value, pass: document.getElementById('ac797_offPass').value});
          ac797Show(document.getElementById('ac797_offOut'), r.ok ? r.result : r.error);
        }
        async function ac797OffDisable(){
          const r = await ac797Api('/_tool/api/offload/disable', {});
          ac797Show(document.getElementById('ac797_offOut'), r.ok ? r.result : r.error);
        }
        async function ac797OffStatus(){
          const r = await ac797Api('/_tool/api/offload/status', {});
          ac797Show(document.getElementById('ac797_offOut'), r.ok ? r.result : r.error);
        }

        async function ac797BridgeRun(){
          document.getElementById('ac797_brOut').textContent = 'working… this takes a bit';
          const r = await ac797Api('/_tool/api/bridge', {ssid: document.getElementById('ac797_brSsid').value});
          ac797Show(document.getElementById('ac797_brOut'), r.ok ? r.result : r.error);
        }

        async function ac797SmsWrite(){
          const r = await ac797Api('/_tool/api/sms/write', {number: document.getElementById('ac797_smsNum').value, body: document.getElementById('ac797_smsBody').value});
          ac797Show(document.getElementById('ac797_smsOut'), r.ok ? r.result : r.error);
        }
        async function ac797SmsList(){
          const r = await ac797Api('/_tool/api/sms/list', {});
          ac797Show(document.getElementById('ac797_smsOut'), r.ok ? r.result : r.error);
        }

        async function ac797FsList(path){
          path = path || document.getElementById('ac797_fsPath').value || '/';
          document.getElementById('ac797_fsPath').value = path;
          const r = await ac797Api('/_tool/api/fs/list', {path});
          const listEl = document.getElementById('ac797_fsList'), outEl = document.getElementById('ac797_fsOut');
          if (!r.ok) { listEl.innerHTML = ''; ac797Show(outEl, r.error); return; }
          const entries = (r.result.entries || []).slice().sort((a,b) => (b.isDir - a.isDir) || a.name.localeCompare(b.name));
          const parent = path.replace(/\/+$/, '').split('/').slice(0, -1).join('/') || '/';
          let html = path !== '/' ? `<div class="ac797row"><button type="button" class="section_submit" onclick="ac797FsList('${parent.replace(/'/g,"\\'")}')">.. (up)</button></div>` : '';
          html += entries.map(e => {
            const full = (path.replace(/\/+$/, '') || '') + '/' + e.name;
            const label = e.name + (e.isDir ? '/' : '') + (e.isLink ? ` -> ${e.linkTarget}` : '') + (e.isDir ? '' : `  (${e.size}b)`);
            const click = e.isDir ? `ac797FsList('${full.replace(/'/g,"\\'")}')` : `ac797FsRead('${full.replace(/'/g,"\\'")}')`;
            return `<div class="ac797row"><button type="button" class="section_submit" onclick="${click}">${label}</button></div>`;
          }).join('');
          listEl.innerHTML = html;
          ac797Show(outEl, '—');
        }
        function ac797FsDownload(path){
          path = path || document.getElementById('ac797_fsPath').value || '/';
          window.location = '/_tool/api/fs/download?path=' + encodeURIComponent(path);
        }
        const AC797_IMAGE_EXT = /\.(png|jpe?g|gif|bmp|webp|ico)$/i;
        async function ac797FsRead(path){
          const outEl = document.getElementById('ac797_fsOut');
          if (AC797_IMAGE_EXT.test(path)) {
            outEl.innerHTML = `-- ${path} --\n<img src="/_tool/api/fs/image?path=${encodeURIComponent(path)}" style="max-width:100%">`;
            return;
          }
          outEl.textContent = 'loading…';
          const r = await ac797Api('/_tool/api/fs/read', {path});
          ac797Show(outEl, r.ok ? `-- ${path} (${r.result.size} bytes total, showing up to 200000) --\n\n${r.result.content}` : r.error);
        }

        async function ac797FwLoad(){
          const outEl = document.getElementById('ac797_fwOut');
          outEl.textContent = 'loading…';
          const r = await ac797Api('/_tool/api/fw/list');
          if (!r.ok) { ac797Show(outEl, r.error); return; }
          const parts = r.result.partitions;
          document.getElementById('ac797_fwList').innerHTML = parts.map(p =>
            `<div class="ac797row"><label><input type="checkbox" class="ac797fwchk" value="${p.index}" checked> mtd${p.index} — ${p.name} (${(p.sizeBytes/1048576).toFixed(2)} MB)</label>
            <button type="button" class="section_submit" onclick="ac797FwOne(${p.index})">Download</button></div>`).join('');
          ac797Show(outEl, `${parts.length} partitions, ${(parts.reduce((s,p)=>s+p.sizeBytes,0)/1048576).toFixed(1)} MB total`);
        }
        function ac797FwOne(mtd){ window.location = '/_tool/api/fw/partition?mtd=' + mtd; }
        function ac797FwZip(){
          const chosen = [...document.querySelectorAll('.ac797fwchk:checked')].map(c => c.value);
          if (chosen.length === 0) { ac797Show(document.getElementById('ac797_fwOut'), 'select at least one partition'); return; }
          ac797Show(document.getElementById('ac797_fwOut'), 'starting download — can take a long time for everything, check your downloads folder…');
          window.location = '/_tool/api/fw/zip?mtds=' + chosen.join(',');
        }
        </script>

        """;

        private static object RunLog(Func<Action<string>, bool> action)
        {
            var sb = new StringBuilder();
            bool ok = action(s => sb.AppendLine(s));
            return new { done = ok, log = sb.ToString() };
        }

        private static object Ports(string ip)
        {
            static bool Open(string ip, int port)
            {
                try { using var c = new System.Net.Sockets.TcpClient(); return c.ConnectAsync(ip, port).Wait(1200) && c.Connected; }
                catch { return false; }
            }
            return new { web = Open(ip, 80), telnetRoot = Open(ip, 23), at5510 = Open(ip, 5510) };
        }

        private static async Task<object> Status(string ip, string webPass)
        {
            var w = new WebApi(ip);
            string login = await w.LoginAsync(webPass);
            string model = await w.GetModelJsonAsync();
            return new { login, summary = WebApi.Summarize(model), model };
        }

        /// <summary>
        /// "Messages from the device" — the alert list the device's own portal shows
        /// (model.json: general.systemAlertList), e.g. "SIM not ready" / "WWAN
        /// disconnected". This is device-status notifications, NOT cellular SMS —
        /// those are a separate thing (the Messaging/SMS tab) gated by a SIM.
        /// </summary>
        private static async Task<object> Messages(string ip, string webPass)
        {
            var w = new WebApi(ip);
            await w.LoginAsync(webPass);
            string model = await w.GetModelJsonAsync();
            try
            {
                using var doc = JsonDocument.Parse(model);
                var list = doc.RootElement.GetProperty("general").GetProperty("systemAlertList").GetProperty("list");
                var items = new List<object>();
                foreach (var el in list.EnumerateArray())
                {
                    if (el.ValueKind != JsonValueKind.Object || !el.TryGetProperty("description", out var d)) continue;
                    items.Add(new
                    {
                        description = d.GetString(),
                        type = el.TryGetProperty("type", out var t) ? t.GetString() : "",
                        active = el.TryGetProperty("active", out var a) ? a.GetString() : "",
                        timestamp = el.TryGetProperty("timestamp", out var ts) ? ts.GetString() : ""
                    });
                }
                return new { count = items.Count, items };
            }
            catch (Exception ex)
            {
                return new { error = "could not parse model.json: " + ex.Message, raw = model };
            }
        }

        /// <summary>Directory listing over root telnet — `ls -la`, parsed into entries.</summary>
        private static object FsList(string ip, string path)
        {
            if (string.IsNullOrWhiteSpace(path)) path = "/";
            // busybox ls colorizes names with ANSI escapes even over a plain telnet pty; strip them.
            string raw = StripAnsi(new RootShell(ip).RunCmd($"ls -la '{ShEsc(path)}' 2>&1", timeoutMs: 6000, idleGapMs: 500));
            var entries = new List<object>();
            foreach (var lineRaw in raw.Split('\n'))
            {
                string line = lineRaw.TrimEnd();
                if (line.Length == 0 || line.StartsWith("total ")) continue;
                var parts = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length < 9) continue;
                string perm = parts[0];
                string size = parts[4];
                string name = string.Join(" ", parts.Skip(8));
                string linkTarget = "";
                int arrow = name.IndexOf(" -> ", StringComparison.Ordinal);
                if (arrow >= 0) { linkTarget = name[(arrow + 4)..]; name = name[..arrow]; }
                if (name == "." || name == "..") continue;
                entries.Add(new { name, isDir = perm.StartsWith('d'), isLink = perm.StartsWith('l'), perm, size, linkTarget });
            }
            return new { path, entries, raw };
        }

        /// <summary>Reads up to ~200KB of a file over root telnet, for the file browser's viewer.</summary>
        private static object FsRead(string ip, string path)
        {
            // Two plain single-line commands, not one combined with `;` — RunCmd's echo-strip
            // only recognizes a line that exactly matches the whole command it sent, and a
            // multi-statement one-liner gets telnet-wrapped on echo, which defeats that match
            // (see AtShell.WriteSms for the same gotcha). Simplest fix: one command per call.
            var rs = new RootShell(ip);
            string size = rs.RunCmd($"wc -c < '{ShEsc(path)}' 2>&1", timeoutMs: 4000, idleGapMs: 500).Trim();
            string content = rs.RunCmd($"head -c 200000 '{ShEsc(path)}' 2>&1", timeoutMs: 8000, idleGapMs: 800);
            return new { path, size, content };
        }

        private static readonly Dictionary<string, string> ImageMime = new(StringComparer.OrdinalIgnoreCase)
        {
            [".png"] = "image/png", [".jpg"] = "image/jpeg", [".jpeg"] = "image/jpeg",
            [".gif"] = "image/gif", [".bmp"] = "image/bmp", [".webp"] = "image/webp", [".ico"] = "image/x-icon",
        };
        private static bool IsImagePath(string path) => ImageMime.ContainsKey(System.IO.Path.GetExtension(path));

        /// <summary>Reads a binary file over root telnet via `base64`, for the file browser's image preview.</summary>
        private static (byte[] bytes, string mime) FsImage(string ip, string path)
        {
            string mime = ImageMime.TryGetValue(System.IO.Path.GetExtension(path), out var m) ? m : "application/octet-stream";
            var rs = new RootShell(ip);
            string sizeStr = rs.RunCmd($"wc -c < '{ShEsc(path)}' 2>&1", timeoutMs: 4000, idleGapMs: 500).Trim();
            if (!int.TryParse(sizeStr, out int size) || size <= 0) throw new Exception($"can't stat file (got '{sizeStr}')");
            if (size > 2_000_000) throw new Exception($"file too large to preview ({size} bytes)");
            string b64 = rs.RunCmd($"base64 '{ShEsc(path)}' 2>&1", timeoutMs: 15000, idleGapMs: 1500);
            string clean = System.Text.RegularExpressions.Regex.Replace(b64, @"\s+", "");
            return (Convert.FromBase64String(clean), mime);
        }

        /// <summary>
        /// Downloads a directory (or file) exactly as it sits on the device: tars + gzips it over
        /// root telnet with `tar czf - ... | base64`, pulls the base64 back, and decodes it here.
        /// Guarded by a `du -sk` size check first since the whole thing has to cross the telnet
        /// link and sit in memory as base64 (~1.37x the gzip size) before decoding.
        /// </summary>
        private static (byte[] bytes, string filename) FsDownload(string ip, string path)
        {
            path = path.TrimEnd('/');
            if (path.Length == 0) path = "/";
            var rs = new RootShell(ip);
            string sizeStr = rs.RunCmd($"du -sk '{ShEsc(path)}' 2>&1 | awk '{{print $1}}'", timeoutMs: 8000, idleGapMs: 800).Trim();
            if (!int.TryParse(sizeStr, out int kb) || kb <= 0) throw new Exception($"can't stat path (got '{sizeStr}')");
            if (kb > 200_000) throw new Exception($"too large to download this way ({kb} KB on the device, cap is 200MB)");
            int timeoutMs = Math.Clamp(10_000 + kb * 50, 10_000, 180_000);
            // A one-liner with &&/$(...)/quotes is long enough that telnet wraps its own echo
            // across several lines, which defeats RunCmd's exact-line echo-strip and corrupts the
            // output (see FsRead's comment for the same class of bug). Instead, push the tar
            // command as a script file (PushFile's proven line-paced heredoc, used by
            // AircardRoot/DroneBridge) and run it with a short, unwrappable RunCmd line.
            string script = path == "/"
                ? "cd / && tar czf - . 2>&1"
                : $"cd \"$(dirname '{ShEsc(path)}')\" && tar czf - \"$(basename '{ShEsc(path)}')\" 2>&1";
            rs.PushFile("/tmp/ac797dl.sh", script);
            string b64 = rs.RunCmd("sh /tmp/ac797dl.sh | base64", timeoutMs: timeoutMs, idleGapMs: 2000);
            string clean = System.Text.RegularExpressions.Regex.Replace(b64, @"\s+", "");
            byte[] bytes;
            try { bytes = Convert.FromBase64String(clean); }
            catch { throw new Exception("tar/base64 failed on the device: " + b64[..Math.Min(300, b64.Length)]); }
            string name = path == "/" ? "root" : System.IO.Path.GetFileName(path);
            if (string.IsNullOrEmpty(name)) name = "download";
            return (bytes, name + ".tar.gz");
        }

        /// <summary>Parses `cat /proc/mtd` into (index, name, sizeBytes) for every raw NAND partition.</summary>
        private static List<(int index, string name, long sizeBytes)> FwPartitions(string ip)
        {
            string raw = new RootShell(ip).RunCmd("cat /proc/mtd 2>&1", timeoutMs: 6000, idleGapMs: 500);
            var parts = new List<(int index, string name, long sizeBytes)>();
            foreach (var line in raw.Split('\n'))
            {
                var m = System.Text.RegularExpressions.Regex.Match(line.Trim(),
                    @"^mtd(\d+):\s*([0-9a-fA-F]+)\s+[0-9a-fA-F]+\s+""([^""]*)""");
                if (!m.Success) continue;
                parts.Add((int.Parse(m.Groups[1].Value), m.Groups[3].Value, Convert.ToInt64(m.Groups[2].Value, 16)));
            }
            if (parts.Count == 0) throw new Exception("couldn't read /proc/mtd: " + raw[..Math.Min(200, raw.Length)]);
            return parts;
        }

        private static object FwList(string ip) =>
            new { partitions = FwPartitions(ip).Select(p => new { index = p.index, name = p.name, sizeBytes = p.sizeBytes }) };

        /// <summary>
        /// Dumps one raw NAND partition over root telnet via its READ-ONLY /dev/mtdNro char
        /// device (never the rw node — this can't trigger an erase cycle), gzipped on-device
        /// then base64'd back, same PushFile-script pattern as FsDownload (a long one-liner
        /// gets corrupted by telnet's own echo-wrapping).
        /// </summary>
        private static byte[] FwReadPartition(string ip, int mtdIndex, long sizeBytes)
        {
            var rs = new RootShell(ip);
            string script = $"cat /dev/mtd{mtdIndex}ro 2>/dev/null | gzip -1";
            rs.PushFile($"/tmp/ac797fw_{mtdIndex}.sh", script);
            int timeoutMs = (int)Math.Clamp(30_000 + sizeBytes / 1024 * 80, 30_000, 900_000);
            string b64 = rs.RunCmd($"sh /tmp/ac797fw_{mtdIndex}.sh | base64", timeoutMs: timeoutMs, idleGapMs: 3000);
            string clean = System.Text.RegularExpressions.Regex.Replace(b64, @"\s+", "");
            try { return Convert.FromBase64String(clean); }
            catch { throw new Exception($"mtd{mtdIndex} dump/base64 failed on the device: " + b64[..Math.Min(300, b64.Length)]); }
        }

        private static (byte[] bytes, string filename) FwDownloadOne(string ip, int mtdIndex)
        {
            var part = FwPartitions(ip).FirstOrDefault(p => p.index == mtdIndex);
            if (part.name == null) throw new Exception($"no mtd{mtdIndex} partition");
            byte[] gz = FwReadPartition(ip, mtdIndex, part.sizeBytes);
            return (gz, $"mtd{mtdIndex}_{part.name}.img.gz");
        }

        /// <summary>Bundles several partitions' .img.gz dumps (already gzipped) plus a manifest into one .zip.</summary>
        private static (byte[] bytes, string filename) FwDownloadZip(string ip, int[] mtds)
        {
            var all = FwPartitions(ip);
            var chosen = mtds.Length > 0 ? all.Where(p => mtds.Contains(p.index)).ToList() : all;
            if (chosen.Count == 0) throw new Exception("no matching partitions selected");
            using var ms = new System.IO.MemoryStream();
            using (var zip = new System.IO.Compression.ZipArchive(ms, System.IO.Compression.ZipArchiveMode.Create, true))
            {
                var manifest = new StringBuilder("AC797 firmware backup\n");
                foreach (var p in chosen)
                {
                    byte[] gz = FwReadPartition(ip, p.index, p.sizeBytes);
                    var entry = zip.CreateEntry($"mtd{p.index}_{p.name}.img.gz", System.IO.Compression.CompressionLevel.NoCompression);
                    using var es = entry.Open();
                    es.Write(gz, 0, gz.Length);
                    manifest.AppendLine($"mtd{p.index}\t{p.name}\t{p.sizeBytes} bytes");
                }
                var mEntry = zip.CreateEntry("manifest.txt");
                using var mes = mEntry.Open();
                byte[] mBytes = Encoding.UTF8.GetBytes(manifest.ToString());
                mes.Write(mBytes, 0, mBytes.Length);
            }
            string stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            return (ms.ToArray(), $"ac797_firmware_backup_{stamp}.zip");
        }

        /// <summary>general + fota.fwupdater sections of model.json — everything the stock Software page shows.</summary>
        private static async Task<object> SwStatus(string ip, string webPass)
        {
            var w = new WebApi(ip);
            await w.LoginAsync(webPass);
            string model = await w.GetModelJsonAsync();
            using var doc = JsonDocument.Parse(model);
            var root = doc.RootElement;
            return new
            {
                general = root.TryGetProperty("general", out var g) ? JsonSerializer.Deserialize<object>(g.GetRawText()) : null,
                fota = root.TryGetProperty("fota", out var f) ? JsonSerializer.Deserialize<object>(f.GetRawText()) : null,
            };
        }

        private static async Task<object> SwAction(string ip, string webPass, Dictionary<string, string> fields)
        {
            var w = new WebApi(ip);
            await w.LoginAsync(webPass);
            string result = await w.SetManyAsync(fields);
            return new { result };
        }

        /// <summary>The stock portal's "Download / Backup Settings" — same /dui/export.cfg the Save button links to.</summary>
        private static async Task<byte[]> SwExport(string ip, string webPass)
        {
            var w = new WebApi(ip);
            await w.LoginAsync(webPass);
            return await w.GetBytesAsync("/dui/export.cfg?save=export.cfg");
        }

        private static object SwImport(string ip, string webPass, byte[] fileBytes, string filename)
        {
            var w = new WebApi(ip);
            w.LoginAsync(webPass).GetAwaiter().GetResult();
            var (status, finalUrl) = w.PostMultipartAsync("/Forms/Import", "config", fileBytes, filename, "import=success", "import=failed").GetAwaiter().GetResult();
            return new { status, finalUrl, ok = finalUrl.Contains("import=success") };
        }

        private static object SwFwUpdate(string ip, string webPass, byte[] fileBytes, string filename)
        {
            var w = new WebApi(ip);
            w.LoginAsync(webPass).GetAwaiter().GetResult();
            var (status, finalUrl) = w.PostMultipartAsync("/Forms/fwupdate", "pkg", fileBytes, filename, "update=success", "update=failed").GetAwaiter().GetResult();
            return new { status, finalUrl, ok = finalUrl.Contains("update=success") };
        }

        private static byte[] ReadAllBytes(System.IO.Stream s)
        {
            using var ms = new System.IO.MemoryStream();
            s.CopyTo(ms);
            return ms.ToArray();
        }

        private static string ShEsc(string s) => s.Replace("'", "'\\''");
        private static string StripAnsi(string s) => System.Text.RegularExpressions.Regex.Replace(s, "\u001B\\[[0-9;]*m", "");

        // ── plumbing ─────────────────────────────────────────────────
        private static JsonElement ReadJson(HttpListenerContext ctx)
        {
            if (ctx.Request.HttpMethod != "POST") return default;
            using var reader = new StreamReader(ctx.Request.InputStream, Encoding.UTF8);
            string text = reader.ReadToEnd();
            if (string.IsNullOrWhiteSpace(text)) return default;
            return JsonDocument.Parse(text).RootElement;
        }

        private static string Str(JsonElement e, string key, string def = "") =>
            e.ValueKind == JsonValueKind.Object && e.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String
                ? v.GetString() ?? def : def;

        private static void WriteJson(HttpListenerContext ctx, object obj)
        {
            byte[] buf = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(obj));
            ctx.Response.ContentType = "application/json";
            ctx.Response.ContentLength64 = buf.Length;
            ctx.Response.OutputStream.Write(buf, 0, buf.Length);
            ctx.Response.OutputStream.Close();
        }

        private static void WriteBinary(HttpListenerContext ctx, byte[] buf, string mime)
        {
            ctx.Response.ContentType = mime;
            ctx.Response.ContentLength64 = buf.Length;
            ctx.Response.OutputStream.Write(buf, 0, buf.Length);
            ctx.Response.OutputStream.Close();
        }

        private static void WriteHtml(HttpListenerContext ctx, string html)
        {
            byte[] buf = Encoding.UTF8.GetBytes(html);
            ctx.Response.ContentType = "text/html; charset=utf-8";
            ctx.Response.ContentLength64 = buf.Length;
            ctx.Response.OutputStream.Write(buf, 0, buf.Length);
            ctx.Response.OutputStream.Close();
        }

        private const string IndexHtml = """
        <!doctype html>
        <html><head><meta charset="utf-8"><title>AC797 Control Panel</title>
        <meta name="viewport" content="width=device-width, initial-scale=1">
        <style>
          :root{--bg:#0f1115;--panel:#171a21;--line:#2a2f3a;--text:#e6e8eb;--muted:#9aa3af;--accent:#3b82f6;--ok:#22c55e;--bad:#ef4444;}
          *{box-sizing:border-box;}
          body{margin:0;background:var(--bg);color:var(--text);font:14px/1.45 system-ui,Segoe UI,Arial,sans-serif;}
          header{padding:14px 20px;border-bottom:1px solid var(--line);display:flex;align-items:center;gap:14px;flex-wrap:wrap;}
          header h1{font-size:16px;margin:0;}
          header input{background:#0b0d11;border:1px solid var(--line);color:var(--text);border-radius:6px;padding:6px 8px;}
          #dot{width:10px;height:10px;border-radius:50%;background:var(--muted);display:inline-block;}
          nav{display:flex;gap:4px;padding:10px 20px 0;border-bottom:1px solid var(--line);flex-wrap:wrap;}
          nav button{background:transparent;border:none;color:var(--muted);padding:10px 14px;cursor:pointer;border-radius:8px 8px 0 0;font-size:13px;}
          nav button.active{color:var(--text);background:var(--panel);}
          main{padding:20px;max-width:980px;margin:0 auto;}
          .tab{display:none;}
          .tab.active{display:block;}
          .card{background:var(--panel);border:1px solid var(--line);border-radius:10px;padding:16px;margin-bottom:16px;}
          .card h2{margin:0 0 10px;font-size:14px;color:var(--muted);text-transform:uppercase;letter-spacing:.04em;}
          .row{display:flex;gap:8px;flex-wrap:wrap;margin-bottom:8px;align-items:center;}
          input[type=text],input[type=password]{background:#0b0d11;border:1px solid var(--line);color:var(--text);border-radius:6px;padding:8px 10px;flex:1;min-width:140px;}
          button.act{background:var(--accent);border:none;color:#fff;padding:8px 14px;border-radius:6px;cursor:pointer;font-weight:600;}
          button.act:hover{opacity:.9;}
          button.danger{background:var(--bad);}
          pre{background:#0b0d11;border:1px solid var(--line);border-radius:8px;padding:10px;white-space:pre-wrap;word-break:break-word;max-height:360px;overflow:auto;margin:8px 0 0;}
          .hint{color:var(--muted);font-size:12px;margin-top:6px;}
          .warn{color:#f59e0b;}
        </style></head>
        <body>
        <header>
          <span id="dot"></span>
          <h1>AC797S Control Panel</h1>
          <input id="ip" type="text" value="192.168.1.1" style="width:120px">
          <input id="pass" type="password" value="7bLAKi2P" style="width:120px" placeholder="web admin pass">
          <button class="act" onclick="refreshPorts()">Refresh</button>
        </header>
        <nav id="nav"></nav>
        <main id="tabs"></main>
        <script>
        const $=s=>document.querySelector(s);
        async function api(path,body){
          const r=await fetch(path,{method:body?'POST':'GET',headers:{'Content-Type':'application/json'},body:body?JSON.stringify(body):undefined});
          return r.json();
        }
        function show(el,obj){ el.textContent = typeof obj==='string' ? obj : JSON.stringify(obj,null,2); }

        const TABS=[
          {id:'status',label:'Status'},
          {id:'dx',label:'dx Console'},
          {id:'at',label:'AT Console'},
          {id:'root',label:'Root'},
          {id:'offload',label:'Wi-Fi Offload'},
          {id:'bridge',label:'Drone Bridge'},
          {id:'messages',label:'Device Messages'},
          {id:'manager',label:'Manager URL'},
          {id:'sms',label:'SMS'},
          {id:'files',label:'Files'},
          {id:'firmware',label:'Firmware Backup'},
          {id:'software',label:'Software & Reset'},
        ];
        const nav=$('#nav'), tabs=$('#tabs');
        TABS.forEach((t,i)=>{
          const b=document.createElement('button'); b.textContent=t.label; b.onclick=()=>select(t.id);
          if(i===0) b.className='active';
          nav.appendChild(b);
          const d=document.createElement('div'); d.className='tab'+(i===0?' active':''); d.id='tab-'+t.id;
          tabs.appendChild(d);
        });
        function select(id){
          [...nav.children].forEach(b=>b.classList.remove('active'));
          [...tabs.children].forEach(d=>d.classList.remove('active'));
          TABS.forEach((t,i)=>{ if(t.id===id){ nav.children[i].classList.add('active'); } });
          $('#tab-'+id).classList.add('active');
        }

        $('#tab-status').innerHTML = `
          <div class="card"><h2>Reachability</h2>
            <p class="hint">Three TCP checks, nothing more: port 80 (the device's own web admin), port 23
            (the root telnet shell this app unlocked), port 5510 (the OEM's AT-over-TCP bridge — usually
            closed; this app talks to the modem directly instead, see the AT Console tab).</p>
            <div id="portsOut" class="row"></div></div>
          <div class="card"><h2>Device status</h2>
            <p class="hint">Logs into the same HTTP API the browser portal at 192.168.1.1 uses
            (<code>/Forms/config</code> → <code>model.json</code>) with the admin password above, and
            summarizes it. No telnet or AT involved here.</p>
            <button class="act" onclick="loadStatus()">Load status</button>
            <pre id="statusOut">—</pre>
          </div>`;
        async function refreshPorts(){
          const r = await api('/_tool/api/ports');
          const p = r.result||{};
          $('#portsOut').innerHTML = ['web','telnetRoot','at5510'].map(k=>
            `<span style="color:${p[k]?'var(--ok)':'var(--bad)'}">${k}: ${p[k]?'open':'closed'}</span>`).join(' &nbsp; ');
          $('#dot').style.background = p.telnetRoot ? 'var(--ok)' : 'var(--bad)';
        }
        async function loadStatus(){
          const r = await api('/_tool/api/status', {});
          show($('#statusOut'), r.ok ? r.result.summary + "\n\n" + r.result.login : r.error);
        }

        $('#tab-dx').innerHTML = `
          <div class="card"><h2>dx config engine</h2>
            <p class="hint"><code>dx</code> is the OEM's own config tool, running as root on the device
            over telnet. <code>dx group.item</code> reads an NV-backed setting; <code>dx -c group.item
            value</code> writes one. This is the same engine the device's firmware uses internally for
            almost everything — Wi-Fi SSID, SIM status, USB mode, and more.</p>
            <div class="row"><input id="dxKey" type="text" placeholder="group.item  e.g. General.Versions"><button class="act" onclick="dxGet()">Get</button></div>
            <div class="row"><input id="dxVal" type="text" placeholder="new value"><button class="act" onclick="dxSet()">Set</button></div>
            <pre id="dxOut">—</pre>
          </div>`;
        async function dxGet(){ const r=await api('/_tool/api/dx/get',{key:$('#dxKey').value}); show($('#dxOut'), r.ok?r.result:r.error); }
        async function dxSet(){ const r=await api('/_tool/api/dx/set',{key:$('#dxKey').value,val:$('#dxVal').value}); show($('#dxOut'), r.ok?r.result:r.error); }

        $('#tab-at').innerHTML = `
          <div class="card"><h2>AT console (direct /dev/smd7, not the flaky :5510 bridge)</h2>
            <p class="hint">The modem's AT command parser runs on the baseband processor, reachable from
            Linux root as the character device <code>/dev/smd7</code>. This opens it, writes your command
            + a carriage return, and reads the reply. Most commands (<code>ATI</code>, <code>AT+CPIN?</code>,
            <code>dx</code> reads, etc.) just work; a few engineering commands (like <code>AT!CUSTOM=...</code>
            or <code>AT!RESET</code>) are locked and need the OPENLOCK keygen unlock first, below.</p>
            <div class="row"><input id="atCmd" type="text" placeholder="AT command, e.g. ATI" value="ATI"><button class="act" onclick="atSend()">Send</button>
            <button class="act" onclick="atUnlock()">AT!OPENLOCK (keygen)</button></div>
            <pre id="atOut">—</pre>
          </div>`;
        async function atSend(){ const r=await api('/_tool/api/at',{cmd:$('#atCmd').value}); show($('#atOut'), r.ok?r.result:r.error); }
        async function atUnlock(){ const r=await api('/_tool/api/unlock',{}); show($('#atOut'), r.ok?r.result:r.error); }

        $('#tab-root').innerHTML = `
          <div class="card"><h2>Root telnet</h2>
            <p class="hint">What this button actually does, step by step: (1) sends <code>AT!OPENLOCK?</code>
            over the AT channel, which returns a random 8-byte challenge; (2) runs the Sierra MDM9x40
            OPENLOCK keygen — a fixed 16-byte key table fed through a small table-based cipher — entirely
            offline in this app to compute the matching response, no network/server involved; (3) sends
            <code>AT!OPENLOCK="&lt;response&gt;"</code> back, which unlocks the engineering AT! commands for
            this session; (4) sends <code>AT!CUSTOM="TELNETENABLE",1</code> and
            <code>AT!CUSTOM="ADBENABLE",1</code>, which set persistent NV flags; (5) if telnet wasn't already
            up, reboots the device (<code>AT!RESET</code>) so the Linux side picks the new flags up at boot.
            Already-root is detected first, so this is safe to click repeatedly — it's a no-op once telnet
            is up.</p>
            <button class="act" onclick="ensureRoot()">Ensure root (enable TELNETENABLE/ADBENABLE if needed)</button>
            <pre id="rootOut">—</pre>
          </div>`;
        async function ensureRoot(){ $('#rootOut').textContent='working…'; const r=await api('/_tool/api/ensureroot',{}); show($('#rootOut'), r.ok?r.result:r.error); }

        $('#tab-offload').innerHTML = `
          <div class="card"><h2>Wi-Fi offload — AirCard joins another AP as a client (wlan0 station)</h2>
            <p class="hint">The AirCard's own <code>Wlan.Offload.*</code> dx keys and the portal's
            "offload network" form are OEM-stubbed — they accept writes but QCMAP never acts on them.
            This instead drives <code>wpa_cli -i wlan0</code> directly (the same wpa_supplicant QCMAP
            itself runs in AP+STA mode): adds a network block for the SSID/password, enables it, and
            waits for <code>wlan0</code> to associate and get an IP from the target AP via DHCP. Root is
            required, so this ensures root first if needed.</p>
            <div class="row"><input id="offSsid" type="text" placeholder="SSID"><input id="offPass" type="text" placeholder="password (blank = open)"></div>
            <div class="row"><button class="act" onclick="offConnect()">Connect</button><button class="act danger" onclick="offDisable()">Disable</button><button class="act" onclick="offStatus()">Status</button></div>
            <pre id="offOut">—</pre>
          </div>`;
        async function offConnect(){ $('#offOut').textContent='working…'; const r=await api('/_tool/api/offload/connect',{ssid:$('#offSsid').value,pass:$('#offPass').value}); show($('#offOut'), r.ok?r.result:r.error); }
        async function offDisable(){ const r=await api('/_tool/api/offload/disable',{}); show($('#offOut'), r.ok?r.result:r.error); }
        async function offStatus(){ const r=await api('/_tool/api/offload/status',{}); show($('#offOut'), r.ok?r.result:r.error); }

        $('#tab-bridge').innerHTML = `
          <div class="card"><h2>One-click bridge — PC → AirCard → another AP (NAT-remap to 10.9.1.1)</h2>
            <p class="hint">Why this needs more than the offload tab above: the target AP (a drone, say)
            usually hands out the same subnet (192.168.100.x) the AirCard itself uses on the PC side, so a
            plain station join would collide. This stages: preflight reachability → ensure root → disable
            the three Wi-Fi standby timers so the radio doesn't sleep mid-task → push <code>multidrone.sh</code>
            (iptables 1:1 NETMAP + a fwmark policy route + MASQUERADE) so the target AP's 192.168.100.1
            answers at 10.9.1.1 instead → join the AP → verify from the AirCard side → run the matching PC
            side (<code>PcNet.Ensure</code>: a static IP + a persistent route for 10.9.0.0/16) → verify the
            PC itself can reach 10.9.1.1. Every stage fails fast and reports which one broke.</p>
            <div class="row"><input id="brSsid" type="text" placeholder="target AP SSID"><button class="act" onclick="bridgeRun()">Bridge</button></div>
            <pre id="brOut">—</pre>
          </div>`;
        async function bridgeRun(){ $('#brOut').textContent='working… this takes a bit'; const r=await api('/_tool/api/bridge',{ssid:$('#brSsid').value}); show($('#brOut'), r.ok?r.result:r.error); }

        $('#tab-sms').innerHTML = `
          <div class="card"><h2>Cellular SMS (AT+CMGW)</h2>
            <p class="hint">This is real carrier SMS — writing a message into the modem's SMS storage over
            AT, the same mechanism a text message from the network would use. It is a separate thing from
            the "Device Messages" tab. <b>Needs a SIM inserted</b> — on this unit <code>AT+CPIN?</code>
            reports "SIM not inserted", so <code>AT+CMGF</code>/<code>CMGW</code>/<code>CMGL</code> all
            answer ERROR; that gate lives in the baseband firmware, not in Linux/root, so nothing on the PC
            side can get past it — any physical SIM (even inactive/no-plan) should be enough to test it.</p>
            <div class="row"><input id="smsNum" type="text" placeholder="from/sender number" value="12345550100"></div>
            <div class="row"><input id="smsBody" type="text" placeholder="message text"></div>
            <div class="row"><button class="act" onclick="smsWrite()">Write to inbox (AT+CMGW)</button><button class="act" onclick="smsList()">List messages</button></div>
            <pre id="smsOut">—</pre>
          </div>`;
        async function smsWrite(){ const r=await api('/_tool/api/sms/write',{number:$('#smsNum').value,body:$('#smsBody').value}); show($('#smsOut'), r.ok?r.result:r.error); }
        async function smsList(){ const r=await api('/_tool/api/sms/list',{}); show($('#smsOut'), r.ok?r.result:r.error); }

        $('#tab-messages').innerHTML = `
          <div class="card"><h2>Device Messages (status alerts)</h2>
            <p class="hint">Not SMS — this is the device's own notification list, the same one the
            portal's bell/message area shows (model.json → <code>general.systemAlertList</code>), things
            like "SIM not ready" or "WWAN disconnected" raised by the firmware itself as state changes
            happen. Read-only here: these are generated by QCMAP/the modem, not something the AT/dx layer
            can inject into directly.</p>
            <button class="act" onclick="loadMessages()">Load device messages</button>
            <pre id="messagesOut">—</pre>
          </div>`;
        async function loadMessages(){
          const r = await api('/_tool/api/messages', {});
          if (!r.ok) { show($('#messagesOut'), r.error); return; }
          const items = r.result.items || [];
          $('#messagesOut').textContent = items.length === 0 ? '(no active alerts)' :
            items.map(m => `[${m.active==='true'?'ACTIVE':'cleared'}] ${m.description} — ${m.type}  (${m.timestamp})`).join('\n');
        }

        $('#tab-manager').innerHTML = `
          <div class="card"><h2>Manager URL (router.hostName)</h2>
            <p class="hint">This is the "AC797S Manager" field on the stock portal's Settings → General
            page — the name you can type instead of 192.168.1.1, shown there as e.g. "mywebui.net". That
            field is wired to a read-only computed value (<code>router.managerUrl</code>), but the real
            underlying setting — <code>router.hostName</code> — is a normal writable dx/NV key, the exact
            same mechanism every other setting in this app uses (it's even got a matching, editable input
            already sitting in the portal's own template, just commented out). Changing it here updates
            what name reaches the device; 192.168.1.1 always keeps working regardless.</p>
            <div class="row"><input id="mgrHost" type="text" placeholder="loading…" maxlength="31">
            <button class="act" onclick="mgrLoad()">Reload</button>
            <button class="act" onclick="mgrSave()">Save</button></div>
            <pre id="mgrOut">—</pre>
          </div>`;
        async function mgrLoad(){
          const r = await api('/_tool/api/dx/get', {key:'router.hostName'});
          $('#mgrHost').value = r.ok ? r.result.trim() : '';
          show($('#mgrOut'), r.ok ? 'current: ' + r.result.trim() : r.error);
        }
        async function mgrSave(){
          const v = $('#mgrHost').value.trim();
          if (!v) { show($('#mgrOut'), 'enter a name first'); return; }
          const r = await api('/_tool/api/dx/set', {key:'router.hostName', val:v});
          show($('#mgrOut'), r.ok ? `saved: ${r.result}` : r.error);
        }
        mgrLoad();

        $('#tab-files').innerHTML = `
          <div class="card"><h2>File browser (root telnet — ls / cat)</h2>
            <p class="hint">Lists directories with <code>ls -la</code> and reads files with
            <code>head -c 200000</code>, both over the same root telnet session everything else here uses.
            Read-only; file size is capped at ~200KB for the viewer so a large/binary file doesn't flood
            the page. Click a folder to open it, click a file to view it. "Download" tars + gzips the
            path below over the same root telnet session (device-side, via <code>tar</code>/<code>base64</code>)
            and saves it as a .tar.gz, exactly as it sits on the device, folders and all — capped at 200MB.</p>
            <div class="row"><input id="fsPath" type="text" value="/" placeholder="/path/to/dir"><button class="act" onclick="fsList()">List</button><button class="act" onclick="fsDownload()">Download (.tar.gz)</button></div>
            <div id="fsList"></div>
            <pre id="fsOut">—</pre>
          </div>`;
        async function fsList(path){
          path = path || $('#fsPath').value || '/';
          $('#fsPath').value = path;
          const r = await api('/_tool/api/fs/list', {path});
          if (!r.ok) { $('#fsList').innerHTML=''; show($('#fsOut'), r.error); return; }
          const entries = (r.result.entries||[]).slice().sort((a,b)=> (b.isDir-a.isDir) || a.name.localeCompare(b.name));
          const parent = path.replace(/\/+$/,'').split('/').slice(0,-1).join('/') || '/';
          let html = path !== '/' ? `<div class="row"><button class="act" onclick="fsList('${parent.replace(/'/g,"\\'")}')">.. (up)</button></div>` : '';
          html += entries.map(e=>{
            const full = (path.replace(/\/+$/,'') || '') + '/' + e.name;
            const label = e.name + (e.isDir?'/':'') + (e.isLink?` -> ${e.linkTarget}`:'') + (e.isDir?'':`  (${e.size}b)`);
            const click = e.isDir ? `fsList('${full.replace(/'/g,"\\'")}')` : `fsRead('${full.replace(/'/g,"\\'")}')`;
            return `<div class="row" style="margin:2px 0"><button class="act" style="background:${e.isDir?'#374151':'#1f2937'}" onclick="${click}">${label}</button></div>`;
          }).join('');
          $('#fsList').innerHTML = html;
          show($('#fsOut'), '—');
        }
        function fsDownload(path){
          path = path || $('#fsPath').value || '/';
          window.location = '/_tool/api/fs/download?path=' + encodeURIComponent(path);
        }
        const IMAGE_EXT = /\.(png|jpe?g|gif|bmp|webp|ico)$/i;
        async function fsRead(path){
          if (IMAGE_EXT.test(path)) {
            $('#fsOut').innerHTML = `-- ${path} --\n`;
            $('#fsOut').innerHTML += `<img src="/_tool/api/fs/image?path=${encodeURIComponent(path)}" style="max-width:100%;image-rendering:pixelated;background:#111;border:1px solid #374151">`;
            return;
          }
          show($('#fsOut'), 'loading…');
          const r = await api('/_tool/api/fs/read', {path});
          show($('#fsOut'), r.ok ? `-- ${path} (${r.result.size} bytes total, showing up to 200000) --\n\n${r.result.content}` : r.error);
        }

        $('#tab-firmware').innerHTML = `
          <div class="card"><h2>Firmware backup (raw NAND partitions)</h2>
            <p class="hint">Reads each selected partition straight off the flash via its READ-ONLY
            <code>/dev/mtdNro</code> device node (never the read-write node, so this can't trigger
            an erase), gzips it on the device, and pulls it back over the same root telnet session
            everything else here uses. Includes everything: bootloader, modem firmware, rootfs, and
            device-specific data like calibration/IMEI (persist/efs2) — leave those checked for a
            backup you could actually restore from. Large partitions (system, modem, ntgfota) can
            take several minutes each; "Download selected" can take a long time for everything.</p>
            <div class="row"><button class="act" onclick="fwLoad()">Refresh partition list</button>
            <button class="act" onclick="fwZip()">Download selected (.zip)</button></div>
            <div id="fwList">—</div>
            <pre id="fwOut">—</pre>
          </div>`;
        async function fwLoad(){
          show($('#fwOut'), 'loading…');
          const r = await api('/_tool/api/fw/list', {});
          if (!r.ok) { show($('#fwOut'), r.error); return; }
          const parts = r.result.partitions;
          $('#fwList').innerHTML = parts.map(p=>
            `<div class="row" style="margin:2px 0">
              <label><input type="checkbox" class="fwchk" value="${p.index}" checked> mtd${p.index} — ${p.name} (${(p.sizeBytes/1048576).toFixed(2)} MB)</label>
              <button class="act" onclick="fwOne(${p.index})">Download</button>
            </div>`).join('');
          show($('#fwOut'), `${parts.length} partitions, ${(parts.reduce((s,p)=>s+p.sizeBytes,0)/1048576).toFixed(1)} MB total`);
        }
        function fwOne(mtd){ window.location = '/_tool/api/fw/partition?mtd=' + mtd; }
        function fwZip(){
          const chosen = [...document.querySelectorAll('.fwchk:checked')].map(c=>c.value);
          if (chosen.length===0) { show($('#fwOut'), 'select at least one partition'); return; }
          show($('#fwOut'), 'starting download — this can take a long time for all partitions, check your downloads folder…');
          window.location = '/_tool/api/fw/zip?mtds=' + chosen.join(',');
        }
        fwLoad();

        $('#tab-software').innerHTML = `
          <div class="card"><h2>Device info</h2>
            <p class="hint">Mirrors the stock portal's Settings → General → Software and Reset page
            (<code>#settings/general/software</code>), using the same HTTP API (<code>/Forms/config</code>,
            <code>/model.json</code>) with the admin password above — no telnet involved for this tab.</p>
            <button class="act" onclick="swLoad()">Reload</button>
            <pre id="swInfo">—</pre>
          </div>
          <div class="card"><h2>Download / Backup Settings</h2>
            <p class="hint">Downloads the device's own settings export — the exact file the stock
            portal's "Save" button produces (<code>/dui/export.cfg</code>), through the same
            authenticated session.</p>
            <button class="act" onclick="swExport()">Save settings backup</button>
          </div>
          <div class="card"><h2>Restore Settings</h2>
            <p class="hint">Uploads a previously-saved settings file to the device's own import
            endpoint (<code>/Forms/Import</code>) — exactly what the stock portal's "Import" button
            does. The device restarts if it's accepted.</p>
            <div class="row"><input type="file" id="swImportFile"><button class="act" onclick="swImport()">Import</button></div>
            <pre id="swImportOut">—</pre>
          </div>
          <div class="card"><h2>Software Update</h2>
            <p class="hint">Same online update check as the stock portal (<code>fota.fwupdater.chknow</code>),
            requires the device to actually have a data connection to reach NETGEAR's update server.</p>
            <div class="row"><button class="act" onclick="swCheckUpdate()">Check for update</button>
            <button class="act" id="swStartBtn" onclick="swStartUpdate()" style="display:none">Start update</button>
            <button class="act" id="swPostponeBtn" onclick="swPostpone()" style="display:none">Postpone 4h</button></div>
            <pre id="swUpdateOut">—</pre>
          </div>
          <div class="card"><h2>Install firmware from file</h2>
            <p class="hint">Uploads a firmware package to the device's own update endpoint
            (<code>/Forms/fwupdate</code>), exactly like the stock portal's "Install from file" —
            the device restarts and flashes it. Only use a genuine NETGEAR update file for this model.</p>
            <div class="row"><input type="file" id="swFwFile"><button class="act" onclick="swFwUpdate()">Upload</button></div>
            <pre id="swFwOut">—</pre>
          </div>
          <div class="card"><h2 style="color:var(--bad)">Factory Reset</h2>
            <p class="hint">Erases ALL customizations and restores factory defaults — the same
            <code>general.factoryReset</code> key the stock portal's confirmed "Reset" button sets.
            This cannot be undone from here; back up your settings first.</p>
            <div class="row"><input id="swResetConfirm" type="text" placeholder="type RESET to enable">
            <button class="act" id="swResetBtn" onclick="swFactoryReset()" disabled style="background:var(--bad)">Factory Reset</button></div>
            <pre id="swResetOut">—</pre>
          </div>`;
        async function swLoad(){
          show($('#swInfo'), 'loading…');
          const r = await api('/_tool/api/sw/status', {});
          if (!r.ok) { show($('#swInfo'), r.error); return; }
          const g = r.result.general||{}, f = (r.result.fota||{}).fwupdater||{};
          const lastChk = f.lastChkDate ? new Date(f.lastChkDate*1000).toLocaleString() : '—';
          show($('#swInfo'),
            `Model: ${g.model}\nFW version: ${g.FWversion}\nBootloader: ${g.BLversion}\nPRI version: ${g.PRIversion}\n`+
            `Web app: ${g.webAppVersion}\nBuild date: ${g.buildDate}\n\n`+
            `Update check status: ${f.chkstatus}\nUpdate state: ${f.state}\nAvailable: ${f.available}\nLast checked: ${lastChk}`);
          $('#swStartBtn').style.display = f.available ? '' : 'none';
          $('#swPostponeBtn').style.display = (f.available && f.isPostponable) ? '' : 'none';
        }
        function swExport(){ window.location = '/_tool/api/sw/export'; }
        async function swImport(){
          const f = $('#swImportFile').files[0];
          if (!f) { show($('#swImportOut'), 'choose a file first'); return; }
          show($('#swImportOut'), 'uploading… the device will restart if this succeeds');
          const buf = await f.arrayBuffer();
          const r = await fetch('/_tool/api/sw/import?filename=' + encodeURIComponent(f.name), {method:'POST', body: buf});
          const j = await r.json();
          show($('#swImportOut'), j.ok ? JSON.stringify(j.result) : j.error);
        }
        async function swCheckUpdate(){
          show($('#swUpdateOut'), 'checking…');
          const r = await api('/_tool/api/sw/checkupdate', {});
          show($('#swUpdateOut'), r.ok ? JSON.stringify(r.result) : r.error);
          setTimeout(swLoad, 2000);
        }
        async function swStartUpdate(){
          show($('#swUpdateOut'), 'starting update…');
          const r = await api('/_tool/api/sw/startupdate', {});
          show($('#swUpdateOut'), r.ok ? JSON.stringify(r.result) : r.error);
        }
        async function swPostpone(){
          const r = await api('/_tool/api/sw/postpone', {hours:'14400'});
          show($('#swUpdateOut'), r.ok ? JSON.stringify(r.result) : r.error);
          setTimeout(swLoad, 2000);
        }
        async function swFwUpdate(){
          const f = $('#swFwFile').files[0];
          if (!f) { show($('#swFwOut'), 'choose a file first'); return; }
          show($('#swFwOut'), 'uploading… the device will restart and flash if this succeeds');
          const buf = await f.arrayBuffer();
          const r = await fetch('/_tool/api/sw/fwupdate?filename=' + encodeURIComponent(f.name), {method:'POST', body: buf});
          const j = await r.json();
          show($('#swFwOut'), j.ok ? JSON.stringify(j.result) : j.error);
        }
        $('#swResetConfirm').oninput = () => { $('#swResetBtn').disabled = $('#swResetConfirm').value !== 'RESET'; };
        async function swFactoryReset(){
          if ($('#swResetConfirm').value !== 'RESET') return;
          show($('#swResetOut'), 'resetting…');
          const r = await api('/_tool/api/sw/factoryreset', {});
          show($('#swResetOut'), r.ok ? JSON.stringify(r.result) : r.error);
        }
        swLoad();

        refreshPorts();
        </script>
        </body></html>
        """;
    }
}

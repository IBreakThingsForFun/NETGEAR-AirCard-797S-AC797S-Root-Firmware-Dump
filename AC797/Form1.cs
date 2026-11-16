using System.Text.RegularExpressions;

namespace AC797
{
    public partial class Form1 : Form
    {
        private WebApi? _web;

        public Form1()
        {
            InitializeComponent();
            Load += (_, _) => Log("OPENLOCK keygen self-test: " + (OpenLockKeygen.SelfTest() ? "PASS" : "FAIL"));
        }

        private string Ip => txtIp.Text.Trim();

        private void Log(string msg)
        {
            void Append()
            {
                txtLog.AppendText($"[{DateTime.Now:HH:mm:ss}] {msg}{Environment.NewLine}");
            }
            if (txtLog.InvokeRequired) txtLog.BeginInvoke(Append);
            else Append();
        }

        private bool RequireWeb()
        {
            if (_web != null) return true;
            Log("Not connected — click Connect first.");
            return false;
        }

        // ── Connection ───────────────────────────────────────────────
        private async void btnConnect_Click(object sender, EventArgs e)
        {
            btnConnect.Enabled = false;
            try
            {
                _web = new WebApi(Ip);
                string r = await _web.LoginAsync(txtPwd.Text);
                Log(r);
                bool admin = _web.Role.Equals("Admin", StringComparison.OrdinalIgnoreCase);
                lblConn.Text = admin ? $"Connected ({_web.Role})" : $"Login failed (role={_web.Role})";
                lblConn.ForeColor = admin ? Color.Green : Color.Firebrick;
                if (admin) await RefreshStatusAsync();
            }
            catch (Exception ex)
            {
                lblConn.Text = "Connection error";
                lblConn.ForeColor = Color.Firebrick;
                Log("Connect error: " + ex.Message);
            }
            finally { btnConnect.Enabled = true; }
        }

        // ── Status ───────────────────────────────────────────────────
        private async void btnRefresh_Click(object sender, EventArgs e) => await RefreshStatusAsync();

        private async Task RefreshStatusAsync()
        {
            if (!RequireWeb()) return;
            try
            {
                string json = await _web!.GetModelJsonAsync();
                txtStatus.Text = WebApi.Summarize(json)
                    + Environment.NewLine + "── raw model.json ──" + Environment.NewLine + json;
                Log("Status refreshed.");
            }
            catch (Exception ex) { Log("Refresh error: " + ex.Message); }
        }

        // ── Settings: hidden menu ────────────────────────────────────
        private async void btnApplyHidden_Click(object sender, EventArgs e)
        {
            if (!RequireWeb()) return;
            try { Log(await _web!.SetAsync("custom.hiddenMenuEnabled", chkHiddenMenu.Checked ? "1" : "0")); }
            catch (Exception ex) { Log("Apply error: " + ex.Message); }
        }

        // ── Settings: Wi-Fi ──────────────────────────────────────────
        private async void btnApplyWifi_Click(object sender, EventArgs e)
        {
            if (!RequireWeb()) return;
            var fields = new Dictionary<string, string>();
            if (txtWifiSsid.Text.Length > 0) fields["wifi.SSID"] = txtWifiSsid.Text;
            if (txtWifiPass.Text.Length > 0) fields["wifi.passPhrase"] = txtWifiPass.Text;
            if (fields.Count == 0) { Log("Nothing to apply (SSID/passphrase empty)."); return; }
            try { Log(await _web!.SetManyAsync(fields)); }
            catch (Exception ex) { Log("Apply Wi-Fi error: " + ex.Message); }
        }

        // ── Settings: Offload / station mode ─────────────────────────
        // One-click: ensure root (auto-enables telnet via the AT keygen if needed), then
        // join the AP as a station over QCMAP's own wpa_supplicant. This deliberately does
        // NOT touch the Wlan.Offload.* dx keys or /Forms/offloadNetwork — those are OEM-
        // stubbed dead ends that silently persist nothing (README Phase 7).
        private async void btnOffloadConnect_Click(object sender, EventArgs e)
        {
            string ssid = txtOffloadSsid.Text.Trim();
            string pass = txtOffloadPass.Text;
            bool enable = chkOffloadEnabled.Checked;
            string ip = Ip;
            btnOffloadConnect.Enabled = false;
            Log($"──────── Offload {(enable ? "connect" : "disable")} (root + wpa_cli station) ────────");
            bool ok = await Task.Run(() => OffloadStation.Connect(ip, ssid, pass, enable, Log));
            Log(ok ? "──────── Offload done ✅ ────────" : "──────── Offload stopped ⛔ (see the stage above) ────────");
            if (ok && enable) await Task.Run(() => RefreshOffloadFromDevice(ip));
            btnOffloadConnect.Enabled = true;
        }

        // Re-read the live station state (wpa_cli) after a connect and update the labels.
        private void RefreshOffloadFromDevice(string ip)
        {
            try
            {
                var sh = new RootShell(ip);
                string st = sh.RunCmd("wpa_cli -i wlan0 status 2>&1");
                string state = Field(st, "wpa_state");
                // wpa_cli status usually omits ip_address; the real STA IP comes from the
                // kernel, so read it from ifconfig (same `inet addr:` the scripts parse).
                string sip = Field(st, "ip_address");
                if (sip.Length == 0)
                {
                    string ifc = sh.RunCmd("ifconfig wlan0 2>/dev/null");
                    var m = Regex.Match(ifc, @"inet addr:\s*([0-9.]+)");
                    if (m.Success) sip = m.Groups[1].Value;
                }
                void Set()
                {
                    if (state.Length > 0) lblOffloadStatus.Text = state;
                    if (sip.Length > 0) lblStationIp.Text = sip;
                }
                if (lblOffloadStatus.InvokeRequired) lblOffloadStatus.BeginInvoke(Set); else Set();
            }
            catch (Exception ex) { Log("Offload status re-read error: " + ex.Message); }
        }

        // ── One-click bridge: PC → AirCard → another AP (proven multidrone.sh slot-1 path) ──
        private async void btnBridge_Click(object sender, EventArgs e)
        {
            string ssid = txtOffloadSsid.Text.Trim();
            if (ssid.Length == 0) { Log("STOP: enter the target AP's SSID first."); return; }
            string ip = Ip;
            btnBridge.Enabled = false;
            Log("──────── One-click bridge starting ────────");
            bool ok = await Task.Run(() => DroneBridge.Run(ip, ssid, Log));
            Log(ok ? "──────── Bridge complete ✅ ────────" : "──────── Bridge stopped ⛔ (see the stage above) ────────");
            btnBridge.Enabled = true;
        }

        private async void btnOffloadRefresh_Click(object sender, EventArgs e)
        {
            string ip = Ip;
            await Task.Run(() => RefreshOffloadFromDevice(ip));
            Log("Offload status refreshed (wlan0 station).");
        }

        // parse "key=value" lines from wpa_cli status output
        private static string Field(string text, string key)
        {
            var m = Regex.Match(text, @"^\s*" + Regex.Escape(key) + @"=(.*)$", RegexOptions.Multiline);
            return m.Success ? m.Groups[1].Value.Trim() : "";
        }

        // ── dx Console ───────────────────────────────────────────────
        private async void btnDxGet_Click(object sender, EventArgs e)
        {
            string key = txtDxKey.Text.Trim();
            await Task.Run(() =>
            {
                try { AppendDx(new RootShell(Ip).DxGet(key), $"dx {key}"); }
                catch (Exception ex) { Log("dx get error: " + ex.Message); }
            });
        }

        private async void btnDxSet_Click(object sender, EventArgs e)
        {
            string key = txtDxKey.Text.Trim();
            string val = txtDxVal.Text.Trim();
            await Task.Run(() =>
            {
                try { AppendDx(new RootShell(Ip).DxSet(key, val), $"dx -c {key} {val}"); }
                catch (Exception ex) { Log("dx set error: " + ex.Message); }
            });
        }

        private void AppendDx(string output, string cmd)
        {
            void Append() => txtDxOut.AppendText($"$ {cmd}{Environment.NewLine}{output}{Environment.NewLine}{Environment.NewLine}");
            if (txtDxOut.InvokeRequired) txtDxOut.BeginInvoke(Append); else Append();
        }

        // ── AT Console ───────────────────────────────────────────────
        private async void btnAtSend_Click(object sender, EventArgs e)
        {
            string cmd = txtAtCmd.Text.Trim();
            await Task.Run(() =>
            {
                try { AppendAt(new AtPort(Ip).Send(cmd), cmd); }
                catch (Exception ex) { Log("AT error: " + ex.Message); }
            });
        }

        private async void btnUnlock_Click(object sender, EventArgs e)
        {
            await Task.Run(() =>
            {
                try { AppendAt(new AtPort(Ip).Unlock(), "AT!OPENLOCK (keygen)"); }
                catch (Exception ex) { Log("Unlock error: " + ex.Message); }
            });
        }

        private void AppendAt(string output, string cmd)
        {
            void Append() => txtAtOut.AppendText($"$ {cmd}{Environment.NewLine}{output}{Environment.NewLine}{Environment.NewLine}");
            if (txtAtOut.InvokeRequired) txtAtOut.BeginInvoke(Append); else Append();
        }
    }
}

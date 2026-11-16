namespace AC797
{
    partial class Form1
    {
        private System.ComponentModel.IContainer components = null;

        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Windows Form Designer generated code

        private void InitializeComponent()
        {
            lblIp = new Label();
            txtIp = new TextBox();
            lblPwd = new Label();
            txtPwd = new TextBox();
            btnConnect = new Button();
            lblConn = new Label();
            tabControl = new TabControl();
            tabStatus = new TabPage();
            btnRefresh = new Button();
            txtStatus = new TextBox();
            tabSettings = new TabPage();
            gbGeneral = new GroupBox();
            chkHiddenMenu = new CheckBox();
            btnApplyHidden = new Button();
            gbWifi = new GroupBox();
            lblWSsid = new Label();
            txtWifiSsid = new TextBox();
            lblWPass = new Label();
            txtWifiPass = new TextBox();
            btnApplyWifi = new Button();
            gbOffload = new GroupBox();
            chkOffloadEnabled = new CheckBox();
            lblOSsid = new Label();
            txtOffloadSsid = new TextBox();
            lblOPass = new Label();
            txtOffloadPass = new TextBox();
            btnOffloadConnect = new Button();
            btnOffloadRefresh = new Button();
            btnBridge = new Button();
            lblOffloadStatusCap = new Label();
            lblOffloadStatus = new Label();
            lblStationIpCap = new Label();
            lblStationIp = new Label();
            lblOffloadNote = new Label();
            tabDx = new TabPage();
            lblDxKey = new Label();
            txtDxKey = new TextBox();
            lblDxVal = new Label();
            txtDxVal = new TextBox();
            btnDxGet = new Button();
            btnDxSet = new Button();
            txtDxOut = new TextBox();
            tabAt = new TabPage();
            lblAt = new Label();
            txtAtCmd = new TextBox();
            btnAtSend = new Button();
            btnUnlock = new Button();
            txtAtOut = new TextBox();
            lblLog = new Label();
            txtLog = new TextBox();
            tabControl.SuspendLayout();
            tabStatus.SuspendLayout();
            tabSettings.SuspendLayout();
            gbGeneral.SuspendLayout();
            gbWifi.SuspendLayout();
            gbOffload.SuspendLayout();
            tabDx.SuspendLayout();
            tabAt.SuspendLayout();
            SuspendLayout();
            //
            // lblIp
            //
            lblIp.AutoSize = true;
            lblIp.Location = new Point(12, 16);
            lblIp.Name = "lblIp";
            lblIp.Size = new Size(56, 17);
            lblIp.Text = "Device IP";
            //
            // txtIp
            //
            txtIp.Location = new Point(74, 13);
            txtIp.Name = "txtIp";
            txtIp.Size = new Size(115, 25);
            txtIp.TabIndex = 0;
            txtIp.Text = "192.168.1.1";
            //
            // lblPwd
            //
            lblPwd.AutoSize = true;
            lblPwd.Location = new Point(203, 16);
            lblPwd.Name = "lblPwd";
            lblPwd.Size = new Size(62, 17);
            lblPwd.Text = "Password";
            //
            // txtPwd
            //
            txtPwd.Location = new Point(271, 13);
            txtPwd.Name = "txtPwd";
            txtPwd.Size = new Size(120, 25);
            txtPwd.TabIndex = 1;
            txtPwd.Text = "7bLAKi2P";
            txtPwd.UseSystemPasswordChar = true;
            //
            // btnConnect
            //
            btnConnect.Location = new Point(400, 12);
            btnConnect.Name = "btnConnect";
            btnConnect.Size = new Size(90, 27);
            btnConnect.TabIndex = 2;
            btnConnect.Text = "Connect";
            btnConnect.UseVisualStyleBackColor = true;
            btnConnect.Click += btnConnect_Click;
            //
            // lblConn
            //
            lblConn.AutoSize = true;
            lblConn.ForeColor = Color.Firebrick;
            lblConn.Location = new Point(500, 16);
            lblConn.Name = "lblConn";
            lblConn.Size = new Size(97, 17);
            lblConn.Text = "Not connected";
            //
            // tabControl
            //
            tabControl.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            tabControl.Controls.Add(tabStatus);
            tabControl.Controls.Add(tabSettings);
            tabControl.Controls.Add(tabDx);
            tabControl.Controls.Add(tabAt);
            tabControl.Location = new Point(12, 48);
            tabControl.Name = "tabControl";
            tabControl.SelectedIndex = 0;
            tabControl.Size = new Size(816, 455);
            tabControl.TabIndex = 3;
            //
            // tabStatus
            //
            tabStatus.Controls.Add(btnRefresh);
            tabStatus.Controls.Add(txtStatus);
            tabStatus.Location = new Point(4, 26);
            tabStatus.Name = "tabStatus";
            tabStatus.Padding = new Padding(3);
            tabStatus.Size = new Size(808, 425);
            tabStatus.Text = "Status";
            tabStatus.UseVisualStyleBackColor = true;
            //
            // btnRefresh
            //
            btnRefresh.Location = new Point(8, 8);
            btnRefresh.Name = "btnRefresh";
            btnRefresh.Size = new Size(110, 27);
            btnRefresh.TabIndex = 0;
            btnRefresh.Text = "Refresh status";
            btnRefresh.UseVisualStyleBackColor = true;
            btnRefresh.Click += btnRefresh_Click;
            //
            // txtStatus
            //
            txtStatus.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            txtStatus.Font = new Font("Consolas", 9F);
            txtStatus.Location = new Point(8, 42);
            txtStatus.Multiline = true;
            txtStatus.Name = "txtStatus";
            txtStatus.ReadOnly = true;
            txtStatus.ScrollBars = ScrollBars.Both;
            txtStatus.Size = new Size(792, 375);
            txtStatus.TabIndex = 1;
            txtStatus.WordWrap = false;
            //
            // tabSettings
            //
            tabSettings.Controls.Add(gbGeneral);
            tabSettings.Controls.Add(gbWifi);
            tabSettings.Controls.Add(gbOffload);
            tabSettings.Location = new Point(4, 26);
            tabSettings.Name = "tabSettings";
            tabSettings.Padding = new Padding(3);
            tabSettings.Size = new Size(808, 425);
            tabSettings.Text = "Settings";
            tabSettings.UseVisualStyleBackColor = true;
            //
            // gbGeneral
            //
            gbGeneral.Controls.Add(chkHiddenMenu);
            gbGeneral.Controls.Add(btnApplyHidden);
            gbGeneral.Location = new Point(8, 8);
            gbGeneral.Name = "gbGeneral";
            gbGeneral.Size = new Size(792, 70);
            gbGeneral.TabStop = false;
            gbGeneral.Text = "General";
            //
            // chkHiddenMenu
            //
            chkHiddenMenu.AutoSize = true;
            chkHiddenMenu.Location = new Point(15, 30);
            chkHiddenMenu.Name = "chkHiddenMenu";
            chkHiddenMenu.Size = new Size(330, 21);
            chkHiddenMenu.Text = "Enable hidden engineering menu (LTE band-lock fields)";
            chkHiddenMenu.UseVisualStyleBackColor = true;
            //
            // btnApplyHidden
            //
            btnApplyHidden.Location = new Point(690, 26);
            btnApplyHidden.Name = "btnApplyHidden";
            btnApplyHidden.Size = new Size(90, 27);
            btnApplyHidden.Text = "Apply";
            btnApplyHidden.UseVisualStyleBackColor = true;
            btnApplyHidden.Click += btnApplyHidden_Click;
            //
            // gbWifi
            //
            gbWifi.Controls.Add(lblWSsid);
            gbWifi.Controls.Add(txtWifiSsid);
            gbWifi.Controls.Add(lblWPass);
            gbWifi.Controls.Add(txtWifiPass);
            gbWifi.Controls.Add(btnApplyWifi);
            gbWifi.Location = new Point(8, 84);
            gbWifi.Name = "gbWifi";
            gbWifi.Size = new Size(792, 90);
            gbWifi.TabStop = false;
            gbWifi.Text = "Wi-Fi access point (2.4 / 5 GHz)";
            //
            // lblWSsid
            //
            lblWSsid.AutoSize = true;
            lblWSsid.Location = new Point(15, 35);
            lblWSsid.Name = "lblWSsid";
            lblWSsid.Size = new Size(35, 17);
            lblWSsid.Text = "SSID";
            //
            // txtWifiSsid
            //
            txtWifiSsid.Location = new Point(90, 32);
            txtWifiSsid.Name = "txtWifiSsid";
            txtWifiSsid.Size = new Size(200, 25);
            //
            // lblWPass
            //
            lblWPass.AutoSize = true;
            lblWPass.Location = new Point(310, 35);
            lblWPass.Name = "lblWPass";
            lblWPass.Size = new Size(72, 17);
            lblWPass.Text = "Passphrase";
            //
            // txtWifiPass
            //
            txtWifiPass.Location = new Point(390, 32);
            txtWifiPass.Name = "txtWifiPass";
            txtWifiPass.Size = new Size(200, 25);
            //
            // btnApplyWifi
            //
            btnApplyWifi.Location = new Point(690, 30);
            btnApplyWifi.Name = "btnApplyWifi";
            btnApplyWifi.Size = new Size(90, 27);
            btnApplyWifi.Text = "Apply";
            btnApplyWifi.UseVisualStyleBackColor = true;
            btnApplyWifi.Click += btnApplyWifi_Click;
            //
            // gbOffload
            //
            gbOffload.Controls.Add(chkOffloadEnabled);
            gbOffload.Controls.Add(lblOSsid);
            gbOffload.Controls.Add(txtOffloadSsid);
            gbOffload.Controls.Add(lblOPass);
            gbOffload.Controls.Add(txtOffloadPass);
            gbOffload.Controls.Add(btnOffloadConnect);
            gbOffload.Controls.Add(btnOffloadRefresh);
            gbOffload.Controls.Add(btnBridge);
            gbOffload.Controls.Add(lblOffloadStatusCap);
            gbOffload.Controls.Add(lblOffloadStatus);
            gbOffload.Controls.Add(lblStationIpCap);
            gbOffload.Controls.Add(lblStationIp);
            gbOffload.Controls.Add(lblOffloadNote);
            gbOffload.Location = new Point(8, 180);
            gbOffload.Name = "gbOffload";
            gbOffload.Size = new Size(792, 190);
            gbOffload.TabStop = false;
            gbOffload.Text = "Wi-Fi Offload — connect the AirCard OUT to another AP as a client (station mode)";
            //
            // chkOffloadEnabled
            //
            chkOffloadEnabled.AutoSize = true;
            chkOffloadEnabled.Location = new Point(15, 28);
            chkOffloadEnabled.Name = "chkOffloadEnabled";
            chkOffloadEnabled.Size = new Size(220, 21);
            chkOffloadEnabled.Text = "Enable station / offload mode";
            chkOffloadEnabled.UseVisualStyleBackColor = true;
            //
            // lblOSsid
            //
            lblOSsid.AutoSize = true;
            lblOSsid.Location = new Point(15, 62);
            lblOSsid.Name = "lblOSsid";
            lblOSsid.Size = new Size(75, 17);
            lblOSsid.Text = "Target SSID";
            //
            // txtOffloadSsid
            //
            txtOffloadSsid.Location = new Point(110, 59);
            txtOffloadSsid.Name = "txtOffloadSsid";
            txtOffloadSsid.Size = new Size(200, 25);
            //
            // lblOPass
            //
            lblOPass.AutoSize = true;
            lblOPass.Location = new Point(330, 62);
            lblOPass.Name = "lblOPass";
            lblOPass.Size = new Size(62, 17);
            lblOPass.Text = "Password";
            //
            // txtOffloadPass
            //
            txtOffloadPass.Location = new Point(400, 59);
            txtOffloadPass.Name = "txtOffloadPass";
            txtOffloadPass.Size = new Size(190, 25);
            //
            // btnOffloadConnect
            //
            btnOffloadConnect.Location = new Point(620, 57);
            btnOffloadConnect.Name = "btnOffloadConnect";
            btnOffloadConnect.Size = new Size(160, 27);
            btnOffloadConnect.Text = "Enable + Connect";
            btnOffloadConnect.UseVisualStyleBackColor = true;
            btnOffloadConnect.Click += btnOffloadConnect_Click;
            //
            // btnOffloadRefresh
            //
            btnOffloadRefresh.Location = new Point(620, 90);
            btnOffloadRefresh.Name = "btnOffloadRefresh";
            btnOffloadRefresh.Size = new Size(160, 27);
            btnOffloadRefresh.Text = "Refresh offload status";
            btnOffloadRefresh.UseVisualStyleBackColor = true;
            btnOffloadRefresh.Click += btnOffloadRefresh_Click;
            //
            // btnBridge
            //
            btnBridge.Font = new Font("Segoe UI", 9F, FontStyle.Bold);
            btnBridge.Location = new Point(620, 123);
            btnBridge.Name = "btnBridge";
            btnBridge.Size = new Size(160, 55);
            btnBridge.Text = "One-Click Bridge:\nPC → AirCard → AP";
            btnBridge.UseVisualStyleBackColor = true;
            btnBridge.Click += btnBridge_Click;
            //
            // lblOffloadStatusCap
            //
            lblOffloadStatusCap.AutoSize = true;
            lblOffloadStatusCap.Location = new Point(15, 98);
            lblOffloadStatusCap.Name = "lblOffloadStatusCap";
            lblOffloadStatusCap.Size = new Size(48, 17);
            lblOffloadStatusCap.Text = "Status:";
            //
            // lblOffloadStatus
            //
            lblOffloadStatus.AutoSize = true;
            lblOffloadStatus.Location = new Point(90, 98);
            lblOffloadStatus.Name = "lblOffloadStatus";
            lblOffloadStatus.Size = new Size(13, 17);
            lblOffloadStatus.Text = "—";
            //
            // lblStationIpCap
            //
            lblStationIpCap.AutoSize = true;
            lblStationIpCap.Location = new Point(15, 123);
            lblStationIpCap.Name = "lblStationIpCap";
            lblStationIpCap.Size = new Size(66, 17);
            lblStationIpCap.Text = "Station IP:";
            //
            // lblStationIp
            //
            lblStationIp.AutoSize = true;
            lblStationIp.Location = new Point(90, 123);
            lblStationIp.Name = "lblStationIp";
            lblStationIp.Size = new Size(13, 17);
            lblStationIp.Text = "—";
            //
            // lblOffloadNote
            //
            lblOffloadNote.AutoSize = true;
            lblOffloadNote.ForeColor = Color.DimGray;
            lblOffloadNote.Location = new Point(15, 155);
            lblOffloadNote.Name = "lblOffloadNote";
            lblOffloadNote.Size = new Size(600, 17);
            lblOffloadNote.Text = "Connect drives wpa_cli directly (the dx Wlan.Offload.* keys are OEM-stubbed dead ends); root is enabled automatically if needed.";
            //
            // tabDx
            //
            tabDx.Controls.Add(lblDxKey);
            tabDx.Controls.Add(txtDxKey);
            tabDx.Controls.Add(lblDxVal);
            tabDx.Controls.Add(txtDxVal);
            tabDx.Controls.Add(btnDxGet);
            tabDx.Controls.Add(btnDxSet);
            tabDx.Controls.Add(txtDxOut);
            tabDx.Location = new Point(4, 26);
            tabDx.Name = "tabDx";
            tabDx.Padding = new Padding(3);
            tabDx.Size = new Size(808, 425);
            tabDx.Text = "dx Console (root)";
            tabDx.UseVisualStyleBackColor = true;
            //
            // lblDxKey
            //
            lblDxKey.AutoSize = true;
            lblDxKey.Location = new Point(8, 14);
            lblDxKey.Name = "lblDxKey";
            lblDxKey.Size = new Size(100, 17);
            lblDxKey.Text = "Key (group.item)";
            //
            // txtDxKey
            //
            txtDxKey.Location = new Point(114, 11);
            txtDxKey.Name = "txtDxKey";
            txtDxKey.Size = new Size(250, 25);
            txtDxKey.Text = "Wlan.Offload";
            //
            // lblDxVal
            //
            lblDxVal.AutoSize = true;
            lblDxVal.Location = new Point(378, 14);
            lblDxVal.Name = "lblDxVal";
            lblDxVal.Size = new Size(40, 17);
            lblDxVal.Text = "Value";
            //
            // txtDxVal
            //
            txtDxVal.Location = new Point(424, 11);
            txtDxVal.Name = "txtDxVal";
            txtDxVal.Size = new Size(150, 25);
            //
            // btnDxGet
            //
            btnDxGet.Location = new Point(585, 9);
            btnDxGet.Name = "btnDxGet";
            btnDxGet.Size = new Size(75, 27);
            btnDxGet.Text = "Get";
            btnDxGet.UseVisualStyleBackColor = true;
            btnDxGet.Click += btnDxGet_Click;
            //
            // btnDxSet
            //
            btnDxSet.Location = new Point(666, 9);
            btnDxSet.Name = "btnDxSet";
            btnDxSet.Size = new Size(75, 27);
            btnDxSet.Text = "Set";
            btnDxSet.UseVisualStyleBackColor = true;
            btnDxSet.Click += btnDxSet_Click;
            //
            // txtDxOut
            //
            txtDxOut.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            txtDxOut.Font = new Font("Consolas", 9F);
            txtDxOut.Location = new Point(8, 45);
            txtDxOut.Multiline = true;
            txtDxOut.Name = "txtDxOut";
            txtDxOut.ReadOnly = true;
            txtDxOut.ScrollBars = ScrollBars.Both;
            txtDxOut.Size = new Size(792, 372);
            txtDxOut.WordWrap = false;
            //
            // tabAt
            //
            tabAt.Controls.Add(lblAt);
            tabAt.Controls.Add(txtAtCmd);
            tabAt.Controls.Add(btnAtSend);
            tabAt.Controls.Add(btnUnlock);
            tabAt.Controls.Add(txtAtOut);
            tabAt.Location = new Point(4, 26);
            tabAt.Name = "tabAt";
            tabAt.Padding = new Padding(3);
            tabAt.Size = new Size(808, 425);
            tabAt.Text = "AT Console (:5510)";
            tabAt.UseVisualStyleBackColor = true;
            //
            // lblAt
            //
            lblAt.AutoSize = true;
            lblAt.Location = new Point(8, 14);
            lblAt.Name = "lblAt";
            lblAt.Size = new Size(84, 17);
            lblAt.Text = "AT command";
            //
            // txtAtCmd
            //
            txtAtCmd.Location = new Point(98, 11);
            txtAtCmd.Name = "txtAtCmd";
            txtAtCmd.Size = new Size(350, 25);
            txtAtCmd.Text = "ATI";
            //
            // btnAtSend
            //
            btnAtSend.Location = new Point(458, 9);
            btnAtSend.Name = "btnAtSend";
            btnAtSend.Size = new Size(80, 27);
            btnAtSend.Text = "Send";
            btnAtSend.UseVisualStyleBackColor = true;
            btnAtSend.Click += btnAtSend_Click;
            //
            // btnUnlock
            //
            btnUnlock.Location = new Point(550, 9);
            btnUnlock.Name = "btnUnlock";
            btnUnlock.Size = new Size(200, 27);
            btnUnlock.Text = "OEM Unlock (OPENLOCK)";
            btnUnlock.UseVisualStyleBackColor = true;
            btnUnlock.Click += btnUnlock_Click;
            //
            // txtAtOut
            //
            txtAtOut.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            txtAtOut.Font = new Font("Consolas", 9F);
            txtAtOut.Location = new Point(8, 45);
            txtAtOut.Multiline = true;
            txtAtOut.Name = "txtAtOut";
            txtAtOut.ReadOnly = true;
            txtAtOut.ScrollBars = ScrollBars.Both;
            txtAtOut.Size = new Size(792, 372);
            txtAtOut.WordWrap = false;
            //
            // lblLog
            //
            lblLog.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
            lblLog.AutoSize = true;
            lblLog.Location = new Point(12, 508);
            lblLog.Name = "lblLog";
            lblLog.Size = new Size(30, 17);
            lblLog.Text = "Log";
            //
            // txtLog
            //
            txtLog.Anchor = AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            txtLog.Font = new Font("Consolas", 8.5F);
            txtLog.Location = new Point(12, 528);
            txtLog.Multiline = true;
            txtLog.Name = "txtLog";
            txtLog.ReadOnly = true;
            txtLog.ScrollBars = ScrollBars.Vertical;
            txtLog.Size = new Size(816, 72);
            //
            // Form1
            //
            AutoScaleDimensions = new SizeF(7F, 17F);
            AutoScaleMode = AutoScaleMode.Font;
            ClientSize = new Size(840, 612);
            Controls.Add(lblIp);
            Controls.Add(txtIp);
            Controls.Add(lblPwd);
            Controls.Add(txtPwd);
            Controls.Add(btnConnect);
            Controls.Add(lblConn);
            Controls.Add(tabControl);
            Controls.Add(lblLog);
            Controls.Add(txtLog);
            MinimumSize = new Size(700, 500);
            Name = "Form1";
            Text = "AC797S Control Panel";
            tabControl.ResumeLayout(false);
            tabStatus.ResumeLayout(false);
            tabStatus.PerformLayout();
            tabSettings.ResumeLayout(false);
            gbGeneral.ResumeLayout(false);
            gbGeneral.PerformLayout();
            gbWifi.ResumeLayout(false);
            gbWifi.PerformLayout();
            gbOffload.ResumeLayout(false);
            gbOffload.PerformLayout();
            tabDx.ResumeLayout(false);
            tabDx.PerformLayout();
            tabAt.ResumeLayout(false);
            tabAt.PerformLayout();
            ResumeLayout(false);
            PerformLayout();
        }

        #endregion

        private Label lblIp;
        private TextBox txtIp;
        private Label lblPwd;
        private TextBox txtPwd;
        private Button btnConnect;
        private Label lblConn;
        private TabControl tabControl;
        private TabPage tabStatus;
        private Button btnRefresh;
        private TextBox txtStatus;
        private TabPage tabSettings;
        private GroupBox gbGeneral;
        private CheckBox chkHiddenMenu;
        private Button btnApplyHidden;
        private GroupBox gbWifi;
        private Label lblWSsid;
        private TextBox txtWifiSsid;
        private Label lblWPass;
        private TextBox txtWifiPass;
        private Button btnApplyWifi;
        private GroupBox gbOffload;
        private CheckBox chkOffloadEnabled;
        private Label lblOSsid;
        private TextBox txtOffloadSsid;
        private Label lblOPass;
        private TextBox txtOffloadPass;
        private Button btnOffloadConnect;
        private Button btnOffloadRefresh;
        private Button btnBridge;
        private Label lblOffloadStatusCap;
        private Label lblOffloadStatus;
        private Label lblStationIpCap;
        private Label lblStationIp;
        private Label lblOffloadNote;
        private TabPage tabDx;
        private Label lblDxKey;
        private TextBox txtDxKey;
        private Label lblDxVal;
        private TextBox txtDxVal;
        private Button btnDxGet;
        private Button btnDxSet;
        private TextBox txtDxOut;
        private TabPage tabAt;
        private Label lblAt;
        private TextBox txtAtCmd;
        private Button btnAtSend;
        private Button btnUnlock;
        private TextBox txtAtOut;
        private Label lblLog;
        private TextBox txtLog;
    }
}

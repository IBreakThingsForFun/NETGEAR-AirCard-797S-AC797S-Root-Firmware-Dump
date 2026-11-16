using System.Diagnostics;
using System.Runtime.InteropServices;

namespace AC797
{
    internal static class Program
    {
        [DllImport("kernel32.dll")]
        private static extern bool AttachConsole(int dwProcessId);
        private const int ATTACH_PARENT_PROCESS = -1;

        private const string Ip = "192.168.1.1";
        private const string Pass = "7bLAKi2P";
        private const int WebPort = 8787;

        [STAThread]
        static int Main(string[] args)
        {
            if (args.Length == 0)
            {
                ApplicationConfiguration.Initialize();
                RunTray();
                return 0;
            }

            // CLI mode: attach to the launching console so Console output is visible.
            AttachConsole(ATTACH_PARENT_PROCESS);
            try
            {
                return Cli.Run(args).GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("error: " + ex.Message);
                return 2;
            }
        }

        // No visible window — the reverse-proxy dashboard (WebUi) runs in the background,
        // reachable via a task-tray icon instead of the WinForms form (Form1 is kept as a
        // standalone, launchable-by-hand GUI, but is no longer the default entry point).
        private static void RunTray()
        {
            new Thread(() => WebUi.Run(Ip, Pass, WebPort, _ => { })) { IsBackground = true }.Start();

            using var menu = new ContextMenuStrip();
            menu.Items.Add("Open Upgraded Portal", null, (_, _) => OpenBrowser($"http://127.0.0.1:{WebPort}/"));
            menu.Items.Add("Open Original Portal", null, (_, _) => OpenBrowser($"http://{Ip}/"));
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("Exit", null, (_, _) => Application.Exit());

            using var icon = new NotifyIcon
            {
                Icon = SystemIcons.Application,
                Text = "AC797",
                ContextMenuStrip = menu,
                Visible = true,
            };
            icon.DoubleClick += (_, _) => OpenBrowser($"http://127.0.0.1:{WebPort}/");

            Application.Run();
        }

        private static void OpenBrowser(string url) =>
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
    }
}

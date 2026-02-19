using System;
using System.Drawing;
using System.Windows.Forms;
using MultiMonitorPlatform.Core;
using MultiMonitorPlatform.Interop;

namespace MultiMonitorPlatform.UI
{
    // ─────────────────────────────────────────────────────────────────────────
    //  TrayApp  –  the UI process entry point
    //  This runs alongside (or instead of) the Windows Service for the UI.
    // ─────────────────────────────────────────────────────────────────────────
    internal class TrayApp : ApplicationContext
    {
        private readonly NotifyIcon _tray;
        private ControlPanel?       _panel;

        public TrayApp()
        {
            // DPI awareness
            NativeMethods.SetProcessDpiAwareness(WinConst.PROCESS_PER_MONITOR_DPI_AWARE_V2);

            // Core init
            MonitorManager.Instance.Refresh();
            WindowTracker.Instance.RefreshAll();
            Automation.AutomationEngine.Instance.WireEvents();
            TaskbarManager.Instance.Initialize();
            TitlebarInjector.Instance.Initialize();
            Automation.AutomationEngine.Instance.OnStartup();

            // Tray icon
            _tray = new NotifyIcon
            {
                Icon    = SystemIcons.Application,
                Text    = "Multi-Monitor Platform",
                Visible = true,
            };
            _tray.DoubleClick    += (_, _) => ShowPanel();
            _tray.ContextMenuStrip = BuildTrayMenu();
        }

        private ContextMenuStrip BuildTrayMenu()
        {
            var menu = new ContextMenuStrip();

            menu.Items.Add("Open Control Panel", null, (_, _) => ShowPanel());
            menu.Items.Add(new ToolStripSeparator());

            // Quick profile submenu
            var profiles = new ToolStripMenuItem("Profiles");
            foreach (var name in ProfileManager.Instance.GetProfileNames())
            {
                var cap = name;
                profiles.DropDownItems.Add(cap, null, (_, _) => ProfileManager.Instance.Restore(cap));
            }
            profiles.DropDownItems.Add("Save Current…", null, (_, _) =>
            {
                string n = PromptString("Profile name:", "Quick Save");
                if (!string.IsNullOrEmpty(n))
                {
                    var p = ProfileManager.Instance.Capture(n);
                    ProfileManager.Instance.Save(p);
                }
            });
            menu.Items.Add(profiles);

            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("Exit", null, (_, _) =>
            {
                _tray.Visible = false;
                Application.Exit();
            });

            return menu;
        }

        private void ShowPanel()
        {
            if (_panel == null || _panel.IsDisposed)
            {
                _panel = new ControlPanel();
                _panel.FormClosed += (_, _) => _panel = null;
            }
            _panel.Show();
            _panel.BringToFront();
            NativeMethods.SetForegroundWindow(_panel.Handle);
        }

        private static string PromptString(string prompt, string def)
        {
            var form = new Form { Text = "Input", Size = new Size(280, 110), FormBorderStyle = FormBorderStyle.FixedDialog, StartPosition = FormStartPosition.CenterScreen };
            var lbl  = new Label { Text = prompt, Left = 8, Top = 8, Width = 250 };
            var txt  = new TextBox { Left = 8, Top = 26, Width = 250, Text = def };
            var ok   = new Button { Text = "OK", Left = 90, Top = 54, Width = 80, DialogResult = DialogResult.OK };
            form.Controls.AddRange(new Control[] { lbl, txt, ok });
            form.AcceptButton = ok;
            return form.ShowDialog() == DialogResult.OK ? txt.Text.Trim() : "";
        }

        [STAThread]
        public static void Main()
        {
            Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new TrayApp());
        }
    }
}

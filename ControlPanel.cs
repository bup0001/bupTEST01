using System;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using MultiMonitorPlatform.Automation;
using MultiMonitorPlatform.Core;

namespace MultiMonitorPlatform.UI
{
    // ─────────────────────────────────────────────────────────────────────────
    //  ControlPanel  –  the main settings UI (system-tray + popup window)
    // ─────────────────────────────────────────────────────────────────────────
    public class ControlPanel : Form
    {
        private TabControl _tabs = new();

        // Tabs
        private TabPage _tabMonitors   = new() { Text = "Monitors"   };
        private TabPage _tabProfiles   = new() { Text = "Profiles"   };
        private TabPage _tabWallpapers = new() { Text = "Wallpapers" };
        private TabPage _tabAutomation = new() { Text = "Automation" };
        private TabPage _tabTaskbar    = new() { Text = "Taskbar"    };
        private TabPage _tabSettings   = new() { Text = "Settings"   };

        public ControlPanel()
        {
            Text            = "Multi-Monitor Platform";
            Size            = new Size(900, 620);
            MinimumSize     = new Size(700, 500);
            StartPosition   = FormStartPosition.CenterScreen;
            FormBorderStyle = FormBorderStyle.Sizable;

            BuildLayout();
            BuildMonitorsTab();
            BuildProfilesTab();
            BuildWallpapersTab();
            BuildAutomationTab();
            BuildTaskbarTab();
            BuildSettingsTab();
        }

        // ── Layout ────────────────────────────────────────────────────────────
        private void BuildLayout()
        {
            _tabs.Dock = DockStyle.Fill;
            _tabs.TabPages.AddRange(new[] { _tabMonitors, _tabProfiles, _tabWallpapers,
                                            _tabAutomation, _tabTaskbar, _tabSettings });
            Controls.Add(_tabs);
        }

        // ── Monitors tab ──────────────────────────────────────────────────────
        private void BuildMonitorsTab()
        {
            var panel = new TableLayoutPanel
            {
                Dock = DockStyle.Fill, ColumnCount = 2,
                RowCount = 1, Padding = new Padding(8),
            };
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 60));
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 40));

            // Monitor diagram (canvas)
            var canvas = new MonitorDiagramPanel { Dock = DockStyle.Fill };

            // Details list
            var details = new ListView
            {
                Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true,
            };
            details.Columns.AddRange(new[] {
                new ColumnHeader { Text = "Monitor",  Width = 120 },
                new ColumnHeader { Text = "Size",     Width = 90  },
                new ColumnHeader { Text = "DPI",      Width = 50  },
                new ColumnHeader { Text = "Scale",    Width = 55  },
                new ColumnHeader { Text = "Primary",  Width = 55  },
            });

            foreach (var m in MonitorManager.Instance.Monitors)
                details.Items.Add(new ListViewItem(new[]
                {
                    m.DeviceName.TrimStart('\\', '.'),
                    $"{m.MonitorRect.Width}×{m.MonitorRect.Height}",
                    m.DpiX.ToString(),
                    $"{m.ScaleFactor * 100:0}%",
                    m.IsPrimary ? "✓" : "",
                }));

            var btnRefresh = new Button { Text = "Refresh", Dock = DockStyle.Bottom };
            btnRefresh.Click += (_, _) =>
            {
                MonitorManager.Instance.Refresh();
                details.Items.Clear();
                foreach (var m in MonitorManager.Instance.Monitors)
                    details.Items.Add(new ListViewItem(new[]
                    {
                        m.DeviceName.TrimStart('\\', '.'),
                        $"{m.MonitorRect.Width}×{m.MonitorRect.Height}",
                        m.DpiX.ToString(), $"{m.ScaleFactor * 100:0}%",
                        m.IsPrimary ? "✓" : "",
                    }));
            };

            var right = new Panel { Dock = DockStyle.Fill };
            right.Controls.Add(details);
            right.Controls.Add(btnRefresh);

            panel.Controls.Add(canvas, 0, 0);
            panel.Controls.Add(right,  1, 0);
            _tabMonitors.Controls.Add(panel);
        }

        // ── Profiles tab ──────────────────────────────────────────────────────
        private void BuildProfilesTab()
        {
            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Padding = new Padding(8),
            };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 40));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 60));

            var lstProfiles = new ListBox { Dock = DockStyle.Fill, IntegralHeight = false };
            foreach (var name in ProfileManager.Instance.GetProfileNames())
                lstProfiles.Items.Add(name);

            var btnPanel = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown,
                Padding = new Padding(4),
            };

            Button Btn(string text) { var b = new Button { Text = text, Width = 160, Margin = new Padding(0, 4, 0, 0) }; btnPanel.Controls.Add(b); return b; }

            var btnSave    = Btn("Save Current Layout");
            var btnRestore = Btn("Restore Selected");
            var btnDelete  = Btn("Delete Selected");
            var btnAuto    = Btn("Find Best Match");

            btnSave.Click += (_, _) =>
            {
                string name = PromptString("Profile name:", "MyProfile");
                if (string.IsNullOrWhiteSpace(name)) return;
                var p = ProfileManager.Instance.Capture(name);
                ProfileManager.Instance.Save(p);
                lstProfiles.Items.Add(name);
                MessageBox.Show($"Profile '{name}' saved.", "Saved", MessageBoxButtons.OK, MessageBoxIcon.Information);
            };

            btnRestore.Click += (_, _) =>
            {
                if (lstProfiles.SelectedItem is not string name) return;
                ProfileManager.Instance.Restore(name);
                MessageBox.Show($"Profile '{name}' restored.", "Restored", MessageBoxButtons.OK, MessageBoxIcon.Information);
            };

            btnDelete.Click += (_, _) =>
            {
                if (lstProfiles.SelectedItem is not string name) return;
                if (MessageBox.Show($"Delete '{name}'?", "Confirm", MessageBoxButtons.YesNo) == DialogResult.Yes)
                {
                    lstProfiles.Items.Remove(name);
                    // TODO: delete file
                }
            };

            btnAuto.Click += (_, _) =>
            {
                var p = ProfileManager.Instance.FindBestMatch();
                if (p == null) { MessageBox.Show("No matching profile found.", "Info"); return; }
                ProfileManager.Instance.Restore(p);
                MessageBox.Show($"Restored best match: '{p.Name}'", "Auto-Restored");
            };

            layout.Controls.Add(lstProfiles, 0, 0);
            layout.Controls.Add(btnPanel,    1, 0);
            _tabProfiles.Controls.Add(layout);
        }

        // ── Wallpapers tab ────────────────────────────────────────────────────
        private void BuildWallpapersTab()
        {
            var flow = new FlowLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(8), AutoScroll = true };

            foreach (var devPath in WallpaperManager.GetMonitorDevicePaths())
            {
                string current = WallpaperManager.GetWallpaper(devPath);
                var grp = new GroupBox
                {
                    Text   = devPath.Length > 50 ? devPath[..50] + "…" : devPath,
                    Width  = 380, Height = 100,
                    Margin = new Padding(4),
                };

                var lbl = new Label  { Text = current, Left = 8, Top = 20, Width = 280, AutoEllipsis = true };
                var btn = new Button { Text = "Browse", Left = 8, Top = 60, Width = 80  };
                var cap = devPath; // closure capture

                btn.Click += (_, _) =>
                {
                    using var dlg = new OpenFileDialog
                    {
                        Title  = "Select Wallpaper",
                        Filter = "Images|*.jpg;*.jpeg;*.png;*.bmp;*.gif;*.webp",
                    };
                    if (dlg.ShowDialog() == DialogResult.OK)
                    {
                        WallpaperManager.SetWallpaper(cap, dlg.FileName);
                        lbl.Text = dlg.FileName;
                    }
                };

                grp.Controls.Add(lbl);
                grp.Controls.Add(btn);
                flow.Controls.Add(grp);
            }

            _tabWallpapers.Controls.Add(flow);
        }

        // ── Automation tab ────────────────────────────────────────────────────
        private void BuildAutomationTab()
        {
            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Padding = new Padding(8),
            };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));

            var lstRules = new ListView
            {
                Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, CheckBoxes = true,
            };
            lstRules.Columns.AddRange(new[]
            {
                new ColumnHeader { Text = "Rule Name",  Width = 160 },
                new ColumnHeader { Text = "Trigger",    Width = 120 },
                new ColumnHeader { Text = "Actions",    Width = 60  },
                new ColumnHeader { Text = "Last Fired", Width = 120 },
            });

            void RefreshRules()
            {
                lstRules.Items.Clear();
                foreach (var r in AutomationEngine.Instance.Rules)
                {
                    var item = new ListViewItem(r.Name)    { Checked = r.Enabled };
                    item.SubItems.Add(r.Trigger.Type.ToString());
                    item.SubItems.Add(r.Actions.Count.ToString());
                    item.SubItems.Add(r.LastFired?.ToString("HH:mm:ss") ?? "—");
                    item.Tag = r.Id;
                    lstRules.Items.Add(item);
                }
            }
            RefreshRules();

            lstRules.ItemChecked += (_, e) =>
            {
                if (e.Item.Tag is Guid id)
                {
                    var rule = AutomationEngine.Instance.Rules.FirstOrDefault(r => r.Id == id);
                    if (rule != null) { rule.Enabled = e.Item.Checked; AutomationEngine.Instance.SaveRules(); }
                }
            };

            var btnPanel = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, Padding = new Padding(4),
            };
            Button RBtn(string t) { var b = new Button { Text = t, Width = 160, Margin = new Padding(0, 4, 0, 0) }; btnPanel.Controls.Add(b); return b; }

            var btnNew   = RBtn("New Rule…");
            var btnEdit  = RBtn("Edit Selected…");
            var btnDel   = RBtn("Delete Selected");
            var btnFire  = RBtn("Fire Now (test)");

            btnNew.Click += (_, _) =>
            {
                using var dlg = new RuleEditorDialog();
                if (dlg.ShowDialog() == DialogResult.OK && dlg.Rule != null)
                {
                    AutomationEngine.Instance.AddRule(dlg.Rule);
                    RefreshRules();
                }
            };

            btnDel.Click += (_, _) =>
            {
                if (lstRules.SelectedItems.Count == 0) return;
                if (lstRules.SelectedItems[0].Tag is Guid id)
                {
                    AutomationEngine.Instance.RemoveRule(id);
                    RefreshRules();
                }
            };

            btnFire.Click += (_, _) =>
            {
                if (lstRules.SelectedItems.Count == 0) return;
                if (lstRules.SelectedItems[0].Tag is Guid id)
                {
                    var rule = AutomationEngine.Instance.Rules.FirstOrDefault(r => r.Id == id);
                    if (rule != null)
                        _ = Task.Run(() => AutomationEngine.Instance.Fire(
                            new TriggerContext { Type = rule.Trigger.Type }));
                }
            };

            layout.Controls.Add(lstRules, 0, 0);
            layout.Controls.Add(btnPanel, 1, 0);
            _tabAutomation.Controls.Add(layout);
        }

        // ── Taskbar tab ───────────────────────────────────────────────────────
        private void BuildTaskbarTab()
        {
            var p = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, Padding = new Padding(12) };

            p.Controls.Add(new Label { Text = "Per-monitor taskbar options:", AutoSize = true });
            var chkEnabled  = new CheckBox { Text = "Enable per-monitor taskbars", Checked = true, AutoSize = true };
            var chkPinned   = new CheckBox { Text = "Show pinned apps", AutoSize = true };
            var chkClock    = new CheckBox { Text = "Show clock on each taskbar", Checked = true, AutoSize = true };
            var numHeight   = new NumericUpDown { Value = 40, Minimum = 28, Maximum = 80, Width = 80 };
            p.Controls.Add(chkEnabled);
            p.Controls.Add(chkPinned);
            p.Controls.Add(chkClock);
            p.Controls.Add(new Label { Text = "Taskbar height (px):", AutoSize = true });
            p.Controls.Add(numHeight);

            p.Controls.Add(new Label { Text = "\nTitlebar button injection:", AutoSize = true });
            var chkTitlebar = new CheckBox { Text = "Inject 'Move to monitor' button", Checked = true, AutoSize = true };
            var chkPin      = new CheckBox { Text = "Inject 'Pin on top' button", Checked = true, AutoSize = true };
            p.Controls.Add(chkTitlebar);
            p.Controls.Add(chkPin);

            _tabTaskbar.Controls.Add(p);
        }

        // ── Settings tab ──────────────────────────────────────────────────────
        private void BuildSettingsTab()
        {
            var p = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, Padding = new Padding(12) };

            p.Controls.Add(new Label { Text = "General settings:", AutoSize = true });
            var chkStartup = new CheckBox { Text = "Start with Windows", AutoSize = true };
            var chkTray    = new CheckBox { Text = "Minimise to system tray", Checked = true, AutoSize = true };
            var chkAutoProfile = new CheckBox { Text = "Auto-apply profile on monitor change", Checked = true, AutoSize = true };
            var chkSnapPreview = new CheckBox { Text = "Show snap zone preview", Checked = true, AutoSize = true };

            chkStartup.CheckedChanged += (_, _) => SetStartupRegistry(chkStartup.Checked);

            p.Controls.Add(chkStartup);
            p.Controls.Add(chkTray);
            p.Controls.Add(chkAutoProfile);
            p.Controls.Add(chkSnapPreview);

            p.Controls.Add(new Label { Text = "\nLog file:", AutoSize = true });
            var btnOpenLog = new Button { Text = "Open Log…", AutoSize = true };
            btnOpenLog.Click += (_, _) =>
            {
                string log = System.IO.Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "MultiMonitorPlatform", "mmp.log");
                if (System.IO.File.Exists(log))
                    System.Diagnostics.Process.Start("notepad.exe", log);
            };
            p.Controls.Add(btnOpenLog);

            _tabSettings.Controls.Add(p);
        }

        // ── Helpers ───────────────────────────────────────────────────────────
        private static string PromptString(string prompt, string def)
        {
            var form  = new Form { Text = "Input", Size = new Size(300, 130), StartPosition = FormStartPosition.CenterParent, FormBorderStyle = FormBorderStyle.FixedDialog, MaximizeBox = false };
            var lbl   = new Label { Text = prompt, Left = 10, Top = 10, Width = 260 };
            var txt   = new TextBox { Left = 10, Top = 30, Width = 260, Text = def };
            var ok    = new Button { Text = "OK",     Left = 100, Top = 60, DialogResult = DialogResult.OK };
            var cancel= new Button { Text = "Cancel", Left = 185, Top = 60, DialogResult = DialogResult.Cancel };
            form.Controls.AddRange(new Control[] { lbl, txt, ok, cancel });
            form.AcceptButton = ok;
            return form.ShowDialog() == DialogResult.OK ? txt.Text : "";
        }

        private static void SetStartupRegistry(bool enable)
        {
            using var key = Microsoft.Win32.Registry.CurrentUser
                .OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", true);
            if (key == null) return;
            if (enable)
                key.SetValue("MultiMonitorPlatform", $"\"{System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName}\"");
            else
                key.DeleteValue("MultiMonitorPlatform", throwOnMissingValue: false);
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  MonitorDiagramPanel  –  visual representation of monitor layout
    // ─────────────────────────────────────────────────────────────────────────
    internal class MonitorDiagramPanel : Panel
    {
        public MonitorDiagramPanel()
        {
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint, true);
            BackColor = Color.FromArgb(20, 20, 20);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            var g = e.Graphics;
            var monitors = MonitorManager.Instance.Monitors;
            if (monitors.Count == 0) return;

            // Compute virtual desktop bounds
            int minX = int.MaxValue, minY = int.MaxValue, maxX = int.MinValue, maxY = int.MinValue;
            foreach (var m in monitors)
            {
                minX = Math.Min(minX, m.MonitorRect.Left);
                minY = Math.Min(minY, m.MonitorRect.Top);
                maxX = Math.Max(maxX, m.MonitorRect.Right);
                maxY = Math.Max(maxY, m.MonitorRect.Bottom);
            }

            float vw = maxX - minX;
            float vh = maxY - minY;
            float pad = 20;
            float scaleX = (Width  - pad * 2) / vw;
            float scaleY = (Height - pad * 2) / vh;
            float scale  = Math.Min(scaleX, scaleY);

            foreach (var m in monitors)
            {
                float x = pad + (m.MonitorRect.Left - minX) * scale;
                float y = pad + (m.MonitorRect.Top  - minY) * scale;
                float w = m.MonitorRect.Width  * scale;
                float h = m.MonitorRect.Height * scale;

                var rect = new RectangleF(x, y, w, h);
                g.FillRectangle(new SolidBrush(m.IsPrimary ? Color.FromArgb(40, 80, 140) : Color.FromArgb(50, 50, 50)), rect);
                g.DrawRectangle(Pens.SteelBlue, x, y, w, h);

                string label = $"{m.DeviceName.TrimStart('\\', '.')}\n{m.MonitorRect.Width}×{m.MonitorRect.Height}\n{m.DpiX} DPI";
                g.DrawString(label, new Font("Segoe UI", 7), Brushes.White, x + 4, y + 4);
            }
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  RuleEditorDialog – simple dialog to create an automation rule
    // ─────────────────────────────────────────────────────────────────────────
    public class RuleEditorDialog : Form
    {
        public AutomationRule? Rule { get; private set; }

        public RuleEditorDialog()
        {
            Text            = "New Automation Rule";
            Size            = new Size(500, 400);
            StartPosition   = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox     = false;

            var flow = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, Padding = new Padding(10) };

            flow.Controls.Add(new Label { Text = "Rule name:", AutoSize = true });
            var txtName = new TextBox { Width = 440, Text = "My Rule" };
            flow.Controls.Add(txtName);

            flow.Controls.Add(new Label { Text = "Trigger:", AutoSize = true });
            var cmbTrigger = new ComboBox { Width = 440, DropDownStyle = ComboBoxStyle.DropDownList };
            foreach (TriggerType t in Enum.GetValues<TriggerType>()) cmbTrigger.Items.Add(t);
            cmbTrigger.SelectedIndex = 0;
            flow.Controls.Add(cmbTrigger);

            flow.Controls.Add(new Label { Text = "Action:", AutoSize = true });
            var cmbAction = new ComboBox { Width = 440, DropDownStyle = ComboBoxStyle.DropDownList };
            foreach (ActionType a in Enum.GetValues<ActionType>()) cmbAction.Items.Add(a);
            cmbAction.SelectedIndex = 0;
            flow.Controls.Add(cmbAction);

            flow.Controls.Add(new Label { Text = "Profile name (if RestoreProfile):", AutoSize = true });
            var txtProfile = new TextBox { Width = 440 };
            flow.Controls.Add(txtProfile);

            var ok = new Button { Text = "Create Rule", DialogResult = DialogResult.OK, Width = 120 };
            ok.Click += (_, _) =>
            {
                Rule = new AutomationRule
                {
                    Name    = txtName.Text,
                    Trigger = new TriggerDef { Type = (TriggerType)cmbTrigger.SelectedItem! },
                    Actions = new()
                    {
                        new ActionDef
                        {
                            Type        = (ActionType)cmbAction.SelectedItem!,
                            ProfileName = txtProfile.Text,
                        }
                    }
                };
            };
            flow.Controls.Add(ok);
            Controls.Add(flow);
            AcceptButton = ok;
        }
    }
}

// Task import
using System.Threading.Tasks;

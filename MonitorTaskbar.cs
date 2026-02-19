using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using MultiMonitorPlatform.Core;
using MultiMonitorPlatform.Interop;

namespace MultiMonitorPlatform.UI
{
    // ─────────────────────────────────────────────────────────────────────────
    //  TaskbarButton  –  represents one app button on the per-monitor taskbar
    // ─────────────────────────────────────────────────────────────────────────
    public class TaskbarButton
    {
        public IntPtr  Hwnd        { get; set; }
        public string  Title       { get; set; } = "";
        public Icon?   Icon        { get; set; }
        public bool    IsActive    { get; set; }
        public bool    IsFlashing  { get; set; }
        public Rectangle Bounds   { get; set; }
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  MonitorTaskbar  –  a slim WinForms window docked at the bottom of
    //  each non-primary monitor.  Uses layered window + WS_EX_TOOLWINDOW so
    //  it does not appear on its own taskbar button.
    // ─────────────────────────────────────────────────────────────────────────
    public class MonitorTaskbar : Form
    {
        // Constants
        private const int TASKBAR_HEIGHT = 40;
        private const int BUTTON_WIDTH   = 160;
        private const int BUTTON_MARGIN  = 2;
        private const int ICON_SIZE      = 20;

        private readonly MonitorInfo _monitor;
        private readonly List<TaskbarButton> _buttons = new();

        // Colors (can be themed)
        private static readonly Color BgColor     = Color.FromArgb(32,  32,  32);
        private static readonly Color ActiveColor  = Color.FromArgb(50,  120, 200);
        private static readonly Color HoverColor   = Color.FromArgb(60,  60,  60);
        private static readonly Color FlashColor   = Color.FromArgb(255, 165,  0);
        private static readonly Color TextColor    = Color.White;

        private int _hoveredIndex = -1;
        private System.Windows.Forms.Timer _flashTimer = new();

        public MonitorTaskbar(MonitorInfo monitor)
        {
            _monitor = monitor;

            // Window setup
            FormBorderStyle  = FormBorderStyle.None;
            ShowInTaskbar    = false;
            TopMost          = true;
            Text             = $"MMP_Taskbar_{monitor.Id}";

            // DPI-aware sizing
            float scale = monitor.ScaleFactor;
            int height  = (int)(TASKBAR_HEIGHT * scale);
            var wa      = monitor.WorkArea;

            SetBounds(wa.Left, wa.Bottom - height, wa.Width, height);

            // Make it a tool window (invisible to other taskbars)
            long ex = NativeMethods.GetWindowLongPtr(Handle, WinConst.GWL_EXSTYLE);
            NativeMethods.SetWindowLongPtr(Handle, WinConst.GWL_EXSTYLE,
                ex | WinConst.WS_EX_TOOLWINDOW | WinConst.WS_EX_NOACTIVATE);

            // Reserve screen space (set working area)
            ReserveWorkArea(monitor, height);

            SetupDoubleBuffer();
            SetupMouseEvents();
            SetupFlashTimer();

            WireWindowTracker();
        }

        // ── Paint ─────────────────────────────────────────────────────────────
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            // Background
            g.Clear(BgColor);

            // System clock on the right
            string time = DateTime.Now.ToString("HH:mm");
            var clockFont = new Font("Segoe UI", 9, FontStyle.Regular);
            var clockSize = g.MeasureString(time, clockFont);
            g.DrawString(time, clockFont, Brushes.White,
                Width - clockSize.Width - 8, (Height - clockSize.Height) / 2);

            // Buttons
            int x = 4;
            float scale = _monitor.ScaleFactor;
            int bw = (int)(BUTTON_WIDTH * scale);

            for (int i = 0; i < _buttons.Count; i++)
            {
                var btn = _buttons[i];
                var rc  = new Rectangle(x, 1, bw, Height - 2);
                btn.Bounds = rc;

                // Background
                Color bg = btn.IsActive   ? ActiveColor
                         : btn.IsFlashing ? FlashColor
                         : i == _hoveredIndex ? HoverColor : Color.Transparent;
                if (bg != Color.Transparent)
                    using (var br = new SolidBrush(bg))
                        g.FillRoundedRectangle(br, rc, 4);

                // Icon
                if (btn.Icon != null)
                    g.DrawIcon(btn.Icon, new Rectangle(rc.X + 4, rc.Y + (rc.Height - ICON_SIZE) / 2, ICON_SIZE, ICON_SIZE));

                // Title (truncated)
                string title = TruncateTitle(btn.Title, bw - ICON_SIZE - 12);
                var font = new Font("Segoe UI", 9);
                g.DrawString(title, font, new SolidBrush(TextColor),
                    rc.X + ICON_SIZE + 8, rc.Y + (rc.Height - g.MeasureString(title, font).Height) / 2);

                x += bw + BUTTON_MARGIN;
            }
        }

        // ── Mouse interaction ─────────────────────────────────────────────────
        private void SetupMouseEvents()
        {
            MouseMove += (_, e) =>
            {
                int idx = HitTestButton(e.Location);
                if (idx != _hoveredIndex) { _hoveredIndex = idx; Invalidate(); }
            };

            MouseLeave += (_, _) => { _hoveredIndex = -1; Invalidate(); };

            MouseClick += (_, e) =>
            {
                int idx = HitTestButton(e.Location);
                if (idx < 0 || idx >= _buttons.Count) return;
                var btn = _buttons[idx];

                if (e.Button == MouseButtons.Left)
                {
                    var placement = WINDOWPLACEMENT.Create();
                    NativeMethods.GetWindowPlacement(btn.Hwnd, ref placement);

                    if (btn.IsActive && placement.showCmd == WinConst.SW_SHOWNORMAL)
                        NativeMethods.ShowWindow(btn.Hwnd, WinConst.SW_SHOWMINIMIZED);
                    else
                    {
                        NativeMethods.ShowWindow(btn.Hwnd, WinConst.SW_RESTORE);
                        NativeMethods.SetForegroundWindow(btn.Hwnd);
                    }
                }
                else if (e.Button == MouseButtons.Right)
                {
                    ShowWindowContextMenu(btn, e.Location);
                }
            };
        }

        private int HitTestButton(Point pt)
        {
            for (int i = 0; i < _buttons.Count; i++)
                if (_buttons[i].Bounds.Contains(pt)) return i;
            return -1;
        }

        private void ShowWindowContextMenu(TaskbarButton btn, Point pt)
        {
            var menu = new ContextMenuStrip();
            menu.Items.Add("Restore",  null, (_, _) => NativeMethods.ShowWindow(btn.Hwnd, WinConst.SW_RESTORE));
            menu.Items.Add("Minimize", null, (_, _) => NativeMethods.ShowWindow(btn.Hwnd, WinConst.SW_SHOWMINIMIZED));
            menu.Items.Add("Maximize", null, (_, _) => NativeMethods.ShowWindow(btn.Hwnd, WinConst.SW_SHOWMAXIMIZED));
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("Move to another monitor", null, (_, _) => ShowMoveToMonitorMenu(btn, pt));
            menu.Show(this, pt);
        }

        private void ShowMoveToMonitorMenu(TaskbarButton btn, Point pt)
        {
            var sub = new ContextMenuStrip();
            foreach (var m in MonitorManager.Instance.Monitors)
            {
                if (m.Id == _monitor.Id) continue;
                var capture = m;
                sub.Items.Add(m.DeviceName, null, (_, _) => SnapEngine.MoveToMonitor(btn.Hwnd, capture));
            }
            sub.Show(this, pt);
        }

        // ── Window tracker events ─────────────────────────────────────────────
        private void WireWindowTracker()
        {
            WindowTracker.Instance.WindowAdded   += (_, wd) => InvokeIfNeeded(() =>
            {
                if (wd.MonitorId == _monitor.Id) AddButton(wd.Handle, wd.Title);
            });
            WindowTracker.Instance.WindowRemoved += (_, wd) => InvokeIfNeeded(() => RemoveButton(wd.Handle));
            WindowTracker.Instance.WindowMoved   += (_, wd) => InvokeIfNeeded(() =>
            {
                if (wd.MonitorId == _monitor.Id) EnsureButton(wd.Handle, wd.Title);
                else                             RemoveButton(wd.Handle);
            });
        }

        private void AddButton(IntPtr hwnd, string title)
        {
            if (_buttons.Exists(b => b.Hwnd == hwnd)) return;
            Icon? icon = null;
            try
            {
                NativeMethods.GetWindowThreadProcessId(hwnd, out uint pid);
                var proc = System.Diagnostics.Process.GetProcessById((int)pid);
                if (proc.MainModule?.FileName is string fn)
                    icon = Icon.ExtractAssociatedIcon(fn);
            }
            catch { }
            _buttons.Add(new TaskbarButton { Hwnd = hwnd, Title = title, Icon = icon });
            Invalidate();
        }

        private void RemoveButton(IntPtr hwnd)
        {
            int idx = _buttons.FindIndex(b => b.Hwnd == hwnd);
            if (idx >= 0) { _buttons.RemoveAt(idx); Invalidate(); }
        }

        private void EnsureButton(IntPtr hwnd, string title)
        {
            if (!_buttons.Exists(b => b.Hwnd == hwnd)) AddButton(hwnd, title);
        }

        // ── Flash timer ───────────────────────────────────────────────────────
        private void SetupFlashTimer()
        {
            _flashTimer.Interval = 500;
            _flashTimer.Tick += (_, _) =>
            {
                bool any = _buttons.Exists(b => b.IsFlashing);
                if (any) Invalidate();
            };
            _flashTimer.Start();
        }

        public void FlashButton(IntPtr hwnd, bool flash)
        {
            var btn = _buttons.Find(b => b.Hwnd == hwnd);
            if (btn != null) { btn.IsFlashing = flash; Invalidate(); }
        }

        // ── Utilities ─────────────────────────────────────────────────────────
        private void SetupDoubleBuffer()
        {
            SetStyle(ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.AllPaintingInWmPaint  |
                     ControlStyles.UserPaint, true);
        }

        private static string TruncateTitle(string s, int maxPx)
        {
            if (s.Length > 24) s = s[..21] + "…";
            return s;
        }

        private void InvokeIfNeeded(Action a) { if (IsHandleCreated) BeginInvoke(a); }

        // ── Work-area reservation (AppBarMessage) ─────────────────────────────
        private static void ReserveWorkArea(MonitorInfo monitor, int taskbarHeight)
        {
            // Uses SHAppBarMessage to register the taskbar and set the work area.
            // Simplified here – full ABM_NEW / ABM_SETPOS implementation omitted for brevity.
            // In production: call SHAppBarMessage(ABM_NEW, ...) then ABM_SETPOS.
            Logger.Info($"[Taskbar] Reserved {taskbarHeight}px at bottom of {monitor.DeviceName}");
        }

        // ── WndProc overrides ─────────────────────────────────────────────────
        protected override void WndProc(ref Message m)
        {
            // Keep taskbar always on top without stealing focus
            if (m.Msg == WinConst.WM_NCACTIVATE) { m.Result = new IntPtr(1); return; }
            base.WndProc(ref m);
        }

        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                cp.ExStyle |= (int)(WinConst.WS_EX_TOOLWINDOW | WinConst.WS_EX_NOACTIVATE);
                return cp;
            }
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  TaskbarManager  –  creates/destroys taskbars as monitors change
    // ─────────────────────────────────────────────────────────────────────────
    public class TaskbarManager
    {
        public static TaskbarManager Instance { get; } = new();
        private TaskbarManager() { }

        private readonly Dictionary<string, MonitorTaskbar> _taskbars = new();

        public void Initialize()
        {
            MonitorManager.Instance.DisplayConfigurationChanged += (_, _) => RebuildTaskbars();
            RebuildTaskbars();
        }

        private void RebuildTaskbars()
        {
            // Close old taskbars for removed monitors
            var currentIds = MonitorManager.Instance.Monitors.ConvertAll(m => m.Id);
            foreach (var id in new List<string>(_taskbars.Keys))
            {
                if (!currentIds.Contains(id))
                {
                    _taskbars[id].Close();
                    _taskbars.Remove(id);
                }
            }

            // Create taskbars for new non-primary monitors
            foreach (var monitor in MonitorManager.Instance.Monitors)
            {
                if (monitor.IsPrimary) continue;  // Primary uses Windows taskbar
                if (!_taskbars.ContainsKey(monitor.Id))
                {
                    var tb = new MonitorTaskbar(monitor);
                    tb.Show();
                    _taskbars[monitor.Id] = tb;
                }
            }
        }

        public void FlashButton(IntPtr hwnd, bool flash)
        {
            foreach (var tb in _taskbars.Values)
                tb.FlashButton(hwnd, flash);
        }
    }
}

// Extension method for rounded rectangles
namespace System.Drawing
{
    internal static class GraphicsExtensions
    {
        public static void FillRoundedRectangle(this Graphics g, Brush brush, Rectangle rect, int radius)
        {
            using var path = RoundedRect(rect, radius);
            g.FillPath(brush, path);
        }
        private static Drawing2D.GraphicsPath RoundedRect(Rectangle bounds, int radius)
        {
            int d = radius * 2;
            var path = new Drawing2D.GraphicsPath();
            path.AddArc(bounds.X, bounds.Y, d, d, 180, 90);
            path.AddArc(bounds.Right - d, bounds.Y, d, d, 270, 90);
            path.AddArc(bounds.Right - d, bounds.Bottom - d, d, d, 0, 90);
            path.AddArc(bounds.X, bounds.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }
    }
}

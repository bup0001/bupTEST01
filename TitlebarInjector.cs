using System;
using System.Collections.Generic;
using System.Drawing;
using System.Runtime.InteropServices;
using MultiMonitorPlatform.Core;
using MultiMonitorPlatform.Interop;

namespace MultiMonitorPlatform.UI
{
    // ─────────────────────────────────────────────────────────────────────────
    //  TitlebarButton definition
    // ─────────────────────────────────────────────────────────────────────────
    public class TitlebarButtonDef
    {
        public string   Id          { get; set; } = Guid.NewGuid().ToString();
        public string   Tooltip     { get; set; } = "";
        public Icon?    Icon        { get; set; }
        public Func<IntPtr, bool>? Filter { get; set; }  // return true to show on this window
        public Action<IntPtr>?     OnClick { get; set; }
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  TitlebarInjector
    //
    //  Strategy: we do NOT modify the target window's non-client area directly
    //  (that requires DLL injection).  Instead we create a tiny transparent
    //  layered child-of-desktop window positioned on top of the titlebar.
    //  This is the same technique used by DisplayFusion and AquaSnap.
    // ─────────────────────────────────────────────────────────────────────────
    public class TitlebarButtonOverlay : System.Windows.Forms.Form
    {
        private const int BTN_SIZE = 22;
        private const int BTN_PAD  = 2;

        private readonly IntPtr _targetHwnd;
        private readonly List<TitlebarButtonDef> _buttons;
        private int _hoveredIdx = -1;

        public TitlebarButtonOverlay(IntPtr targetHwnd, List<TitlebarButtonDef> buttons)
        {
            _targetHwnd = targetHwnd;
            _buttons    = buttons;

            FormBorderStyle  = System.Windows.Forms.FormBorderStyle.None;
            ShowInTaskbar    = false;
            BackColor        = System.Drawing.Color.Magenta;   // chroma key
            TransparencyKey  = System.Drawing.Color.Magenta;
            TopMost          = true;

            // Mark as tool window so it doesn't appear in taskbars
            long ex = NativeMethods.GetWindowLongPtr(Handle, WinConst.GWL_EXSTYLE);
            NativeMethods.SetWindowLongPtr(Handle, WinConst.GWL_EXSTYLE,
                ex | WinConst.WS_EX_TOOLWINDOW | WinConst.WS_EX_NOACTIVATE);

            SetStyle(System.Windows.Forms.ControlStyles.OptimizedDoubleBuffer |
                     System.Windows.Forms.ControlStyles.AllPaintingInWmPaint  |
                     System.Windows.Forms.ControlStyles.UserPaint, true);

            Width  = (BTN_SIZE + BTN_PAD) * buttons.Count;
            Height = BTN_SIZE;

            UpdatePosition();

            MouseMove  += (_, e) => { int i = HitTest(e.Location); if (i != _hoveredIdx) { _hoveredIdx = i; Invalidate(); } };
            MouseLeave += (_, _) => { _hoveredIdx = -1; Invalidate(); };
            MouseClick += (_, e) =>
            {
                int i = HitTest(e.Location);
                if (i >= 0 && i < _buttons.Count)
                    _buttons[i].OnClick?.Invoke(_targetHwnd);
            };
        }

        public void UpdatePosition()
        {
            if (!NativeMethods.GetWindowRect(_targetHwnd, out RECT r)) return;

            // Position just left of the standard caption buttons
            // (approx: right edge - standard 3 buttons - our buttons)
            uint dpi     = NativeMethods.GetDpiForWindow(_targetHwnd);
            float scale  = dpi / 96f;
            int  offset  = (int)(105 * scale); // 3 × ~34px standard buttons

            int x = r.Right  - offset - Width;
            int y = r.Top    + (int)(1 * scale);

            NativeMethods.SetWindowPos(Handle, IntPtr.Zero, x, y, Width, Height,
                WinConst.SWP_NOZORDER | WinConst.SWP_NOACTIVATE);
        }

        protected override void OnPaint(System.Windows.Forms.PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(BackColor);  // chroma-keyed transparent

            for (int i = 0; i < _buttons.Count; i++)
            {
                var rc = new Rectangle(i * (BTN_SIZE + BTN_PAD), 0, BTN_SIZE, BTN_SIZE);
                bool hovered = i == _hoveredIdx;

                // Hover background
                if (hovered)
                    using (var br = new SolidBrush(Color.FromArgb(80, 80, 80)))
                        g.FillEllipse(br, rc);

                // Icon
                if (_buttons[i].Icon != null)
                    g.DrawIcon(_buttons[i].Icon!, new Rectangle(rc.X + 3, rc.Y + 3, BTN_SIZE - 6, BTN_SIZE - 6));
                else
                {
                    // Draw a simple circle as placeholder
                    g.DrawEllipse(Pens.White, rc.X + 4, rc.Y + 4, BTN_SIZE - 9, BTN_SIZE - 9);
                }
            }
        }

        private int HitTest(Point p)
        {
            for (int i = 0; i < _buttons.Count; i++)
                if (new Rectangle(i * (BTN_SIZE + BTN_PAD), 0, BTN_SIZE, BTN_SIZE).Contains(p))
                    return i;
            return -1;
        }

        protected override void WndProc(ref System.Windows.Forms.Message m)
        {
            if (m.Msg == WinConst.WM_NCACTIVATE) { m.Result = new IntPtr(1); return; }
            base.WndProc(ref m);
        }

        protected override System.Windows.Forms.CreateParams CreateParams
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
    //  TitlebarInjector  –  manages overlays for all tracked windows
    // ─────────────────────────────────────────────────────────────────────────
    public class TitlebarInjector
    {
        public static TitlebarInjector Instance { get; } = new();
        private TitlebarInjector() { }

        private readonly Dictionary<IntPtr, TitlebarButtonOverlay> _overlays = new();
        private readonly List<TitlebarButtonDef> _globalButtons = new();

        // ── Built-in buttons ──────────────────────────────────────────────────
        public void RegisterDefaultButtons()
        {
            // "Move to next monitor" button
            _globalButtons.Add(new TitlebarButtonDef
            {
                Id      = "move_next_monitor",
                Tooltip = "Move to next monitor",
                OnClick = hwnd =>
                {
                    var current = MonitorManager.Instance.FromWindow(hwnd);
                    var monitors = MonitorManager.Instance.Monitors;
                    if (current == null || monitors.Count < 2) return;

                    int idx  = monitors.IndexOf(current);
                    var next = monitors[(idx + 1) % monitors.Count];
                    SnapEngine.MoveToMonitor(hwnd, next);
                }
            });

            // "Pin / always on top" button
            _globalButtons.Add(new TitlebarButtonDef
            {
                Id      = "pin_on_top",
                Tooltip = "Toggle always on top",
                OnClick = hwnd =>
                {
                    var hwnds = new IntPtr[]
                    {
                        new IntPtr(-1),   // HWND_TOPMOST
                        new IntPtr(-2),   // HWND_NOTOPMOST
                    };
                    long ex = NativeMethods.GetWindowLongPtr(hwnd, WinConst.GWL_EXSTYLE);
                    bool isTop = (ex & 0x00000008L) != 0; // WS_EX_TOPMOST
                    NativeMethods.SetWindowPos(hwnd,
                        isTop ? new IntPtr(-2) : new IntPtr(-1),
                        0, 0, 0, 0,
                        WinConst.SWP_NOMOVE | WinConst.SWP_NOSIZE);
                }
            });
        }

        // ── Lifecycle ─────────────────────────────────────────────────────────
        public void Initialize()
        {
            RegisterDefaultButtons();

            WindowTracker.Instance.WindowAdded   += (_, wd) =>
                System.Windows.Forms.Application.OpenForms[0]?.BeginInvoke(() => CreateOverlay(wd.Handle));
            WindowTracker.Instance.WindowRemoved += (_, wd) =>
                System.Windows.Forms.Application.OpenForms[0]?.BeginInvoke(() => DestroyOverlay(wd.Handle));
            WindowTracker.Instance.WindowMoved   += (_, wd) =>
                System.Windows.Forms.Application.OpenForms[0]?.BeginInvoke(() => UpdateOverlay(wd.Handle));
        }

        private void CreateOverlay(IntPtr hwnd)
        {
            var relevant = _globalButtons.FindAll(b => b.Filter?.Invoke(hwnd) ?? true);
            if (relevant.Count == 0) return;

            var overlay = new TitlebarButtonOverlay(hwnd, relevant);
            overlay.Show();
            _overlays[hwnd] = overlay;
        }

        private void DestroyOverlay(IntPtr hwnd)
        {
            if (_overlays.TryGetValue(hwnd, out var ov))
            {
                ov.Close();
                _overlays.Remove(hwnd);
            }
        }

        private void UpdateOverlay(IntPtr hwnd)
        {
            if (_overlays.TryGetValue(hwnd, out var ov))
                ov.UpdatePosition();
        }
    }
}

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Text;
using MultiMonitorPlatform.Interop;

namespace MultiMonitorPlatform.Core
{
    // ─────────────────────────────────────────────────────────────────────────
    //  Window descriptor  (lightweight snapshot of a top-level window)
    // ─────────────────────────────────────────────────────────────────────────
    public class WindowDescriptor
    {
        public IntPtr   Handle     { get; init; }
        public string   Title      { get; set; } = "";
        public uint     ProcessId  { get; init; }
        public string   ProcessName{ get; init; } = "";
        public Rectangle Bounds    { get; set; }
        public uint     ShowCmd    { get; set; }
        public string?  MonitorId  { get; set; }
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  WindowTracker  –  maintains a live dict of visible top-level windows
    // ─────────────────────────────────────────────────────────────────────────
    public class WindowTracker
    {
        public static WindowTracker Instance { get; } = new();
        private WindowTracker() { }

        private readonly ConcurrentDictionary<IntPtr, WindowDescriptor> _windows = new();
        public  IEnumerable<WindowDescriptor> Windows => _windows.Values;

        public event EventHandler<WindowDescriptor>? WindowAdded;
        public event EventHandler<WindowDescriptor>? WindowRemoved;
        public event EventHandler<WindowDescriptor>? WindowMoved;
        public event EventHandler<WindowDescriptor>? WindowTitleChanged;

        // ── Shell hook callback (called from background service) ──────────────
        public void OnWindowCreated(IntPtr hwnd)
        {
            if (!IsTrackable(hwnd)) return;
            var wd = Snapshot(hwnd);
            if (_windows.TryAdd(hwnd, wd))
                WindowAdded?.Invoke(this, wd);
        }

        public void OnWindowDestroyed(IntPtr hwnd)
        {
            if (_windows.TryRemove(hwnd, out var wd))
                WindowRemoved?.Invoke(this, wd);
        }

        public void OnWindowActivated(IntPtr hwnd) => RefreshWindow(hwnd);

        public void OnWindowMoved(IntPtr hwnd)
        {
            if (!_windows.TryGetValue(hwnd, out var wd)) return;
            var placement = WINDOWPLACEMENT.Create();
            if (!NativeMethods.GetWindowPlacement(hwnd, ref placement)) return;
            NativeMethods.GetWindowRect(hwnd, out RECT r);
            wd.Bounds  = r.ToRectangle();
            wd.ShowCmd = placement.showCmd;
            wd.MonitorId = MonitorManager.Instance.FromWindow(hwnd)?.Id;
            WindowMoved?.Invoke(this, wd);
        }

        // ── Full enumeration (on startup / refresh) ───────────────────────────
        public void RefreshAll()
        {
            _windows.Clear();
            NativeMethods.EnumWindows((hwnd, _) =>
            {
                if (IsTrackable(hwnd)) _windows[hwnd] = Snapshot(hwnd);
                return true;
            }, IntPtr.Zero);
        }

        // ── Helpers ───────────────────────────────────────────────────────────
        private void RefreshWindow(IntPtr hwnd)
        {
            if (!IsTrackable(hwnd)) return;
            var wd = Snapshot(hwnd);
            _windows[hwnd] = wd;
        }

        private static bool IsTrackable(IntPtr hwnd)
        {
            if (!NativeMethods.IsWindowVisible(hwnd)) return false;
            long ex = NativeMethods.GetWindowLongPtr(hwnd, WinConst.GWL_EXSTYLE);
            if ((ex & WinConst.WS_EX_TOOLWINDOW) != 0) return false;
            // Skip windows with no title
            int len = NativeMethods.GetWindowTextLength(hwnd);
            return len > 0;
        }

        private static WindowDescriptor Snapshot(IntPtr hwnd)
        {
            var sb = new StringBuilder(512);
            NativeMethods.GetWindowText(hwnd, sb, 512);
            NativeMethods.GetWindowRect(hwnd, out RECT r);
            NativeMethods.GetWindowThreadProcessId(hwnd, out uint pid);
            var placement = WINDOWPLACEMENT.Create();
            NativeMethods.GetWindowPlacement(hwnd, ref placement);

            string procName = "";
            try { procName = Process.GetProcessById((int)pid).ProcessName; } catch { }

            return new WindowDescriptor
            {
                Handle      = hwnd,
                Title       = sb.ToString(),
                ProcessId   = pid,
                ProcessName = procName,
                Bounds      = r.ToRectangle(),
                ShowCmd     = placement.showCmd,
                MonitorId   = MonitorManager.Instance.FromWindow(hwnd)?.Id
            };
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  SnapEngine  –  edge snapping + zone assignment
    // ─────────────────────────────────────────────────────────────────────────
    public enum SnapZone { None, Left, Right, TopLeft, TopRight, BottomLeft, BottomRight, Maximize, Minimize }

    public static class SnapEngine
    {
        /// <summary>
        /// Call this while a window is being moved (WM_MOVING). Adjusts the
        /// proposed RECT so the window snaps to monitor edges or snap zones.
        /// </summary>
        public static SnapZone Evaluate(IntPtr hwnd, ref Rectangle proposed, Point cursor)
        {
            var monitor = MonitorManager.Instance.FromPoint(cursor);
            if (monitor == null) return SnapZone.None;

            var work = monitor.WorkArea;
            int t = WinConst.SNAP_THRESHOLD;

            // Top-left corner
            if (cursor.X <= monitor.MonitorRect.Left + t && cursor.Y <= monitor.MonitorRect.Top + t)
                return Snap(hwnd, ref proposed, new Rectangle(work.Left, work.Top, work.Width / 2, work.Height / 2), SnapZone.TopLeft);

            // Top-right corner
            if (cursor.X >= monitor.MonitorRect.Right - t && cursor.Y <= monitor.MonitorRect.Top + t)
                return Snap(hwnd, ref proposed, new Rectangle(work.Left + work.Width / 2, work.Top, work.Width / 2, work.Height / 2), SnapZone.TopRight);

            // Bottom-left corner
            if (cursor.X <= monitor.MonitorRect.Left + t && cursor.Y >= monitor.MonitorRect.Bottom - t)
                return Snap(hwnd, ref proposed, new Rectangle(work.Left, work.Top + work.Height / 2, work.Width / 2, work.Height / 2), SnapZone.BottomLeft);

            // Bottom-right corner
            if (cursor.X >= monitor.MonitorRect.Right - t && cursor.Y >= monitor.MonitorRect.Bottom - t)
                return Snap(hwnd, ref proposed, new Rectangle(work.Left + work.Width / 2, work.Top + work.Height / 2, work.Width / 2, work.Height / 2), SnapZone.BottomRight);

            // Left edge → left half
            if (cursor.X <= monitor.MonitorRect.Left + t)
                return Snap(hwnd, ref proposed, new Rectangle(work.Left, work.Top, work.Width / 2, work.Height), SnapZone.Left);

            // Right edge → right half
            if (cursor.X >= monitor.MonitorRect.Right - t)
                return Snap(hwnd, ref proposed, new Rectangle(work.Left + work.Width / 2, work.Top, work.Width / 2, work.Height), SnapZone.Right);

            // Top edge → maximize
            if (cursor.Y <= monitor.MonitorRect.Top + t)
                return Snap(hwnd, ref proposed, work, SnapZone.Maximize);

            return SnapZone.None;
        }

        private static SnapZone Snap(IntPtr hwnd, ref Rectangle proposed, Rectangle target, SnapZone zone)
        {
            proposed = target;
            NativeMethods.SetWindowPos(hwnd, IntPtr.Zero, target.X, target.Y, target.Width, target.Height,
                WinConst.SWP_NOZORDER | WinConst.SWP_NOACTIVATE);
            return zone;
        }

        /// <summary>Move a window to a different monitor, preserving relative position.</summary>
        public static void MoveToMonitor(IntPtr hwnd, MonitorInfo destination)
        {
            var src = MonitorManager.Instance.FromWindow(hwnd);
            if (src == null) return;

            NativeMethods.GetWindowRect(hwnd, out RECT r);
            var bounds = r.ToRectangle();

            double relX = (double)(bounds.Left - src.WorkArea.Left) / src.WorkArea.Width;
            double relY = (double)(bounds.Top  - src.WorkArea.Top)  / src.WorkArea.Height;

            int newX = destination.WorkArea.Left + (int)(relX * destination.WorkArea.Width);
            int newY = destination.WorkArea.Top  + (int)(relY * destination.WorkArea.Height);

            NativeMethods.SetWindowPos(hwnd, IntPtr.Zero, newX, newY, bounds.Width, bounds.Height,
                WinConst.SWP_NOZORDER | WinConst.SWP_NOACTIVATE);
        }
    }
}

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Runtime.InteropServices;
using MultiMonitorPlatform.Interop;

namespace MultiMonitorPlatform.Core
{
    // ─────────────────────────────────────────────────────────────────────────
    //  Monitor descriptor
    // ─────────────────────────────────────────────────────────────────────────
    public class MonitorInfo
    {
        public IntPtr  Handle        { get; init; }
        public string  DeviceName    { get; init; } = "";
        public Rectangle MonitorRect { get; init; }
        public Rectangle WorkArea    { get; init; }
        public bool    IsPrimary     { get; init; }
        public uint    DpiX          { get; init; }
        public uint    DpiY          { get; init; }
        public float   ScaleFactor   => DpiX / 96f;
        public string  Id            => DeviceName.TrimStart('\\').Replace(".", "_");

        // Snap zones: edges of this monitor (inflated by SNAP_THRESHOLD)
        public Rectangle SnapLeft   => new(MonitorRect.Left,   MonitorRect.Top,    WinConst.SNAP_THRESHOLD, MonitorRect.Height);
        public Rectangle SnapRight  => new(MonitorRect.Right - WinConst.SNAP_THRESHOLD, MonitorRect.Top, WinConst.SNAP_THRESHOLD, MonitorRect.Height);
        public Rectangle SnapTop    => new(MonitorRect.Left,   MonitorRect.Top,    MonitorRect.Width, WinConst.SNAP_THRESHOLD);
        public Rectangle SnapBottom => new(MonitorRect.Left,   MonitorRect.Bottom - WinConst.SNAP_THRESHOLD, MonitorRect.Width, WinConst.SNAP_THRESHOLD);
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  MonitorManager  –  enumerates / tracks physical displays
    // ─────────────────────────────────────────────────────────────────────────
    public class MonitorManager
    {
        public static MonitorManager Instance { get; } = new();
        private MonitorManager() => Refresh();

        private readonly List<MonitorInfo> _monitors = new();
        public  IReadOnlyList<MonitorInfo> Monitors  => _monitors;

        public event EventHandler? DisplayConfigurationChanged;

        // ── Refresh ──────────────────────────────────────────────────────────
        public void Refresh()
        {
            _monitors.Clear();
            NativeMethods.EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, EnumProc, IntPtr.Zero);
            DisplayConfigurationChanged?.Invoke(this, EventArgs.Empty);
        }

        private bool EnumProc(IntPtr hMonitor, IntPtr hdcMonitor, ref RECT lprcMonitor, IntPtr dwData)
        {
            var mi = MONITORINFOEX.Create();
            if (!NativeMethods.GetMonitorInfo(hMonitor, ref mi)) return true;

            NativeMethods.GetDpiForMonitor(hMonitor, WinConst.MDT_EFFECTIVE_DPI, out uint dpiX, out uint dpiY);

            _monitors.Add(new MonitorInfo
            {
                Handle      = hMonitor,
                DeviceName  = mi.szDevice,
                MonitorRect = mi.rcMonitor.ToRectangle(),
                WorkArea    = mi.rcWork.ToRectangle(),
                IsPrimary   = (mi.dwFlags & 1) != 0,
                DpiX        = dpiX,
                DpiY        = dpiY,
            });
            return true;
        }

        // ── Lookup helpers ────────────────────────────────────────────────────
        public MonitorInfo? FromWindow(IntPtr hwnd)
        {
            IntPtr h = NativeMethods.MonitorFromWindow(hwnd, WinConst.MONITOR_DEFAULTTONEAREST);
            return _monitors.Find(m => m.Handle == h);
        }

        public MonitorInfo? FromPoint(Point pt)
        {
            var p = new POINT { X = pt.X, Y = pt.Y };
            IntPtr h = NativeMethods.MonitorFromPoint(p, WinConst.MONITOR_DEFAULTTONEAREST);
            return _monitors.Find(m => m.Handle == h);
        }

        public MonitorInfo? Primary => _monitors.Find(m => m.IsPrimary);
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  WallpaperManager  –  per-monitor wallpapers via IDesktopWallpaper COM
    // ─────────────────────────────────────────────────────────────────────────
    public static class WallpaperManager
    {
        // IDesktopWallpaper COM interface (Windows 8+)
        [ComImport, Guid("B92B56A9-8B55-4E14-9A89-0199BBB6F93B"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IDesktopWallpaper
        {
            void SetWallpaper([MarshalAs(UnmanagedType.LPWStr)] string monitorID, [MarshalAs(UnmanagedType.LPWStr)] string wallpaper);
            [return: MarshalAs(UnmanagedType.LPWStr)] string GetWallpaper([MarshalAs(UnmanagedType.LPWStr)] string monitorID);
            [return: MarshalAs(UnmanagedType.LPWStr)] string GetMonitorDevicePathAt(uint monitorIndex);
            [return: MarshalAs(UnmanagedType.U4)] uint GetMonitorDevicePathCount();
            void GetMonitorRECT([MarshalAs(UnmanagedType.LPWStr)] string monitorID, out RECT displayRect);
            void SetBackgroundColor([MarshalAs(UnmanagedType.U4)] uint color);
            [return: MarshalAs(UnmanagedType.U4)] uint GetBackgroundColor();
            void SetPosition([MarshalAs(UnmanagedType.I4)] int position);
            [return: MarshalAs(UnmanagedType.I4)] int GetPosition();
            void SetSlideshow(IntPtr items);
            void GetSlideshow(out IntPtr items);
            void SetSlideshowOptions(int options, uint slideshowTick);
            void GetSlideshowOptions(out int options, out uint slideshowTick);
            void AdvanceSlideshow([MarshalAs(UnmanagedType.LPWStr)] string monitorID, [MarshalAs(UnmanagedType.I4)] int direction);
            void GetStatus([MarshalAs(UnmanagedType.I4)] out int state);
            [return: MarshalAs(UnmanagedType.Bool)] bool Enable([MarshalAs(UnmanagedType.Bool)] bool enable);
        }

        [ComImport, Guid("C2CF3110-460E-4FC1-B9D0-8A1C0C9CC4BD")]
        private class DesktopWallpaperClass { }

        private static IDesktopWallpaper? _wallpaper;
        private static IDesktopWallpaper Wallpaper => _wallpaper ??= (IDesktopWallpaper)new DesktopWallpaperClass();

        /// <summary>Set a different wallpaper on each monitor.</summary>
        public static void SetWallpaper(string monitorDevicePath, string imagePath)
        {
            try { Wallpaper.SetWallpaper(monitorDevicePath, imagePath); }
            catch (Exception ex) { Logger.Error($"WallpaperManager.SetWallpaper: {ex.Message}"); }
        }

        public static string GetWallpaper(string monitorDevicePath)
        {
            try { return Wallpaper.GetWallpaper(monitorDevicePath); }
            catch { return ""; }
        }

        /// <summary>Returns the COM device path for every monitor (used as keys).</summary>
        public static IEnumerable<string> GetMonitorDevicePaths()
        {
            uint count = Wallpaper.GetMonitorDevicePathCount();
            for (uint i = 0; i < count; i++)
                yield return Wallpaper.GetMonitorDevicePathAt(i);
        }

        /// <summary>Apply a saved wallpaper mapping all at once.</summary>
        public static void ApplyProfile(Dictionary<string, string> wallpaperMap)
        {
            foreach (var kv in wallpaperMap)
                SetWallpaper(kv.Key, kv.Value);
        }
    }
}

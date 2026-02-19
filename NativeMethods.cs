using System;
using System.Runtime.InteropServices;
using System.Text;

namespace MultiMonitorPlatform.Interop
{
    // ─────────────────────────────────────────────────────────────────────────
    //  Win32 Constants
    // ─────────────────────────────────────────────────────────────────────────
    public static class WinConst
    {
        // Window Messages
        public const int WM_NCCALCSIZE    = 0x0083;
        public const int WM_NCHITTEST     = 0x0084;
        public const int WM_NCPAINT       = 0x0085;
        public const int WM_NCACTIVATE    = 0x0086;
        public const int WM_NCLBUTTONDOWN = 0x00A1;
        public const int WM_NCLBUTTONDBLCLK = 0x00A3;
        public const int WM_WINDOWPOSCHANGED = 0x0047;
        public const int WM_DISPLAYCHANGE = 0x007E;
        public const int WM_DPICHANGED    = 0x02E0;
        public const int WM_SETTINGCHANGE = 0x001A;

        // Hit-test values for WM_NCHITTEST
        public const int HTCLIENT    = 1;
        public const int HTCAPTION   = 2;
        public const int HTCLOSE     = 20;
        public const int HTMAXBUTTON = 9;
        public const int HTMINBUTTON = 8;

        // SetWindowPos flags
        public const uint SWP_NOSIZE       = 0x0001;
        public const uint SWP_NOMOVE       = 0x0002;
        public const uint SWP_NOZORDER     = 0x0004;
        public const uint SWP_NOACTIVATE   = 0x0010;
        public const uint SWP_FRAMECHANGED = 0x0020;
        public const uint SWP_SHOWWINDOW   = 0x0040;

        // Window styles
        public const int GWL_STYLE   = -16;
        public const int GWL_EXSTYLE = -20;
        public const long WS_CAPTION       = 0x00C00000L;
        public const long WS_THICKFRAME    = 0x00040000L;
        public const long WS_EX_APPWINDOW  = 0x00040000L;
        public const long WS_EX_TOOLWINDOW = 0x00000080L;
        public const long WS_EX_NOACTIVATE = 0x08000000L;

        // ShowWindow commands
        public const int SW_SHOWNORMAL  = 1;
        public const int SW_SHOWMINIMIZED = 2;
        public const int SW_SHOWMAXIMIZED = 3;
        public const int SW_RESTORE      = 9;
        public const int SW_HIDE         = 0;

        // DPI Awareness
        public const int PROCESS_PER_MONITOR_DPI_AWARE_V2 = 4;

        // Hooks
        public const int WH_CBT        = 5;
        public const int WH_SHELL      = 10;
        public const int HCBT_ACTIVATE = 5;
        public const int HCBT_MOVESIZE = 0;
        public const int HCBT_MINMAX   = 3;
        public const int HCBT_CREATEWND = 3; // reuse intentional for SHELL
        public const int HSHELL_WINDOWCREATED    = 1;
        public const int HSHELL_WINDOWDESTROYED  = 2;
        public const int HSHELL_WINDOWACTIVATED  = 4;
        public const int HSHELL_REDRAW           = 6;
        public const int HSHELL_TASKMAN          = 7;
        public const int HSHELL_FLASH            = 32774;

        // Monitor flags
        public const uint MONITOR_DEFAULTTONULL    = 0x00000000;
        public const uint MONITOR_DEFAULTTOPRIMARY = 0x00000001;
        public const uint MONITOR_DEFAULTTONEAREST = 0x00000002;
        public const int  MDT_EFFECTIVE_DPI        = 0;

        // Wallpaper
        public const int SPI_SETDESKWALLPAPER = 0x0014;
        public const int SPIF_UPDATEINIFILE   = 0x01;
        public const int SPIF_SENDCHANGE      = 0x02;

        // Snap zones
        public const int SNAP_THRESHOLD = 20; // pixels
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  Structures
    // ─────────────────────────────────────────────────────────────────────────
    [StructLayout(LayoutKind.Sequential)]
    public struct RECT
    {
        public int Left, Top, Right, Bottom;
        public int Width  => Right  - Left;
        public int Height => Bottom - Top;
        public System.Drawing.Rectangle ToRectangle() =>
            System.Drawing.Rectangle.FromLTRB(Left, Top, Right, Bottom);
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct POINT { public int X, Y; }

    [StructLayout(LayoutKind.Sequential)]
    public struct MONITORINFO
    {
        public uint   cbSize;
        public RECT   rcMonitor;
        public RECT   rcWork;
        public uint   dwFlags;
        public static MONITORINFO Create() => new MONITORINFO { cbSize = (uint)Marshal.SizeOf<MONITORINFO>() };
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    public struct MONITORINFOEX
    {
        public uint   cbSize;
        public RECT   rcMonitor;
        public RECT   rcWork;
        public uint   dwFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string szDevice;
        public static MONITORINFOEX Create() => new MONITORINFOEX { cbSize = (uint)Marshal.SizeOf<MONITORINFOEX>() };
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct WINDOWPLACEMENT
    {
        public uint   length;
        public uint   flags;
        public uint   showCmd;
        public POINT  ptMinPosition;
        public POINT  ptMaxPosition;
        public RECT   rcNormalPosition;
        public static WINDOWPLACEMENT Create() => new WINDOWPLACEMENT { length = (uint)Marshal.SizeOf<WINDOWPLACEMENT>() };
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct TITLEBARINFO
    {
        public uint dwSize;
        public RECT rcTitleBar;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 6)]
        public uint[] rgstate;
        public static TITLEBARINFO Create() => new TITLEBARINFO { dwSize = (uint)Marshal.SizeOf<TITLEBARINFO>() };
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  P/Invoke declarations
    // ─────────────────────────────────────────────────────────────────────────
    public static class NativeMethods
    {
        // Monitors
        [DllImport("user32.dll")] public static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr lprcClip, MonitorEnumProc lpfnEnum, IntPtr dwData);
        [DllImport("user32.dll")] public static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFOEX lpmi);
        [DllImport("user32.dll")] public static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint dwFlags);
        [DllImport("user32.dll")] public static extern IntPtr MonitorFromPoint(POINT pt, uint dwFlags);
        [DllImport("shcore.dll")] public static extern int GetDpiForMonitor(IntPtr hmonitor, int dpiType, out uint dpiX, out uint dpiY);
        [DllImport("user32.dll")] public static extern uint GetDpiForWindow(IntPtr hwnd);
        [DllImport("shcore.dll")] public static extern int SetProcessDpiAwareness(int value);

        // Windows
        [DllImport("user32.dll")] public static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);
        [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr hWnd);
        [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);
        [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);
        [DllImport("user32.dll")] public static extern bool GetWindowPlacement(IntPtr hWnd, ref WINDOWPLACEMENT lpwndpl);
        [DllImport("user32.dll")] public static extern bool SetWindowPlacement(IntPtr hWnd, ref WINDOWPLACEMENT lpwndpl);
        [DllImport("user32.dll")] public static extern int  GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);
        [DllImport("user32.dll")] public static extern int  GetWindowTextLength(IntPtr hWnd);
        [DllImport("user32.dll")] public static extern bool GetTitleBarInfo(IntPtr hwnd, ref TITLEBARINFO pti);
        [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hWnd);
        [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtr")] public static extern long GetWindowLongPtr(IntPtr hWnd, int nIndex);
        [DllImport("user32.dll", EntryPoint = "SetWindowLongPtr")] public static extern long SetWindowLongPtr(IntPtr hWnd, int nIndex, long dwNewLong);
        [DllImport("user32.dll")] public static extern IntPtr GetWindowDC(IntPtr hWnd);
        [DllImport("user32.dll")] public static extern int   ReleaseDC(IntPtr hWnd, IntPtr hDC);
        [DllImport("user32.dll")] public static extern bool  InvalidateRect(IntPtr hWnd, IntPtr lpRect, bool bErase);
        [DllImport("user32.dll")] public static extern uint  GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);
        [DllImport("user32.dll")] public static extern IntPtr FindWindow(string lpClassName, string lpWindowName);

        // Messages & Hooks
        [DllImport("user32.dll")] public static extern IntPtr SetWindowsHookEx(int idHook, HookProc lpfn, IntPtr hMod, uint dwThreadId);
        [DllImport("user32.dll")] public static extern bool UnhookWindowsHookEx(IntPtr hhk);
        [DllImport("user32.dll")] public static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);
        [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);
        [DllImport("user32.dll")] public static extern IntPtr SendMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);
        [DllImport("user32.dll")] public static extern bool RegisterShellHookWindow(IntPtr hWnd);
        [DllImport("user32.dll")] public static extern bool DeregisterShellHookWindow(IntPtr hWnd);
        [DllImport("user32.dll")] public static extern uint RegisterWindowMessage(string lpString);

        // Desktop / Wallpaper
        [DllImport("user32.dll")] public static extern bool SystemParametersInfo(int uAction, uint uParam, string lpvParam, int fuWinIni);

        // Process
        [DllImport("kernel32.dll")] public static extern IntPtr GetModuleHandle(string lpModuleName);
        [DllImport("kernel32.dll")] public static extern bool  OpenProcessToken(IntPtr ProcessHandle, uint DesiredAccess, out IntPtr TokenHandle);
        [DllImport("advapi32.dll")] public static extern bool  LookupPrivilegeValue(string lpSystemName, string lpName, out LUID lpLuid);
        [DllImport("advapi32.dll")] public static extern bool  AdjustTokenPrivileges(IntPtr TokenHandle, bool DisableAllPrivileges, ref TOKEN_PRIVILEGES NewState, uint BufferLength, IntPtr PreviousState, IntPtr ReturnLength);

        // GDI
        [DllImport("gdi32.dll")] public static extern bool BitBlt(IntPtr hdcDest, int nXDest, int nYDest, int nWidth, int nHeight, IntPtr hdcSrc, int nXSrc, int nYSrc, uint dwRop);
        [DllImport("gdi32.dll")] public static extern IntPtr CreateCompatibleDC(IntPtr hdc);
        [DllImport("gdi32.dll")] public static extern IntPtr CreateCompatibleBitmap(IntPtr hdc, int nWidth, int nHeight);
        [DllImport("gdi32.dll")] public static extern IntPtr SelectObject(IntPtr hdc, IntPtr hgdiobj);
        [DllImport("gdi32.dll")] public static extern bool DeleteObject(IntPtr hObject);
        [DllImport("gdi32.dll")] public static extern bool DeleteDC(IntPtr hdc);

        // COM / Shell
        [DllImport("shell32.dll")] public static extern int SHChangeNotify(int eventId, uint flags, IntPtr dwItem1, IntPtr dwItem2);

        [StructLayout(LayoutKind.Sequential)] public struct LUID { public uint LowPart; public int HighPart; }
        [StructLayout(LayoutKind.Sequential)]
        public struct TOKEN_PRIVILEGES
        {
            public uint PrivilegeCount;
            public LUID Luid;
            public uint Attributes;
        }
    }

    // Delegate types for hooks & enumerations
    public delegate bool MonitorEnumProc(IntPtr hMonitor, IntPtr hdcMonitor, ref RECT lprcMonitor, IntPtr dwData);
    public delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);
    public delegate IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam);
}

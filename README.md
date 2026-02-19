# MultiMonitorPlatform

A Windows 10/11 system-level multi-monitor management platform built on Win32 APIs.

---

## Features

| Feature | Implementation |
|---|---|
| Independent taskbars per monitor | `MonitorTaskbar.cs` — layered WinForms window docked per monitor |
| Window snapping & movement control | `WindowTracker.cs` + `SnapEngine.cs` — WH_CBT hook + geometry |
| Save / restore monitor profiles | `ProfileManager.cs` — JSON snapshots of monitor + window state |
| Per-monitor wallpapers | `MonitorManager.cs` → `IDesktopWallpaper` COM interface |
| Trigger-based automation engine | `AutomationEngine.cs` — rule/action engine with shell hook events |
| Titlebar button injection | `TitlebarInjector.cs` — transparent layered overlay windows |
| DPI-aware + Windows 11 compatible | `app.manifest` (PerMonitorV2) + `SetProcessDpiAwareness` |
| Deep Win32 API usage | `NativeMethods.cs` — 40+ P/Invoke signatures |
| Background service + UI app | `MultiMonitorService.cs` (SCM) + `TrayApp.cs` (WinForms) |

---

## Architecture

```
┌─────────────────────────────────────────────────────────────────────────┐
│  MultiMonitorPlatform.exe                                               │
│                                                                         │
│  ┌─────────────┐   ┌────────────────┐   ┌──────────────────────────┐   │
│  │  TrayApp    │   │ControlPanel UI │   │   TaskbarManager         │   │
│  │ (AppContext)│──▶│  (WinForms)    │   │ MonitorTaskbar ×N        │   │
│  └─────────────┘   └────────────────┘   └──────────────────────────┘   │
│                                                                         │
│  ┌─────────────────────────────────────────────────────────────────┐   │
│  │  Core Layer                                                     │   │
│  │  MonitorManager ─ WallpaperManager ─ WindowTracker ─ SnapEngine │   │
│  │  ProfileManager                                                 │   │
│  └─────────────────────────────────────────────────────────────────┘   │
│                                                                         │
│  ┌──────────────────────────┐   ┌────────────────────────────────────┐  │
│  │  AutomationEngine        │   │  Interop / Win32                   │  │
│  │  Rules → Triggers+Actions│   │  NativeMethods (P/Invoke)          │  │
│  └──────────────────────────┘   └────────────────────────────────────┘  │
│                                                                         │
│  ┌─────────────────────────────────────────────────────────────────┐   │
│  │  MultiMonitorService  (Windows Service mode)                    │   │
│  │  ShellHookWindow + WinPositionHook (WH_CBT)                     │   │
│  └─────────────────────────────────────────────────────────────────┘   │
└─────────────────────────────────────────────────────────────────────────┘
```

---

## Building

### Prerequisites
- .NET 8 SDK
- Windows 10 22H2+ or Windows 11
- Visual Studio 2022 or `dotnet` CLI

### Build
```powershell
cd MultiMonitorPlatform
dotnet build -c Release
```

### Publish as single EXE
```powershell
dotnet publish -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true
```

---

## Running

### As UI tray application (no elevation needed)
```powershell
./MultiMonitorPlatform.exe
```

### As Windows Service (requires Administrator)
```powershell
# Install & start
.\Install-Service.ps1 -Action install

# Stop & remove
.\Install-Service.ps1 -Action uninstall

# Check status
.\Install-Service.ps1 -Action status
```

### Console / debug mode
```powershell
./MultiMonitorPlatform.exe --console
```

---

## Key Win32 APIs Used

| API | Purpose |
|---|---|
| `EnumDisplayMonitors` | Enumerate all physical monitors |
| `GetMonitorInfo` (MONITORINFOEX) | Per-monitor device name, rect, work area |
| `GetDpiForMonitor` (shcore) | Per-monitor DPI |
| `SetProcessDpiAwareness` | PerMonitorV2 process mode |
| `IDesktopWallpaper` COM | Per-monitor wallpaper set/get |
| `RegisterShellHookWindow` | Window create/destroy/activate events |
| `SetWindowsHookEx(WH_CBT)` | Intercept window moves for snap |
| `SetWindowPos` | Reposition/resize windows |
| `GetWindowPlacement / SetWindowPlacement` | Save/restore minimized/maximized state |
| `GetWindowLongPtr / SetWindowLongPtr` | Modify window styles |
| `MonitorFromWindow / MonitorFromPoint` | Hit-test monitor from coords |

---

## Automation Engine

Rules are stored in `%APPDATA%\MultiMonitorPlatform\automation_rules.json`.

### Trigger types
- `MonitorConnected` / `MonitorDisconnected` / `MonitorCountChanged`
- `WindowOpened` / `WindowClosed` / `WindowFocused` (filter by process/title)
- `TimeOfDay` (HH:mm)
- `AppStartup`
- `DisplayResolutionChanged`

### Action types
- `RestoreProfile` — load a saved layout
- `ApplyWallpaper` — set wallpaper on a specific monitor
- `MoveWindowToMonitor` — relocate a window
- `LaunchProcess` — start an application
- `ShowNotification` — toast notification
- `RunScript` — execute a PowerShell script
- `SendKeyCombo` — simulate keyboard shortcut

### Example rule (JSON)
```json
{
  "Name": "Dock Setup",
  "Enabled": true,
  "Trigger": { "Type": "MonitorCountChanged", "MonitorCount": 3 },
  "Actions": [
    { "Type": "RestoreProfile", "ProfileName": "Triple Monitor", "DelayMs": 500 },
    { "Type": "ShowNotification", "NotificationTitle": "Layout Applied", "NotificationBody": "Triple monitor profile restored." }
  ]
}
```

---

## Profile JSON Example

Stored in `%APPDATA%\MultiMonitorPlatform\Profiles\<name>.json`

```json
{
  "Name": "Office Dual",
  "SavedAt": "2025-01-15T09:00:00Z",
  "Monitors": [
    { "DeviceName": "\\\\.\\DISPLAY1", "X": 0,    "Y": 0, "Width": 2560, "Height": 1440, "IsPrimary": true  },
    { "DeviceName": "\\\\.\\DISPLAY2", "X": 2560, "Y": 0, "Width": 1920, "Height": 1080, "IsPrimary": false }
  ],
  "Wallpapers": {
    "\\\\?\\display#...": "C:\\Wallpapers\\primary.jpg",
    "\\\\?\\display#...": "C:\\Wallpapers\\secondary.jpg"
  },
  "Windows": [
    { "ProcessName": "chrome",   "Title": "Google", "X": 0, "Y": 0, "Width": 1280, "Height": 1440, "ShowCmd": 1, "MonitorId": "DISPLAY1" },
    { "ProcessName": "devenv",   "Title": "Visual", "X": 2560, "Y": 0, "Width": 1920, "Height": 1080, "ShowCmd": 3, "MonitorId": "DISPLAY2" }
  ]
}
```

---

## Per-Monitor Taskbar

Each non-primary monitor gets a `MonitorTaskbar`:
- Renders all windows on **that monitor only**
- Left-click → focus/minimize toggle
- Right-click → context menu (restore, move to monitor…)
- Shows application icons extracted from process EXEs
- Shows clock in the bottom-right corner
- Registers via `SHAppBarMessage` to reserve screen real-estate
- Respects DPI scaling per-monitor

---

## Titlebar Button Injection

`TitlebarInjector` places a transparent layered window (chroma-keyed) on top of each window's title bar, left of the standard caption buttons. Default injected buttons:

1. **Move to next monitor** — cycles the window through all displays
2. **Pin on top** — toggles `WS_EX_TOPMOST`

Custom buttons can be added via `TitlebarInjector.Instance._globalButtons`.

---

## DPI / Windows 11 Notes

- Process is declared `PerMonitorV2` in both `app.manifest` and via `SetProcessDpiAwareness`
- All geometry is queried via `GetDpiForWindow` at time of use, not cached globally
- `WM_DPICHANGED` is handled in `WndProc` overrides
- Taskbar height and button sizes are scaled by `monitor.ScaleFactor`
- `GetMonitorInfo` returns physical pixels; all layout math stays in physical pixels

---

## Data Locations

| Data | Path |
|---|---|
| Profiles | `%APPDATA%\MultiMonitorPlatform\Profiles\` |
| Automation rules | `%APPDATA%\MultiMonitorPlatform\automation_rules.json` |
| Log file | `%APPDATA%\MultiMonitorPlatform\mmp.log` |

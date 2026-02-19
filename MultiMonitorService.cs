using System;
using System.Runtime.InteropServices;
using System.ServiceProcess;
using System.Threading;
using System.Windows.Forms;
using MultiMonitorPlatform.Automation;
using MultiMonitorPlatform.Core;
using MultiMonitorPlatform.Interop;

namespace MultiMonitorPlatform.Service
{
    // ─────────────────────────────────────────────────────────────────────────
    //  ShellHookWindow  –  hidden message-only window that receives shell
    //  notifications via RegisterShellHookWindow()
    // ─────────────────────────────────────────────────────────────────────────
    internal class ShellHookWindow : NativeWindow, IDisposable
    {
        private static readonly uint WM_SHELLHOOK = NativeMethods.RegisterWindowMessage("SHELLHOOK");

        public ShellHookWindow()
        {
            CreateHandle(new CreateParams
            {
                Parent = new IntPtr(-3),  // HWND_MESSAGE
                Caption = "MMP_ShellHook",
            });
            NativeMethods.RegisterShellHookWindow(Handle);
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WM_SHELLHOOK)
            {
                int code  = m.WParam.ToInt32() & 0xFFFF;
                IntPtr hwnd = m.LParam;

                switch (code)
                {
                    case WinConst.HSHELL_WINDOWCREATED:
                        WindowTracker.Instance.OnWindowCreated(hwnd);
                        AutomationEngine.Instance.Fire(new TriggerContext
                        {
                            Type = TriggerType.WindowOpened, WindowHandle = hwnd
                        });
                        break;

                    case WinConst.HSHELL_WINDOWDESTROYED:
                        WindowTracker.Instance.OnWindowDestroyed(hwnd);
                        break;

                    case WinConst.HSHELL_WINDOWACTIVATED:
                        WindowTracker.Instance.OnWindowActivated(hwnd);
                        break;

                    case WinConst.HSHELL_FLASH:
                        UI.TaskbarManager.Instance.FlashButton(hwnd, true);
                        break;
                }
            }
            else if (m.Msg == WinConst.WM_DISPLAYCHANGE)
            {
                Logger.Info("[Service] WM_DISPLAYCHANGE received – refreshing monitors.");
                MonitorManager.Instance.Refresh();
                AutomationEngine.Instance.Fire(new TriggerContext
                {
                    Type = TriggerType.DisplayResolutionChanged,
                    MonitorCount = MonitorManager.Instance.Monitors.Count,
                });
            }

            base.WndProc(ref m);
        }

        public void Dispose()
        {
            NativeMethods.DeregisterShellHookWindow(Handle);
            DestroyHandle();
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  WinPositionHook  –  system-wide CBT hook that intercepts window moves
    //  so the snap engine can respond in real-time
    // ─────────────────────────────────────────────────────────────────────────
    internal static class WinPositionHook
    {
        private static IntPtr _hook = IntPtr.Zero;
        private static HookProc? _proc;

        public static void Install()
        {
            _proc = HookCallback;
            _hook = NativeMethods.SetWindowsHookEx(WinConst.WH_CBT, _proc,
                        NativeMethods.GetModuleHandle(null!), 0);
            Logger.Info(_hook != IntPtr.Zero
                ? "[Hook] CBT hook installed."
                : "[Hook] Failed to install CBT hook.");
        }

        public static void Uninstall()
        {
            if (_hook != IntPtr.Zero)
            {
                NativeMethods.UnhookWindowsHookEx(_hook);
                _hook = IntPtr.Zero;
            }
        }

        private static IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode == WinConst.HCBT_MOVESIZE)
            {
                // Notify the window tracker that this window has moved
                WindowTracker.Instance.OnWindowMoved(wParam);
            }
            return NativeMethods.CallNextHookEx(_hook, nCode, wParam, lParam);
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  MultiMonitorService  –  the Windows Service entry point
    // ─────────────────────────────────────────────────────────────────────────
    public class MultiMonitorService : ServiceBase
    {
        public const string SERVICE_NAME = "MultiMonitorPlatform";

        private Thread?           _msgThread;
        private ShellHookWindow?  _shellHook;
        private bool              _running;

        public MultiMonitorService()
        {
            ServiceName = SERVICE_NAME;
            CanStop     = true;
            CanPauseAndContinue = false;
        }

        protected override void OnStart(string[] args)
        {
            Logger.Info("[Service] Starting...");
            _running = true;

            // DPI awareness (must be before any UI)
            NativeMethods.SetProcessDpiAwareness(WinConst.PROCESS_PER_MONITOR_DPI_AWARE_V2);

            // Initialise core subsystems
            MonitorManager.Instance.Refresh();
            WindowTracker.Instance.RefreshAll();
            AutomationEngine.Instance.WireEvents();
            AutomationEngine.Instance.OnStartup();

            // Auto-restore best matching profile
            var profile = ProfileManager.Instance.FindBestMatch();
            if (profile != null)
            {
                Logger.Info($"[Service] Auto-restoring profile '{profile.Name}'");
                ProfileManager.Instance.Restore(profile);
            }

            // Message pump thread for shell hook + CBT hook
            _msgThread = new Thread(() =>
            {
                _shellHook = new ShellHookWindow();
                WinPositionHook.Install();

                // Standard Win32 message loop
                while (_running)
                {
                    Application.DoEvents();
                    Thread.Sleep(10);
                }

                WinPositionHook.Uninstall();
                _shellHook.Dispose();
            })
            {
                IsBackground = true,
                Name = "MMP_MessagePump",
            };
            _msgThread.SetApartmentState(ApartmentState.STA);
            _msgThread.Start();

            Logger.Info("[Service] Started successfully.");
        }

        protected override void OnStop()
        {
            Logger.Info("[Service] Stopping...");
            _running = false;
            _msgThread?.Join(3000);
            Logger.Info("[Service] Stopped.");
        }

        // ── Entry point (runs as service OR as console for debugging) ─────────
        public static void Main(string[] args)
        {
            // Enable DPI awareness as early as possible
            NativeMethods.SetProcessDpiAwareness(WinConst.PROCESS_PER_MONITOR_DPI_AWARE_V2);

            if (args.Length > 0 && args[0] == "--console")
            {
                // Debug mode: run without SCM
                Console.WriteLine("MultiMonitorPlatform – console mode");
                var svc = new MultiMonitorService();
                svc.OnStart(args);
                Console.WriteLine("Press ENTER to stop.");
                Console.ReadLine();
                svc.OnStop();
            }
            else
            {
                ServiceBase.Run(new MultiMonitorService());
            }
        }
    }
}

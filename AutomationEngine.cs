using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace MultiMonitorPlatform.Automation
{
    // ─────────────────────────────────────────────────────────────────────────
    //  Trigger types
    // ─────────────────────────────────────────────────────────────────────────
    public enum TriggerType
    {
        MonitorConnected,       // a specific monitor (by device name) appears
        MonitorDisconnected,    // a monitor disappears
        MonitorCountChanged,    // total monitor count changes
        WindowOpened,           // a window matching criteria opens
        WindowClosed,
        WindowFocused,
        TimeOfDay,              // fires at a specific HH:mm daily
        AppStartup,             // platform starts
        DisplayResolutionChanged,
        ManualOnly,             // user-triggered via UI button
    }

    public enum ActionType
    {
        RestoreProfile,
        ApplyWallpaper,
        MoveWindowToMonitor,
        SnapWindow,
        LaunchProcess,
        ShowNotification,
        SetMonitorPrimary,
        RunScript,              // PowerShell
        SendKeyCombo,
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  Rule  –  one trigger + ordered list of actions
    // ─────────────────────────────────────────────────────────────────────────
    public class AutomationRule
    {
        public Guid        Id          { get; set; } = Guid.NewGuid();
        public string      Name        { get; set; } = "New Rule";
        public bool        Enabled     { get; set; } = true;
        public TriggerDef  Trigger     { get; set; } = new();
        public List<ActionDef> Actions { get; set; } = new();
        public DateTime?   LastFired   { get; set; }
        public int         FireCount   { get; set; }
    }

    public class TriggerDef
    {
        public TriggerType Type             { get; set; }
        // MonitorConnected/Disconnected
        public string?  MonitorDeviceName   { get; set; }
        // MonitorCountChanged
        public int?     MonitorCount        { get; set; }
        // Window triggers
        public string?  ProcessName         { get; set; }
        public string?  TitleContains       { get; set; }
        // TimeOfDay
        public string?  TimeOfDay           { get; set; }  // "HH:mm"
        // ResolutionChanged
        public int?     TargetWidth         { get; set; }
        public int?     TargetHeight        { get; set; }
    }

    public class ActionDef
    {
        public ActionType Type              { get; set; }
        public string?  ProfileName        { get; set; }
        public string?  WallpaperPath      { get; set; }
        public string?  MonitorDevicePath  { get; set; }
        public string?  ProcessName        { get; set; }
        public string?  SnapZone           { get; set; }
        public string?  ScriptPath         { get; set; }
        public string?  NotificationTitle  { get; set; }
        public string?  NotificationBody   { get; set; }
        public string?  KeyCombo           { get; set; }
        public int      DelayMs            { get; set; }
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  Context passed to actions so they can query runtime state
    // ─────────────────────────────────────────────────────────────────────────
    public class TriggerContext
    {
        public TriggerType       Type        { get; init; }
        public string?           MonitorId   { get; init; }
        public string?           ProcessName { get; init; }
        public IntPtr            WindowHandle{ get; init; }
        public int               MonitorCount{ get; init; }
        public DateTime          FiredAt     { get; init; } = DateTime.Now;
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  AutomationEngine  –  evaluates rules against incoming events
    // ─────────────────────────────────────────────────────────────────────────
    public class AutomationEngine
    {
        public static AutomationEngine Instance { get; } = new();
        private AutomationEngine() => LoadRules();

        private List<AutomationRule> _rules = new();
        public  IReadOnlyList<AutomationRule> Rules => _rules;

        private readonly string _rulesPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "MultiMonitorPlatform", "automation_rules.json");

        private static readonly JsonSerializerOptions _json = new()
        {
            WriteIndented          = true,
            Converters             = { new JsonStringEnumConverter() },
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        };

        // ── Wiring up to core events ───────────────────────────────────────────
        public void WireEvents()
        {
            MonitorManager.Instance.DisplayConfigurationChanged += (_, _) =>
            {
                int count = MonitorManager.Instance.Monitors.Count;
                Fire(new TriggerContext { Type = TriggerType.MonitorCountChanged, MonitorCount = count });
            };

            WindowTracker.Instance.WindowAdded += (_, wd) =>
                Fire(new TriggerContext
                {
                    Type = TriggerType.WindowOpened,
                    ProcessName  = wd.ProcessName,
                    WindowHandle = wd.Handle,
                });

            WindowTracker.Instance.WindowRemoved += (_, wd) =>
                Fire(new TriggerContext
                {
                    Type = TriggerType.WindowClosed,
                    ProcessName  = wd.ProcessName,
                    WindowHandle = wd.Handle,
                });
        }

        /// <summary>Fire on startup.</summary>
        public void OnStartup() =>
            Fire(new TriggerContext { Type = TriggerType.AppStartup });

        // ── Core dispatch ─────────────────────────────────────────────────────
        public void Fire(TriggerContext ctx)
        {
            foreach (var rule in _rules.Where(r => r.Enabled && Matches(r.Trigger, ctx)))
            {
                Logger.Info($"[Automation] Rule '{rule.Name}' fired.");
                rule.LastFired = DateTime.Now;
                rule.FireCount++;
                _ = ExecuteActionsAsync(rule.Actions, ctx);
            }
        }

        // ── Matching ──────────────────────────────────────────────────────────
        private static bool Matches(TriggerDef trigger, TriggerContext ctx)
        {
            if (trigger.Type != ctx.Type) return false;
            return trigger.Type switch
            {
                TriggerType.MonitorConnected or
                TriggerType.MonitorDisconnected =>
                    string.IsNullOrEmpty(trigger.MonitorDeviceName) ||
                    trigger.MonitorDeviceName == ctx.MonitorId,

                TriggerType.MonitorCountChanged =>
                    !trigger.MonitorCount.HasValue || trigger.MonitorCount == ctx.MonitorCount,

                TriggerType.WindowOpened or
                TriggerType.WindowClosed or
                TriggerType.WindowFocused =>
                    (string.IsNullOrEmpty(trigger.ProcessName) ||
                     ctx.ProcessName?.Contains(trigger.ProcessName, StringComparison.OrdinalIgnoreCase) == true),

                _ => true,
            };
        }

        // ── Action execution ──────────────────────────────────────────────────
        private async Task ExecuteActionsAsync(IEnumerable<ActionDef> actions, TriggerContext ctx)
        {
            foreach (var action in actions)
            {
                if (action.DelayMs > 0) await Task.Delay(action.DelayMs);
                try { ExecuteAction(action, ctx); }
                catch (Exception ex) { Logger.Error($"[Automation] Action {action.Type} failed: {ex.Message}"); }
            }
        }

        private static void ExecuteAction(ActionDef action, TriggerContext ctx)
        {
            switch (action.Type)
            {
                case ActionType.RestoreProfile when action.ProfileName != null:
                    ProfileManager.Instance.Restore(action.ProfileName);
                    break;

                case ActionType.ApplyWallpaper when action.MonitorDevicePath != null && action.WallpaperPath != null:
                    WallpaperManager.SetWallpaper(action.MonitorDevicePath, action.WallpaperPath);
                    break;

                case ActionType.MoveWindowToMonitor when ctx.WindowHandle != IntPtr.Zero && action.MonitorDevicePath != null:
                    var dest = MonitorManager.Instance.Monitors
                                   .FirstOrDefault(m => m.DeviceName.Contains(action.MonitorDevicePath));
                    if (dest != null) SnapEngine.MoveToMonitor(ctx.WindowHandle, dest);
                    break;

                case ActionType.LaunchProcess when action.ProcessName != null:
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                    {
                        FileName        = action.ProcessName,
                        UseShellExecute = true,
                    });
                    break;

                case ActionType.ShowNotification:
                    NotificationService.Show(
                        action.NotificationTitle ?? "MultiMonitor",
                        action.NotificationBody  ?? "");
                    break;

                case ActionType.RunScript when action.ScriptPath != null:
                    System.Diagnostics.Process.Start("powershell.exe", $"-NonInteractive -File \"{action.ScriptPath}\"");
                    break;

                default:
                    Logger.Warn($"Unhandled action type: {action.Type}");
                    break;
            }
        }

        // ── Persistence ───────────────────────────────────────────────────────
        public void LoadRules()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_rulesPath)!);
            if (!File.Exists(_rulesPath)) { _rules = new(); return; }
            try
            {
                string json = File.ReadAllText(_rulesPath);
                _rules = JsonSerializer.Deserialize<List<AutomationRule>>(json, _json) ?? new();
            }
            catch (Exception ex) { Logger.Error($"AutomationEngine.LoadRules: {ex.Message}"); _rules = new(); }
        }

        public void SaveRules()
        {
            string json = JsonSerializer.Serialize(_rules, _json);
            File.WriteAllText(_rulesPath, json);
        }

        public void AddRule(AutomationRule rule)   { _rules.Add(rule);         SaveRules(); }
        public void RemoveRule(Guid id)            { _rules.RemoveAll(r => r.Id == id); SaveRules(); }
        public void UpdateRule(AutomationRule rule){ RemoveRule(rule.Id); AddRule(rule); }
    }

    // Stubs for services used by actions (real impls in service layer)
    public static class NotificationService
    {
        public static void Show(string title, string body) =>
            Logger.Info($"[Notification] {title}: {body}");
    }

    // Re-export core types so automation namespace can use them
    using Core;
}

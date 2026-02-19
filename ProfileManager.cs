using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using MultiMonitorPlatform.Interop;

namespace MultiMonitorPlatform.Core
{
    // ─────────────────────────────────────────────────────────────────────────
    //  Data model for a saved monitor+window layout
    // ─────────────────────────────────────────────────────────────────────────
    public class ProfileMonitor
    {
        public string   DeviceName    { get; set; } = "";
        public int      X             { get; set; }
        public int      Y             { get; set; }
        public int      Width         { get; set; }
        public int      Height        { get; set; }
        public bool     IsPrimary     { get; set; }
        public string   WallpaperPath { get; set; } = "";
        public string   WallpaperDevicePath { get; set; } = "";
    }

    public class ProfileWindow
    {
        public string   ProcessName   { get; set; } = "";
        public string   Title         { get; set; } = "";
        public int      X             { get; set; }
        public int      Y             { get; set; }
        public int      Width         { get; set; }
        public int      Height        { get; set; }
        public uint     ShowCmd       { get; set; }
        public string   MonitorId     { get; set; } = "";
    }

    public class MonitorProfile
    {
        public string              Name       { get; set; } = "Default";
        public DateTime            SavedAt    { get; set; }
        public List<ProfileMonitor> Monitors  { get; set; } = new();
        public List<ProfileWindow>  Windows   { get; set; } = new();
        public Dictionary<string, string> Wallpapers { get; set; } = new(); // monitorDevicePath → imagePath
        public Dictionary<string, string> TaskbarSettings { get; set; } = new(); // monitorId → settings JSON
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  ProfileManager  –  save / load / restore profiles
    // ─────────────────────────────────────────────────────────────────────────
    public class ProfileManager
    {
        public static ProfileManager Instance { get; } = new();

        private readonly string _profileDir;
        private static readonly JsonSerializerOptions _json = new()
        {
            WriteIndented          = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        };

        private ProfileManager()
        {
            _profileDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "MultiMonitorPlatform", "Profiles");
            Directory.CreateDirectory(_profileDir);
        }

        // ── Enumerate ──────────────────────────────────────────────────────────
        public IEnumerable<string> GetProfileNames() =>
            Directory.EnumerateFiles(_profileDir, "*.json")
                     .Select(f => Path.GetFileNameWithoutExtension(f));

        public MonitorProfile? Load(string name)
        {
            string path = ProfilePath(name);
            if (!File.Exists(path)) return null;
            try
            {
                string json = File.ReadAllText(path);
                return JsonSerializer.Deserialize<MonitorProfile>(json, _json);
            }
            catch (Exception ex) { Logger.Error($"ProfileManager.Load: {ex.Message}"); return null; }
        }

        // ── Capture current state ──────────────────────────────────────────────
        public MonitorProfile Capture(string name)
        {
            var profile = new MonitorProfile
            {
                Name    = name,
                SavedAt = DateTime.UtcNow,
            };

            // Monitors
            foreach (var m in MonitorManager.Instance.Monitors)
            {
                profile.Monitors.Add(new ProfileMonitor
                {
                    DeviceName = m.DeviceName,
                    X          = m.MonitorRect.X,
                    Y          = m.MonitorRect.Y,
                    Width      = m.MonitorRect.Width,
                    Height     = m.MonitorRect.Height,
                    IsPrimary  = m.IsPrimary,
                });
            }

            // Wallpapers
            foreach (string devPath in WallpaperManager.GetMonitorDevicePaths())
            {
                string wallpaper = WallpaperManager.GetWallpaper(devPath);
                if (!string.IsNullOrEmpty(wallpaper))
                    profile.Wallpapers[devPath] = wallpaper;
            }

            // Windows
            foreach (var wd in WindowTracker.Instance.Windows)
            {
                NativeMethods.GetWindowRect(wd.Handle, out RECT r);
                profile.Windows.Add(new ProfileWindow
                {
                    ProcessName = wd.ProcessName,
                    Title       = wd.Title,
                    X           = r.Left,
                    Y           = r.Top,
                    Width       = r.Right  - r.Left,
                    Height      = r.Bottom - r.Top,
                    ShowCmd     = wd.ShowCmd,
                    MonitorId   = wd.MonitorId ?? "",
                });
            }

            return profile;
        }

        public void Save(MonitorProfile profile)
        {
            string json = JsonSerializer.Serialize(profile, _json);
            File.WriteAllText(ProfilePath(profile.Name), json);
        }

        // ── Restore ────────────────────────────────────────────────────────────
        public void Restore(string name)
        {
            var profile = Load(name);
            if (profile == null) { Logger.Warn($"Profile '{name}' not found."); return; }
            Restore(profile);
        }

        public void Restore(MonitorProfile profile)
        {
            // 1. Restore wallpapers
            WallpaperManager.ApplyProfile(profile.Wallpapers);

            // 2. Restore window positions
            //    Match heuristic: process name + fuzzy title
            var living = WindowTracker.Instance.Windows.ToList();

            foreach (var pw in profile.Windows)
            {
                var match = living.Find(w =>
                    w.ProcessName.Equals(pw.ProcessName, StringComparison.OrdinalIgnoreCase) &&
                    w.Title.Contains(pw.Title.Length > 10 ? pw.Title[..10] : pw.Title,
                        StringComparison.OrdinalIgnoreCase));

                if (match == null) continue;

                var placement = WINDOWPLACEMENT.Create();
                NativeMethods.GetWindowPlacement(match.Handle, ref placement);
                placement.showCmd         = pw.ShowCmd;
                placement.rcNormalPosition = new RECT
                {
                    Left   = pw.X,
                    Top    = pw.Y,
                    Right  = pw.X + pw.Width,
                    Bottom = pw.Y + pw.Height,
                };
                NativeMethods.SetWindowPlacement(match.Handle, ref placement);
            }

            Logger.Info($"Profile '{profile.Name}' restored.");
        }

        // ── Auto-matching profiles ─────────────────────────────────────────────
        /// <summary>Find the best saved profile for the current monitor topology.</summary>
        public MonitorProfile? FindBestMatch()
        {
            var current = MonitorManager.Instance.Monitors
                              .Select(m => m.DeviceName)
                              .OrderBy(d => d)
                              .ToArray();

            MonitorProfile? best = null;
            int bestScore = 0;

            foreach (string name in GetProfileNames())
            {
                var p = Load(name);
                if (p == null) continue;
                var saved = p.Monitors.Select(m => m.DeviceName).OrderBy(d => d).ToArray();
                int score = current.Intersect(saved).Count();
                if (score > bestScore) { bestScore = score; best = p; }
            }
            return best;
        }

        private string ProfilePath(string name) =>
            Path.Combine(_profileDir, $"{name.Replace(' ', '_')}.json");
    }
}

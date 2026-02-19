using System;
using System.IO;

namespace MultiMonitorPlatform
{
    public static class Logger
    {
        private static readonly string _logPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "MultiMonitorPlatform", "mmp.log");

        private static readonly object _lock = new();

        static Logger()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_logPath)!);
        }

        public static void Info(string msg)  => Write("INFO ", msg);
        public static void Warn(string msg)  => Write("WARN ", msg);
        public static void Error(string msg) => Write("ERROR", msg);

        private static void Write(string level, string msg)
        {
            string line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{level}] {msg}";
            Console.WriteLine(line);
            lock (_lock)
            {
                try { File.AppendAllText(_logPath, line + Environment.NewLine); }
                catch { /* best-effort */ }
            }
        }
    }
}

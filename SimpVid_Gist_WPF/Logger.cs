using System.IO;
using System.Text;

namespace SimpVid_Gist_WPF
{
    public static class Logger
    {
        private static readonly string LogPath =
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "simpvidlog.txt");

        private static readonly object _lock = new();

        public static void Log(string message)
        {
            try
            {
                lock (_lock)
                {
                    string line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] {message}";
                    File.AppendAllText(LogPath, line + Environment.NewLine, Encoding.UTF8);
                }
            }
            catch { /* ignore logging failures */ }
        }

        public static void LogError(string context, Exception ex)
        {
            Log($"{context}: {ex?.GetType().Name ?? "null"}: {ex?.Message ?? "null"}");
            Log($"Stack Trace:\n{ex?.StackTrace ?? "null"}");
            if (ex?.InnerException != null)
                Log($"Inner: {ex.InnerException.GetType().Name}: {ex.InnerException.Message}");
        }

        public static void Separate()
        {
            Log(new string('=', 60));
        }
    }
}

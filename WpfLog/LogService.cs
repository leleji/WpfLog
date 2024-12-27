using System.Windows.Media;

namespace WpfLog
{
    public class LogService : ILogService
    {
        private readonly LogViewer _logViewer;

        public LogService(LogViewer logViewer)
        {
            _logViewer = logViewer;
        }

        public void Log(string message, Brush color = null)
        {
            _logViewer.AddLog(message, color);
        }

        public void LogInfo(string message) => Log(message, Brushes.White);
        public void LogWarning(string message) => Log(message, Brushes.Yellow);
        public void LogError(string message) => Log(message, Brushes.Red);
        public void LogDebug(string message) => Log(message, Brushes.Gray);
        public void Clear() => _logViewer.Clear();
    }
} 
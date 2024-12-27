using System.Windows.Media;


namespace WpfLog
{
    public interface ILogOutput
    {
        Action<string, Brush> LogHandler { get; set; }
        void LogInfo(string message);
        void LogWarning(string message);
        void LogError(string message);
        void LogDebug(string message);
        void LogSuccess(string message);
        void Log(string message, Brush color);
        void Clear();
    }

    public class LogOutput : ILogOutput
    {
        public Action<string, Brush> LogHandler { get; set; }

        public void LogInfo(string message) => LogHandler?.Invoke(message, Brushes.White);
        public void LogWarning(string message) => LogHandler?.Invoke(message, Brushes.Yellow);
        public void LogError(string message) => LogHandler?.Invoke(message, Brushes.Red);
        public void LogDebug(string message) => LogHandler?.Invoke(message, Brushes.Gray);
        public void LogSuccess(string message) => LogHandler?.Invoke(message, Brushes.Green);
        public void Log(string message, Brush color) => LogHandler?.Invoke(message, color);
        public void Clear() => LogHandler?.Invoke(null, null);
    }
}
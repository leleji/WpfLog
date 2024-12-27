using System.Windows.Media;

namespace WpfLog
{
    public interface ILogService
    {
        void Log(string message, Brush color = null);
        void LogInfo(string message);
        void LogWarning(string message);
        void LogError(string message);
        void LogDebug(string message);
        void Clear();
    }
} 
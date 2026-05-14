namespace WpfLog.Core;

public interface ILogOutput
{
    Action<LogEntry>? LogHandler { get; set; }

    void LogInfo(string message);
    void LogWarning(string message);
    void LogError(string message);
    void LogDebug(string message);
    void LogSuccess(string message);

    void Log(LogLevel level, string message);
    void Log(LogColor color, string message);
    void Log(LogLevel level, LogColor color, string message);

    void Clear();
}

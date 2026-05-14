namespace WpfLog.Core;

public enum LogEventType
{
    Append,
    Clear
}

public sealed record LogEntry(
    LogEventType Type,
    string? Message = null,
    LogLevel Level = LogLevel.Info,
    LogColor? Foreground = null,
    DateTimeOffset? Timestamp = null)
{
    public static LogEntry Append(string message, LogLevel level, LogColor? foreground = null, DateTimeOffset? timestamp = null)
        => new(LogEventType.Append, message, level, foreground, timestamp ?? DateTimeOffset.Now);

    public static LogEntry Clear() => new(LogEventType.Clear);
}

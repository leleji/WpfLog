namespace WpfLog.Core;

public abstract class LogOutputBase : ILogOutput
{
    private readonly Queue<LogEntry> _pendingLogs = new();
    private Action<LogEntry>? _logHandler;

    public Action<LogEntry>? LogHandler
    {
        get => _logHandler;
        set
        {
            _logHandler = value;
            FlushPendingLogs();
        }
    }

    protected abstract void Dispatch(Action action);

    public void LogInfo(string message) => Log(LogLevel.Info, message);
    public void LogWarning(string message) => Log(LogLevel.Warning, message);
    public void LogError(string message) => Log(LogLevel.Error, message);
    public void LogDebug(string message) => Log(LogLevel.Debug, message);
    public void LogSuccess(string message) => Log(LogLevel.Success, message);

    public void Log(LogLevel level, string message) => Publish(LogEntry.Append(message, level));
    public void Log(LogColor color, string message) => Publish(LogEntry.Append(message, LogLevel.Info, color));
    public void Log(LogLevel level, LogColor color, string message) => Publish(LogEntry.Append(message, level, color));
    public void Clear() => Publish(LogEntry.Clear());

    protected void Publish(LogEntry entry)
    {
        if (_logHandler == null)
        {
            lock (_pendingLogs)
            {
                _pendingLogs.Enqueue(entry);
            }
            return;
        }

        Dispatch(() => _logHandler?.Invoke(entry));
    }

    private void FlushPendingLogs()
    {
        if (_logHandler == null)
            return;

        List<LogEntry> pending;
        lock (_pendingLogs)
        {
            if (_pendingLogs.Count == 0)
                return;

            pending = new List<LogEntry>(_pendingLogs.Count);
            while (_pendingLogs.Count > 0)
                pending.Add(_pendingLogs.Dequeue());
        }

        foreach (var entry in pending)
            Publish(entry);
    }
}

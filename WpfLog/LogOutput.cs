using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using WpfLog.Core;


namespace WpfLog
{
    public class LogOutput : ILogOutput
    {
        private readonly Dispatcher _dispatcher;
        private readonly Queue<(string Message, LogColor Color)> _pendingLogs = new();
        private Action<string, LogColor> _logHandler;

        public LogOutput()
        {
            _dispatcher = Application.Current?.Dispatcher ?? Dispatcher.CurrentDispatcher;
        }

        public Action<string, LogColor> LogHandler
        {
            get => _logHandler;
            set
            {
                _logHandler = value;
                if (_logHandler != null)
                {
                    lock (_pendingLogs)
                    {
                        while (_pendingLogs.Count > 0)
                        {
                            var (msg, col) = _pendingLogs.Dequeue();
                            InvokeLog(msg, col);
                        }
                    }
                }
            }
        }

        private void InvokeLog(string message, LogColor color)
        {
            if (_logHandler == null)
            {
                lock (_pendingLogs) { _pendingLogs.Enqueue((message, color)); }
                return;
            }

            if (_dispatcher.CheckAccess())
                _logHandler.Invoke(message, color);
            else
                _dispatcher.BeginInvoke(new Action(() => _logHandler.Invoke(message, color)));
        }

        public void LogInfo(string message) => Log(LogColor.White, message);
        public void LogWarning(string message) => Log(LogColor.Yellow, message);
        public void LogError(string message) => Log(LogColor.Red, message);
        public void LogDebug(string message) => Log(LogColor.Gray, message);
        public void LogSuccess(string message) => Log(LogColor.Green, message);
        public void Log(LogColor color, string message) => InvokeLog(message, color);
        public void Clear() => InvokeLog(null, LogColor.White);
    }
}
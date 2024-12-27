using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;


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
        void Log(Brush color, string message);
        void Clear();
    }

    public class LogOutput : ILogOutput
    {
        private readonly Dispatcher _dispatcher;
        private readonly Queue<(string Message, Brush Color)> _pendingLogs = new();
        private Action<string, Brush> _logHandler;

        public LogOutput()
        {
            _dispatcher = Application.Current.Dispatcher;
        }

        public Action<string, Brush> LogHandler
        {
            get => _logHandler;
            set
            {
                _logHandler = value;
                // 当 LogHandler 被设置时，输出所有待处理的日志
                if (_logHandler != null)
                {
                    while (_pendingLogs.Count > 0)
                    {
                        var (message, color) = _pendingLogs.Dequeue();
                        InvokeOnUIThread(message, color);
                    }
                }
            }
        }

        private void InvokeOnUIThread(string message, Brush color)
        {
            if (_logHandler == null)
            {
                // 如果 LogHandler 还没有设置，将日志消息加入队列
                _pendingLogs.Enqueue((message, color));
                return;
            }

            if (_dispatcher.CheckAccess())
            {
                _logHandler.Invoke(message, color);
            }
            else
            {
                _dispatcher.BeginInvoke(new Action(() => _logHandler.Invoke(message, color)));
            }
        }


        public void LogInfo(string message) => InvokeOnUIThread(message, Brushes.White);
        public void LogWarning(string message) => InvokeOnUIThread(message, Brushes.Yellow);
        public void LogError(string message) => InvokeOnUIThread(message, Brushes.Red);
        public void LogDebug(string message) => InvokeOnUIThread(message, Brushes.Gray);
        public void LogSuccess(string message) => InvokeOnUIThread(message, Brushes.Green);
        public void Log(Brush color,string message) => InvokeOnUIThread(message, color);
        public void Clear() => LogHandler?.Invoke(null, null);

    
    }
}
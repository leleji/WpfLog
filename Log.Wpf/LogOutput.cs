using System.Windows;
using System.Windows.Threading;
using WpfLog.Core;

namespace WpfLog
{
    public class LogOutput : LogOutputBase
    {
        private readonly Dispatcher _dispatcher;

        public LogOutput()
        {
            _dispatcher = Application.Current?.Dispatcher ?? Dispatcher.CurrentDispatcher;
        }

        protected override void Dispatch(Action action)
        {
            if (_dispatcher.CheckAccess())
                action();
            else
                _dispatcher.BeginInvoke(action);
        }
    }
}

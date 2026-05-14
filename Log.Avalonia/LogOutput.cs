using Avalonia.Threading;
using WpfLog.Core;

namespace AvaLog;

public sealed class LogOutput : LogOutputBase
{
    protected override void Dispatch(Action action)
    {
        if (Dispatcher.UIThread.CheckAccess())
            action();
        else
            Dispatcher.UIThread.Post(action);
    }
}

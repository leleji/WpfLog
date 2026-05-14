using System.Windows.Media;
using WpfLog.Core;

namespace WpfLog;

internal sealed class LogLine : ILogViewLine
{
    public LogLine(string text, Brush foreground)
    {
        Text = text;
        Foreground = foreground;
    }

    public string Text { get; }
    public Brush Foreground { get; }
    public double Height { get; set; }
    public double LayoutWidth { get; set; }
    public FormattedText? Layout { get; set; }
}

using Avalonia.Media;

namespace AvaLog;

public sealed class LogLine
{
    public LogLine(string text, IBrush foreground)
    {
        Text = text;
        Foreground = foreground;
    }

    public string Text { get; }
    public IBrush Foreground { get; }
}

using Avalonia.Media;
using Avalonia.Media.TextFormatting;

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
    public double Height { get; set; }
    public double LayoutWidth { get; set; }
    public TextLayout? Layout { get; set; }
}

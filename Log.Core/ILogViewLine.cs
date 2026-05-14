namespace WpfLog.Core;

public interface ILogViewLine
{
    string Text { get; }
    double Height { get; set; }
    double LayoutWidth { get; set; }
}

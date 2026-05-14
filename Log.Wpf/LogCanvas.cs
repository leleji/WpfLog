using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Media;
using WpfLog.Core;

namespace WpfLog;

internal sealed class LogCanvas : FrameworkElement
{
    private IReadOnlyList<LogLine>? _lines;
    private IReadOnlyList<double>? _lineTops;
    private IReadOnlySet<int>? _selectedIndices;
    private Typeface _typeface = new("Microsoft YaHei UI");
    private double _fontSize = 12;
    private double _pixelsPerDip = 1;
    private double _totalHeight;

    private static readonly Brush SelectionBrush = new SolidColorBrush(Color.FromArgb(80, 0, 120, 215));

    static LogCanvas()
    {
        SelectionBrush.Freeze();
    }

    public double VerticalOffset { get; private set; }
    public double ViewportHeight { get; private set; }

    public void UpdateView(
        IReadOnlyList<LogLine> lines,
        IReadOnlyList<double> lineTops,
        double totalHeight,
        double verticalOffset,
        double viewportHeight,
        Typeface typeface,
        double fontSize,
        double pixelsPerDip,
        IReadOnlySet<int> selectedIndices)
    {
        _lines = lines;
        _lineTops = lineTops;
        _totalHeight = totalHeight;
        VerticalOffset = verticalOffset;
        ViewportHeight = viewportHeight;
        _typeface = typeface;
        _fontSize = fontSize;
        _pixelsPerDip = pixelsPerDip;
        _selectedIndices = selectedIndices;
        Height = totalHeight;
        InvalidateMeasure();
        InvalidateVisual();
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var width = double.IsInfinity(availableSize.Width) ? ActualWidth : availableSize.Width;
        return new Size(width, _totalHeight);
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);

        if (_lines == null || _lineTops == null || _lines.Count == 0 || _lineTops.Count != _lines.Count)
            return;

        var width = Math.Max(1, ActualWidth);
        var viewTop = VerticalOffset;
        var viewBottom = viewTop + ViewportHeight;
        var startIndex = LogViewportState<LogLine>.FindStartIndex(_lines, _lineTops, viewTop);
        if (startIndex < 0)
            return;

        for (var i = startIndex; i < _lines.Count; i++)
        {
            var y = _lineTops[i];
            if (y > viewBottom)
                break;

            var line = _lines[i];
            if (_selectedIndices != null && _selectedIndices.Contains(i))
                drawingContext.DrawRectangle(SelectionBrush, null, new Rect(0, y, width, line.Height));

            var layout = line.Layout;
            if (layout == null || Math.Abs(line.LayoutWidth - width) >= 1)
                layout = CreateLayout(line.Text, width, line.Foreground);

            drawingContext.DrawText(layout, new Point(0, y));
        }
    }

    private FormattedText CreateLayout(string text, double width, Brush foreground)
    {
        return new FormattedText(
            text,
            CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            _typeface,
            _fontSize,
            foreground,
            _pixelsPerDip)
        {
            MaxTextWidth = Math.Max(1, width),
            TextAlignment = TextAlignment.Left
        };
    }
}

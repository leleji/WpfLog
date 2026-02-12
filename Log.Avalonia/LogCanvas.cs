using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.TextFormatting;

namespace AvaLog;

public sealed class LogCanvas : Control
{
    private IReadOnlyList<LogLine>? _lines;
    private IReadOnlyList<double>? _lineTops;
    private double _totalHeight;
    private Typeface _typeface = new(FontFamily.Default);
    private double _fontSize = 12;
    private IReadOnlySet<int>? _selectedIndices;
    private static readonly IBrush SelectionBrush = new SolidColorBrush(Color.FromArgb(80, 0, 120, 215));

    public double VerticalOffset { get; set; }
    public double ViewportHeight { get; set; }

    public void UpdateView(
        IReadOnlyList<LogLine> lines,
        IReadOnlyList<double> lineTops,
        double totalHeight,
        double verticalOffset,
        double viewportHeight,
        Typeface typeface,
        double fontSize,
        IReadOnlySet<int> selectedIndices)
    {
        _lines = lines;
        _lineTops = lineTops;
        _totalHeight = totalHeight;
        VerticalOffset = verticalOffset;
        ViewportHeight = viewportHeight;
        _typeface = typeface;
        _fontSize = fontSize;
        _selectedIndices = selectedIndices;
        Height = totalHeight;
        InvalidateMeasure();
        InvalidateVisual();
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var width = double.IsInfinity(availableSize.Width) ? Bounds.Width : availableSize.Width;
        return new Size(width, _totalHeight);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        return new Size(finalSize.Width, _totalHeight);
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);

        if (_lines == null || _lineTops == null || _lines.Count == 0)
            return;

        if (_lineTops.Count != _lines.Count)
            return;

        var width = Math.Max(1, Bounds.Width);
        var viewTop = VerticalOffset;
        var viewBottom = viewTop + ViewportHeight;

        var startIndex = FindStartIndex(viewTop);
        if (startIndex < 0) return;

        for (var i = startIndex; i < _lines.Count; i++)
        {
            var line = _lines[i];
            var y = _lineTops[i];

            if (y > viewBottom)
                break;

            if (_selectedIndices != null && _selectedIndices.Contains(i))
                context.FillRectangle(SelectionBrush, new Rect(0, y, width, line.Height));

            if (line.Layout != null && Math.Abs(line.LayoutWidth - width) < 1)
            {
                line.Layout.Draw(context, new Point(0, y));
            }
            else
            {
                var layout = new TextLayout(
                    line.Text,
                    _typeface,
                    _fontSize,
                    line.Foreground,
                    TextAlignment.Left,
                    TextWrapping.Wrap,
                    maxWidth: width);

                layout.Draw(context, new Point(0, y));
            }
        }
    }

    private int FindStartIndex(double viewTop)
    {
        if (_lineTops == null || _lineTops.Count == 0)
            return -1;

        var low = 0;
        var high = _lineTops.Count - 1;
        while (low <= high)
        {
            var mid = (low + high) / 2;
            var midTop = _lineTops[mid];
            var midBottom = midTop + _lines![mid].Height;

            if (midBottom < viewTop)
                low = mid + 1;
            else if (midTop > viewTop)
                high = mid - 1;
            else
                return mid;
        }

        return Math.Clamp(low, 0, _lineTops.Count - 1);
    }
}

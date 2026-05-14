using System.Collections.Concurrent;

namespace WpfLog.Core;

public sealed class LogViewportState<TLine> where TLine : ILogViewLine
{
    private readonly ConcurrentQueue<TLine> _pendingLines = new();
    private readonly List<TLine> _lines = new();
    private readonly List<double> _lineTops = new();
    private readonly HashSet<int> _selectedIndices = new();

    private double _totalHeight;
    private double _lastLayoutWidth;
    private bool _needsRebuild;

    public IReadOnlyList<TLine> Lines => _lines;
    public IReadOnlyList<double> LineTops => _lineTops;
    public IReadOnlySet<int> SelectedIndices => _selectedIndices;
    public double TotalHeight => _totalHeight;
    public double LastLayoutWidth => _lastLayoutWidth;
    public bool NeedsRebuild => _needsRebuild;
    public bool HasPending => !_pendingLines.IsEmpty;
    public int Count => _lines.Count;

    public void Enqueue(TLine line)
    {
        _pendingLines.Enqueue(line);
    }

    public void Clear()
    {
        _pendingLines.Clear();
        _lines.Clear();
        _lineTops.Clear();
        _selectedIndices.Clear();
        _totalHeight = 0;
        _lastLayoutWidth = 0;
        _needsRebuild = false;
    }

    public bool Drain(int maxCount, double width, Action<TLine, double> measureLine)
    {
        var drained = false;
        var count = 0;
        while (count++ < maxCount && _pendingLines.TryDequeue(out var line))
        {
            AddLine(line, width, measureLine);
            drained = true;
        }

        return drained;
    }

    public void RequestRebuild()
    {
        _needsRebuild = true;
    }

    public bool RebuildIfNeeded(double width, Action<TLine, double> measureLine)
    {
        if (!_needsRebuild || !IsValidWidth(width))
            return false;

        Rebuild(width, measureLine);
        _needsRebuild = false;
        return true;
    }

    public void Rebuild(double width, Action<TLine, double> measureLine)
    {
        if (!IsValidWidth(width))
        {
            _needsRebuild = true;
            return;
        }

        _lineTops.Clear();
        _totalHeight = 0;
        foreach (var line in _lines)
        {
            measureLine(line, width);
            _lineTops.Add(_totalHeight);
            _totalHeight += line.Height;
        }

        _lastLayoutWidth = width;
    }

    public bool TrimIfNeeded(int maxEntries, int retainEntries)
    {
        maxEntries = Math.Max(0, maxEntries);
        if (_lines.Count <= maxEntries)
            return false;

        var retain = Math.Clamp(retainEntries, 0, maxEntries);
        var removeCount = _lines.Count - retain;
        if (removeCount <= 0)
            return false;

        _lines.RemoveRange(0, removeCount);
        _selectedIndices.Clear();
        RecalculateLineTops();
        return true;
    }

    public int HitTest(double y)
    {
        if (_lines.Count == 0 || _lineTops.Count != _lines.Count)
            return -1;

        var low = 0;
        var high = _lineTops.Count - 1;
        while (low <= high)
        {
            var mid = (low + high) / 2;
            var top = _lineTops[mid];
            var bottom = top + _lines[mid].Height;

            if (y < top)
                high = mid - 1;
            else if (y > bottom)
                low = mid + 1;
            else
                return mid;
        }

        return -1;
    }

    public int FindStartIndex(double viewTop) => FindStartIndex(_lines, _lineTops, viewTop);

    public static int FindStartIndex(IReadOnlyList<TLine>? lines, IReadOnlyList<double>? lineTops, double viewTop)
    {
        if (lines == null || lineTops == null || lineTops.Count == 0 || lineTops.Count != lines.Count)
            return -1;

        var low = 0;
        var high = lineTops.Count - 1;
        while (low <= high)
        {
            var mid = (low + high) / 2;
            var top = lineTops[mid];
            var bottom = top + lines[mid].Height;

            if (bottom < viewTop)
                low = mid + 1;
            else if (top > viewTop)
                high = mid - 1;
            else
                return mid;
        }

        return Math.Clamp(low, 0, lineTops.Count - 1);
    }

    public bool ToggleSelection(int index)
    {
        if (index < 0 || index >= _lines.Count)
            return false;

        if (!_selectedIndices.Add(index))
            _selectedIndices.Remove(index);

        return true;
    }

    public void ClearSelection()
    {
        _selectedIndices.Clear();
    }

    public void RangeSelection(int start, int end)
    {
        _selectedIndices.Clear();
        if (start < 0 || end < 0)
            return;

        var min = Math.Min(start, end);
        var max = Math.Max(start, end);
        for (var i = min; i <= max && i < _lines.Count; i++)
            _selectedIndices.Add(i);
    }

    private void AddLine(TLine line, double width, Action<TLine, double> measureLine)
    {
        _lines.Add(line);

        if (!IsValidWidth(width))
        {
            _needsRebuild = true;
            return;
        }

        if (Math.Abs(width - _lastLayoutWidth) >= 1 && _lines.Count > 1)
        {
            _needsRebuild = true;
            return;
        }

        measureLine(line, width);
        _lineTops.Add(_totalHeight);
        _totalHeight += line.Height;
        _lastLayoutWidth = width;
    }

    private static bool IsValidWidth(double width)
    {
        return width > 0 && !double.IsNaN(width) && !double.IsInfinity(width);
    }

    private void RecalculateLineTops()
    {
        _lineTops.Clear();
        _totalHeight = 0;
        foreach (var line in _lines)
        {
            _lineTops.Add(_totalHeight);
            _totalHeight += line.Height;
        }
    }
}

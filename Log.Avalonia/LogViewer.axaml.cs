using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.TextFormatting;
using Avalonia.Threading;
using WpfLog.Core;

namespace AvaLog;

public partial class LogViewer : UserControl
{
    public static readonly StyledProperty<int> MaxLogEntriesProperty =
        AvaloniaProperty.Register<LogViewer, int>(nameof(MaxLogEntries), 1000);

    public static readonly StyledProperty<int> RetainLogEntriesProperty =
        AvaloniaProperty.Register<LogViewer, int>(nameof(RetainLogEntries), 100);

    public static readonly StyledProperty<bool> ShowTimeStampProperty =
        AvaloniaProperty.Register<LogViewer, bool>(nameof(ShowTimeStamp), true);

    public static readonly StyledProperty<bool> ShowDateProperty =
        AvaloniaProperty.Register<LogViewer, bool>(nameof(ShowDate), false);

    public static readonly StyledProperty<ILogOutput?> LogOutputProperty =
        AvaloniaProperty.Register<LogViewer, ILogOutput?>(nameof(LogOutput));

    private readonly List<LogLine> _lines = new();
    private readonly ConcurrentQueue<LogLine> _pendingLines = new();
    private readonly List<double> _lineTops = new();
    private bool _autoScrollEnabled = false;
    private bool _needsRebuild = false;
    private double _cachedTotalHeight = 0;
    private double _lastLayoutWidth = 0;
    private readonly Typeface _typeface = new(FontFamily.Default);
    private const double FontSizeValue = 12;
    private bool _initialScrollPending = true;

    private const int MaxDrainPerFrame = 200;

    private readonly HashSet<int> _selectedIndices = new();
    private bool _isSelecting = false;
    private int _selectionStartIndex = -1;

    private const double MinRenderWidth = 2;

    public LogViewer()
    {
        InitializeComponent();
        ScrollViewer.ScrollChanged += ScrollViewer_ScrollChanged;
        LogCanvas.SizeChanged += LogCanvas_SizeChanged;
        LogCanvas.PointerPressed += LogCanvas_PointerPressed;
        LogCanvas.PointerReleased += LogCanvas_PointerReleased;
        LogCanvas.PointerMoved += LogCanvas_PointerMoved;
        AttachedToVisualTree += LogViewer_AttachedToVisualTree;

        LogOutputProperty.Changed.AddClassHandler<LogViewer>((x, e) => x.OnLogOutputChanged(e));
        Dispatcher.UIThread.Post(DrainLogs, DispatcherPriority.Background);
    }

    public int MaxLogEntries
    {
        get => GetValue(MaxLogEntriesProperty);
        set => SetValue(MaxLogEntriesProperty, value);
    }

    public int RetainLogEntries
    {
        get => GetValue(RetainLogEntriesProperty);
        set => SetValue(RetainLogEntriesProperty, value);
    }

    public bool ShowTimeStamp
    {
        get => GetValue(ShowTimeStampProperty);
        set => SetValue(ShowTimeStampProperty, value);
    }

    public bool ShowDate
    {
        get => GetValue(ShowDateProperty);
        set => SetValue(ShowDateProperty, value);
    }

    public ILogOutput? LogOutput
    {
        get => GetValue(LogOutputProperty);
        set => SetValue(LogOutputProperty, value);
    }

    private static readonly Dictionary<LogColor, IBrush> ColorMap = new()
    {
        [LogColor.White] = Brushes.Black,
        [LogColor.Yellow] = new SolidColorBrush(Color.FromRgb(184, 134, 11)),
        [LogColor.Red] = Brushes.Red,
        [LogColor.Gray] = Brushes.Gray,
        [LogColor.Green] = Brushes.Green
    };

    public void AddLog(string message, IBrush? brush)
    {
        if (string.IsNullOrEmpty(message)) return;

        if (ShowTimeStamp)
        {
            var now = DateTime.Now;
            if (ShowDate)
                message = $"[{now.Month:00}-{now.Day:00} {now.Hour:00}:{now.Minute:00}:{now.Second:00}.{now.Millisecond:000}] {message}";
            else
                message = $"[{now.Hour:00}:{now.Minute:00}:{now.Second:00}.{now.Millisecond:000}] {message}";
        }

        _pendingLines.Enqueue(new LogLine(message, brush ?? Brushes.White));
        Dispatcher.UIThread.Post(DrainLogs, DispatcherPriority.Background);
    }

    public void Clear()
    {
        _pendingLines.Clear();
        _lines.Clear();
        _lineTops.Clear();
        _selectedIndices.Clear();
        _cachedTotalHeight = 0;
        _autoScrollEnabled = false;
        _initialScrollPending = true;
        UpdateCanvasView();
        ScrollViewer.Offset = new Vector(0, 0);
        Dispatcher.UIThread.Post(() => ScrollViewer.ScrollToHome(), DispatcherPriority.Loaded);
    }

    private void ScrollViewer_ScrollChanged(object? sender, ScrollChangedEventArgs e)
    {
        if (_initialScrollPending)
        {
            ScrollViewer.Offset = new Vector(0, 0);
            Dispatcher.UIThread.Post(() => ScrollViewer.ScrollToHome(), DispatcherPriority.Render);
            if (ScrollViewer.Offset.Y <= 0.1)
                _initialScrollPending = false;
        }

        if (e.ExtentDelta.Y == 0)
            _autoScrollEnabled = IsAtBottom();

        UpdateCanvasView();
    }

    private bool IsAtBottom()
    {
        return ScrollViewer.Offset.Y >= ScrollViewer.Extent.Height - ScrollViewer.Viewport.Height - 2;
    }

    private void DrainLogs()
    {
        var drained = false;
        var count = 0;
        while (count++ < MaxDrainPerFrame && _pendingLines.TryDequeue(out var line))
        {
            AddLine(line);
            drained = true;
        }

        if (_needsRebuild)
        {
            RebuildAllLines();
            _needsRebuild = false;
            drained = true;
        }

        if (TrimIfNeeded())
            drained = true;

        if (drained)
        {
            UpdateCanvasView();
            if (_initialScrollPending)
            {
                ScrollViewer.Offset = new Vector(0, 0);
                Dispatcher.UIThread.Post(() => ScrollViewer.ScrollToHome(), DispatcherPriority.Render);
                return;
            }
            if (_autoScrollEnabled)
                ScrollViewer.ScrollToEnd();
        }
    }

    private bool TrimIfNeeded()
    {
        if (_lines.Count <= MaxLogEntries) return false;

        var removeCount = _lines.Count - RetainLogEntries;
        if (removeCount <= 0) return false;

        _lines.RemoveRange(0, removeCount);
        _lineTops.RemoveRange(0, removeCount);
        _selectedIndices.Clear();
        RebuildAllLines();

        return true;
    }

    private void LogViewer_AttachedToVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
    {
        _initialScrollPending = true;
        ScrollViewer.Offset = new Vector(0, 0);
        Dispatcher.UIThread.Post(() => ScrollViewer.ScrollToHome(), DispatcherPriority.Loaded);
    }

    private void LogCanvas_SizeChanged(object? sender, SizeChangedEventArgs e)
    {
        if (Math.Abs(e.NewSize.Width - e.PreviousSize.Width) < 1)
            return;

        _needsRebuild = true;
        Dispatcher.UIThread.Post(DrainLogs, DispatcherPriority.Background);
    }

    private bool HasValidRenderWidth()
    {
        var width = LogCanvas.Bounds.Width;
        return !double.IsNaN(width) && width >= MinRenderWidth;
    }

    private void RebuildAllLines()
    {
        if (!HasValidRenderWidth())
            return;

        _lineTops.Clear();
        _cachedTotalHeight = 0;
        var width = Math.Max(1, LogCanvas.Bounds.Width);

        foreach (var line in _lines)
        {
            var layout = CreateLayout(line.Text, width, line.Foreground);
            line.Layout = layout;
            line.LayoutWidth = width;
            line.Height = layout.Height;
            _lineTops.Add(_cachedTotalHeight);
            _cachedTotalHeight += line.Height;
        }

        _lastLayoutWidth = width;
    }

    private void AddLine(LogLine line)
    {
        _lines.Add(line);

        if (!HasValidRenderWidth())
        {
            _needsRebuild = true;
            return;
        }

        var width = Math.Max(1, LogCanvas.Bounds.Width);
        if (Math.Abs(width - _lastLayoutWidth) >= 1)
        {
            _needsRebuild = true;
            return;
        }

        var layout = CreateLayout(line.Text, width, line.Foreground);
        line.Layout = layout;
        line.LayoutWidth = width;
        line.Height = layout.Height;
        _lineTops.Add(_cachedTotalHeight);
        _cachedTotalHeight += line.Height;
    }

    private TextLayout CreateLayout(string text, double width, IBrush brush)
    {
        return new TextLayout(
            text,
            _typeface,
            FontSizeValue,
            brush,
            TextAlignment.Left,
            TextWrapping.Wrap,
            maxWidth: width);
    }

    private void UpdateCanvasView()
    {
        if (_lines.Count == 0)
        {
            LogCanvas.UpdateView(_lines, _lineTops, 0, 0, ScrollViewer.Viewport.Height, _typeface, FontSizeValue, _selectedIndices);
            return;
        }

        if (_lineTops.Count != _lines.Count && HasValidRenderWidth())
        {
            RebuildAllLines();
        }

        LogCanvas.UpdateView(
            _lines,
            _lineTops,
            _cachedTotalHeight,
            ScrollViewer.Offset.Y,
            ScrollViewer.Viewport.Height,
            _typeface,
            FontSizeValue,
            _selectedIndices);

        if (_initialScrollPending)
        {
            ScrollViewer.Offset = new Vector(0, 0);
        }
    }

    private int GetLineIndexFromPosition(Point position)
    {
        if (_lines.Count == 0) return -1;

        var y = position.Y;
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

    private void EnsureLayoutForHitTest()
    {
        if ((_needsRebuild || _lineTops.Count != _lines.Count) && HasValidRenderWidth())
        {
            RebuildAllLines();
            _needsRebuild = false;
        }
    }

    private void LogCanvas_PointerPressed(object? sender, Avalonia.Input.PointerPressedEventArgs e)
    {
        var pointState = e.GetCurrentPoint(LogCanvas);
        if (!pointState.Properties.IsLeftButtonPressed)
            return;

        EnsureLayoutForHitTest();
        var point = e.GetPosition(LogCanvas);
        _selectionStartIndex = GetLineIndexFromPosition(point);
        _isSelecting = true;

        LogCanvas.Focus();

        if ((e.KeyModifiers & Avalonia.Input.KeyModifiers.Control) == 0)
            _selectedIndices.Clear();

        if (_selectionStartIndex >= 0)
            ToggleSelection(_selectionStartIndex);

        UpdateCanvasView();
        e.Handled = true;
    }

    private void LogCanvas_PointerReleased(object? sender, Avalonia.Input.PointerReleasedEventArgs e)
    {
        if (_isSelecting)
            _isSelecting = false;
        e.Handled = true;
    }

    private void LogCanvas_PointerMoved(object? sender, Avalonia.Input.PointerEventArgs e)
    {
        if (!_isSelecting || !e.GetCurrentPoint(LogCanvas).Properties.IsLeftButtonPressed)
            return;

        EnsureLayoutForHitTest();
        var point = e.GetPosition(LogCanvas);
        var currentIndex = GetLineIndexFromPosition(point);
        if (currentIndex >= 0 && currentIndex != _selectionStartIndex)
            RangeSelection(_selectionStartIndex, currentIndex);

        UpdateCanvasView();
        e.Handled = true;
    }

    private void ToggleSelection(int index)
    {
        if (index < 0 || index >= _lines.Count) return;

        if (_selectedIndices.Contains(index))
            _selectedIndices.Remove(index);
        else
            _selectedIndices.Add(index);
    }

    private void RangeSelection(int start, int end)
    {
        _selectedIndices.Clear();
        var min = Math.Min(start, end);
        var max = Math.Max(start, end);
        for (var i = min; i <= max; i++)
            _selectedIndices.Add(i);
    }

    private void OnLogOutputChanged(AvaloniaPropertyChangedEventArgs e)
    {
        if (e.OldValue is ILogOutput oldOut)
            oldOut.LogHandler = null;

        if (e.NewValue is ILogOutput newOut)
        {
            newOut.LogHandler = (msg, col) =>
            {
                if (msg == null)
                    Clear();
                else
                    AddLog(msg, ColorMap.GetValueOrDefault(col, Brushes.White));
            };
        }
    }

    private async void CopySelectedLogs_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (_selectedIndices.Count == 0) return;

        var indices = new List<int>(_selectedIndices);
        indices.Sort();
        var texts = new List<string>(indices.Count);
        foreach (var index in indices)
            texts.Add(_lines[index].Text);

        var text = string.Join(Environment.NewLine, texts);
        var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
        if (clipboard != null)
            await clipboard.SetTextAsync(text);
    }

    private async void CopyAllLogs_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (_lines.Count == 0) return;
        var text = string.Join(Environment.NewLine, _lines.ConvertAll(l => l.Text));
        var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
        if (clipboard != null)
            await clipboard.SetTextAsync(text);
    }

    private void ClearLogs_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        Clear();
    }
}

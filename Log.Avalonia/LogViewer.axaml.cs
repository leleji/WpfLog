using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input.Platform;
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

    public static readonly StyledProperty<IDictionary<LogLevel, IBrush>?> LevelBrushesProperty =
        AvaloniaProperty.Register<LogViewer, IDictionary<LogLevel, IBrush>?>(nameof(LevelBrushes));

    private readonly LogViewportState<LogLine> _state = new();
    private bool _autoScrollEnabled = false;

    private readonly Typeface _typeface = new(FontFamily.Default);
    private const double FontSizeValue = 12;
    private bool _initialScrollPending = true;

    private const int MaxDrainPerFrame = 200;


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

    public IDictionary<LogLevel, IBrush>? LevelBrushes
    {
        get => GetValue(LevelBrushesProperty);
        set => SetValue(LevelBrushesProperty, value);
    }

    private static readonly Dictionary<LogLevel, IBrush> DefaultLevelBrushes = new()
    {
        [LogLevel.Trace] = Brushes.Gray,
        [LogLevel.Debug] = Brushes.Gray,
        [LogLevel.Info] = Brushes.Black,
        [LogLevel.Success] = Brushes.Green,
        [LogLevel.Warning] = new SolidColorBrush(Color.FromRgb(184, 134, 11)),
        [LogLevel.Error] = Brushes.Red,
        [LogLevel.Critical] = new SolidColorBrush(Color.FromRgb(139, 0, 0))
    };

    private IBrush ResolveBrush(LogEntry entry)
    {
        if (entry.Foreground.HasValue)
            return ToBrush(entry.Foreground.Value);

        if (LevelBrushes != null && LevelBrushes.TryGetValue(entry.Level, out var configuredBrush))
            return configuredBrush;

        return DefaultLevelBrushes.GetValueOrDefault(entry.Level, Brushes.Black);
    }

    private static IBrush ToBrush(LogColor color)
    {
        return new SolidColorBrush(Color.FromArgb(color.A, color.R, color.G, color.B));
    }

    public void AddLog(string message, IBrush? brush) => AddLog(message, brush, DateTimeOffset.Now);

    private void AddLog(string message, IBrush? brush, DateTimeOffset timestamp)
    {
        if (string.IsNullOrEmpty(message)) return;

        if (ShowTimeStamp)
        {
            var now = timestamp.LocalDateTime;
            if (ShowDate)
                message = $"[{now.Month:00}-{now.Day:00} {now.Hour:00}:{now.Minute:00}:{now.Second:00}.{now.Millisecond:000}] {message}";
            else
                message = $"[{now.Hour:00}:{now.Minute:00}:{now.Second:00}.{now.Millisecond:000}] {message}";
        }

        _state.Enqueue(new LogLine(message, brush ?? Brushes.White));
        Dispatcher.UIThread.Post(DrainLogs, DispatcherPriority.Background);
    }

    public void Clear()
    {
        _state.Clear();
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
        var width = HasValidRenderWidth() ? Math.Max(1, LogCanvas.Bounds.Width) : 0;
        var drained = _state.Drain(MaxDrainPerFrame, width, MeasureLine);
        drained |= _state.RebuildIfNeeded(width, MeasureLine);
        drained |= _state.TrimIfNeeded(MaxLogEntries, RetainLogEntries);

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

            if (_state.HasPending)
                Dispatcher.UIThread.Post(DrainLogs, DispatcherPriority.Background);
        }
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

        _state.RequestRebuild();
        Dispatcher.UIThread.Post(DrainLogs, DispatcherPriority.Background);
    }

    private bool HasValidRenderWidth()
    {
        var width = LogCanvas.Bounds.Width;
        return !double.IsNaN(width) && width >= MinRenderWidth;
    }

    private void MeasureLine(LogLine line, double width)
    {
        var layout = CreateLayout(line.Text, width, line.Foreground);
        line.Layout = layout;
        line.LayoutWidth = width;
        line.Height = layout.Height;
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
        if (_state.LineTops.Count != _state.Lines.Count && HasValidRenderWidth())
            _state.Rebuild(Math.Max(1, LogCanvas.Bounds.Width), MeasureLine);

        LogCanvas.UpdateView(
            _state.Lines,
            _state.LineTops,
            _state.TotalHeight,
            ScrollViewer.Offset.Y,
            ScrollViewer.Viewport.Height,
            _typeface,
            FontSizeValue,
            _state.SelectedIndices);

        if (_initialScrollPending)
        {
            ScrollViewer.Offset = new Vector(0, 0);
        }
    }

    private int GetLineIndexFromPosition(Point position) => _state.HitTest(position.Y);

    private void EnsureLayoutForHitTest()
    {
        if ((_state.NeedsRebuild || _state.LineTops.Count != _state.Lines.Count) && HasValidRenderWidth())
            _state.Rebuild(Math.Max(1, LogCanvas.Bounds.Width), MeasureLine);
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
            _state.ClearSelection();

        if (_selectionStartIndex >= 0)
            _state.ToggleSelection(_selectionStartIndex);

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
            _state.RangeSelection(_selectionStartIndex, currentIndex);

        UpdateCanvasView();
        e.Handled = true;
    }

    private void OnLogOutputChanged(AvaloniaPropertyChangedEventArgs e)
    {
        if (e.OldValue is ILogOutput oldOut)
            oldOut.LogHandler = null;

        if (e.NewValue is ILogOutput newOut)
        {
            newOut.LogHandler = entry =>
            {
                if (entry.Type == LogEventType.Clear)
                {
                    Clear();
                    return;
                }

                if (!string.IsNullOrEmpty(entry.Message))
                    AddLog(entry.Message, ResolveBrush(entry), entry.Timestamp ?? DateTimeOffset.Now);
            };
        }
    }

    private async void CopySelectedLogs_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (_state.SelectedIndices.Count == 0) return;

        var indices = new List<int>(_state.SelectedIndices);
        indices.Sort();
        var texts = new List<string>(indices.Count);
        foreach (var index in indices)
            texts.Add(_state.Lines[index].Text);

        var text = string.Join(Environment.NewLine, texts);
        var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
        if (clipboard != null)
            await clipboard.SetTextAsync(text);
    }

    private async void CopyAllLogs_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (_state.Lines.Count == 0) return;
        var text = string.Join(Environment.NewLine, _state.Lines.Select(l => l.Text));
        var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
        if (clipboard != null)
            await clipboard.SetTextAsync(text);
    }

    private void ClearLogs_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        Clear();
    }
}

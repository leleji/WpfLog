using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
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

    private readonly ObservableCollection<LogLine> _lines = new();
    private readonly ConcurrentQueue<LogLine> _pendingLines = new();
    private bool _autoScrollEnabled = true;

    public LogViewer()
    {
        InitializeComponent();
        ItemsHost.ItemsSource = _lines;
        ScrollViewer.ScrollChanged += ScrollViewer_ScrollChanged;

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
        [LogColor.White] = Brushes.White,
        [LogColor.Yellow] = Brushes.Yellow,
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
        _autoScrollEnabled = true;
    }

    private void ScrollViewer_ScrollChanged(object? sender, ScrollChangedEventArgs e)
    {
        if (e.ExtentDelta.Y == 0)
            _autoScrollEnabled = IsAtBottom();
    }

    private bool IsAtBottom()
    {
        return ScrollViewer.Offset.Y >= ScrollViewer.Extent.Height - ScrollViewer.Viewport.Height - 2;
    }

    private void DrainLogs()
    {
        var drained = false;
        while (_pendingLines.TryDequeue(out var line))
        {
            _lines.Add(line);
            drained = true;
        }

        if (drained && TrimIfNeeded())
            drained = true;

        if (drained && _autoScrollEnabled)
            ScrollViewer.ScrollToEnd();
    }

    private bool TrimIfNeeded()
    {
        if (_lines.Count <= MaxLogEntries) return false;

        var removeCount = _lines.Count - RetainLogEntries;
        if (removeCount <= 0) return false;

        for (var i = 0; i < removeCount; i++)
            _lines.RemoveAt(0);

        return true;
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
}

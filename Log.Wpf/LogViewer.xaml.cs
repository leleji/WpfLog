using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using WpfLog.Core;

namespace WpfLog
{
    public partial class LogViewer : UserControl
    {
        public static readonly DependencyProperty MaxLogEntriesProperty =
            DependencyProperty.Register(nameof(MaxLogEntries), typeof(int), typeof(LogViewer), new PropertyMetadata(1000));

        public static readonly DependencyProperty RetainLogEntriesProperty =
            DependencyProperty.Register(nameof(RetainLogEntries), typeof(int), typeof(LogViewer), new PropertyMetadata(100));

        public static readonly DependencyProperty ShowTimeStampProperty =
            DependencyProperty.Register(nameof(ShowTimeStamp), typeof(bool), typeof(LogViewer), new PropertyMetadata(true));

        public static readonly DependencyProperty ShowDateProperty =
            DependencyProperty.Register(nameof(ShowDate), typeof(bool), typeof(LogViewer), new PropertyMetadata(false));

        public static readonly DependencyProperty LogOutputProperty =
            DependencyProperty.Register(nameof(LogOutput), typeof(ILogOutput), typeof(LogViewer), new PropertyMetadata(null, OnLogOutputChanged));

        public static readonly DependencyProperty LevelBrushesProperty =
            DependencyProperty.Register(nameof(LevelBrushes), typeof(IDictionary<LogLevel, Brush>), typeof(LogViewer), new PropertyMetadata(null));

        private const int MaxDrainPerFrame = 200;
        private const double FontSizeValue = 12;
        private const double MinRenderWidth = 2;

        private readonly LogViewportState<LogLine> _state = new();
        private readonly Typeface _typeface = new("Microsoft YaHei UI");

        private bool _autoScrollEnabled = true;
        private bool _drainScheduled;
        private bool _isSelecting;
        private int _selectionStartIndex = -1;
        private System.Timers.Timer? _resizeTimer;

        private static readonly Dictionary<LogLevel, Brush> DefaultLevelBrushes = new()
        {
            [LogLevel.Trace] = Brushes.Gray,
            [LogLevel.Debug] = Brushes.Gray,
            [LogLevel.Info] = Brushes.Black,
            [LogLevel.Success] = Brushes.Green,
            [LogLevel.Warning] = CreateFrozenBrush(184, 134, 11),
            [LogLevel.Error] = Brushes.Red,
            [LogLevel.Critical] = CreateFrozenBrush(139, 0, 0)
        };

        public LogViewer()
        {
            InitializeComponent();
            TextOptions.SetTextRenderingMode(this, TextRenderingMode.ClearType);
            TextOptions.SetTextFormattingMode(this, TextFormattingMode.Display);

            ScrollViewer.ScrollChanged += ScrollViewer_ScrollChanged;
            LogCanvas.SizeChanged += LogCanvas_SizeChanged;
            LogCanvas.MouseLeftButtonDown += LogCanvas_MouseLeftButtonDown;
            LogCanvas.MouseLeftButtonUp += LogCanvas_MouseLeftButtonUp;
            LogCanvas.MouseMove += LogCanvas_MouseMove;
            Loaded += LogViewer_Loaded;
            Unloaded += LogViewer_Unloaded;
        }

        public int MaxLogEntries
        {
            get => (int)GetValue(MaxLogEntriesProperty);
            set => SetValue(MaxLogEntriesProperty, value);
        }

        public int RetainLogEntries
        {
            get => (int)GetValue(RetainLogEntriesProperty);
            set => SetValue(RetainLogEntriesProperty, value);
        }

        public bool ShowTimeStamp
        {
            get => (bool)GetValue(ShowTimeStampProperty);
            set => SetValue(ShowTimeStampProperty, value);
        }

        public bool ShowDate
        {
            get => (bool)GetValue(ShowDateProperty);
            set => SetValue(ShowDateProperty, value);
        }

        public ILogOutput? LogOutput
        {
            get => (ILogOutput?)GetValue(LogOutputProperty);
            set => SetValue(LogOutputProperty, value);
        }

        public IDictionary<LogLevel, Brush>? LevelBrushes
        {
            get => (IDictionary<LogLevel, Brush>?)GetValue(LevelBrushesProperty);
            set => SetValue(LevelBrushesProperty, value);
        }

        public void AddLog(string message, Brush color) => AddLog(message, color, DateTimeOffset.Now);

        public void Clear()
        {
            _state.Clear();
            _autoScrollEnabled = true;
            _isSelecting = false;
            _selectionStartIndex = -1;
            UpdateCanvasView();
            ScrollViewer.ScrollToHome();
        }

        private void AddLog(string message, Brush color, DateTimeOffset timestamp)
        {
            if (string.IsNullOrEmpty(message))
                return;

            if (ShowTimeStamp)
                message = FormatTimestamp(timestamp) + message;

            _state.Enqueue(new LogLine(message, color ?? Brushes.White));
            ScheduleDrain();
        }

        private string FormatTimestamp(DateTimeOffset timestamp)
        {
            var now = timestamp.LocalDateTime;
            return ShowDate
                ? $"[{now.Month:00}-{now.Day:00} {now.Hour:00}:{now.Minute:00}:{now.Second:00}.{now.Millisecond:000}] "
                : $"[{now.Hour:00}:{now.Minute:00}:{now.Second:00}.{now.Millisecond:000}] ";
        }

        private void LogViewer_Loaded(object sender, RoutedEventArgs e)
        {
            AttachLogOutput(LogOutput);
            _autoScrollEnabled = true;
            _state.RequestRebuild();
            DrainLogs(int.MaxValue);
            ScrollToEndDeferred();
        }

        private void LogViewer_Unloaded(object sender, RoutedEventArgs e)
        {
            _resizeTimer?.Stop();
            _resizeTimer?.Dispose();
            _resizeTimer = null;

        }

        private void ScrollViewer_ScrollChanged(object sender, ScrollChangedEventArgs e)
        {
            if (e.ExtentHeightChange == 0)
                _autoScrollEnabled = IsAtBottom();

            UpdateCanvasView();
        }

        private bool IsAtBottom()
        {
            return ScrollViewer.VerticalOffset >= ScrollViewer.ExtentHeight - ScrollViewer.ViewportHeight - 2;
        }

        private void ScrollToEndDeferred()
        {
            Dispatcher.BeginInvoke(new Action(ScrollViewer.ScrollToEnd), DispatcherPriority.Loaded);
            Dispatcher.BeginInvoke(new Action(ScrollViewer.ScrollToEnd), DispatcherPriority.ContextIdle);
        }

        private void LogCanvas_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (Math.Abs(e.NewSize.Width - e.PreviousSize.Width) < 1)
                return;

            _resizeTimer?.Stop();
            _resizeTimer?.Dispose();
            _resizeTimer = new System.Timers.Timer(150) { AutoReset = false };
            _resizeTimer.Elapsed += (_, _) => Dispatcher.BeginInvoke(() =>
            {
                _state.RequestRebuild();
                DrainLogs();
            }, DispatcherPriority.Background);
            _resizeTimer.Start();
        }

        private void ScheduleDrain()
        {
            if (_drainScheduled)
                return;

            _drainScheduled = true;
            Dispatcher.BeginInvoke(new Action(DrainLogs), DispatcherPriority.Background);
        }

        private void DrainLogs() => DrainLogs(MaxDrainPerFrame);

        private void DrainLogs(int maxCount)
        {
            _drainScheduled = false;
            var width = HasValidRenderWidth() ? RenderWidth : 0;
            var drained = _state.Drain(maxCount, width, MeasureLine);
            drained |= _state.RebuildIfNeeded(width, MeasureLine);
            drained |= _state.TrimIfNeeded(MaxLogEntries, RetainLogEntries);

            if (!drained)
                return;

            UpdateCanvasView();
            if (_autoScrollEnabled)
                ScrollToEndDeferred();

            if (_state.HasPending)
                ScheduleDrain();
        }

        private void MeasureLine(LogLine line, double width)
        {
            var layout = CreateLayout(line.Text, width, line.Foreground);
            line.Layout = layout;
            line.LayoutWidth = width;
            line.Height = layout.Height;
        }

        private FormattedText CreateLayout(string text, double width, Brush foreground)
        {
            return new FormattedText(
                text,
                CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight,
                _typeface,
                FontSizeValue,
                foreground,
                VisualTreeHelper.GetDpi(this).PixelsPerDip)
            {
                MaxTextWidth = Math.Max(1, width),
                TextAlignment = TextAlignment.Left
            };
        }

        private void UpdateCanvasView()
        {
            if (_state.LineTops.Count != _state.Lines.Count && HasValidRenderWidth())
                _state.Rebuild(RenderWidth, MeasureLine);

            LogCanvas.UpdateView(
                _state.Lines,
                _state.LineTops,
                _state.TotalHeight,
                ScrollViewer.VerticalOffset,
                ScrollViewer.ViewportHeight,
                _typeface,
                FontSizeValue,
                VisualTreeHelper.GetDpi(this).PixelsPerDip,
                _state.SelectedIndices);
        }

        private bool HasValidRenderWidth() => !double.IsNaN(RenderWidth) && RenderWidth >= MinRenderWidth;

        private double RenderWidth => Math.Max(1, LogCanvas.ActualWidth > 0 ? LogCanvas.ActualWidth : ScrollViewer.ActualWidth);

        private int GetLineIndexFromPosition(Point position) => _state.HitTest(position.Y);

        private void LogCanvas_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            EnsureLayoutForHitTest();
            _selectionStartIndex = GetLineIndexFromPosition(e.GetPosition(LogCanvas));
            _isSelecting = true;
            LogCanvas.Focus();

            if ((Keyboard.Modifiers & ModifierKeys.Control) == 0)
                _state.ClearSelection();

            if (_selectionStartIndex >= 0)
                ToggleSelection(_selectionStartIndex);

            LogCanvas.CaptureMouse();
            UpdateCanvasView();
            e.Handled = true;
        }

        private void LogCanvas_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (_isSelecting)
            {
                _isSelecting = false;
                LogCanvas.ReleaseMouseCapture();
            }

            e.Handled = true;
        }

        private void LogCanvas_MouseMove(object sender, MouseEventArgs e)
        {
            if (!_isSelecting || e.LeftButton != MouseButtonState.Pressed)
                return;

            EnsureLayoutForHitTest();
            var currentIndex = GetLineIndexFromPosition(e.GetPosition(LogCanvas));
            if (currentIndex >= 0 && currentIndex != _selectionStartIndex)
                RangeSelection(_selectionStartIndex, currentIndex);

            UpdateCanvasView();
            e.Handled = true;
        }

        private void EnsureLayoutForHitTest()
        {
            if ((_state.NeedsRebuild || _state.LineTops.Count != _state.Lines.Count) && HasValidRenderWidth())
                _state.Rebuild(RenderWidth, MeasureLine);
        }

        private void ToggleSelection(int index) => _state.ToggleSelection(index);

        private void RangeSelection(int start, int end) => _state.RangeSelection(start, end);

        private Brush ResolveBrush(LogEntry entry)
        {
            if (entry.Foreground.HasValue)
                return ToBrush(entry.Foreground.Value);

            if (LevelBrushes != null && LevelBrushes.TryGetValue(entry.Level, out var configuredBrush))
                return configuredBrush;

            return DefaultLevelBrushes.GetValueOrDefault(entry.Level, Brushes.Black);
        }

        private static Brush ToBrush(LogColor color)
        {
            var brush = new SolidColorBrush(Color.FromArgb(color.A, color.R, color.G, color.B));
            brush.Freeze();
            return brush;
        }

        private static Brush CreateFrozenBrush(byte r, byte g, byte b)
        {
            var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
            brush.Freeze();
            return brush;
        }

        private static void OnLogOutputChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is not LogViewer viewer)
                return;

            if (e.OldValue is ILogOutput oldOut)
                oldOut.LogHandler = null;

            viewer.AttachLogOutput(e.NewValue as ILogOutput);
        }

        private void AttachLogOutput(ILogOutput? output)
        {
            if (output == null)
                return;

            output.LogHandler = entry =>
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

        private void CopySelectedLogs_Click(object sender, RoutedEventArgs e)
        {
            if (_state.SelectedIndices.Count == 0)
                return;

            var text = string.Join(Environment.NewLine, _state.SelectedIndices.OrderBy(i => i).Select(i => _state.Lines[i].Text));
            Clipboard.SetText(text);
        }

        private void CopyAllLogs_Click(object sender, RoutedEventArgs e)
        {
            if (_state.Lines.Count == 0)
                return;

            Clipboard.SetText(string.Join(Environment.NewLine, _state.Lines.Select(l => l.Text)));
        }

        private void ClearLogs_Click(object sender, RoutedEventArgs e)
        {
            Clear();
        }
    }
}


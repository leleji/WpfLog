using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using WpfLog.Core;

namespace WpfLog
{
    public partial class LogViewer : UserControl
    {
        #region ==== 对外 API（保持不变） ====

        public static readonly DependencyProperty MaxLogEntriesProperty =
            DependencyProperty.Register(nameof(MaxLogEntries), typeof(int),
                typeof(LogViewer), new PropertyMetadata(1000));

        public static readonly DependencyProperty ShowTimeStampProperty =
            DependencyProperty.Register(nameof(ShowTimeStamp), typeof(bool),
                typeof(LogViewer), new PropertyMetadata(true));

        public static readonly DependencyProperty LogOutputProperty =
            DependencyProperty.Register(nameof(LogOutput), typeof(ILogOutput),
                typeof(LogViewer), new PropertyMetadata(null, OnLogOutputChanged));

        public int MaxLogEntries
        {
            get => (int)GetValue(MaxLogEntriesProperty);
            set => SetValue(MaxLogEntriesProperty, value);
        }

        public bool ShowTimeStamp
        {
            get => (bool)GetValue(ShowTimeStampProperty);
            set => SetValue(ShowTimeStampProperty, value);
        }

        public ILogOutput LogOutput
        {
            get => (ILogOutput)GetValue(LogOutputProperty);
            set => SetValue(LogOutputProperty, value);
        }

        #endregion

        #region ==== 内部类型 ====

        private sealed class LogLine
        {
            public string Text { get; }
            public DrawingVisual Visual { get; }
            public double Height { get; }

            public LogLine(string text, Brush color, double maxWidth, double dpi)
            {
                Text = text;
                Visual = new DrawingVisual();

                using var dc = Visual.RenderOpen();
                var ft = new FormattedText(
                    text,
                    CultureInfo.CurrentCulture,
                    FlowDirection.LeftToRight,
                    new Typeface("Consolas"),
                    12,
                    color,
                    dpi)
                {
                    MaxTextWidth = maxWidth
                };

                Height = ft.Height;
                dc.DrawText(ft, new Point(0, 0));
            }
        }


        private sealed class VisualHost : FrameworkElement
        {
            private readonly List<Visual> _children = new();

            protected override int VisualChildrenCount => _children.Count;
            protected override Visual GetVisualChild(int index) => _children[index];

            public void Add(Visual v)
            {
                _children.Add(v);
                AddVisualChild(v);
            }

            public void RemoveAt(int index)
            {
                var v = _children[index];
                RemoveVisualChild(v);
                _children.RemoveAt(index);
            }

            public void Clear()
            {
                foreach (var v in _children)
                    RemoveVisualChild(v);
                _children.Clear();
            }
        }

        #endregion

        #region ==== 字段 ====

        private readonly ConcurrentQueue<(string Text, Brush Color)> _pendingLogs = new();
        private readonly List<LogLine> _lines = new();

        private readonly VisualHost _visualHost = new();
        private bool _autoScroll = true;

        private const int MaxDrainPerFrame = 50;

        private bool _autoScrollEnabled = true;

        private static readonly Dictionary<LogColor, Brush> ColorMap = new()
        {
            { LogColor.White, Brushes.White },
            { LogColor.Yellow, Brushes.Yellow },
            { LogColor.Red, Brushes.Red },
            { LogColor.Gray, Brushes.Gray },
            { LogColor.Green, Brushes.Green }
        };

        #endregion

        public LogViewer()
        {
            InitializeComponent();

            LogHost.Children.Add(_visualHost);

            ScrollViewer.ScrollChanged += ScrollViewer_ScrollChanged;
            CompositionTarget.Rendering += OnRendering;
        }

        private bool IsAtBottom()
        {
            return ScrollViewer.VerticalOffset >=
                   ScrollViewer.ExtentHeight - ScrollViewer.ViewportHeight - 2;
        }

        #region ==== 日志入口（线程安全） ====

        public void AddLog(string message, Brush color)
        {
            if (string.IsNullOrEmpty(message))
                return;

            if (ShowTimeStamp)
                message = $"[{DateTime.Now:HH:mm:ss.fff}] {message}";

            _pendingLogs.Enqueue((message, color ?? Brushes.White));
        }

        public void Clear()
        {
            _pendingLogs.Clear();
            _lines.Clear();
            _visualHost.Clear();
        }

        #endregion

        #region ==== UI 帧循环 ====

        private void OnRendering(object sender, EventArgs e)
        {
            DrainLogs();
            UpdateLayoutPositions();
        }

        private void DrainLogs()
        {
            int count = 0;
            var dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
            double width = Math.Max(0, LogHost.ActualWidth - 10);

            while (count++ < MaxDrainPerFrame &&
                   _pendingLogs.TryDequeue(out var item))
            {
                var line = new LogLine(item.Text, item.Color, width, dpi);
                _lines.Add(line);
                _visualHost.Add(line.Visual);
            }

            TrimIfNeeded();
        }

        private void TrimIfNeeded()
        {
            if (_lines.Count <= MaxLogEntries)
                return;

            int remove = _lines.Count - MaxLogEntries;
            for (int i = 0; i < remove; i++)
            {
                _visualHost.RemoveAt(0);
                _lines.RemoveAt(0);
            }
        }

        private void UpdateLayoutPositions()
        {
            double y = 0;
            foreach (var line in _lines)
            {
                line.Visual.Transform = new TranslateTransform(0, y);
                y += line.Height;
            }

            _visualHost.Height = y;
            LogHost.Height = y;   // ⭐ 关键一行

            if (_autoScrollEnabled)
            {
                Dispatcher.BeginInvoke(
                    new Action(() => ScrollViewer.ScrollToEnd()),
                    System.Windows.Threading.DispatcherPriority.Background);
            }

        }

        #endregion

        #region ==== LogOutput 绑定 ====

        private void ScrollViewer_ScrollChanged(object sender, ScrollChangedEventArgs e)
        {
            if (e.ExtentHeightChange == 0) // 用户滚动
            {
                _autoScrollEnabled = IsAtBottom();
            }
        }

        private static void OnLogOutputChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is not LogViewer v)
                return;

            if (e.OldValue is ILogOutput oldOut)
                oldOut.LogHandler = null;

            if (e.NewValue is ILogOutput newOut)
            {
                newOut.LogHandler = (msg, col) =>
                {
                    if (msg == null)
                        v.Clear();
                    else
                        v.AddLog(msg, ColorMap.GetValueOrDefault(col, Brushes.White));
                };
            }
        }

        #endregion

        #region ==== 右键菜单 ====

        private void CopyAllLogs_Click(object sender, RoutedEventArgs e)
        {
            if (_lines.Count == 0) return;

            var text = string.Join(Environment.NewLine,
                _lines.Select(l => l.Text));

            Clipboard.SetText(text);
        }
        private void ClearLogs_Click(object sender, RoutedEventArgs e)
        {
            Clear();
        }

        #endregion
    }
}

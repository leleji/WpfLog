using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using System.Linq;
using System.Windows.Input;
using System.Windows.Data;

namespace WpfLog
{
    public partial class LogViewer : UserControl
    {
        #region 依赖属性定义

        /// <summary>
        /// 最大日志条数的依赖属性
        /// </summary>
        public static readonly DependencyProperty MaxLogEntriesProperty =
            DependencyProperty.Register(
                nameof(MaxLogEntries),
                typeof(int),
                typeof(LogViewer),
                new PropertyMetadata(1000)); // 默认值1000条

        /// <summary>
        /// 超出上限时保留的日志条数的依赖属性
        /// </summary>
        public static readonly DependencyProperty RetainLogEntriesProperty =
            DependencyProperty.Register(
                nameof(RetainLogEntries),
                typeof(int),
                typeof(LogViewer),
                new PropertyMetadata(100)); // 默认值100条

        /// <summary>
        /// 日志行高的依赖属性
        /// </summary>
        public static readonly DependencyProperty LineHeightProperty =
            DependencyProperty.Register(
                nameof(LineHeight),
                typeof(double),
                typeof(LogViewer),
                new PropertyMetadata(18.0, OnLineHeightChanged)); // 默认值20像素

        /// <summary>
        /// 日志输出接口的依赖属性
        /// </summary>
        public static readonly DependencyProperty LogOutputProperty =
            DependencyProperty.Register(
                nameof(LogOutput),
                typeof(ILogOutput),
                typeof(LogViewer),
                new PropertyMetadata(null, OnLogOutputChanged));

        #endregion

        #region 公共属性

        /// <summary>
        /// 获取或设置最大日志条数
        /// 当日志数量超过此值时，会触发清理机制
        /// </summary>
        public int MaxLogEntries
        {
            get => (int)GetValue(MaxLogEntriesProperty);
            set => SetValue(MaxLogEntriesProperty, value);
        }

        /// <summary>
        /// 获取或设置保留的日志条数
        /// 当日志数量超过最大值时，会保留最新的这些日志
        /// </summary>
        public int RetainLogEntries
        {
            get => (int)GetValue(RetainLogEntriesProperty);
            set => SetValue(RetainLogEntriesProperty, value);
        }

        /// <summary>
        /// 获取或设置日志行高
        /// </summary>
        public double LineHeight
        {
            get => (double)GetValue(LineHeightProperty);
            set => SetValue(LineHeightProperty, value);
        }


        /// <summary>
        /// 获取或设置日志输出接口
        /// </summary>
        public ILogOutput LogOutput
        {
            get => (ILogOutput)GetValue(LogOutputProperty);
            set => SetValue(LogOutputProperty, value);
        }

        #endregion

        /// <summary>
        /// 当行高改变时触发重新渲染
        /// </summary>
        private static void OnLineHeightChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is LogViewer logViewer)
            {
                logViewer.UpdateVisuals();
            }
        }

        // 日志条目类，实现对象池接口
        private class LogEntry
        {
            public string Message { get; set; }
            public Brush TextColor { get; set; }
            public double Y { get; set; }
            public double Height { get; set; }

            // 重置对象状态
            public void Reset()
            {
                Message = null;
                TextColor = null;
                Y = 0;
                Height = 0;
            }
        }

        // 对象池实现
        private class ObjectPool<T> where T : class, new()
        {
            private readonly ConcurrentBag<T> _objects = new();
            private readonly Action<T> _resetAction;

            public ObjectPool(Action<T> resetAction = null)
            {
                _resetAction = resetAction;
            }

            public T Get()
            {
                return _objects.TryTake(out T item) ? item : new T();
            }

            public void Return(T item)
            {
                _resetAction?.Invoke(item);
                _objects.Add(item);
            }
        }

        // VisualHost 类，用于承载 DrawingVisual
        private class VisualHost : FrameworkElement
        {
            private readonly List<DrawingVisual> _visuals = new();

            // 添加背景属性
            public static readonly DependencyProperty BackgroundProperty =
                DependencyProperty.Register(
                    nameof(Background),
                    typeof(Brush),
                    typeof(VisualHost),
                    new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

            public Brush Background
            {
                get => (Brush)GetValue(BackgroundProperty);
                set => SetValue(BackgroundProperty, value);
            }

            protected override int VisualChildrenCount => _visuals.Count;

            protected override Visual GetVisualChild(int index)
            {
                return _visuals[index];
            }

            public void AddVisual(DrawingVisual visual)
            {
                _visuals.Add(visual);
                AddVisualChild(visual);
            }

            public void ClearVisuals()
            {
                foreach (var visual in _visuals)
                {
                    RemoveVisualChild(visual);
                }
                _visuals.Clear();
            }

            // 添加背景绘制
            protected override void OnRender(DrawingContext drawingContext)
            {
                base.OnRender(drawingContext);
                if (Background != null)
                {
                    // 使用父容器的实际大小来绘制背景
                    var parent = VisualParent as FrameworkElement;
                    if (parent != null)
                    {
                        drawingContext.DrawRectangle(Background, null,
                            new Rect(0, 0, parent.ActualWidth, Math.Max(parent.ActualHeight, ActualHeight)));
                    }
                }
            }
        }

        // 配置参数
        private const int RenderMargin = 5;

        // 内部状态
        private readonly List<LogEntry> _logEntries = new();
        private readonly Queue<LogEntry> _pendingEntries = new();
        private readonly ObjectPool<LogEntry> _entryPool;
        private readonly ObjectPool<DrawingVisual> _visualPool;
        private bool _autoScroll = true;
        private DateTime _lastManualScrollTime;
        private readonly DispatcherTimer _renderTimer;
        private readonly DispatcherTimer _autoScrollTimer;
        private readonly FormattedText _dummyText; // 用于测量文本
        private readonly VisualHost _visualHost;

        // 添加选中范围
        private int _selectionStart = -1;
        private int _selectionEnd = -1;

        // 添加是否显示时间前缀的依赖属性
        public static readonly DependencyProperty ShowTimeStampProperty =
            DependencyProperty.Register(
                nameof(ShowTimeStamp),
                typeof(bool),
                typeof(LogViewer),
                new PropertyMetadata(true));

        public bool ShowTimeStamp
        {
            get => (bool)GetValue(ShowTimeStampProperty);
            set => SetValue(ShowTimeStampProperty, value);
        }

        // 添加背景色的依赖属性
        public static readonly DependencyProperty BackgroundColorProperty =
            DependencyProperty.Register(
                nameof(BackgroundColor),
                typeof(Brush),
                typeof(LogViewer),
                new PropertyMetadata(Brushes.Black));

        public Brush BackgroundColor
        {
            get => (Brush)GetValue(BackgroundColorProperty);
            set => SetValue(BackgroundColorProperty, value);
        }

        public LogViewer()
        {
            InitializeComponent();

            // 绑定背景色
            LogHost.SetBinding(Panel.BackgroundProperty,
                new Binding(nameof(BackgroundColor)) { Source = this });

            // 创建 VisualHost 并添加到 Canvas
            _visualHost = new VisualHost();
            _visualHost.SetBinding(VisualHost.BackgroundProperty,
                new Binding(nameof(BackgroundColor)) { Source = this });
            LogHost.Children.Add(_visualHost);

            // 确保 Canvas 和 VisualHost 填充整个可见区域
            LogHost.Width = double.NaN;
            LogHost.Height = double.NaN;
            _visualHost.Width = double.NaN;
            _visualHost.Height = double.NaN;

            // 初始化时滚动到顶部
            ScrollViewer.ScrollToTop();

            // 初始化对象池
            _entryPool = new ObjectPool<LogEntry>(entry => entry.Reset());
            _visualPool = new ObjectPool<DrawingVisual>();

            // 初始化文本测量对象
            _dummyText = new FormattedText(
                "X",
                CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight,
                new Typeface("Consolas"),
                12,
                Brushes.White,
                VisualTreeHelper.GetDpi(this).PixelsPerDip);

            // 初始化渲染计时器
            _renderTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(16)
            };
            _renderTimer.Tick += RenderTimer_Tick;
            _renderTimer.Start();

            // 初始化自动滚动计时器
            _autoScrollTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(10)
            };
            _autoScrollTimer.Tick += (s, e) => _autoScroll = true;
            _autoScrollTimer.Start();

            // 注册大小变化事件
            SizeChanged += LogViewer_SizeChanged;

            // 添加鼠标事件处理
            LogHost.MouseDown += LogHost_MouseDown;
            LogHost.MouseMove += LogHost_MouseMove;
            LogHost.MouseUp += LogHost_MouseUp;
        }

        public void AddLog(string message, Brush color = null)
        {
            if (string.IsNullOrEmpty(message)) return;

            var entry = _entryPool.Get();

            // 添加时间前缀
            if (ShowTimeStamp)
            {
                entry.Message = $"[{DateTime.Now:HH:mm:ss.ff}] {message}";
            }
            else
            {
                entry.Message = message;
            }

            entry.TextColor = color ?? Brushes.White;

            lock (_pendingEntries)
            {
                _pendingEntries.Enqueue(entry);
            }
        }

        private void RenderTimer_Tick(object sender, EventArgs e)
        {
            lock (_pendingEntries)
            {
                while (_pendingEntries.Count > 0)
                {
                    var entry = _pendingEntries.Dequeue();
                    AddLogEntry(entry);
                }
            }

            UpdateVisuals();
        }

        /// <summary>
        /// 添加日志条目
        /// </summary>
        /// <param name="entry">日志条目对象</param>
        private void AddLogEntry(LogEntry entry)
        {
            // 计算新条目的位置
            entry.Y = _logEntries.Count > 0
                ? _logEntries[^1].Y + _logEntries[^1].Height
                : 0;
            entry.Height = LineHeight; // 使用属性而不是常量

            _logEntries.Add(entry);

            // 限制日志数量，当超过最大值时只保留指定量的最新日志
            if (_logEntries.Count > MaxLogEntries) // 使用属性而不是常量
            {
                int removeCount = _logEntries.Count - RetainLogEntries; // 使用属性而不是常量
                for (int i = 0; i < removeCount; i++)
                {
                    var removedEntry = _logEntries[0];
                    _logEntries.RemoveAt(0);
                    _entryPool.Return(removedEntry);
                }

                // 更新剩余条目的位置
                double offset = _logEntries[0].Y;
                foreach (var remainingEntry in _logEntries)
                {
                    remainingEntry.Y -= offset;
                }
            }

            // 更新画布高度
            double newHeight = Math.Max(_logEntries.Count * LineHeight, ScrollViewer.ViewportHeight);
            _visualHost.Height = newHeight;
            LogHost.Height = newHeight;

            // 强制精确滚动到底部
            if (_autoScroll)
            {
                ScrollViewer.ScrollToVerticalOffset(double.MaxValue);
            }

            // 强制重新渲染背景
            _visualHost.InvalidateVisual();
        }

        private void UpdateVisuals()
        {
            _visualHost.ClearVisuals();

            // 确保背景填充整个可见区域
            double minHeight = Math.Max(_logEntries.Count * LineHeight, ScrollViewer.ViewportHeight);
            _visualHost.Height = minHeight;
            LogHost.Height = minHeight;

            if (_logEntries.Count == 0)
            {
                _visualHost.InvalidateVisual();
                return;
            }

            double scrollOffset = ScrollViewer.VerticalOffset;
            double viewportHeight = ScrollViewer.ViewportHeight;

            // 始终从顶部开始计算可见范围
            int startIndex = 0;
            int endIndex = Math.Min(_logEntries.Count - 1,
                (int)((scrollOffset + viewportHeight) / LineHeight) + RenderMargin);

            for (int i = startIndex; i <= endIndex; i++)
            {
                var entry = _logEntries[i];
                var visual = _visualPool.Get();

                using (var dc = visual.RenderOpen())
                {
                    // 绘制选中背景
                    if (_selectionStart >= 0 && _selectionEnd >= 0)
                    {
                        int selStart = Math.Min(_selectionStart, _selectionEnd);
                        int selEnd = Math.Max(_selectionStart, _selectionEnd);
                        if (i >= selStart && i <= selEnd)
                        {
                            dc.DrawRectangle(new SolidColorBrush(Color.FromArgb(80, 100, 100, 255)),
                                null, new Rect(0, entry.Y, LogHost.ActualWidth, LineHeight));
                        }
                    }

                    var formattedText = new FormattedText(
                        entry.Message,
                        CultureInfo.CurrentCulture,
                        FlowDirection.LeftToRight,
                        new Typeface("Consolas"),
                        12,
                        entry.TextColor,
                        VisualTreeHelper.GetDpi(this).PixelsPerDip);

                    dc.DrawText(formattedText, new Point(5, entry.Y));
                }

                _visualHost.AddVisual(visual);
            }
        }

        private void LogViewer_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            UpdateVisuals();
        }

        public void Clear()
        {
            foreach (var entry in _logEntries)
            {
                _entryPool.Return(entry);
            }
            _logEntries.Clear();

            lock (_pendingEntries)
            {
                while (_pendingEntries.Count > 0)
                {
                    var entry = _pendingEntries.Dequeue();
                    _entryPool.Return(entry);
                }
            }

            _visualHost.ClearVisuals();
            _visualHost.Height = 0;
        }

        // 添加右键菜单处理法
        private void CopyAllLogs_Click(object sender, RoutedEventArgs e)
        {
            var text = string.Join(Environment.NewLine, _logEntries.Select(entry => entry.Message));
            try
            {
                Clipboard.SetText(text);
            }
            catch { }
        }

        private void CopySelectedLogs_Click(object sender, RoutedEventArgs e)
        {
            if (_selectionStart < 0 || _selectionEnd < 0) return;

            int start = Math.Min(_selectionStart, _selectionEnd);
            int end = Math.Max(_selectionStart, _selectionEnd);

            var selectedLogs = _logEntries
                .Skip(start)
                .Take(end - start + 1)
                .Select(entry => entry.Message);

            try
            {
                var text = string.Join(Environment.NewLine, selectedLogs);
                Clipboard.SetText(text);
            }
            catch { }
        }

        private void ClearLogs_Click(object sender, RoutedEventArgs e)
        {
            Clear();
        }

        private bool _isSelecting = false;

        private void LogHost_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
            {
                _isSelecting = true;
                var point = e.GetPosition(LogHost);
                _selectionStart = GetLogIndexAtPosition(point.Y);
                _selectionEnd = _selectionStart;
                UpdateVisuals();
            }
        }

        private void LogHost_MouseMove(object sender, MouseEventArgs e)
        {
            if (_isSelecting)
            {
                var point = e.GetPosition(LogHost);
                _selectionEnd = GetLogIndexAtPosition(point.Y);
                UpdateVisuals();
            }
        }

        private void LogHost_MouseUp(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
            {
                _isSelecting = false;
            }
        }

        private int GetLogIndexAtPosition(double y)
        {
            int index = (int)(y / LineHeight);
            return Math.Max(0, Math.Min(_logEntries.Count - 1, index));
        }


        private static void OnLogOutputChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is LogViewer logViewer && e.NewValue is ILogOutput logOutput)
            {
                logOutput.LogHandler = (message, color) => logViewer.AddLog(message, color);
            }
        }
    }

}
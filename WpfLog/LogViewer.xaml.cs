using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using WpfLog.Core;

namespace WpfLog
{
    public partial class LogViewer : UserControl
    {
        // 在 LogViewer 类中定义颜色转换字典
        private static readonly Dictionary<LogColor, Brush> ColorMap = new()
        {
            { LogColor.White, Brushes.White },
            { LogColor.Yellow, Brushes.Yellow },
            { LogColor.Red, Brushes.Red },
            { LogColor.Gray, Brushes.Gray },
            { LogColor.Green, Brushes.Green }
        };


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
                new PropertyMetadata(18.0, OnLineHeightChanged)); // 默认值18像素


        /// <summary>
        /// 日志输出接口的依赖属性
        /// </summary>
        public static readonly DependencyProperty LogOutputProperty =
            DependencyProperty.Register(
                nameof(LogOutput),
                typeof(ILogOutput),
                typeof(LogViewer),
                new PropertyMetadata(null, OnLogOutputChanged));

        /// <summary>
        /// 是否自动换行的依赖属性
        /// </summary>
        public static readonly DependencyProperty AutoWrapProperty =
            DependencyProperty.Register(
                nameof(AutoWrap),
                typeof(bool),
                typeof(LogViewer),
                new PropertyMetadata(true, OnAutoWrapChanged)); // 默认开启自动换行

        /// <summary>
        /// 渲染缓冲区大小的依赖属性
        /// 该值决定了在可见区域之外预渲染的行数，值越大滚动越流畅，但会消耗更多内存
        /// </summary>
        public static readonly DependencyProperty RenderMarginProperty =
            DependencyProperty.Register(
                nameof(RenderMargin),
                typeof(int),
                typeof(LogViewer),
                new PropertyMetadata(5, OnRenderMarginChanged)); // 默认值5行

        /// <summary>
        /// 日志内容左边距的依赖属性
        /// </summary>
        public static readonly DependencyProperty LeftMarginProperty =
            DependencyProperty.Register(
                nameof(LeftMargin),
                typeof(double),
                typeof(LogViewer),
                new PropertyMetadata(0.0, OnMarginChanged));

        /// <summary>
        /// 日志内容右边距的依赖属性
        /// </summary>
        public static readonly DependencyProperty RightMarginProperty =
            DependencyProperty.Register(
                nameof(RightMargin),
                typeof(double),
                typeof(LogViewer),
                new PropertyMetadata(0.0, OnMarginChanged));

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

        public bool AutoWrap
        {
            get => (bool)GetValue(AutoWrapProperty);
            set => SetValue(AutoWrapProperty, value);
        }

        /// <summary>
        /// 获取或设置渲染缓冲区大小
        /// 该值表示在可见区域上下方额外渲染的行数
        /// 增���此值可以使滚动更流畅，但会占用更多内存
        /// 建议值：3-10，默认值：5
        /// </summary>
        public int RenderMargin
        {
            get => (int)GetValue(RenderMarginProperty);
            set => SetValue(RenderMarginProperty, value);
        }

        /// <summary>
        /// 获取或设置日志内容的左边距
        /// </summary>
        public double LeftMargin
        {
            get => (double)GetValue(LeftMarginProperty);
            set => SetValue(LeftMarginProperty, value);
        }

        /// <summary>
        /// 获取或设置日志内容的右边距
        /// </summary>
        public double RightMargin
        {
            get => (double)GetValue(RightMarginProperty);
            set => SetValue(RightMarginProperty, value);
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

        /// <summary>
        /// 当渲染缓冲区大小改变时触发重新渲染
        /// </summary>
        private static void OnRenderMarginChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
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

        // 添加中范围
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

            // 创建临时 FormattedText 来计算高度
            var formattedText = new FormattedText(
                entry.Message,
                CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight,
                new Typeface("Consolas"),
                12,
                entry.TextColor,
                VisualTreeHelper.GetDpi(this).PixelsPerDip);

            if (AutoWrap)
            {
                formattedText.MaxTextWidth = Math.Max(0, LogHost.ActualWidth - 10);
                entry.Height = formattedText.Height;
            }
            else
            {
                entry.Height = LineHeight;
            }

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

            // 修改画布高度计算
            double totalHeight = _logEntries.Sum(e => e.Height);
            _visualHost.Height = Math.Max(totalHeight, ScrollViewer.ViewportHeight);
            LogHost.Height = _visualHost.Height;

            // 强制精确滚动到底部
            if (_autoScroll)
            {
                ScrollViewer.ScrollToVerticalOffset(_visualHost.Height);
            }

            // 强制重新渲染背景
            _visualHost.InvalidateVisual();
        }

        private void UpdateVisuals()
        {

            if (LogHost.ActualWidth <= 0 || _logEntries.Count == 0)
            {
                _visualHost.ClearVisuals();
                return;
            }

            _visualHost.ClearVisuals();

            // 修改高度计算
            double totalHeight = _logEntries.Sum(e => e.Height);
            _visualHost.Height = Math.Max(totalHeight, ScrollViewer.ViewportHeight);
            LogHost.Height = _visualHost.Height;

            if (_logEntries.Count == 0)
            {
                _visualHost.InvalidateVisual();
                return;
            }

            double scrollOffset = ScrollViewer.VerticalOffset;
            double viewportHeight = ScrollViewer.ViewportHeight;

            // 修改可见范围��算
            int startIndex = 0;
            double currentHeight = 0;
            while (startIndex < _logEntries.Count && currentHeight + _logEntries[startIndex].Height < scrollOffset - RenderMargin * LineHeight)
            {
                currentHeight += _logEntries[startIndex].Height;
                startIndex++;
            }

            int endIndex = startIndex;
            while (endIndex < _logEntries.Count && currentHeight < scrollOffset + viewportHeight + RenderMargin * LineHeight)
            {
                currentHeight += _logEntries[endIndex].Height;
                endIndex++;
            }

            endIndex = Math.Min(endIndex, _logEntries.Count - 1);

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
                                null, new Rect(0, entry.Y, LogHost.ActualWidth, entry.Height));
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

                    // 设置文本换行宽度
                    if (AutoWrap)
                    {
                        double availableWidth = LogHost.ActualWidth - (LeftMargin + RightMargin);
                        // 确保不为负数
                        formattedText.MaxTextWidth = Math.Max(0, availableWidth);
                        entry.Height = formattedText.Height;
                    }

                    dc.DrawText(formattedText, new Point(LeftMargin, entry.Y));
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
            int index = 0;
            double currentHeight = 0;

            while (index < _logEntries.Count && currentHeight <= y)
            {
                currentHeight += _logEntries[index].Height;
                if (currentHeight > y)
                    break;
                index++;
            }

            return Math.Max(0, Math.Min(_logEntries.Count - 1, index));
        }

        private static void OnLogOutputChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is LogViewer logViewer)
            {
                if (e.OldValue is ILogOutput oldOutput) oldOutput.LogHandler = null;

                if (e.NewValue is ILogOutput logOutput)
                {
                    logOutput.LogHandler = (message, colorEnum) =>
                    {
                        if (message == null)
                        {
                            logViewer.Clear();
                        }
                        else
                        {
                            // 在这里完成枚举到 Brush 的最后一步转换
                            var brush = ColorMap.TryGetValue(colorEnum, out var b) ? b : Brushes.White;
                            logViewer.AddLog(message, brush);
                        }
                    };
                }
            }
        }

        private static void OnAutoWrapChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is LogViewer logViewer)
            {
                logViewer.UpdateVisuals();
            }
        }

        // 添加边距改变的回调方法
        private static void OnMarginChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is LogViewer logViewer)
            {
                logViewer.UpdateVisuals();
            }
        }
    }


}
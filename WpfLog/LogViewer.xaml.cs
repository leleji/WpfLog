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
        /// <summary>
        /// 允许的最大日志条数上限。
        /// 当日志条数超过该值时，会触发裁剪逻辑（TrimIfNeeded）。
        /// </summary>
        public static readonly DependencyProperty MaxLogEntriesProperty =
            DependencyProperty.Register(nameof(MaxLogEntries), typeof(int),
                typeof(LogViewer), new PropertyMetadata(1000));
        /// <summary>
        /// 触发裁剪时，实际保留的日志条数。
        /// 用于“批量裁剪”，避免频繁小规模删除带来的性能抖动。
        /// </summary>
        public static readonly DependencyProperty RetainLogEntriesProperty =
            DependencyProperty.Register(nameof(RetainLogEntries), typeof(int),
                typeof(LogViewer), new PropertyMetadata(100));
        /// <summary>
        /// 是否在日志前显示时间戳（HH:mm:ss.fff）。
        /// </summary>
        public static readonly DependencyProperty ShowTimeStampProperty =
            DependencyProperty.Register(nameof(ShowTimeStamp), typeof(bool),
                typeof(LogViewer), new PropertyMetadata(true));
        /// <summary>
        /// 是否在时间戳前额外显示日期（MM-dd）。
        /// 仅当 ShowTimeStamp == true 时生效。
        /// </summary>
        public static readonly DependencyProperty LogOutputProperty =
            DependencyProperty.Register(nameof(LogOutput), typeof(ILogOutput),
                typeof(LogViewer), new PropertyMetadata(null, OnLogOutputChanged));
        /// <summary>
        /// 外部日志输出接口。
        /// LogViewer 会将其绑定为日志接收入口。
        /// </summary>
        public static readonly DependencyProperty ShowDateProperty =
            DependencyProperty.Register(
                nameof(ShowDate),
                typeof(bool),
                typeof(LogViewer),
                new PropertyMetadata(false));

        public bool ShowDate
        {
            get => (bool)GetValue(ShowDateProperty);
            set => SetValue(ShowDateProperty, value);
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

        public ILogOutput LogOutput
        {
            get => (ILogOutput)GetValue(LogOutputProperty);
            set => SetValue(LogOutputProperty, value);
        }

        #endregion

        #region ==== 内部类型 ====
/// <summary>
        /// 表示一条已经完成排版的日志行。
        /// 包含文本、绘制用的 DrawingVisual 以及行高。
        /// </summary>
        private sealed class LogLine
        {
            public string Text { get; }
            public Brush Color { get; }
            public bool IsSelected { get; set; }

            public DrawingVisual Visual { get; private set; }
            public DrawingVisual BackgroundVisual { get; private set; }
            public double Height { get; private set; }

            public LogLine(string text, Brush color, double maxWidth, double dpi)
            {
                Text = text;
                Color = color;
                Visual = new DrawingVisual();
                BackgroundVisual = new DrawingVisual();
                IsSelected = false;
                Rebuild(maxWidth, dpi);
            }
/// <summary>
            /// 根据新的宽度重新排版文本（用于窗口 Resize）
            /// </summary>
            public void Rebuild(double maxWidth, double dpi)
            {
                // 重新绘制文本
                using var dc = Visual.RenderOpen();
                
                var ft = new FormattedText(
                    Text,
                    CultureInfo.InvariantCulture, // 使用InvariantCulture更快
                    FlowDirection.LeftToRight,
                    CachedTypeface,
                    12,
                    Color,
                    dpi)
                {
                    MaxTextWidth = maxWidth > 0 ? maxWidth : double.PositiveInfinity,
                    TextAlignment = TextAlignment.Left
                };

                Height = ft.Height;
                dc.DrawText(ft, new Point(0, 0));
            }

            /// <summary>
            /// 更新选中状态的背景色
            /// </summary>
            public void UpdateSelection(double maxWidth)
            {
                using var dc = BackgroundVisual.RenderOpen();
                
                if (IsSelected)
                {
                    // 绘制选中背景
                    var selectionBrush = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0, 120, 215)) { Opacity = 0.3 };
                    dc.DrawRectangle(selectionBrush, null, 
                                   new Rect(0, 0, maxWidth, Height));
                }
                else
                {
                    // 清除背景（透明）
                    dc.DrawRectangle(Brushes.Transparent, null, 
                                   new Rect(0, 0, maxWidth, Height));
                }
            }
        }

        /// <summary>
        /// 用于承载 DrawingVisual 的宿主元素。
        /// 通过维护 VisualChildren 集合参与 WPF 的视觉树。
        /// </summary>
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

            public void AddRange(Visual[] visuals)
            {
                foreach (var v in visuals)
                {
                    _children.Add(v);
                    AddVisualChild(v);
                }
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
        /// <summary>
        /// 跨线程安全的日志缓冲队列。
        /// 所有后台线程写日志都会先进这里。
        /// </summary>
        private readonly ConcurrentQueue<(string Text, Brush Color)> _pendingLogs = new();

        /// <summary>
        /// 当前已经显示在界面上的日志行集合。
        /// 顺序即显示顺序。
        /// </summary>
        private readonly List<LogLine> _lines = new();

        private readonly VisualHost _visualHost = new();

/// <summary>
        /// 是否存在新增或删除日志，需要重新计算布局。
        /// 用于避免每一帧都执行昂贵的布局操作。
        /// </summary>
        private bool _needsUpdate = false; // 标记是否需要更新布局
        
        /// <summary>
        /// 缓存上次计算的总高度，避免重复计算
        /// </summary>
        private double _cachedTotalHeight = 0;
        
        /// <summary>
        /// 最后一次更新布局的行数，用于增量更新判断
        /// </summary>
        private int _lastLayoutLineCount = 0;
        
/// <summary>
        /// 是否需要强制重新计算所有布局位置（用于裁剪后）
        /// </summary>
        private bool _forceRecalculateAll = false;

        /// <summary>
        /// 当前选中的日志行索引集合
        /// </summary>
        private readonly HashSet<int> _selectedIndices = new();

        /// <summary>
        /// 是否正在进行拖拽选择
        /// </summary>
        private bool _isSelecting = false;

        /// <summary>
        /// 拖拽选择的起始行索引
        /// </summary>
        private int _selectionStartIndex = -1;

        /// <summary>
        /// 鼠标按下时的位置
        /// </summary>
        private Point _mouseDownPosition;

        private const int MaxDrainPerFrame = 100; // 增加批处理量，减少UI更新频率



        private bool _needsRebuild = false;
        private System.Timers.Timer _resizeTimer;
        /// <summary>
        /// 是否启用自动滚动到底部。
        /// 当用户手动向上滚动时会被关闭。
        /// </summary>
        private bool _autoScrollEnabled = true;

private static readonly Dictionary<LogColor, Brush> ColorMap = new()
        {
            { LogColor.White, Brushes.White },
            { LogColor.Yellow, Brushes.Yellow },
            { LogColor.Red, Brushes.Red },
            { LogColor.Gray, Brushes.Gray },
            { LogColor.Green, Brushes.Green }
        };

/// <summary>
        /// 缓存的Typeface，避免重复创建
        /// </summary>
        private static readonly Typeface CachedTypeface = new("Microsoft YaHei UI");

        #endregion

public LogViewer()
        {
            InitializeComponent();
            // 提高文字渲染的清晰度（减少模糊）
            TextOptions.SetTextRenderingMode(this, TextRenderingMode.ClearType);
            TextOptions.SetTextFormattingMode(this, TextFormattingMode.Display);

            LogHost.Children.Add(_visualHost);

            ScrollViewer.ScrollChanged += ScrollViewer_ScrollChanged;
            CompositionTarget.Rendering += OnRendering;
            LogHost.SizeChanged += LogHost_SizeChanged;
            
            // 添加鼠标事件支持选择
            LogHost.MouseLeftButtonDown += LogHost_MouseLeftButtonDown;
            LogHost.MouseLeftButtonUp += LogHost_MouseLeftButtonUp;
            LogHost.MouseMove += LogHost_MouseMove;
        }

private bool IsAtBottom()
        {
            return ScrollViewer.VerticalOffset >=
                   ScrollViewer.ExtentHeight - ScrollViewer.ViewportHeight - 2;
        }

        /// <summary>
        /// 根据鼠标位置获取对应的日志行索引
        /// </summary>
        private int GetLineIndexFromPosition(Point position)
        {
            double y = 0;
            for (int i = 0; i < _lines.Count; i++)
            {
                y += _lines[i].Height;
                if (position.Y <= y)
                    return i;
            }
            return -1; // 没有找到对应的行
        }

        private void LogHost_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            _mouseDownPosition = e.GetPosition(LogHost);
            _selectionStartIndex = GetLineIndexFromPosition(_mouseDownPosition);
            _isSelecting = true;

            // 如果没有按住Ctrl键，清除之前的选择
            if ((Keyboard.Modifiers & ModifierKeys.Control) == 0)
            {
                ClearSelection();
            }

            // 如果点击在了有效行上
            if (_selectionStartIndex >= 0)
            {
                ToggleSelection(_selectionStartIndex);
            }

            LogHost.CaptureMouse();
            e.Handled = true;
        }

        private void LogHost_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (_isSelecting)
            {
                _isSelecting = false;
                LogHost.ReleaseMouseCapture();
            }
            e.Handled = true;
        }

        private void LogHost_MouseMove(object sender, MouseEventArgs e)
        {
            if (_isSelecting && e.LeftButton == MouseButtonState.Pressed)
            {
                var currentPosition = e.GetPosition(LogHost);
                int currentIndex = GetLineIndexFromPosition(currentPosition);

                if (currentIndex >= 0 && currentIndex != _selectionStartIndex)
                {
                    // 拖拽选择：选择起始位置到当前位置之间的所有行
                    RangeSelection(_selectionStartIndex, currentIndex);
                }
            }
            e.Handled = true;
        }

        /// <summary>
        /// 清除所有选择
        /// </summary>
        private void ClearSelection()
        {
            foreach (var index in _selectedIndices)
            {
                if (index >= 0 && index < _lines.Count)
                {
                    _lines[index].IsSelected = false;
                    UpdateLineBackground(index);
                }
            }
            _selectedIndices.Clear();
        }

        /// <summary>
        /// 切换指定行的选择状态
        /// </summary>
        private void ToggleSelection(int index)
        {
            if (index < 0 || index >= _lines.Count) return;

            if (_lines[index].IsSelected)
            {
                _lines[index].IsSelected = false;
                _selectedIndices.Remove(index);
            }
            else
            {
                _lines[index].IsSelected = true;
                _selectedIndices.Add(index);
            }
            UpdateLineBackground(index);
        }

        /// <summary>
        /// 范围选择：从start到end之间的所有行
        /// </summary>
        private void RangeSelection(int start, int end)
        {
            // 清除之前的选择
            ClearSelection();

            // 确定起始和结束位置
            int min = Math.Min(start, end);
            int max = Math.Max(start, end);

            // 选择范围内的所有行
            for (int i = min; i <= max; i++)
            {
                if (i >= 0 && i < _lines.Count)
                {
                    _lines[i].IsSelected = true;
                    _selectedIndices.Add(i);
                    UpdateLineBackground(i);
                }
            }
        }

        /// <summary>
        /// 更新指定行的背景显示
        /// </summary>
        private void UpdateLineBackground(int index)
        {
            if (index < 0 || index >= _lines.Count) return;

            var line = _lines[index];
            double width = Math.Max(0, LogHost.ActualWidth - 10);
            line.UpdateSelection(width);

            // 确保背景Visual在正确的位置
            double y = 0;
            for (int i = 0; i < index; i++)
            {
                y += _lines[i].Height;
            }
            line.BackgroundVisual.Transform = new TranslateTransform(0, y);
        }

        #region ==== 日志入口（线程安全） ====
        /// <summary>
        /// 添加一条日志（线程安全）。
        /// 实际渲染会延迟到 UI 帧循环中处理。
        /// </summary>
public void AddLog(string message, Brush color)
        {
            if (string.IsNullOrEmpty(message))
                return;

            if (ShowTimeStamp)
            {
                // 使用更高效的时间格式化
                var now = DateTime.Now;
                if (ShowDate)
                    message = $"[{now.Month:00}-{now.Day:00} {now.Hour:00}:{now.Minute:00}:{now.Second:00}.{now.Millisecond:000}] {message}";
                else
                    message = $"[{now.Hour:00}:{now.Minute:00}:{now.Second:00}.{now.Millisecond:000}] {message}";
            }

            _pendingLogs.Enqueue((message, color ?? Brushes.White));
        }

public void Clear()
        {
            _pendingLogs.Clear();
            _lines.Clear();
            _visualHost.Clear();
            _autoScrollEnabled = true;
            
// 重置所有缓存状态
            _cachedTotalHeight = 0;
            _lastLayoutLineCount = 0;
            _forceRecalculateAll = false;
            
            // 清除选择状态
            _selectedIndices.Clear();
            _isSelecting = false;
            _selectionStartIndex = -1;
        }

        #endregion

        #region ==== UI 帧循环 ====
        private void LogHost_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (Math.Abs(e.NewSize.Width - e.PreviousSize.Width) < 1) return;

            // 停止旧计时器，启动新计时器。只有停止缩放 200ms 后才真正 Rebuild
            _resizeTimer?.Stop();
            _resizeTimer = new System.Timers.Timer(200);
            _resizeTimer.Elapsed += (s, ev) => {
                _resizeTimer.Stop();
                Dispatcher.BeginInvoke(() => { _needsRebuild = true; });
            };
            _resizeTimer.Start();
        }
        private void OnRendering(object sender, EventArgs e)
        {
            DrainLogs();

            if (_needsRebuild)
            {
                RebuildAllLines();
                _needsRebuild = false;
                _needsUpdate = true; // 重排后一定要重新布局
            }

            if (!_needsUpdate) return;

            UpdateLayoutPositions();
            _needsUpdate = false;
        }
        private void RebuildAllLines()
        {
            var dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
            double width = Math.Max(0, LogHost.ActualWidth - 10);

            foreach (var line in _lines)
            {
                line.Rebuild(width, dpi);
            }
        }
        /// <summary>
        /// 从待处理队列中批量取出日志并创建 LogLine。
        /// 每帧最多处理 MaxDrainPerFrame 条，防止 UI 卡顿。
        /// </summary>
        private void DrainLogs()
        {
            int count = 0;
            var dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
            double width = Math.Max(0, LogHost.ActualWidth - 10);

while (count++ < MaxDrainPerFrame && _pendingLogs.TryDequeue(out var item))
            {
                var line = new LogLine(item.Text, item.Color, width, dpi);
                _lines.Add(line);
                _visualHost.AddRange(new[] { line.BackgroundVisual, line.Visual });
                _needsUpdate = true; // 只有真的收到新日志，才标记需要更新
            }

            if (TrimIfNeeded()) _needsUpdate = true; // 如果删除了旧日志，也标记需要更新
        }
/// <summary>
        /// 当日志数量超过 MaxLogEntries 时，
        /// 一次性裁剪到 RetainLogEntries，减少频繁删除带来的性能损耗。
        /// </summary>
        /// <returns>是否发生了裁剪</returns>
        private bool TrimIfNeeded()
        {
            // 只有当当前行数真正超过了"最大上限"时，才触发清理
            if (_lines.Count <= MaxLogEntries)
                return false;

            // 计算需要删除的数量：当前总数 - 想要保留的数量
            // 例如：1005条时触发，保留100条，则删除 905条
            int removeCount = _lines.Count - RetainLogEntries;

            // 防御性编程：确保不会删成负数
            if (removeCount <= 0) return false;

            // 批量移除：一次性移除多个，减少多次操作的开销
            for (int i = 0; i < removeCount; i++)
            {
                _visualHost.RemoveAt(0);
            }

            // 批量移除数据
            _lines.RemoveRange(0, removeCount);

// 重置缓存高度，需要重新计算
            _cachedTotalHeight = 0;
            _lastLayoutLineCount = _lines.Count;
            _forceRecalculateAll = true; // 强制重新计算所有位置

            return true; // 返回 true 表示发生了变动，通知外层更新布局
        }
/// <summary>
        /// 重新计算所有日志行的 Y 偏移，并更新容器高度。
        /// 同时根据自动滚动状态决定是否滚动到底部。
        /// </summary>
        private void UpdateLayoutPositions()
        {
            double y = 0;
            
// 如果需要强制重新计算所有位置，或者行数减少了，则全量计算
            if (_forceRecalculateAll || _lines.Count < _lastLayoutLineCount)
            {
                // 重新计算所有行的位置（包括背景）
                for (int i = 0; i < _lines.Count; i++)
                {
                    var line = _lines[i];
                    line.Visual.Transform = new TranslateTransform(0, y);
                    line.BackgroundVisual.Transform = new TranslateTransform(0, y);
                    y += line.Height;
                }
                _cachedTotalHeight = y;
                _forceRecalculateAll = false;
            }
            // 如果只是新增行，则增量更新
            else if (_lines.Count > _lastLayoutLineCount)
            {
                // 只更新新增行的位置
                y = _cachedTotalHeight;
                for (int i = _lastLayoutLineCount; i < _lines.Count; i++)
                {
                    var line = _lines[i];
                    line.Visual.Transform = new TranslateTransform(0, y);
                    line.BackgroundVisual.Transform = new TranslateTransform(0, y);
                    y += line.Height;
                }
                _cachedTotalHeight = y;
            }
            // 如果行数相等，可能是窗口大小变化导致的高度变化，需要重新计算
            else
            {
                for (int i = 0; i < _lines.Count; i++)
                {
                    var line = _lines[i];
                    line.Visual.Transform = new TranslateTransform(0, y);
                    line.BackgroundVisual.Transform = new TranslateTransform(0, y);
                    y += line.Height;
                }
                _cachedTotalHeight = y;
            }

            _visualHost.Height = _cachedTotalHeight;
            LogHost.Height = _cachedTotalHeight;
            _lastLayoutLineCount = _lines.Count;

            // 只有在开启了自动滚动的情况下，才在添加新日志后滚动到底部
            if (_autoScrollEnabled)
            {
                Dispatcher.BeginInvoke(
                new Action(ScrollViewer.ScrollToEnd),
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

private void CopySelectedLogs_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedIndices.Count == 0) return;

            var selectedTexts = _selectedIndices.OrderBy(i => i)
                .Select(i => _lines[i].Text);
            
            var text = string.Join(Environment.NewLine, selectedTexts);
            Clipboard.SetText(text);
        }

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

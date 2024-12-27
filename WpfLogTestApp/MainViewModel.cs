using System;
using System.Windows.Input;
using System.Windows.Media;
using WpfLog;

namespace WpfLogTestApp
{
    public class MainViewModel : ViewModelBase
    {
        private LogMessageInfo _logMessage;
        private bool _showTimeStamp = true;
        private int _maxLogEntries = 1000;
        private int _retainLogEntries = 100;
        private double _lineHeight = 18;

        public bool ShowTimeStamp
        {
            get => _showTimeStamp;
            set
            {
                if (_showTimeStamp != value)
                {
                    _showTimeStamp = value;
                    OnPropertyChanged();
                }
            }
        }

        public LogMessageInfo LogMessage
        {
            get => _logMessage;
            set
            {
                _logMessage = value;
                OnPropertyChanged();
            }
        }

        /// <summary>
        /// 最大日志条数
        /// </summary>
        public int MaxLogEntries
        {
            get => _maxLogEntries;
            set
            {
                if (_maxLogEntries != value)
                {
                    _maxLogEntries = value;
                    OnPropertyChanged();
                }
            }
        }

        /// <summary>
        /// 保留的日志条数
        /// </summary>
        public int RetainLogEntries
        {
            get => _retainLogEntries;
            set
            {
                if (_retainLogEntries != value)
                {
                    _retainLogEntries = value;
                    OnPropertyChanged();
                }
            }
        }

        /// <summary>
        /// 日志行高
        /// </summary>
        public double LineHeight
        {
            get => _lineHeight;
            set
            {
                if (_lineHeight != value)
                {
                    _lineHeight = value;
                    OnPropertyChanged();
                }
            }
        }

        public ICommand ClearCommand { get; }
        public ICommand TestLogCommand { get; }

        public MainViewModel()
        {
            ClearCommand = new RelayCommand(Clear);
            TestLogCommand = new RelayCommand(GenerateTestLogs);
        }

        private void GenerateTestLogs()
        {
            LogMessage = new LogMessageInfo("这是一条信息日志", Brushes.White);
            LogMessage = new LogMessageInfo("这是一条警告日志", Brushes.Yellow);
            LogMessage = new LogMessageInfo("这是一条错误日志", Brushes.Red);
            LogMessage = new LogMessageInfo("这是一条调试日志", Brushes.Gray);
        }

        private void Clear()
        {
            // 你需要在 LogViewer 中实现这个功能
        }
    }
}
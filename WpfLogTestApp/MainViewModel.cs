using System;
using System.Windows.Input;
using System.Windows.Media;
using WpfLog;

namespace WpfLogTestApp
{
    public class MainViewModel : ViewModelBase
    {
        private ILogOutput _logOutput;
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

        public ILogOutput LogOutput
        {
            get => _logOutput;
            set
            {
                _logOutput = value;
                OnPropertyChanged();
            }
        }

        public MainViewModel()
        {
            LogOutput = new LogOutput();
            LogOutput.LogSuccess("这是一条成功日志");
            ClearCommand = new RelayCommand(Clear);
            TestLogCommand = new RelayCommand(GenerateTestLogs);
        }

        private void GenerateTestLogs()
        {
            LogOutput.LogInfo("这是一条信息日志");
            LogOutput.LogWarning("这是一条警告日志");
            LogOutput.LogError("这是一条错误日志");
            LogOutput.LogDebug("这是一条调试日志");
            LogOutput.LogSuccess("这是一条成功日志");
            LogOutput.Log(Brushes.Green,"这是一条测试日志");
        }

        private void Clear()
        {
            LogOutput.Clear();
        }
    }
}
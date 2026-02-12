using System.Threading.Tasks;
using System.Windows.Input;
using AvaLog;
using WpfLog.Core;

namespace AvaLogTestApp;

public sealed class MainViewModel : ViewModelBase
{
    private bool _showTimeStamp = true;
    private int _maxLogEntries = 1000;
    private int _retainLogEntries = 100;

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

    public ILogOutput LogOutput { get; }
    public ICommand ClearCommand { get; }
    public ICommand TestLogCommand { get; }

    public MainViewModel()
    {
        LogOutput = new LogOutput();
        ClearCommand = new RelayCommand(Clear);
        TestLogCommand = new RelayCommand(GenerateTestLogs);
        LogOutput.LogInfo("这是一条信息日志");
        LogOutput.LogWarning("这是一条警告日志");
        LogOutput.LogError("这是一条错误日志");
        LogOutput.LogDebug("这是一条调试日志");
        LogOutput.LogSuccess("这是一条成功日志");
    }

    private void GenerateTestLogs()
    {
        LogOutput.LogInfo("这是一条信息日志");
        LogOutput.LogWarning("这是一条警告日志");
        LogOutput.LogError("这是一条错误日志");
        LogOutput.LogDebug("这是一条调试日志");
        LogOutput.LogSuccess("这是一条成功日志");

        _ = Task.Run(async () =>
        {
            for (int i = 0; i < 1100; i++)
            {
                LogOutput.LogSuccess($"后台日志 {i}");
                await Task.Delay(10);
            }
        });
    }

    private void Clear()
    {
        LogOutput.Clear();
    }
}

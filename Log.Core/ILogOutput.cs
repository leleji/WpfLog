using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace WpfLog.Core
{
    public interface ILogOutput
    {
        // 这里的 Action 只传递基础字符串和抽象颜色枚举
        // message 为 null 时代表 Clear 指令
        Action<string, LogColor> LogHandler { get; set; }

        void LogInfo(string message);
        void LogWarning(string message);
        void LogError(string message);
        void LogDebug(string message);
        void LogSuccess(string message);
        void Log(LogColor color, string message);
        void Clear();
    }
}

using System;
using System.ComponentModel;
using System.IO;
using System.Linq;

namespace SWGrade.SolidWorks
{
    internal static class CadConnectionAdvice
    {
        internal static string For(CadDetection status, string expectedYear, Exception error)
        {
            if (status.IsWindowsSupported == false)
                return "运行 winver 核对系统内部版本。学校的 Windows 10 1903 x64 应使用 1903 机房试用包。";
            if (status.Windows != null && !status.IsX64)
                return "在 64 位 Windows 上使用 x64 版学生端。";
            if (status.Windows != null && !status.HasFramework48)
                return "请机房管理员检查或修复 .NET Framework 4.8，然后重新运行检测。";
            if (status.IsRunning == false)
            {
                if (int.TryParse(expectedYear, out int year) && status.InstalledYears.Contains(year))
                    return "用当前 Windows 账号打开已安装的 SOLIDWORKS " + expectedYear + "，等主界面就绪再检测。";
                return "启动机房安装的 SOLIDWORKS 后再检测。找不到程序时，请机房管理员核对安装。";
            }

            for (var cause = error; cause != null; cause = cause.InnerException)
            {
                if (cause is FileNotFoundException || cause is FileLoadException || cause is BadImageFormatException ||
                    cause.HResult == unchecked((int)0x80040154))
                    return "请机房管理员用 SOLIDWORKS 安装管理器修复 CAD 的 API 组件或注册。";
                if (cause is UnauthorizedAccessException || cause.HResult == unchecked((int)0x80070005) ||
                    cause is Win32Exception native && native.NativeErrorCode == 5)
                    return "关闭诊断工具，用同一个 Windows 账号和相同权限启动 CAD 与学生端。仍然拒绝访问时，把本报告交给机房管理员。";
                if (cause.HResult == unchecked((int)0x80010001) || cause.HResult == unchecked((int)0x8001010A))
                    return "关闭 SOLIDWORKS 的欢迎窗口或其他对话框，等 CAD 空闲后重新运行检测。";
                if (cause.HResult == unchecked((int)0x800401E3))
                    return "等 SOLIDWORKS 主界面就绪，点击一次 CAD 窗口，再用同一 Windows 账号及相同权限重新检测。";
                if (cause is Win32Exception process && process.NativeErrorCode == 299)
                    return "等待 SOLIDWORKS 启动完成后再试。若仍失败，请保留报告以检查运行进程。";
            }

            if (status.ConnectedYear != null && expectedYear != null && status.ConnectedYear != expectedYear)
                return "手动关闭非目标版本，只保留 SOLIDWORKS " + expectedYear + "，然后重新检测。";
            return "记录上方的失败原因和报告末尾的错误码。若提示未给出处理办法，请把完整报告发来排查。";
        }
    }
}

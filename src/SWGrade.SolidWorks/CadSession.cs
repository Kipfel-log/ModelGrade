using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;

namespace SWGrade.SolidWorks
{
    internal sealed class CadSession
    {
        private const string AppInterface = "ISldWorks";
        private readonly CadInterop interop;
        private readonly string interopFolder;
        internal object App { get; }
        internal int ProcessId { get; }
        internal string Year { get; }
        internal string Revision { get; }

        private CadSession(CadInterop interop, string interopFolder, object app, int processId, string year, string revision)
        {
            this.interop = interop;
            this.interopFolder = interopFolder;
            App = app;
            ProcessId = processId;
            Year = year;
            Revision = revision;
        }

        internal static CadSession Connect(CadInterop interop, string expectedYear, CadDetection detection = null)
        {
            var processes = Process.GetProcessesByName("SLDWORKS");
            try
            {
                int currentSession;
                using (var current = Process.GetCurrentProcess())
                    currentSession = current.SessionId;

                Process candidate = null;
                foreach (var process in processes)
                {
                    if (process.SessionId != currentSession || !WindowsEnvironment.IsCurrentUser(process))
                        continue;

                    if (detection != null)
                        detection.IsRunning = true;
                    interop.Log("同一用户会话发现 SOLIDWORKS 进程 PID {0}", process.Id);
                    if (candidate != null)
                        throw new InvalidOperationException("发现多个 SOLIDWORKS 实例。请手动保留目标实例，再重新检测。");
                    candidate = process;
                }

                if (candidate == null)
                {
                    if (detection != null)
                        detection.IsRunning = false;
                    throw new InvalidOperationException("当前登录会话未发现 SOLIDWORKS 进程。已安装时，请用当前账号启动 CAD，确认主窗口和许可证可用，再检测。");
                }

                string folder = Path.Combine(Path.GetDirectoryName(candidate.MainModule.FileName), "api", "redist");
                interop.Load(folder, CadInterop.Core);
                interop.Load(folder, CadInterop.Constants);

                object app;
                try
                {
                    app = Marshal.GetActiveObject(WindowsEnvironment.CadProgId);
                }
                catch (COMException exception)
                {
                    interop.Log("[COM 附加失败] " + CadAnalysis.DescribeError(exception));
                    throw new InvalidOperationException("无法从运行对象表取得 SOLIDWORKS。请确认 CAD 已启动到主界面、许可证可用，并让 SWGrade 与 CAD 使用相同权限。", exception);
                }

                interop.Track(app);
                int actualPid = (int)interop.Call(AppInterface, app, "GetProcessID");
                if (candidate.Id != actualPid)
                    throw new InvalidOperationException("COM 返回的实例与当前用户会话中预期的 SOLIDWORKS 进程不同。");

                if (detection != null)
                    detection.IsComConnected = true;
                string revision = (string)interop.Call(AppInterface, app, "RevisionNumber");
                string major = revision?.Split('.')[0];
                string year = major == "33" ? "2025" : major == "34" ? "2026" : null;
                if (detection != null)
                {
                    detection.Revision = revision;
                    detection.ConnectedYear = year;
                }
                interop.Log("COM 已连接：PID {0}；SOLIDWORKS {1}；RevisionNumber={2}",
                    actualPid, year ?? "未纳入验证范围", revision);

                if (year == null)
                    throw new InvalidOperationException("目标 CAD 版本为 SOLIDWORKS 2025 或 2026。");
                if (!WindowsEnvironment.IsWindows11 && year == "2026")
                    throw new InvalidOperationException("SOLIDWORKS 2026 不在 Windows 10 的官方支持范围内，停止模型分析。");
                if (expectedYear != null && year != expectedYear)
                    throw new InvalidOperationException("已连接版本与预期版本不同。请切换预期版本或启动目标版本，再重新检测。");
                if (!(bool)interop.Get(AppInterface, app, "Visible") ||
                    !(bool)interop.Get(AppInterface, app, "UserControl"))
                    throw new InvalidOperationException("探针要求交互式可见实例。请先显示 SOLIDWORKS 主窗口。");

                interop.Log("组合诊断：属于项目候选组合，真机验收以样本读取及复测记录为准。");
                return new CadSession(interop, folder, app, actualPid, year, revision);
            }
            finally
            {
                foreach (var process in processes)
                    process.Dispose();
            }
        }

        internal void ProbeUtilities()
        {
            // https://help.solidworks.com/2025/English/api/swutilitiesapi/Set_Tolerances_and_Compare_Geometry_VBNET.htm
            try
            {
                object utilities = interop.Call(AppInterface, App, "GetAddInObject", "Utilities.UtilitiesApp");
                if (utilities == null)
                {
                    interop.Log("Utilities / CompareGeometry3：插件对象未取得。请在 CAD 中核对插件安装、启用与许可。");
                    return;
                }

                interop.Load(interopFolder, CadInterop.Utilities);
                var utilitiesType = interop.Interface("IUtilities", CadInterop.Utilities);
                int geometryTool = interop.Constant("gtSwTools_e", "gtSwToolGeomDiff", CadInterop.Utilities);
                object[] args = { geometryTool, 0 };
                object tool = interop.Invoke(utilitiesType.GetMethod("GetToolInterface"), utilities, args);
                interop.Log("Utilities GetToolInterface 状态码：{0}", args[1]);
                var geometry = interop.Interface("ICompareGeometry", CadInterop.Utilities);

                if (tool == null || (int)args[1] != 0 || !geometry.IsInstanceOfType(tool) ||
                    geometry.GetMethod("CompareGeometry3") == null)
                {
                    interop.Log("Utilities / CompareGeometry3：比较接口未确认，待复核。");
                    return;
                }

                interop.Log("Utilities / CompareGeometry3：已取得比较接口。未执行几何比较，比较许可及实际结果待双样本验证。");
            }
            catch (Exception exception)
            {
                interop.Log("[Utilities 能力未确认] " + CadAnalysis.DescribeError(exception));
            }
        }
    }
}

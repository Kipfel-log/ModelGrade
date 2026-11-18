using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using SWGrade.Domain;

namespace SWGrade.SolidWorks
{
    public static class CadAnalysis
    {
        public static ModelInspectionResult Run(string modelPath, string configuration, string expectedYear,
            bool probeUtilities, Action<string> report)
        {
            return WithSession(expectedYear, report, (interop, session) =>
            {
                if (probeUtilities)
                    session.ProbeUtilities();
                if (modelPath == null)
                {
                    interop.Log("模式：连接诊断。模型读取未执行。");
                    return null;
                }

                return ReadSavedCopy(interop, session, modelPath, configuration);
            });
        }

        public static CadDetection Detect(string expectedYear, Action<string> report)
        {
            var detection = new CadDetection();
            try
            {
                WithSession(expectedYear, report, (interop, session) =>
                {
                    detection.CanAnalyze = true;
                    session.ProbeUtilities();
                    return true;
                }, detection);
            }
            catch (Exception exception)
            {
                report?.Invoke(DescribeError(exception));
                if (exception is FileNotFoundException || exception is BadImageFormatException || exception is FileLoadException)
                    detection.Message = "SOLIDWORKS 的 API 组件缺失或无法载入。请用安装管理器检查或修复 CAD 安装。";
                else if (detection.IsRunning == false && detection.InstalledYears.Count == 0)
                    detection.Message = "未确认已安装 SOLIDWORKS 2025 或 2026，也未发现正在运行的实例。请核对安装，或启动已安装的 CAD 后重试。";
                else if (exception is InvalidOperationException)
                    detection.Message = exception.Message;
                else
                    detection.Message = "环境或 CAD 连接检查失败。请确认 CAD 主窗口、许可证以及运行权限，然后重试。原因可在设置的详细诊断中查看。";
            }
            return detection;
        }

        public static PartConfigurations ReadConfigurations(string modelPath, string expectedYear, Action<string> report)
        {
            return WithSession(expectedYear, report, (interop, session) =>
            {
                using (var copy = ModelCopy.Create(modelPath))
                {
                    // Reading the saved configuration catalog does not require opening the part.
                    var rawNames = interop.Call("ISldWorks", session.App, "GetConfigurationNames", copy.WorkingPath) as Array;
                    if (rawNames == null || rawNames.Length == 0)
                        throw new InvalidOperationException("SOLIDWORKS 未能读取此零件的配置。请检查文件版本，或在 CAD 中保存后重试。");

                    string active = (string)interop.Call("ISldWorks", session.App, "GetActiveConfigurationName", copy.WorkingPath);
                    var catalog = new PartConfigurations(copy.Hash, active, rawNames.Cast<object>().OfType<string>());
                    copy.VerifySource();
                    if (catalog.Names.Count == 0)
                        throw new InvalidOperationException("零件没有可读取的配置名称。");
                    return catalog;
                }
            });
        }

        public static IReadOnlyList<OpenPartInfo> ListOpenParts(string expectedYear, Action<string> report) =>
            WithSession(expectedYear, report, OpenCadParts.List);

        public static ModelInspectionResult ReadActivePart(string configuration, string expectedYear, Action<string> report) =>
            WithSession(expectedYear, report, (interop, session) =>
                ReadOpenPartCopy(interop, session, OpenCadParts.Active(interop, session), configuration));

        public static ModelInspectionResult ReadOpenPart(OpenPartInfo part, string configuration, string expectedYear,
            Action<string> report)
        {
            if (part == null)
                throw new ArgumentNullException(nameof(part));
            EnsureSavedPart(part);

            return WithSession(expectedYear, report, (interop, session) =>
                ReadOpenPartCopy(interop, session, OpenCadParts.RefreshSelection(interop, session, part), configuration));
        }

        public static OpenPartInfo GetActivePart(string expectedYear, Action<string> report) =>
            WithSession(expectedYear, report, OpenCadParts.Active);

        public static CadPartPreview PreviewFile(string path, string configuration, string expectedYear, Action<string> report)
        {
            return WithSession(expectedYear, report, (interop, session) =>
            {
                var preview = new CadPartPreview();
                preview.Inspection = ReadSavedCopy(interop, session, path, configuration, preview);
                return preview;
            });
        }

        public static CadPartPreview PreviewOpenPart(OpenPartInfo part, string configuration,
            string expectedYear, Action<string> report)
        {
            EnsureSavedPart(part);
            return WithSession(expectedYear, report, (interop, session) =>
            {
                var preview = new CadPartPreview();
                var refreshed = OpenCadParts.RefreshSelection(interop, session, part);
                preview.Inspection = ReadOpenPartCopy(interop, session, refreshed, configuration, preview);
                return preview;
            });
        }

        private static T WithSession<T>(string expectedYear, Action<string> report,
            Func<CadInterop, CadSession, T> action, CadDetection detection = null)
        {
            if (Environment.OSVersion.Platform != PlatformID.Win32NT ||
                Thread.CurrentThread.GetApartmentState() != ApartmentState.STA)
                throw new InvalidOperationException("SOLIDWORKS 分析需要 Windows x64 STA 线程。");

            Action<string> log = report ?? (_ => { });
            WindowsEnvironment.Report(log, detection);
            using (var sessionLock = new Mutex(false, @"Local\SWGrade.SolidWorks"))
            {
                bool acquired;
                bool abandoned = false;
                try
                {
                    acquired = sessionLock.WaitOne(0);
                }
                catch (AbandonedMutexException)
                {
                    acquired = true;
                    abandoned = true;
                }

                if (!acquired)
                    throw new InvalidOperationException("当前 Windows 会话已有 SWGrade 正在操作 SOLIDWORKS。");

                try
                {
                    if (abandoned)
                        log("上一次探测异常退出。若 CAD 中仍有探针副本，请检查并手动关闭。");
                    using (var interop = new CadInterop(log))
                        return action(interop, CadSession.Connect(interop, expectedYear, detection));
                }
                finally
                {
                    sessionLock.ReleaseMutex();
                }
            }
        }

        private static void EnsureSavedPart(OpenPartInfo part)
        {
            if (part == null || !part.CanRead)
                throw new InvalidOperationException("该零件没有可读取的 .SLDPRT 磁盘版本。请先在 SOLIDWORKS 中保存。");
        }

        private static ModelInspectionResult ReadOpenPartCopy(CadInterop interop, CadSession session, OpenPartInfo part,
            string configuration, CadPartPreview preview = null)
        {
            EnsureSavedPart(part);
            interop.Log("已从 CAD 会话选定零件。接下来读取磁盘上的已保存版本。");
            string warning = part.NeedsSave ? "SOLIDWORKS 标记该文档需保存。本次读取磁盘已保存版本，当前未保存改动未纳入。" : null;
            if (warning != null)
                interop.Log(warning);

            var result = ReadSavedCopy(interop, session, part.Path, part.ConfigurationForRead(configuration), preview);
            result.FromOpenCadDocument = true;
            result.OpenCadDocumentNeedsSave = part.NeedsSave;
            if (warning != null)
                result.WarningMessages.Add(warning);

            return result;
        }

        private static ModelInspectionResult ReadSavedCopy(CadInterop interop, CadSession session, string modelPath,
            string configuration, CadPartPreview preview = null)
        {
            using (var copy = ModelCopy.Create(modelPath))
            {
                var result = new ModelInspectionResult
                {
                    SourcePath = copy.SourcePath,
                    DocumentHash = copy.Hash,
                    SolidWorksVersion = session.Year,
                    SolidWorksRevision = session.Revision
                };
                interop.Log("源文件：{0}", copy.SourcePath);
                interop.Log("SHA-256：{0}", copy.Hash);
                try
                {
                    Action<object> readPreview = null;
                    if (preview != null)
                        readPreview = model => preview.ReadMesh(interop, model);
                    new PartProbe(interop, session.App, result).Read(copy.WorkingPath, configuration, readPreview);
                }
                finally
                {
                    copy.VerifySource();
                    interop.Log("源文件保护检查：SHA-256 与最后写入时间一致。");
                }

                result.SourceIntegrityVerified = true;
                return result;
            }
        }

        public static string DescribeError(Exception exception)
        {
            while (exception is TargetInvocationException && exception.InnerException != null)
                exception = exception.InnerException;

            return string.Format(CultureInfo.InvariantCulture,
                "{0} (HRESULT 0x{1:X8})", exception.Message, exception.HResult);
        }
    }
}

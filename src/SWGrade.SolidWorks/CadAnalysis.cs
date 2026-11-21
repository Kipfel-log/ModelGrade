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
    public static partial class CadAnalysis
    {
        public static ModelInspectionResult Run(string modelPath, string configuration, string expectedYear,
            bool probeUtilities, Action<string> report, SheetMetalMode sheetMetal = SheetMetalMode.Automatic)
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

                return ReadSavedCopy(interop, session, modelPath, configuration, sheetMetal: sheetMetal);
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
                for (var cause = exception; cause != null; cause = cause.InnerException)
                    if (!(cause is TargetInvocationException) || cause.InnerException == null)
                        report?.Invoke(cause.GetType().Name + " · " + DescribeError(cause));
                if (exception is FileNotFoundException || exception is BadImageFormatException || exception is FileLoadException)
                    detection.Message = "SOLIDWORKS 的 API 组件缺失或无法载入。请用安装管理器检查或修复 CAD 安装。";
                else if (detection.IsRunning == false && detection.InstalledYears.Count == 0)
                    detection.Message = "未确认已安装 SOLIDWORKS 2025 或 2026，也未发现正在运行的实例。请核对安装，或启动已安装的 CAD 后重试。";
                else if (exception is InvalidOperationException)
                    detection.Message = exception.Message;
                else
                    detection.Message = "CAD 连接检查失败 · " + DescribeError(exception);
            }
            return detection;
        }

        public static PartConfigurations ReadConfigurations(string modelPath, string expectedYear, Action<string> report)
        {
            return WithSession(expectedYear, report, (interop, session) =>
            {
                using (var copy = ModelCopy.Create(modelPath, interop.Log))
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

        public static CadPartPreview PreviewFile(string path, string configuration, string expectedYear, Action<string> report,
            SheetMetalMode sheetMetal = SheetMetalMode.Automatic)
        {
            return WithSession(expectedYear, report, (interop, session) =>
            {
                var preview = new CadPartPreview();
                preview.Inspection = ReadSavedCopy(interop, session, path, configuration, preview, sheetMetal);
                return preview;
            });
        }

        public static CadPartPreview PreviewOpenPart(OpenPartInfo part, string configuration,
            string expectedYear, Action<string> report, SheetMetalMode sheetMetal = SheetMetalMode.Automatic)
        {
            EnsureSavedPart(part);
            return WithSession(expectedYear, report, (interop, session) =>
            {
                var preview = new CadPartPreview();
                var refreshed = OpenCadParts.RefreshSelection(interop, session, part);
                preview.Inspection = ReadOpenPartCopy(interop, session, refreshed, configuration, preview, sheetMetal);
                return preview;
            });
        }

        public static CadPartPreview PreviewDatum(string path, string configuration, string expectedYear, Action<string> report)
        {
            return WithSession(expectedYear, report, (interop, session) =>
            {
                var preview = new CadPartPreview();
                preview.Inspection = ReadSavedCopy(interop, session, path, configuration, preview, SheetMetalMode.StandardPart,
                    inspectModel: model =>
                    {
                        var probe = new GeometryProbe(interop, session.App);
                        preview.DatumPlanes = DatumCad.ExteriorPlanes(interop, probe.CaptureSolid(model));
                    });
                VerifyGeometryConfiguration(interop, preview.Inspection, "标准件");
                if (!preview.Inspection.SourceIntegrityVerified || preview.DatumPlanes.Count < 4)
                    throw new InvalidOperationException("标准件需要可核对的单实体及至少四个外侧平面。");
                return preview;
            });
        }

        public static GeometryProbeReport ProbeGeometry(string referencePath, string studentPath,
            string referenceConfiguration, string studentConfiguration, string expectedYear,
            GeometryProbeAlignment alignment, bool includeMesh, Action<string> report, string placementReportPath = null,
            bool includeDeviation = false, AssignmentDatum datum = null)
        {
            var result = new GeometryProbeReport
            {
                ReferencePath = referencePath,
                StudentPath = studentPath,
                RequestedReferenceConfiguration = referenceConfiguration,
                RequestedStudentConfiguration = studentConfiguration,
                Alignment = alignment,
                Datum = datum,
                AlignmentState = alignment == GeometryProbeAlignment.OriginalCoordinates ? "OriginalCoordinates" :
                    alignment == GeometryProbeAlignment.ReusePlacement ? "AwaitingPlacement" :
                    alignment == GeometryProbeAlignment.AssignmentDatum ? "AwaitingDatum" : "AwaitingCoincidence"
            };

            try
            {
                if (!Enum.IsDefined(typeof(GeometryProbeAlignment), alignment))
                    throw new ArgumentException("几何探针的对齐模式无效。", nameof(alignment));

                if ((alignment == GeometryProbeAlignment.ReusePlacement) != !string.IsNullOrWhiteSpace(placementReportPath))
                    throw new ArgumentException("复用放置方式需要指定对齐报告。", nameof(placementReportPath));
                if (alignment == GeometryProbeAlignment.AssignmentDatum)
                {
                    if (datum == null)
                        throw new ArgumentException("请先选取标准件的三个对齐基准。");
                    datum.Validate();
                }

                double[] placement = null;
                if (alignment == GeometryProbeAlignment.ReusePlacement)
                {
                    string path = Path.GetFullPath(placementReportPath);
                    var calibration = InspectionJson.ReadGeometryProbe(path);
                    result.PlacementSource = GeometryProbeValidation.ReadPlacementSource(path, calibration);
                    placement = calibration.StudentToReferenceTransform;
                    result.RequestedReferenceConfiguration = referenceConfiguration ?? result.PlacementSource.ReferenceConfiguration;
                    result.RequestedStudentConfiguration = studentConfiguration ?? result.PlacementSource.StudentConfiguration;
                }

                return WithSession(expectedYear, report, (interop, session) =>
                {
                    result.CadRevision = session.Revision;
                    var probe = new GeometryProbe(interop, session.App);
                    ReadSavedCopy(interop, session, referencePath, result.RequestedReferenceConfiguration, sheetMetal: SheetMetalMode.StandardPart,
                        recordInspection: inspection => result.Reference = inspection,
                        inspectModel: model =>
                        {
                            VerifyGeometryConfiguration(interop, result.Reference, "参考件");
                            if (alignment == GeometryProbeAlignment.AssignmentDatum &&
                                !datum.AppliesTo(result.Reference.DocumentHash, result.Reference.Configuration))
                                throw new InvalidOperationException("此基准属于其他参考件或配置，请重新设置。");
                            if (result.PlacementSource != null)
                                GeometryProbeValidation.CheckPlacementReference(result.PlacementSource, result.Reference);
                            var reference = probe.CaptureSolid(model);
                            ReadSavedCopy(interop, session, studentPath, result.RequestedStudentConfiguration, sheetMetal: SheetMetalMode.StandardPart,
                                recordInspection: inspection => result.Student = inspection,
                                inspectModel: studentModel =>
                                {
                                    VerifyGeometryConfiguration(interop, result.Student, "学生件");
                                    if (!string.Equals(result.Reference.Configuration, result.Student.Configuration,
                                            StringComparison.Ordinal) &&
                                        (result.RequestedReferenceConfiguration == null || result.RequestedStudentConfiguration == null))
                                        throw new InvalidOperationException("文件配置不同。请明确指定两侧的配置后重试。");

                                    if (result.PlacementSource != null)
                                    {
                                        GeometryProbeValidation.CheckPlacementStudent(result.PlacementSource, result.Student);
                                        if (!string.Equals(result.PlacementSource.StudentHash, result.Student.DocumentHash, StringComparison.OrdinalIgnoreCase))
                                            interop.Log("学生文件内容已变更。按命令指定的对齐报告复用其放置方式。");
                                    }
                                    var student = probe.CaptureSolid(studentModel);
                                    probe.Compare(result, reference, student, includeMesh, placement, includeDeviation);
                                    if (includeDeviation)
                                    {
                                        try { result.Deviation = DeviationProbe.Read(interop, reference, student, result.Missing, result.Extra); }
                                        catch (Exception exception)
                                        {
                                            result.DeviationIssue = DescribeError(exception);
                                            interop.Log("偏差云图未取得：" + result.DeviationIssue);
                                        }
                                    }
                                });
                        });

                    if (!result.Reference.SourceIntegrityVerified || !result.Student.SourceIntegrityVerified)
                        throw new InvalidOperationException("源文件完整性检查未通过。");

                    result.Measured = true;
                    return result;
                });
            }
            catch (Exception exception)
            {
                result.Measured = false;
                result.DifferenceRatio = null;
                result.ReviewReason = DescribeError(exception);
                return result;
            }
        }

        private static void VerifyGeometryConfiguration(CadInterop interop, ModelInspectionResult inspection, string label)
        {
            interop.Log("{0}配置：{1}；读取警告：0x{2:X8}；重建状态：{3}。",
                label, inspection.Configuration, inspection.ReviewOpenWarningCode, inspection.RebuildState);

            if (!inspection.OpenedSuccessfully || string.IsNullOrWhiteSpace(inspection.Configuration) ||
                inspection.ReviewOpenWarningCode != 0 ||
                inspection.RebuildStatusCode != interop.Constant("swModelRebuildStatus_e", "swModelRebuildStatus_FullyRebuilt"))
                throw new InvalidOperationException(label + "的配置或重建状态未通过几何比较前置检查。");
            if (inspection.FeatureTypes.ContainsKey("SheetMetal") || inspection.FeatureTypes.ContainsKey("FlatPattern"))
                throw new InvalidOperationException(label + "包含钣金特征，折弯态和展开态需单独核实。");
        }

        private static T WithSession<T>(string expectedYear, Action<string> report,
            Func<CadInterop, CadSession, T> action, CadDetection detection = null)
        {
            if (Environment.OSVersion.Platform != PlatformID.Win32NT ||
                Thread.CurrentThread.GetApartmentState() != ApartmentState.STA)
                throw new InvalidOperationException("SOLIDWORKS 分析需要 Windows x64 STA 线程。");

            Action<string> log = report ?? (_ => { });
            WindowsEnvironment.Report(log, detection);
            log("正在获取当前 Windows 会话的 CAD 分析锁。");
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
                    try { ModelCopy.RetryPending(log); }
                    finally { sessionLock.ReleaseMutex(); }
                }
            }
        }

        private static void EnsureSavedPart(OpenPartInfo part)
        {
            if (part == null || !part.CanRead)
                throw new InvalidOperationException("该零件没有可读取的 .SLDPRT 磁盘版本。请先在 SOLIDWORKS 中保存。");
        }

        private static ModelInspectionResult ReadOpenPartCopy(CadInterop interop, CadSession session, OpenPartInfo part,
            string configuration, CadPartPreview preview = null, SheetMetalMode sheetMetal = SheetMetalMode.Automatic)
        {
            EnsureSavedPart(part);
            interop.Log("已从 CAD 会话选定零件。接下来读取磁盘上的已保存版本。");
            string warning = part.NeedsSave ? "SOLIDWORKS 标记该文档需保存。本次读取磁盘已保存版本，当前未保存改动未纳入。" : null;
            if (warning != null)
                interop.Log(warning);

            var result = ReadSavedCopy(interop, session, part.Path, part.ConfigurationForRead(configuration), preview, sheetMetal);
            result.FromOpenCadDocument = true;
            result.OpenCadDocumentNeedsSave = part.NeedsSave;
            if (warning != null)
                result.WarningMessages.Add(warning);

            return result;
        }

        private static ModelInspectionResult ReadSavedCopy(CadInterop interop, CadSession session, string modelPath,
            string configuration, CadPartPreview preview = null, SheetMetalMode sheetMetal = SheetMetalMode.Automatic,
            Action<ModelInspectionResult> recordInspection = null, Action<object> inspectModel = null)
        {
            using (var copy = ModelCopy.Create(modelPath, interop.Log))
            {
                var result = new ModelInspectionResult
                {
                    SourcePath = copy.SourcePath,
                    DocumentHash = copy.Hash,
                    SolidWorksVersion = session.Year,
                    SolidWorksRevision = session.Revision
                };
                recordInspection?.Invoke(result);
                interop.Log("源文件：{0}", copy.SourcePath);
                interop.Log("SHA-256：{0}", copy.Hash);
                int references = interop.ReferenceCheckpoint;
                bool closed = false;
                try
                {
                    new PartProbe(interop, session.App, result).Read(copy.WorkingPath, configuration, sheetMetal, preview, inspectModel);
                    closed = true;
                }
                finally
                {
                    if (!closed && !IsCopyClosed(interop, session, copy.WorkingPath))
                        copy.PreserveForCad();
                    interop.ReleaseAfter(references);

                    copy.VerifySource();
                    result.SourceIntegrityVerified = true;
                    interop.Log("源文件保护检查：SHA-256 与最后写入时间一致。");
                }

                return result;
            }
        }

        private static bool IsCopyClosed(CadInterop interop, CadSession session, string path)
        {
            try
            {
                return interop.Call("ISldWorks", session.App, "GetOpenDocumentByName", path) == null;
            }
            catch (Exception exception)
            {
                interop.Log("无法核实工作副本的关闭状态：" + DescribeError(exception));
                return false;
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

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using Microsoft.Win32;
using SWGrade.Application;
using SWGrade.Domain;
using SWGrade.Infrastructure;
using SWGrade.SolidWorks;

namespace SWGrade.Desktop
{
    internal sealed class AnalysisViewModel : INotifyPropertyChanged
    {
        private readonly Window owner;
        private readonly Func<string> expectedYear;
        private readonly NotificationCenter notices;
        private AnalysisScene scene;
        private ModelComparison report;
        private string activity = "";
        private string diagnostics = "";
        private bool busy;
        private bool showResult;
        private Assignment selectedAssignment;
        private GradeResult trialGrade;
        private int resultTab;
        private string trialMessage;

        internal AnalysisViewModel(Window owner, Func<string> expectedYear, ObservableCollection<Assignment> assignments,
            NotificationCenter notices)
        {
            this.owner = owner;
            this.expectedYear = expectedYear;
            this.notices = notices;
            Assignments = assignments;
            Assignments.CollectionChanged += (_, __) => UpdateTrial();
            Reference = new PartInput(owner, this);
            Current = new PartInput(owner, this);
            SwapCommand = new RelayCommand(Swap, () => CanEdit && (Reference.HasPart || Current.HasPart));
            AnalyzeCommand = new RelayCommand(async () => await AnalyzeAsync(SheetMetal), () => CanEdit && Current.HasPart);
            CheckFlatCommand = new RelayCommand(async () => await AnalyzeAsync(SheetMetalMode.RequireFlatPattern, openFlat: true),
                () => CanEdit && ShowResult && ShowSheetMetal);
            ApplyCoordinatesCommand = new RelayCommand(async () => await AnalyzeAsync(SheetMetal),
                () => CanEdit && ShowResult && Scene?.Reference != null);
            CalculateDeviationCommand = new RelayCommand(async () => await AnalyzeAsync(SheetMetal, includeDeviation: true),
                () => CanEdit && ShowResult && HasSolidPair && Scene.Geometry?.ValidatedComparison == true);
            BackCommand = new RelayCommand(() => ShowResult = false, () => CanEdit);
            CopyCommand = new RelayCommand(Copy, () => CanEdit && Scene != null);
            ExportCommand = new RelayCommand(Export, () => CanEdit && Scene != null);
            UpdateTrial();
        }

        public event PropertyChangedEventHandler PropertyChanged;
        public PartInput Reference { get; }
        public PartInput Current { get; }
        public ICommand SwapCommand { get; }
        public ICommand AnalyzeCommand { get; }
        public ICommand CheckFlatCommand { get; }
        public ICommand ApplyCoordinatesCommand { get; }
        public ICommand CalculateDeviationCommand { get; }
        public ICommand BackCommand { get; }
        public ICommand CopyCommand { get; }
        public ICommand ExportCommand { get; }
        public bool CanEdit => !IsBusy;
        public string AnalyzeLabel => Reference.HasPart ? "开始对照 →" : "分析当前件 →";
        public SheetMetalOption[] SheetMetalOptions => SheetMetalOption.Choices;
        public GeometryCoordinateChoice[] GeometryCoordinateChoices => GeometryCoordinateChoice.Choices;
        public GeometryCoordinates GeometryCoordinates { get; set; }
        public SheetMetalMode SheetMetal { get; set; }
        public int ResultTab { get => resultTab; set { resultTab = value; Notify(); } }
        public bool ShowSheetMetal => Scene?.HasSheetMetal == true;
        public bool HasSolidPair => Scene?.Reference != null && !Scene.HasSheetMetal;
        public string FoldedTabTitle => !ShowSheetMetal ? "三维叠加" :
            (Scene.Current.Inspection.SheetMetal?.FoldedVerified == true &&
                (Scene.Reference == null || Scene.Reference.Inspection.SheetMetal?.FoldedVerified == true))
                ? "折弯态" : "保存态 / 折弯态";
        public string ResultTitle => report == null ? "单件分析结果" : "零件对照结果";
        public string Diagnostics { get => diagnostics; private set { diagnostics = value; Notify(); } }
        public string Activity
        {
            get => activity;
            private set { activity = value; Notify(); }
        }

        public AnalysisScene Scene
        {
            get => scene;
            private set
            {
                scene = value;
                Notify();
                Notify(nameof(ResultTitle));
                Notify(nameof(ShowSheetMetal));
                Notify(nameof(HasSolidPair));
                Notify(nameof(FoldedTabTitle));
                Notify(nameof(Details));
                Notify(nameof(ParameterRows));
                Notify(nameof(KeyFeatures));
                Notify(nameof(AllFeatures));
                Notify(nameof(FeatureNote));
                Notify(nameof(Findings));
                UpdateTrial();
                CommandManager.InvalidateRequerySuggested();
            }
        }

        public ObservableCollection<Assignment> Assignments { get; }

        public Assignment SelectedAssignment
        {
            get => selectedAssignment;
            set
            {
                if (selectedAssignment == value)
                    return;
                selectedAssignment = value;
                Notify();
                UpdateTrial();
            }
        }

        public string TrialMessage => trialMessage;
        public IEnumerable<ComparisonRow> TrialChecks => trialGrade?.Checks.Where(check => check.Weight > 0 || check.Name == "钣金展开图")
            .Select(check => new ComparisonRow(check)) ?? Enumerable.Empty<ComparisonRow>();

        private void UpdateTrial()
        {
            trialGrade = null;
            trialMessage = Assignments.Count == 0
                ? "保存一份作业后，即可在这里用该作业的规则试算。"
                : "选一个已保存的作业，按同一套规则查看各项得分。试算结果不会写入成绩册。";
            if (Scene != null && SelectedAssignment != null)
            {
                if (Scene.Reference == null)
                    trialMessage = "先把作业的标准零件设为参考件，再试算。";
                else
                {
                    try
                    {
                        trialGrade = TrialGrading.Evaluate(SelectedAssignment, Scene.Reference.Inspection,
                            Scene.Current.Inspection, Scene.Geometry);
                        trialMessage = trialGrade.FinalAutoScore.HasValue
                            ? $"自动得分 {ReportExporter.Score(trialGrade.FinalAutoScore)} / 100"
                            : trialGrade.EvaluatedPoints == 0 ? "暂无可评估条目，100 分待复核。"
                            : $"自动得分 {ReportExporter.Score(trialGrade.EarnedPoints)} / {ReportExporter.Score(trialGrade.EvaluatedPoints)} · 另 {ReportExporter.Score(trialGrade.ReviewPoints)} 分待复核";
                    }
                    catch (Exception exception) when (exception is InvalidOperationException || exception is ArgumentException)
                    {
                        trialMessage = exception.Message;
                    }
                }
            }
            Notify(nameof(TrialMessage));
            Notify(nameof(TrialChecks));
        }

        public bool ShowResult
        {
            get => showResult;
            private set { showResult = value; Notify(); }
        }

        public bool IsBusy
        {
            get => busy;
            private set
            {
                busy = value;
                Notify();
                Notify(nameof(CanEdit));
                CommandManager.InvalidateRequerySuggested();
            }
        }

        public PartDetails Details => Scene == null ? null :
            new PartDetails(Scene.Current.Inspection, Scene.Reference?.Inspection, Scene.HasSheetMetal);

        public IEnumerable<ComparisonRow> ParameterRows => report?.Checks.Where(check =>
            check.Name != "模型与配置" && check.Name != "几何差异")
            .Select(check => new ComparisonRow(check)) ?? Enumerable.Empty<ComparisonRow>();

        public IEnumerable<FeatureRow> KeyFeatures => AllFeatures.Where(row => row.IsKeyFeature);

        public IEnumerable<FeatureRow> AllFeatures
        {
            get
            {
                var current = Scene?.Current?.Inspection;
                if (current == null)
                    yield break;
                var reference = Scene.Reference?.Inspection;
                var types = current.FeatureTypes.Keys.Union(reference?.FeatureTypes.Keys ?? Enumerable.Empty<string>());
                foreach (var kind in types.OrderBy(kind => kind, StringComparer.Ordinal))
                    yield return new FeatureRow(kind, CountActive(current, kind), CountActive(reference, kind));
            }
        }

        public string FeatureNote => Scene?.Current?.Inspection.FeatureCountsComplete == false ||
            Scene?.Reference?.Inspection.FeatureCountsComplete == false
            ? "部分特征未读取完整。已确认的数量用「至少」标出。"
            : "只统计当前配置中启用的特征。特征种类和数量可辅助复核建模过程。";

        public IEnumerable<AnalysisFinding> Findings
        {
            get
            {
                if (Scene == null)
                    yield break;
                string blocker = report?.Checks.FirstOrDefault(check => check.Name == "模型与配置")?.Reason;
                if (blocker != null)
                    yield return new AnalysisFinding("检查配置或保存状态", blocker +
                        " 请核对两边的配置，在 SOLIDWORKS 中重建并保存有问题的零件，再重新分析。");

                if (report != null)
                {
                    foreach (var check in report.Checks.Where(check => check.Status == CheckStatus.Fail))
                        yield return AnalysisFinding.ForCheck(check);
                    foreach (var check in report.Checks.Where(check => check.Status == CheckStatus.Unverified &&
                        check.Name != "模型与配置" && check.Name != "质量" && check.Name != "零件级材料" &&
                        check.Name != "几何差异" && check.Reason != blocker))
                        yield return AnalysisFinding.ForCheck(check);
                }

                var current = Scene.Current.Inspection;
                var reference = Scene.Reference?.Inspection;
                if (current.OpenCadDocumentNeedsSave == true || reference?.OpenCadDocumentNeedsSave == true)
                    yield return new AnalysisFinding("有尚未保存的 CAD 改动", "本次比较的是磁盘上保存的版本。如需比较最新修改，请在 SOLIDWORKS 保存后重试。");
                if (report == null && (current.RebuildStatusCode != 0 || current.ReviewOpenWarningCode != 0))
                    yield return new AnalysisFinding("请检查模型状态", "请在 SOLIDWORKS 确认重建及打开状态后重新分析。");
                if (report == null && !current.VolumeM3.HasValue)
                    yield return new AnalysisFinding("体积和面积未取得", current.SolidBodyCount == 0
                        ? "当前配置没有实体。"
                        : "SOLIDWORKS 未返回有效的实体几何度量。请检查当前配置和实体。");
                if (!current.FeatureCountsComplete || reference?.FeatureCountsComplete == false)
                    yield return new AnalysisFinding("特征清单读取不完整", "「建模特征」显示已核实的数量。");
            }
        }

        private static string CountActive(ModelInspectionResult model, string kind)
        {
            if (model == null)
                return "—";
            model.ActiveFeatureTypes.TryGetValue(kind, out int active);
            return model.IsActiveFeatureCountVerified(kind) ? active.ToString() :
                active == 0 ? "未核实" : "至少 " + active;
        }

        internal async Task SelectAsync(PartInput target, bool active)
        {
            IsBusy = true;
            Activity = "正在查询 SOLIDWORKS 打开的零件…";
            try
            {
                string version = expectedYear();
                IProgress<string> progress = new Progress<string>(line => Diagnostics += line + Environment.NewLine);
                var selected = active
                    ? await StaWorker.Run(() => CadAnalysis.GetActivePart(version, progress.Report))
                    : await OpenPartPicker.PickAsync(owner, version, progress.Report);
                if (selected == null)
                    return;
                if (!selected.CanRead)
                    throw new InvalidOperationException("请先在 SOLIDWORKS 中将该零件保存为 .SLDPRT。");
                target.Set(selected.Path, selected, null);
                if (selected.NeedsSave)
                    notices.Publish("零件有未保存的改动", "分析将读取磁盘上的保存版本。", NoticeKind.Warning);
            }
            catch (Exception exception)
            {
                Diagnostics += CadAnalysis.DescribeError(exception) + Environment.NewLine;
                notices.Publish("无法选择零件", FriendlyError(exception), NoticeKind.Error);
            }
            finally
            {
                Activity = "";
                IsBusy = false;
            }
        }

        internal void UpdateInputs()
        {
            Notify(nameof(AnalyzeLabel));
            CommandManager.InvalidateRequerySuggested();
        }

        internal void ShowCloseMessage() =>
            notices.Publish("分析还在运行", "请等待当前 CAD 调用返回及临时副本关闭。", NoticeKind.Warning);

        private void Swap()
        {
            string path = Reference.SourcePath;
            var openPart = Reference.OpenPart;
            string config = Reference.Configuration;
            Reference.Set(Current.SourcePath, Current.OpenPart, Current.Configuration);
            Current.Set(path, openPart, config);
        }

        private async Task AnalyzeAsync(SheetMetalMode mode, bool openFlat = false, bool includeDeviation = false)
        {
            var coordinates = includeDeviation ? Scene.Geometry.Coordinates : GeometryCoordinates;
            IsBusy = true;
            Diagnostics = "";
            Activity = openFlat ? "正在展开只读副本…" : "正在读取只读副本…";
            if (!openFlat && !includeDeviation)
            {
                ShowResult = false;
                ResultTab = 0;
                report = null;
                Scene = null;
            }
            try
            {
                string version = expectedYear();
                IProgress<string> progress = new Progress<string>(line => Diagnostics += line + Environment.NewLine);
                var reference = Reference.HasPart ? await Reference.ReadAsync(version, progress.Report, mode) : null;
                var current = await Current.ReadAsync(version, progress.Report, mode);
                var result = new AnalysisScene(reference, current, mode);
                if (reference != null)
                {
                    var standard = reference.Inspection;
                    var student = current.Inspection;
                    bool configured = standard.Configuration == student.Configuration ||
                        (Reference.RequestedConfiguration != null && Current.RequestedConfiguration != null);
                    if (!result.HasSheetMetal && configured)
                    {
                        Activity = includeDeviation ? "正在计算 SOLIDWORKS 表面偏差…" : "正在计算 SOLIDWORKS 实体差集…";
                        result.SolidComparison = await CadGeometryComparison.CompareAsync(standard, student,
                            coordinates, true, progress.Report, includeDeviation);
                    }
                    result.Geometry = result.SolidComparison?.EvidenceFor(standard, student) ??
                        SavedGeometry.CompareIdenticalSnapshot(standard, student, coordinates);
                }
                report = reference == null ? null :
                    ModelComparison.Evaluate(reference.Inspection, current.Inspection,
                        Reference.RequestedConfiguration, Current.RequestedConfiguration, mode, result.Geometry);

                Scene = result;
                if (openFlat)
                    ResultTab = 1;
                ShowResult = true;
            }
            catch (Exception exception)
            {
                string reason = CadAnalysis.DescribeError(exception);
                Diagnostics += reason + Environment.NewLine;
                notices.Publish("模型分析失败", FriendlyError(exception), NoticeKind.Error);
            }
            finally
            {
                Activity = "";
                IsBusy = false;
            }
        }

        private string ResultJson() => report == null ?
            InspectionJson.Serialize(Scene.Current.Inspection) : InspectionJson.SerializeComparison(report);

        private void Copy()
        {
            try
            {
                Clipboard.SetText(ResultJson());
                notices.Publish("已复制", "分析结果 JSON 已复制到剪贴板。");
            }
            catch (Exception exception)
            {
                Diagnostics += CadAnalysis.DescribeError(exception) + Environment.NewLine;
                notices.Publish("复制失败", "剪贴板暂时无法写入，请再试一次。", NoticeKind.Error);
            }
        }

        private void Export()
        {
            try
            {
                var dialog = new SaveFileDialog
                {
                    Title = "导出分析 JSON",
                    Filter = "JSON 文件 (*.json)|*.json",
                    DefaultExt = ".json",
                    AddExtension = true,
                    OverwritePrompt = false,
                    FileName = Path.GetFileNameWithoutExtension(Scene.Current.Inspection.SourcePath) + "-分析.json"
                };
                if (dialog.ShowDialog(owner) != true)
                    return;
                InspectionJson.SaveNew(dialog.FileName, ResultJson());
                notices.Publish("已导出", "分析结果 JSON 已保存。");
            }
            catch (Exception exception)
            {
                Diagnostics += CadAnalysis.DescribeError(exception) + Environment.NewLine;
                notices.Publish("导出失败", "请选择可写目录和新的 .json 文件名。", NoticeKind.Error);
            }
        }

        private static string FriendlyError(Exception exception) =>
            exception is InvalidOperationException || exception is ArgumentException
                ? exception.Message
                : "无法读取零件。请确认 SOLIDWORKS 已启动，并检查所选文件和配置。详细原因可在设置的开发模式查看。";

        private void Notify([CallerMemberName] string property = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(property));
    }

    internal sealed class AnalysisRow
    {
        internal AnalysisRow(string label, string current, string reference, bool? matches)
        {
            Label = label;
            Current = current;
            Reference = reference;
            Matches = matches;
        }

        public string Label { get; }
        public string Current { get; }
        public string Reference { get; }
        public bool? Matches { get; }
    }

    internal sealed class PartDetails
    {
        private const string Unavailable = "未取得";

        internal PartDetails(ModelInspectionResult current, ModelInspectionResult reference, bool showSheetMetal)
        {
            CurrentName = Path.GetFileName(current.SourcePath);
            ReferenceName = Path.GetFileName(reference?.SourcePath);
            var fields = new List<AnalysisRow>
            {
                Row("配置", current.Configuration, reference?.Configuration,
                    TextMatches(current.Configuration, reference?.Configuration, StringComparison.Ordinal)),
                Measure("体积", current.VolumeM3, reference?.VolumeM3, ToleranceUnits.CubicCentimetresPerCubicMetre, "cm³"),
                Measure("实体表面积", current.SurfaceAreaM2, reference?.SurfaceAreaM2, ToleranceUnits.SquareCentimetresPerSquareMetre, "cm²"),
                Count("实体面数", current.SolidFaceCount, reference?.SolidFaceCount),
                Count("曲面体面数", current.SurfaceFaceCount, reference?.SurfaceFaceCount),
                Row("实体 / 曲面体", BodyCounts(current), BodyCounts(reference), reference == null ? (bool?)null :
                    current.SolidBodyCount == reference.SolidBodyCount && current.SurfaceBodyCount == reference.SurfaceBodyCount),
                Measure("质量", current.MassKg, reference?.MassKg, ToleranceUnits.GramsPerKilogram, "g"),
                Row("材料", current.MaterialName, reference?.MaterialName,
                    TextMatches(current.MaterialName, reference?.MaterialName, StringComparison.OrdinalIgnoreCase)),
                Row("重建状态", Rebuild(current), Rebuild(reference), reference?.RebuildState == null || current.RebuildState == null
                    ? (bool?)null : current.RebuildStatusCode == reference.RebuildStatusCode)
            };
            if (showSheetMetal)
            {
                var currentFlat = current.SheetMetal;
                var referenceFlat = reference?.SheetMetal;
                fields.Add(Row("展开读取", FlatStatus(currentFlat), FlatStatus(referenceFlat),
                    currentFlat?.HasFlatMeasurements == true && referenceFlat?.HasFlatMeasurements == true ? true : (bool?)null));
                if (currentFlat?.HasFlatMeasurements == true || referenceFlat?.HasFlatMeasurements == true)
                    fields.Add(Measure("展开态表面积", currentFlat?.FlatSurfaceAreaM2, referenceFlat?.FlatSurfaceAreaM2,
                        ToleranceUnits.SquareCentimetresPerSquareMetre, "cm²"));
            }
            Fields = fields;

            AnalysisRow Row(string label, string currentValue, string referenceValue, bool? matches) =>
                new AnalysisRow(label, currentValue ?? Unavailable, reference == null ? null : referenceValue ?? Unavailable, matches);

            AnalysisRow Count(string label, int? currentCount, int? referenceCount) =>
                Row(label, currentCount?.ToString(), referenceCount?.ToString(),
                    currentCount.HasValue && referenceCount.HasValue ? currentCount == referenceCount : (bool?)null);

            AnalysisRow Measure(string label, double? currentValue, double? referenceValue, double conversion, string unit)
            {
                // Ignore numerical noise from rebuilding the same CAD geometry.
                bool? matches = currentValue.HasValue && referenceValue.HasValue
                    ? Math.Abs(currentValue.Value - referenceValue.Value) <=
                        1e-9 * Math.Max(Math.Abs(currentValue.Value), Math.Abs(referenceValue.Value))
                    : (bool?)null;
                return Row(label, Unit(currentValue, conversion, unit), Unit(referenceValue, conversion, unit), matches);
            }
        }

        public string CurrentName { get; }
        public string ReferenceName { get; }
        public IReadOnlyList<AnalysisRow> Fields { get; }

        private static bool? TextMatches(string current, string reference, StringComparison comparison) =>
            string.IsNullOrWhiteSpace(current) || string.IsNullOrWhiteSpace(reference)
                ? (bool?)null : string.Equals(current, reference, comparison);

        private static string BodyCounts(ModelInspectionResult model) =>
            model == null ? null : $"{model.SolidBodyCount} / {model.SurfaceBodyCount}";

        private static string Rebuild(ModelInspectionResult model) =>
            model?.RebuildState == null ? null : model.RebuildStatusCode == 0 ? "已完成" : "需确认";

        private static string FlatStatus(SheetMetalInspection sheet) => sheet?.HasFlatMeasurements == true
            ? "已读取" : sheet?.Issue == null ? null : "待核对";

        private static string Unit(double? value, double conversion, string unit) => value.HasValue
            ? ReportExporter.Measurement(ToleranceUnits.ToDisplay(value.Value, conversion), unit) : null;
    }

    internal sealed class FeatureRow
    {
        private static readonly Dictionary<string, string> Names = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            { "Boss", "拉伸凸台" }, { "BaseBody", "拉伸基体" }, { "BossThin", "薄壁拉伸" }, { "Cut", "切除拉伸" }, { "CutThin", "薄壁切除" },
            { "Fillet", "圆角" }, { "Chamfer", "倒角" }, { "HoleWzd", "异型孔" },
            { "Revolution", "旋转" }, { "RevCut", "旋转切除" }, { "Sweep", "扫描" },
            { "Blend", "放样" }, { "BlendCut", "放样切除" }, { "LPattern", "线性阵列" }, { "CirPattern", "圆周阵列" },
            { "ProfileFeature", "草图" }, { "3DProfileFeature", "三维草图" },
            { "SheetMetal", "钣金" }, { "FlatPattern", "平板型式（控制展开）" },
            { "UnFold", "展开折弯" }, { "Fold", "折叠" }, { "SMBaseFlange", "钣金基体法兰" },
            { "EdgeFlange", "钣金边线法兰" }
        };

        internal FeatureRow(string code, string current, string reference)
        {
            Code = code;
            Name = Names.TryGetValue(code, out string name) ? name : code;
            Current = current;
            Reference = reference;
            IsKeyFeature = name != null;
        }

        public string Code { get; }
        public string Name { get; }
        public string Current { get; }
        public string Reference { get; }
        public bool IsKeyFeature { get; }
    }

    internal sealed class AnalysisFinding
    {
        internal AnalysisFinding(string name, string detail)
        {
            Name = name;
            Detail = detail;
        }

        public string Name { get; }
        public string Detail { get; }

        internal static AnalysisFinding ForCheck(GradeCheck check)
        {
            if (check.Status != CheckStatus.Fail)
                return new AnalysisFinding(check.Name + "未能核对", check.Reason);
            var row = new ComparisonRow(check);
            return new AnalysisFinding(check.Name + "与参考件不同", check.AbsoluteError.HasValue
                ? $"参考 {row.Standard}，当前 {row.Student}。实际差值 {row.Absolute}，允许 {row.Tolerance}。"
                : check.Reason);
        }
    }

    public sealed class AnalysisScene
    {
        internal AnalysisScene(CadPartPreview reference, CadPartPreview current, SheetMetalMode mode)
        {
            Reference = reference;
            Current = current;
            HasSheetMetal = mode == SheetMetalMode.RequireFlatPattern ||
                reference?.Inspection.SheetMetal?.Detected == true || current?.Inspection.SheetMetal?.Detected == true ||
                reference?.Inspection.SheetMetal?.Issue != null || current?.Inspection.SheetMetal?.Issue != null;
        }

        internal bool HasSheetMetal { get; }

        internal CadPartPreview Reference { get; }
        internal CadPartPreview Current { get; }
        internal GeometryProbeReport SolidComparison { get; set; }
        internal GeometryEvidence Geometry { get; set; }
    }
}

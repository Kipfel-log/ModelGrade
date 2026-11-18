using System;
using System.Collections.Generic;
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
        private AnalysisScene scene;
        private ModelComparison report;
        private string status = "";
        private string diagnostics = "";
        private bool busy;
        private bool showResult;
        private bool onlyIssues = true;
        private bool hasError;

        internal AnalysisViewModel(Window owner, Func<string> expectedYear)
        {
            this.owner = owner;
            this.expectedYear = expectedYear;
            Reference = new PartInput(owner, this);
            Current = new PartInput(owner, this);
            SwapCommand = new RelayCommand(Swap, () => CanEdit && (Reference.HasPart || Current.HasPart));
            AnalyzeCommand = new RelayCommand(async () => await AnalyzeAsync(), () => CanEdit && Current.HasPart);
            BackCommand = new RelayCommand(() => ShowResult = false, () => CanEdit);
            CopyCommand = new RelayCommand(Copy, () => CanEdit && Scene != null);
            ExportCommand = new RelayCommand(Export, () => CanEdit && Scene != null);
        }

        public event PropertyChangedEventHandler PropertyChanged;
        public PartInput Reference { get; }
        public PartInput Current { get; }
        public ICommand SwapCommand { get; }
        public ICommand AnalyzeCommand { get; }
        public ICommand BackCommand { get; }
        public ICommand CopyCommand { get; }
        public ICommand ExportCommand { get; }
        public bool CanEdit => !IsBusy;
        public string AnalyzeLabel => Reference.HasPart ? "开始对照 →" : "分析当前件 →";
        public string ResultTitle => report == null ? "单件分析结果" : "零件对照结果";
        public string Diagnostics { get => diagnostics; private set { diagnostics = value; Notify(); } }
        public string Status
        {
            get => status;
            private set { status = value; Notify(); Notify(nameof(HasStatus)); }
        }

        public bool HasStatus => !string.IsNullOrWhiteSpace(Status);
        public bool HasError { get => hasError; private set { hasError = value; Notify(); } }

        public AnalysisScene Scene
        {
            get => scene;
            private set
            {
                scene = value;
                Notify();
                Notify(nameof(ResultTitle));
                Notify(nameof(Overview));
                Notify(nameof(Details));
                Notify(nameof(FeatureRows));
                Notify(nameof(Findings));
                Notify(nameof(FindingHeading));
                CommandManager.InvalidateRequerySuggested();
            }
        }

        public bool OnlyIssues
        {
            get => onlyIssues;
            set
            {
                onlyIssues = value;
                Notify();
                Notify(nameof(Findings));
            }
        }

        public string FindingHeading => report == null ? "异常与提示" : "异常与待复核";

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

        public IEnumerable<AnalysisRow> Overview
        {
            get
            {
                if (Scene == null)
                    yield break;

                var current = Scene.Current.Inspection;
                var reference = Scene.Reference?.Inspection;
                yield return Row("当前件", Path.GetFileName(current.SourcePath));
                if (reference != null)
                    yield return Row("参考件", Path.GetFileName(reference.SourcePath));
                yield return Row("当前配置", current.Configuration);
                if (reference != null)
                    yield return Row("参考配置", reference.Configuration);
                yield return Row("当前体积", Volume(current.VolumeM3));
                yield return Row("当前质量", ReportExporter.Measurement(current.MassKg, "kg"));
                yield return Row("当前材料", current.MaterialName ?? "待复核");
            }
        }

        public IEnumerable<AnalysisRow> Details
        {
            get
            {
                if (Scene == null)
                    yield break;
                var current = Scene.Current.Inspection;
                if (Scene.Reference != null)
                {
                    foreach (var entry in ModelDetails("参考件", Scene.Reference.Inspection))
                        yield return entry;
                }
                foreach (var entry in ModelDetails("当前件", current))
                    yield return entry;
            }
        }

        public IEnumerable<FeatureRow> FeatureRows
        {
            get
            {
                if (Scene?.Reference != null)
                {
                    foreach (var item in Scene.Reference.Inspection.FeatureTypes.OrderBy(item => item.Key, StringComparer.Ordinal))
                        yield return new FeatureRow("参考件", item.Key, item.Value);
                }
                if (Scene?.Current != null)
                {
                    foreach (var item in Scene.Current.Inspection.FeatureTypes.OrderBy(item => item.Key, StringComparer.Ordinal))
                        yield return new FeatureRow("当前件", item.Key, item.Value);
                }
            }
        }

        public IEnumerable<AnalysisFinding> Findings
        {
            get
            {
                if (Scene == null)
                    yield break;
                if (report != null)
                {
                    foreach (var check in report.Checks.Where(check => !OnlyIssues || check.Status != CheckStatus.Pass))
                        yield return AnalysisFinding.ForCheck(check);
                }
                if (Scene.Reference != null)
                {
                    foreach (var item in InspectionMessages(Scene.Reference.Inspection))
                        yield return new AnalysisFinding("参考件", item, "待复核");
                }
                foreach (var item in InspectionMessages(Scene.Current.Inspection))
                    yield return new AnalysisFinding("当前件", item, "待复核");
            }
        }

        internal async Task SelectAsync(PartInput target, bool active)
        {
            IsBusy = true;
            HasError = false;
            Status = "正在查询 SOLIDWORKS 打开的零件…";
            try
            {
                string version = expectedYear();
                var selected = active
                    ? await StaWorker.Run(() => CadAnalysis.GetActivePart(version, null))
                    : await OpenPartPicker.PickAsync(owner, version, null);
                if (selected == null)
                {
                    Status = "已取消选择。";
                    return;
                }
                if (!selected.CanRead)
                    throw new InvalidOperationException("请先在 SOLIDWORKS 中将该零件保存为 .SLDPRT。");
                target.Set(selected.Path, selected, null);
                Status = selected.NeedsSave ? "CAD 标记该文档需保存，将读取磁盘版本。" : "";
            }
            catch (Exception exception)
            {
                HasError = true;
                Status = CadAnalysis.DescribeError(exception);
            }
            finally
            {
                IsBusy = false;
            }
        }

        internal void UpdateInputs()
        {
            Notify(nameof(AnalyzeLabel));
            CommandManager.InvalidateRequerySuggested();
        }

        internal void ShowCloseMessage() => Status = "请等待当前 CAD 调用返回及临时副本关闭。";

        private void Swap()
        {
            string path = Reference.SourcePath;
            var openPart = Reference.OpenPart;
            string config = Reference.Configuration;
            Reference.Set(Current.SourcePath, Current.OpenPart, Current.Configuration);
            Current.Set(path, openPart, config);
            HasError = false;
            Status = "";
        }

        private async Task AnalyzeAsync()
        {
            IsBusy = true;
            Diagnostics = "";
            HasError = false;
            Status = "正在读取只读副本…";
            ShowResult = false;
            report = null;
            Scene = null;
            try
            {
                string version = expectedYear();
                IProgress<string> progress = new Progress<string>(line => Diagnostics += line + Environment.NewLine);
                var reference = Reference.HasPart ? await Reference.ReadAsync(version, progress.Report) : null;
                var current = await Current.ReadAsync(version, progress.Report);
                if (reference != null)
                    report = ModelComparison.Evaluate(reference.Inspection, current.Inspection,
                        Reference.RequestedConfiguration, Current.RequestedConfiguration);

                Scene = new AnalysisScene(reference, current);
                ShowResult = true;
                Status = reference == null ? "当前件读取完成。" : "已读取两件。对照结论和待复核项见结果。";
            }
            catch (Exception exception)
            {
                string reason = CadAnalysis.DescribeError(exception);
                Diagnostics += reason + Environment.NewLine;
                HasError = true;
                Status = reason;
            }
            finally
            {
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
                HasError = false;
                Status = "结果 JSON 已复制。";
            }
            catch (Exception exception)
            {
                HasError = true;
                Status = "复制失败。 " + exception.Message;
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
                HasError = false;
                Status = "结果 JSON 已导出。";
            }
            catch (Exception exception)
            {
                HasError = true;
                Status = "导出失败。请选择新的 .json 文件名。 " + exception.Message;
            }
        }

        private static IEnumerable<string> InspectionMessages(ModelInspectionResult model) => model.Warnings.Concat(model.Errors);

        private static IEnumerable<AnalysisRow> ModelDetails(string role, ModelInspectionResult item)
        {
            yield return Row(role, item.SourcePath);
            yield return Row("来源和版本", (item.FromOpenCadDocument ? "CAD 中已打开的零件 · " : "") +
                "已保存版本 · SOLIDWORKS " + item.SolidWorksRevision);
            yield return Row("配置", item.Configuration);
            yield return Row("重建状态", $"{item.RebuildState}（{item.RebuildStatusCode}）");
            yield return Row("实体 / 曲面体", $"{item.SolidBodyCount} / {item.SurfaceBodyCount}（含隐藏）");
            yield return Row("体积", Volume(item.VolumeM3));
            yield return Row("实体表面积", ReportExporter.Measurement(item.SurfaceAreaM2, "m²"));
            yield return Row("材料", item.MaterialName ?? "待复核");
            yield return Row("质量", ReportExporter.Measurement(item.MassKg, "kg"));
            yield return Row("密度", ReportExporter.Measurement(item.DensityKgPerM3, "kg/m³"));
            yield return Row("源文件校验", item.SourceIntegrityVerified ? "哈希与最后写入时间一致" : "待复核");
            yield return Row("SHA-256", item.DocumentHash);
        }

        private static string Volume(double? value) => value.HasValue
            ? ReportExporter.Measurement(value, "m³") + " / " + ReportExporter.Measurement(
                ModelUnits.CubicMetresToCubicCentimetres(value.Value), "cm³")
            : "待复核";

        private static AnalysisRow Row(string name, string value) => new AnalysisRow(name, value);

        private void Notify([CallerMemberName] string property = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(property));
    }

    internal sealed class AnalysisRow
    {
        internal AnalysisRow(string label, string value) { Label = label; Value = value; }
        public string Label { get; }
        public string Value { get; }
    }

    internal sealed class FeatureRow
    {
        internal FeatureRow(string role, string name, int count) { Role = role; Name = name; Count = count; }
        public string Role { get; }
        public string Name { get; }
        public int Count { get; }
    }

    internal sealed class AnalysisFinding
    {
        internal AnalysisFinding(string name, string detail, string status)
        {
            Name = name; Detail = detail; Status = status;
        }

        public string Name { get; }
        public string Detail { get; }
        public string Status { get; }

        internal static AnalysisFinding ForCheck(GradeCheck check)
        {
            string detail = check.Reason;
            if (check.AbsoluteError.HasValue)
            {
                detail += Environment.NewLine +
                    $"参考 {ReportExporter.Measurement(check.StandardValue, check.Unit)} · 当前 {ReportExporter.Measurement(check.StudentValue, check.Unit)}" +
                    Environment.NewLine +
                    $"绝对误差 {ReportExporter.Measurement(check.AbsoluteError, check.Unit)} · 容差 {ReportExporter.Measurement(check.EffectiveTolerance, check.Unit)}" +
                    Environment.NewLine +
                    $"相对误差 {ReportExporter.Measurement(check.RelativeError, null)}";
            }
            return new AnalysisFinding(check.Name, detail, StateText.Check(check.Status));
        }
    }

    public sealed class AnalysisScene
    {
        internal AnalysisScene(CadPartPreview reference, CadPartPreview current)
        {
            Reference = reference;
            Current = current;
        }

        internal CadPartPreview Reference { get; }
        internal CadPartPreview Current { get; }
    }
}

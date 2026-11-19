using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using Microsoft.Win32;
using SWGrade.Application;
using SWGrade.Domain;
using SWGrade.Infrastructure;
using SWGrade.SolidWorks;
using Forms = System.Windows.Forms;

namespace SWGrade.Desktop
{
    internal sealed class TeacherViewModel : INotifyPropertyChanged, IDisposable
    {
        private readonly Window owner;
        private readonly NotificationCenter notices;
        private readonly string folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "SWGrade", "Teacher");
        private SqliteGradeStore store;
        private Assignments assignments;
        private BatchGrading batch;
        private Assignment current;
        private SubmissionRow selected;
        private RulesDraft draft = new RulesDraft();
        private bool isBusy;
        private bool isBatch;
        private bool preflightPassed;
        private bool pauseRequested;
        private bool cancelRequested;
        private string activity = "";
        private string precheck = "";
        private int workspaceTab;
        private int detailTab;
        private string studentId;
        private string studentName;
        private string reviewScore;
        private string reviewReason;

        internal TeacherViewModel(Window owner, NotificationCenter notices)
        {
            this.owner = owner;
            this.notices = notices;
            ReopenCommand = Command(() => OpenStore(showSuccess: true), () => store == null);
            OpenFolderCommand = Command(() => Process.Start(new ProcessStartInfo { FileName = folder, UseShellExecute = true }),
                () => store != null);
            NewDraftCommand = Command(() => { Draft = new RulesDraft(); WorkspaceTab = 1; }, () => CanEdit);
            ReuseRulesCommand = Command(async () => await WithBusyAsync(ReuseRulesAsync), () => HasAssignment);
            ChooseStandardCommand = Command(async () => await WithBusyAsync(ChooseStandardAsync), () => CanEdit);
            RefreshConfigurationsCommand = Command(async () => await WithBusyAsync(ReadDraftConfigurationsAsync),
                () => CanEdit && !string.IsNullOrWhiteSpace(Draft.StandardPath));
            SelectStudentConfigurationCommand = Command(async () => await WithBusyAsync(SelectStudentConfigurationAsync),
                () => HasAssignment && SelectedNeedsConfiguration);
            ChooseOpenStandardCommand = Command(async () => await WithBusyAsync(ChooseOpenStandardAsync), () => CanEdit);
            RedistributeWeightsCommand = Command(() => Draft.Redistribute(), () => CanEdit && Draft.CanRedistribute());
            SaveAssignmentCommand = Command(async () => await WithBusyAsync(SaveAssignmentAsync), () => CanEdit);
            PrecheckCommand = Command(async () => await WithBusyAsync(PrecheckAsync), () => HasAssignment);
            ImportFilesCommand = Command(async () => await WithBusyAsync(ChooseFilesAsync), () => HasAssignment);
            ImportFolderCommand = Command(async () => await WithBusyAsync(ChooseFolderAsync), () => HasAssignment);
            ImportOpenPartCommand = Command(async () => await WithBusyAsync(ImportOpenPartAsync), () => HasAssignment);
            StartCommand = Command(async () => await WithBusyAsync(RunBatchAsync),
                () => HasAssignment && preflightPassed && PendingCount > 0);
            PauseCommand = Command(Pause, () => isBatch && !pauseRequested && !cancelRequested);
            CancelCommand = Command(Cancel, () => (isBatch && !cancelRequested) || (HasAssignment && PendingCount > 0));
            RetryCommand = Command(Retry, () => HasAssignment && Selected != null &&
                (Selected.Item.State == TaskState.Failed || Selected.Item.State == TaskState.Canceled));
            SaveStudentCommand = Command(SaveStudent, () => HasAssignment && Selected != null);
            SaveReviewCommand = Command(SaveReview, () => HasAssignment && Selected?.Item.Grade != null);
            ExportCsvCommand = Command(ExportCsv, () => HasAssignment && Rows.Count > 0);
            ExportHtmlCommand = Command(ExportHtml, () => HasAssignment && Selected != null);
            Draft.PropertyChanged += DraftChanged;
            OpenStore();
        }

        private static void DraftChanged(object sender, PropertyChangedEventArgs args)
        {
            if (args.PropertyName == nameof(RulesDraft.WeightTotal))
                CommandManager.InvalidateRequerySuggested();
        }

        public event PropertyChangedEventHandler PropertyChanged;
        public ObservableCollection<Assignment> Jobs { get; } = new ObservableCollection<Assignment>();
        public ObservableCollection<SubmissionRow> Rows { get; } = new ObservableCollection<SubmissionRow>();
        public SheetMetalOption[] SheetMetalOptions => SheetMetalOption.Choices;
        public string[] CadYears { get; } = new[] { "自动识别", "2025", "2026" };
        public string[] GeometryPolicies { get; } = new[] { "通过 / 不通过", "分档", "教师复核" };

        public ICommand ReopenCommand { get; }
        public ICommand OpenFolderCommand { get; }
        public ICommand NewDraftCommand { get; }
        public ICommand ReuseRulesCommand { get; }
        public ICommand ChooseStandardCommand { get; }
        public ICommand RefreshConfigurationsCommand { get; }
        public ICommand SelectStudentConfigurationCommand { get; }
        public bool SelectedNeedsConfiguration => Selected?.Item.NeedsConfigurationChoice == true;
        public ICommand ChooseOpenStandardCommand { get; }
        public ICommand RedistributeWeightsCommand { get; }
        public ICommand SaveAssignmentCommand { get; }
        public ICommand PrecheckCommand { get; }
        public ICommand ImportFilesCommand { get; }
        public ICommand ImportFolderCommand { get; }
        public ICommand ImportOpenPartCommand { get; }
        public ICommand StartCommand { get; }
        public ICommand PauseCommand { get; }
        public ICommand CancelCommand { get; }
        public ICommand RetryCommand { get; }
        public ICommand SaveStudentCommand { get; }
        public ICommand SaveReviewCommand { get; }
        public ICommand ExportCsvCommand { get; }
        public ICommand ExportHtmlCommand { get; }

        public bool IsBusy
        {
            get => isBusy;
            private set
            {
                isBusy = value;
                Notify();
                Notify(nameof(CanEdit));
                CommandManager.InvalidateRequerySuggested();
            }
        }

        public bool CanEdit => store != null && !IsBusy;
        private bool HasAssignment => CanEdit && Current != null;
        public int PendingCount => Rows.Count(row => row.Item.State == TaskState.Pending);
        public int SettledCount => Rows.Count(row => row.Item.State != TaskState.Pending && row.Item.State != TaskState.Running);
        public int TotalCount => Rows.Count;
        public int ProgressMaximum => Math.Max(1, TotalCount);
        public string QueueSummary => $"已结束 {SettledCount} / {TotalCount}，待处理 {PendingCount}";
        public string StandardSummary => Current == null ? "请新建或选择作业。" :
            $"标准件 {Path.GetFileName(Current.StandardPath)} · {(preflightPassed ? "已预检" : "待预检")} · 几何 {Current.Rules.GeometryWeight} / 参数 {Current.Rules.ParametersWeight} / 特征 {Current.Rules.FeaturesWeight} 分";

        public Assignment Current
        {
            get => current;
            set
            {
                if (current == value)
                    return;
                current = value;
                Precheck = "";
                preflightPassed = false;
                Notify();
                Notify(nameof(StandardSummary));
                ExecuteSafely(ReloadRows);
            }
        }

        public SubmissionRow Selected
        {
            get => selected;
            set
            {
                if (selected == value)
                    return;
                selected = value;
                StudentId = value?.Item.StudentId;
                StudentName = value?.Item.StudentName;
                ReviewScore = ReportExporter.Score(value?.Item.FinalScore);
                ReviewReason = "";
                Notify();
                NotifyDetails();
            }
        }

        public RulesDraft Draft
        {
            get => draft;
            private set
            {
                draft.PropertyChanged -= DraftChanged;
                draft = value;
                draft.PropertyChanged += DraftChanged;
                Notify();
            }
        }

        public string Activity { get => activity; private set { activity = value; Notify(); } }
        public string Precheck { get => precheck; private set { precheck = value; Notify(); } }
        public int WorkspaceTab
        {
            get => workspaceTab;
            set
            {
                if (workspaceTab == value)
                    return;
                workspaceTab = value;
                Notify();
                Notify(nameof(WorkspaceHeading));
                Notify(nameof(IsBatchPage));
                Notify(nameof(WorkspaceDescription));
            }
        }

        public bool IsBatchPage => WorkspaceTab == 0;
        public string WorkspaceHeading => WorkspaceTab == 1 ? "作业管理" : "批量批改";
        public string WorkspaceDescription => WorkspaceTab == 1
            ? "选择标准模型，设置评分规则并保存作业。"
            : "导入提交，追踪队列进度并复核结果。";
        public int DetailTab { get => detailTab; set { detailTab = value; Notify(); } }
        public string StudentId { get => studentId; set { studentId = value; Notify(); } }
        public string StudentName { get => studentName; set { studentName = value; Notify(); } }
        public string ReviewScore { get => reviewScore; set { reviewScore = value; Notify(); } }
        public string ReviewReason { get => reviewReason; set { reviewReason = value; Notify(); } }

        public IEnumerable<ComparisonRow> Comparison =>
            Selected?.Item.Grade?.Checks.Select(check => new ComparisonRow(check)) ?? Enumerable.Empty<ComparisonRow>();

        public string ResultSummary
        {
            get
            {
                var item = Selected?.Item;
                if (item == null)
                    return "选择一条提交查看结果。";
                var automatic = item.Grade == null ? "" : $" 自动已评估 {ReportExporter.Summary(item.Grade)}。";
                var final = item.FinalScore.HasValue ? $" 最终成绩 {ReportExporter.Score(item.FinalScore)}。" : "";
                return StateText.Task(item.State) + "。 " + item.StatusDetail + automatic + final;
            }
        }

        public string ResultEvidence
        {
            get
            {
                var item = Selected?.Item;
                if (item == null)
                    return "";
                var details = new StringBuilder();
                details.AppendLine("SHA-256  " + item.FileHash);
                details.AppendLine("标准配置 / 学生配置  " + item.StandardModel?.Configuration + " / " + item.StudentModel?.Configuration);
                details.AppendLine("SOLIDWORKS  " + item.StudentModel?.SolidWorksRevision);
                if (item.Grade?.Geometry?.Reason != null)
                    details.AppendLine(item.Grade.Geometry.Reason);
                if (item.StudentModel != null)
                {
                    details.AppendLine("特征类型  " + string.Join("，", item.StudentModel.FeatureTypes
                        .OrderBy(pair => pair.Key, StringComparer.Ordinal).Select(pair => pair.Key + " × " + pair.Value)));
                    foreach (string warning in item.StudentModel.Warnings.Concat(item.StudentModel.Errors))
                        details.AppendLine(warning);
                }
                foreach (var review in item.Reviews)
                    details.AppendLine($"人工复核  {review.TimeUtc}  {ReportExporter.Score(review.Score)}  {review.Reason}");
                foreach (var attempt in item.Attempts)
                {
                    details.AppendLine($"任务 {attempt.TaskId}  {attempt.StartedUtc} 至 {attempt.FinishedUtc}");
                    if (attempt.Error != null)
                        details.AppendLine(attempt.Error);
                    foreach (var line in attempt.Diagnostics)
                        details.AppendLine(line);
                }
                return details.ToString();
            }
        }

        internal Task<IReadOnlyList<HistoryEntry>> ReadHistoryAsync()
        {
            var database = store;
            if (database == null)
                return Task.FromException<IReadOnlyList<HistoryEntry>>(
                    new InvalidOperationException("本地作业未打开。请先在批量批改页重开本地作业。"));

            return Task.Run<IReadOnlyList<HistoryEntry>>(() => database.LoadAssignments()
                .SelectMany(job => database.LoadSubmissions(job.Id)
                    .Where(item => item.Attempts.Count > 0)
                    .Select(item => new HistoryEntry(job, item)))
                .OrderByDescending(entry => entry.Timestamp).ToArray());
        }

        internal void OpenHistoryEntry(HistoryEntry entry)
        {
            var job = Jobs.FirstOrDefault(item => item.Id == entry.AssignmentId);
            if (job == null || !CanEdit)
                return;

            Current = job;
            Selected = Rows.FirstOrDefault(row => row.Item.Id == entry.SubmissionId);
            DetailTab = 0;
            WorkspaceTab = 0;
        }

        internal void ShowCloseMessage() =>
            notices.Publish("作业还在处理", "请等待当前文件及记录保存完成。连续批改可先请求暂停。", NoticeKind.Warning);

        private void OpenStore(bool showSuccess = false)
        {
            try
            {
                store = new SqliteGradeStore(folder);
                assignments = new Assignments(store);
                batch = new BatchGrading(store, new CadReader(), typeof(TeacherViewModel).Assembly.GetName().Version.ToString());
                int interrupted = batch.RecoverInterrupted();
                Jobs.Clear();
                foreach (var job in store.LoadAssignments())
                    Jobs.Add(job);
                Current = Jobs.FirstOrDefault();
                if (interrupted > 0)
                    notices.Publish("发现中断的提交", $"有 {interrupted} 份上次中断的提交，可选择重试。", NoticeKind.Warning);
                else if (showSuccess)
                    notices.Publish("本地作业", "本地作业已打开。");
            }
            catch (Exception exception)
            {
                store?.Dispose();
                store = null;
                Trace.TraceWarning(CadAnalysis.DescribeError(exception));
                notices.Publish("无法打开本地作业", "请检查目录权限或关闭另一个 SWGrade 实例。", NoticeKind.Error);
            }
            Notify(nameof(CanEdit));
            CommandManager.InvalidateRequerySuggested();
        }

        private async Task ReuseRulesAsync()
        {
            Draft = RulesDraft.FromAssignment(Current);
            WorkspaceTab = 1;
            await ReadDraftConfigurationsAsync();
        }

        private async Task ChooseStandardAsync()
        {
            var picker = PartDialog(false);
            if (picker.ShowDialog(owner) == true)
            {
                Draft.ChangeStandard(picker.FileName);
                await ReadDraftConfigurationsAsync();
            }
        }

        private async Task ChooseOpenStandardAsync()
        {
            Activity = "正在查询 SOLIDWORKS 打开的零件…";
            string year = Draft.CadYear == "自动识别" ? null : Draft.CadYear;
            var selected = await OpenPartPicker.PickAsync(owner, year, null);
            if (selected == null)
            {
                return;
            }

            Draft.ChangeStandard(selected.Path);
            await ReadDraftConfigurationsAsync();
        }

        private async Task ReadDraftConfigurationsAsync()
        {
            var draftToRead = Draft;
            string path = draftToRead.StandardPath;
            string preferred = draftToRead.StandardConfiguration;
            string year = draftToRead.ExpectedYear;
            draftToRead.ConfigurationStatus = "正在读取标准模型的已保存配置…";
            Activity = "正在读取标准模型配置。";
            try
            {
                var catalog = await StaWorker.Run(() => CadAnalysis.ReadConfigurations(path, year, null));
                draftToRead.SetConfigurations(catalog, preferred);
                notices.Publish(WorkspaceHeading, "标准模型配置已读取。");
            }
            catch (Exception exception)
            {
                draftToRead.ConfigurationStatus = "暂时无法列出配置。请启动 SOLIDWORKS 或检查文件后重试。" +
                    (string.IsNullOrWhiteSpace(preferred) ? "可先保存作业，预检时使用文件的活动配置。" : "已保留原作业的选择。");
                Trace.TraceWarning("标准件配置读取失败：" + CadAnalysis.DescribeError(exception));
                notices.Publish("标准模型配置未读取", draftToRead.ConfigurationStatus, NoticeKind.Warning);
            }
        }

        private async Task SelectStudentConfigurationAsync()
        {
            var item = Selected.Item;
            var assignment = Current;
            var options = await batch.ReadStudentConfigurationsAsync(assignment, item);
            var dialog = new StudentConfigurationPicker(owner, options);
            if (dialog.ShowDialog() != true)
                return;

            batch.SetStudentConfiguration(item, options, dialog.SelectedConfiguration);
            UpdateRow(item);
            notices.Publish(WorkspaceHeading, "已指定这个学生的配置。点击开始／继续队列重新批改。");
        }

        private async Task SaveAssignmentAsync()
        {
            if (string.IsNullOrWhiteSpace(Draft.StandardPath))
                throw new ArgumentException("请先选择一份用于批改的标准件。");
            var rules = Draft.ToRules(acceptReview: true);
            var name = Draft.Name;
            var standardPath = Draft.StandardPath;
            string configurationHash = Draft.ConfigurationFileHash;
            Activity = "正在保存作业…";
            var assignment = await Task.Run(() => assignments.Create(name, standardPath, rules, configurationHash));
            Jobs.Insert(0, assignment);
            Current = assignment;
            WorkspaceTab = 0;
            notices.Publish("已保存作业", "作业规则快照已保存。");
        }

        private async Task PrecheckAsync()
        {
            Precheck = "";
            preflightPassed = false;
            Notify(nameof(StandardSummary));
            DetailTab = 2;
            Activity = "正在预检标准件。";
            var progress = new Progress<string>(message => Precheck += message + Environment.NewLine);
            var model = await batch.InspectReferenceAsync(Current, message => ((IProgress<string>)progress).Report(message));
            preflightPassed = true;
            Notify(nameof(StandardSummary));
            notices.Publish("标准件预检完成",
                $"配置 {model.Configuration}，体积 {ReportExporter.Measurement(model.VolumeM3, "m³")}。");
        }

        private async Task ChooseFilesAsync()
        {
            var picker = PartDialog(true);
            if (picker.ShowDialog(owner) == true)
                await ImportAsync(() => picker.FileNames);
        }

        private async Task ChooseFolderAsync()
        {
            using (var picker = new Forms.FolderBrowserDialog { Description = "选择包含班级作业的文件夹", ShowNewFolderButton = false })
            {
                if (picker.ShowDialog() != Forms.DialogResult.OK)
                    return;

                string path = picker.SelectedPath;
                await ImportAsync(() => Directory.EnumerateFiles(path)
                    .Where(PartFiles.IsPart).OrderBy(file => file, StringComparer.OrdinalIgnoreCase));
            }
        }

        private async Task ImportOpenPartAsync()
        {
            Activity = "正在查询 SOLIDWORKS 打开的零件…";
            var selected = await OpenPartPicker.PickAsync(owner, Current.Rules.ExpectedCadYear, null);
            if (selected == null)
            {
                return;
            }

            await ImportAsync(() => new[] { selected.Path }, selected.NeedsSave);
        }

        private async Task ImportAsync(Func<IEnumerable<string>> files, bool savedVersionOnly = false)
        {
            var job = Current;
            Activity = "正在检查并导入零件。";
            var imported = await Task.Run(() => assignments.Import(job, files()));
            ReloadRows();
            int messages = imported.Count(item => item.ImportNotice != null);
            string message = imported.Count == 0 ? "所选位置没有可导入的 .SLDPRT。" :
                $"已导入 {imported.Count} 份。 {messages} 份有重复或读取提示。";
            if (savedVersionOnly)
                message += " CAD 标记文档需保存，本次导入磁盘版本。";
            notices.Publish("导入零件", message,
                imported.Count == 0 || savedVersionOnly ? NoticeKind.Warning : NoticeKind.Information);
        }

        private async Task RunBatchAsync()
        {
            isBatch = true;
            pauseRequested = cancelRequested = false;
            Precheck = "";
            DetailTab = 2;
            Activity = "正在读取标准件并准备批改。";
            var progress = new Progress<string>(message => Precheck += message + Environment.NewLine);
            CommandManager.InvalidateRequerySuggested();
            try
            {
                var end = await batch.RunAsync(Current, UpdateRow, message => ((IProgress<string>)progress).Report(message));
                notices.Publish("批改队列", end == BatchEnd.Paused ? "已在文件之间暂停，可继续。" :
                    end == BatchEnd.Canceled ? "剩余任务已取消。已处理结果已保留。" : "本轮队列执行完毕。");
            }
            catch
            {
                preflightPassed = false;
                Notify(nameof(StandardSummary));
                throw;
            }
            finally
            {
                isBatch = false;
                CommandManager.InvalidateRequerySuggested();
            }
        }

        private void Pause()
        {
            batch.RequestPause();
            pauseRequested = true;
            Activity = "将在当前文件完成后暂停。";
            CommandManager.InvalidateRequerySuggested();
        }

        private void Cancel()
        {
            if (isBatch)
            {
                batch.RequestCancel();
                cancelRequested = true;
                Activity = "将在当前文件完成后取消剩余任务。";
            }
            else
            {
                batch.CancelPending(Current.Id, UpdateRow);
                notices.Publish(WorkspaceHeading, "待处理任务已取消。");
            }
            CommandManager.InvalidateRequerySuggested();
        }

        private void Retry()
        {
            var item = LoadSelected();
            batch.ResetForRetry(item);
            UpdateRow(item);
            notices.Publish(WorkspaceHeading, "提交已加入队列，点「开始 / 继续」执行。");
        }

        private void SaveStudent()
        {
            var item = LoadSelected();
            item.StudentId = StudentId?.Trim();
            item.StudentName = StudentName?.Trim();
            store.SaveSubmission(item);
            UpdateRow(item);
            notices.Publish(WorkspaceHeading, "学生信息已保存。");
        }

        private void SaveReview()
        {
            var item = LoadSelected();
            item.ConfirmScore(RulesDraft.ParseScore(ReviewScore, "终评"), ReviewReason);
            store.SaveSubmission(item);
            UpdateRow(item);
            ReviewReason = "";
            notices.Publish(WorkspaceHeading, "教师终评及原因已记录。");
        }

        private Submission LoadSelected() =>
            store.LoadSubmissions(Current.Id).Single(item => item.Id == Selected.Item.Id);

        private void ExportCsv()
        {
            var path = ReportPath(".csv", "班级成绩");
            if (path == null)
                return;
            ReportExporter.SaveCsv(path, Current, Rows.Select(row => row.Item));
            notices.Publish(WorkspaceHeading, "班级成绩 CSV 已导出。");
        }

        private void ExportHtml()
        {
            var path = ReportPath(".html", "单件批改报告");
            if (path == null)
                return;
            ReportExporter.SaveHtml(path, Current, Selected.Item);
            notices.Publish(WorkspaceHeading, "单件 HTML 已导出。");
        }

        private string ReportPath(string extension, string kind)
        {
            var picker = new SaveFileDialog
            {
                Title = "导出" + kind,
                DefaultExt = extension,
                Filter = $"{kind} (*{extension})|*{extension}",
                FileName = "SWGrade-" + kind + extension,
                AddExtension = true,
                OverwritePrompt = false
            };
            return picker.ShowDialog(owner) == true ? picker.FileName : null;
        }

        private void ReloadRows()
        {
            Selected = null;
            Rows.Clear();
            if (Current != null)
            {
                foreach (var item in store.LoadSubmissions(Current.Id))
                    Rows.Add(new SubmissionRow(Rows.Count + 1, item));
            }
            NotifyQueue();
        }

        private void UpdateRow(Submission item)
        {
            var row = Rows.FirstOrDefault(existing => existing.Item.Id == item.Id);
            row?.Update(item);
            if (ReferenceEquals(row, Selected))
                NotifyDetails();
            NotifyQueue();
        }

        private void NotifyDetails()
        {
            Notify(nameof(Comparison));
            Notify(nameof(ResultSummary));
            Notify(nameof(ResultEvidence));
            Notify(nameof(SelectedNeedsConfiguration));
            CommandManager.InvalidateRequerySuggested();
        }

        private void NotifyQueue()
        {
            Notify(nameof(PendingCount));
            Notify(nameof(SettledCount));
            Notify(nameof(TotalCount));
            Notify(nameof(QueueSummary));
            Notify(nameof(ProgressMaximum));
            CommandManager.InvalidateRequerySuggested();
        }

        private static OpenFileDialog PartDialog(bool multiple) => new OpenFileDialog
        {
            Title = multiple ? "导入学生零件" : "选择标准零件",
            Filter = "SOLIDWORKS 零件 (*.SLDPRT)|*.SLDPRT",
            CheckFileExists = true,
            Multiselect = multiple
        };

        private async Task WithBusyAsync(Func<Task> action)
        {
            IsBusy = true;
            try { await action(); }
            catch (Exception exception)
            {
                PublishFailure(exception);
            }
            finally
            {
                Activity = "";
                IsBusy = false;
            }
        }

        private ICommand Command(Action action, Func<bool> canExecute) =>
            new RelayCommand(() => ExecuteSafely(action), canExecute);

        private void ExecuteSafely(Action action)
        {
            try { action(); }
            catch (Exception exception) { PublishFailure(exception); }
        }

        private void PublishFailure(Exception exception)
        {
            var detail = CadAnalysis.DescribeError(exception);
            Trace.TraceWarning(detail);
            notices.Publish(WorkspaceHeading + "操作失败", exception is ArgumentException ? exception.Message : detail, NoticeKind.Error);
        }

        private void Notify([CallerMemberName] string name = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

        public void Dispose() => store?.Dispose();

        private sealed class CadReader : IModelReader, IGeometryComparer
        {
            public async Task<GeometryEvidence> CompareAsync(ModelInspectionResult reference, ModelInspectionResult student,
                GeometryCoordinates coordinates, Action<string> report)
            {
                var comparison = await CadGeometryComparison.CompareAsync(reference, student, coordinates, false, report);
                return comparison.EvidenceFor(reference, student);
            }

            public Task<PartConfigurations> ReadConfigurationsAsync(string path, string cadYear, Action<string> report) =>
                StaWorker.Run(() => CadAnalysis.ReadConfigurations(path, cadYear, report));

            public Task<ModelInspectionResult> ReadAsync(string path, string configuration, string cadYear, Action<string> report,
                SheetMetalMode sheetMetal = SheetMetalMode.Automatic) =>
                StaWorker.Run(() =>
                {
                    try { return CadAnalysis.Run(path, configuration, cadYear, false, report, sheetMetal); }
                    catch (Exception exception)
                    {
                        throw new InvalidOperationException(CadAnalysis.DescribeError(exception), exception);
                    }
                });
        }
    }
}

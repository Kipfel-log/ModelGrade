using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading.Tasks;
using SWGrade.Domain;

namespace SWGrade.Application
{
    public enum TaskState { Pending, Running, Completed, NeedsReview, Failed, Canceled }

    public sealed class Assignment
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string StandardPath { get; set; }
        public string StandardHash { get; set; }
        public string CreatedUtc { get; set; }
        public AssignmentRules Rules { get; set; }
    }

    public sealed class Submission
    {
        public string Id { get; set; }
        public string AssignmentId { get; set; }
        public string FilePath { get; set; }
        public string FileHash { get; set; }
        public string StudentId { get; set; }
        public string StudentName { get; set; }
        public string ConfigurationOverride { get; set; }
        public bool NeedsConfigurationChoice { get; set; }
        public string ImportNotice { get; set; }
        public TaskState State { get; set; }
        public string StatusDetail { get; set; }
        public GradeResult Grade { get; set; }
        public ModelInspectionResult StandardModel { get; set; }
        public ModelInspectionResult StudentModel { get; set; }
        public List<Attempt> Attempts { get; set; } = new List<Attempt>();
        public List<TeacherReview> Reviews { get; set; } = new List<TeacherReview>();

        public decimal? FinalScore => Reviews.Count > 0 ? Reviews[Reviews.Count - 1].Score : Grade?.FinalAutoScore;

        public void ConfirmScore(decimal score, string reason)
        {
            if (Grade == null || (State != TaskState.Completed && State != TaskState.NeedsReview))
                throw new InvalidOperationException("请先取得自动分析结果。");
            if (score < 0 || score > 100 || decimal.Round(score, 2) != score || string.IsNullOrWhiteSpace(reason))
                throw new ArgumentException("终评须在 0 至 100 之间，最多两位小数，并填写复核原因。");

            Reviews.Add(new TeacherReview
            {
                Score = score,
                Reason = reason.Trim(),
                TimeUtc = Clock.UtcNow()
            });
            State = TaskState.Completed;
            StatusDetail = "教师已确认终评。";
        }
    }

    public sealed class Attempt
    {
        public string TaskId { get; set; }
        public string StartedUtc { get; set; }
        public string FinishedUtc { get; set; }
        public string ExecutableVersion { get; set; }
        public string Error { get; set; }
        public List<string> Diagnostics { get; set; } = new List<string>();
    }

    public sealed class TeacherReview
    {
        public decimal Score { get; set; }
        public string Reason { get; set; }
        public string TimeUtc { get; set; }
    }

    public interface IGradeStore
    {
        IReadOnlyList<Assignment> LoadAssignments();
        IReadOnlyList<Submission> LoadSubmissions(string assignmentId);
        void AddAssignment(Assignment assignment);
        void AddSubmissions(IReadOnlyList<Submission> submissions);
        void SaveSubmission(Submission submission);
    }

    public interface IModelReader
    {
        Task<PartConfigurations> ReadConfigurationsAsync(string path, string cadYear, Action<string> report);
        Task<ModelInspectionResult> ReadAsync(string path, string configuration, string cadYear, Action<string> report,
            SheetMetalMode sheetMetal = SheetMetalMode.Automatic);
    }

    public interface IGeometryComparer
    {
        Task<GeometryEvidence> CompareAsync(ModelInspectionResult reference, ModelInspectionResult student,
            GeometryCoordinates coordinates, Action<string> report, AssignmentDatum datum = null);
    }

    public static class Clock
    {
        public static string UtcNow() => DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture);
    }

    public static class StateText
    {
        public static string Task(TaskState state)
        {
            switch (state)
            {
                case TaskState.Pending: return "待处理";
                case TaskState.Running: return "处理中";
                case TaskState.Completed: return "已完成";
                case TaskState.NeedsReview: return "待复核";
                case TaskState.Failed: return "失败";
                case TaskState.Canceled: return "已取消";
                default: return state.ToString();
            }
        }

        public static string Check(CheckStatus status)
        {
            switch (status)
            {
                case CheckStatus.Pass: return "通过";
                case CheckStatus.Fail: return "未通过";
                case CheckStatus.NotApplicable: return "不计分";
                case CheckStatus.Unverified: return "待复核";
                case CheckStatus.Error: return "错误";
                default: return status.ToString();
            }
        }
    }
}

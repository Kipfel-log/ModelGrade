using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SWGrade.Domain;

namespace SWGrade.Application
{
    public enum BatchEnd { Finished, Paused, Canceled }

    public sealed class BatchGrading
    {
        private const int Pause = 1;
        private const int Cancel = 2;
        private readonly IGradeStore store;
        private readonly IModelReader reader;
        private readonly string version;
        private readonly GradingEngine engine = new GradingEngine();
        private int active;
        private int stop;

        public BatchGrading(IGradeStore store, IModelReader reader, string version)
        {
            this.store = store;
            this.reader = reader;
            this.version = version;
        }

        public void RequestPause() => Interlocked.CompareExchange(ref stop, Pause, 0);
        public void RequestCancel() => Interlocked.Exchange(ref stop, Cancel);

        public async Task<BatchEnd> RunAsync(Assignment assignment, Action<Submission> changed, Action<string> report)
        {
            if (Interlocked.Exchange(ref active, 1) != 0)
                throw new InvalidOperationException("已有批改任务在运行。");

            try
            {
                Interlocked.Exchange(ref stop, 0);
                var queue = store.LoadSubmissions(assignment.Id).Where(item => item.State == TaskState.Pending).ToList();
                if (queue.Count == 0)
                    return BatchEnd.Finished;

                // A single reference snapshot is reused throughout this run.
                ModelInspectionResult standard;
                try
                {
                    standard = await InspectReferenceAsync(assignment, report);
                }
                catch (Exception) when (Volatile.Read(ref stop) == Cancel)
                {
                    CancelPending(assignment.Id, changed);
                    return BatchEnd.Canceled;
                }
                foreach (var submission in queue)
                {
                    if (Volatile.Read(ref stop) != 0)
                        break;
                    await GradeOneAsync(assignment, standard, submission, changed);
                }

                int command = Volatile.Read(ref stop);
                if (command == Cancel)
                {
                    CancelPending(assignment.Id, changed);
                    return BatchEnd.Canceled;
                }
                return command == Pause ? BatchEnd.Paused : BatchEnd.Finished;
            }
            finally
            {
                Volatile.Write(ref active, 0);
            }
        }

        public async Task<ModelInspectionResult> InspectReferenceAsync(Assignment assignment, Action<string> report)
        {
            assignment.Rules.Validate();
            if (await Task.Run(() => PartFiles.Hash(assignment.StandardPath)) != assignment.StandardHash)
                throw new InvalidOperationException("标准件与保存作业时的哈希不同。请保存新作业以更新标准件。");

            var model = await reader.ReadAsync(assignment.StandardPath, assignment.Rules.StandardConfiguration,
                assignment.Rules.ExpectedCadYear, report);
            if (model == null || !model.OpenedSuccessfully || !model.SourceIntegrityVerified ||
                model.DocumentHash != assignment.StandardHash || model.VolumeM3 == null || model.SurfaceAreaM2 == null)
                throw new InvalidOperationException("标准件未取得完整的只读实体度量，队列尚未开始。");
            return model;
        }

        public void CancelPending(string assignmentId, Action<Submission> changed)
        {
            foreach (var submission in store.LoadSubmissions(assignmentId).Where(item => item.State == TaskState.Pending))
            {
                submission.State = TaskState.Canceled;
                submission.StatusDetail = "教师取消，未执行 CAD 读取。";
                store.SaveSubmission(submission);
                changed?.Invoke(submission);
            }
        }

        public void ResetForRetry(Submission submission)
        {
            if (submission.State != TaskState.Failed && submission.State != TaskState.Canceled)
                throw new InvalidOperationException("失败或已取消的提交可以重试。");
            submission.State = TaskState.Pending;
            submission.StatusDetail = "已重新加入队列。";
            store.SaveSubmission(submission);
        }

        public async Task<PartConfigurations> ReadStudentConfigurationsAsync(Assignment assignment, Submission submission)
        {
            var options = await reader.ReadConfigurationsAsync(submission.FilePath,
                assignment.Rules.ExpectedCadYear ?? submission.StandardModel?.SolidWorksVersion, null);
            if (submission.FileHash != null && options.DocumentHash != submission.FileHash)
                throw new InvalidOperationException("源文件已变化。请重新导入文件后再选择配置。");
            return options;
        }

        public void SetStudentConfiguration(Submission submission, PartConfigurations options, string configuration)
        {
            if (!submission.NeedsConfigurationChoice)
                throw new InvalidOperationException("这份提交当前不需要选择配置。");
            if (options.DocumentHash != submission.FileHash)
                throw new InvalidOperationException("配置列表与提交的文件哈希不同，请重新读取配置。");
            if (!options.Contains(configuration))
                throw new ArgumentException("请选择列表中的学生模型配置。");
            submission.ConfigurationOverride = configuration;
            submission.NeedsConfigurationChoice = false;
            submission.State = TaskState.Pending;
            submission.StatusDetail = "已指定配置，等待重新批改。";
            store.SaveSubmission(submission);
        }

        public int RecoverInterrupted()
        {
            int recovered = 0;
            foreach (var assignment in store.LoadAssignments())
            {
                foreach (var submission in store.LoadSubmissions(assignment.Id).Where(item => item.State == TaskState.Running))
                {
                    submission.State = TaskState.Failed;
                    submission.StatusDetail = "上次运行中断，可重试。";
                    var attempt = submission.Attempts.LastOrDefault();
                    if (attempt != null)
                    {
                        attempt.FinishedUtc = Clock.UtcNow();
                        attempt.Error = submission.StatusDetail;
                    }
                    store.SaveSubmission(submission);
                    recovered++;
                }
            }
            return recovered;
        }

        private async Task GradeOneAsync(Assignment assignment, ModelInspectionResult standard,
            Submission submission, Action<Submission> changed)
        {
            var attempt = new Attempt
            {
                TaskId = Guid.NewGuid().ToString("N"),
                StartedUtc = Clock.UtcNow(),
                ExecutableVersion = version
            };
            submission.Attempts.Add(attempt);
            submission.State = TaskState.Running;
            submission.StatusDetail = "正在读取临时副本。";
            store.SaveSubmission(submission);
            changed?.Invoke(submission);

            var diagnostics = new List<string>();
            try
            {
                var localHash = await Task.Run(() => PartFiles.Hash(submission.FilePath));
                if (submission.FileHash != null && submission.FileHash != localHash)
                    throw new InvalidOperationException("源文件已变化。如需批改新文件，请重新导入。");
                submission.FileHash = localHash;
                string requested = submission.ConfigurationOverride ?? assignment.Rules.StudentConfiguration ?? standard.Configuration;
                var configurations = await reader.ReadConfigurationsAsync(submission.FilePath, standard.SolidWorksVersion, diagnostics.Add);
                if (configurations.DocumentHash != localHash)
                    throw new InvalidOperationException("读取配置时源文件发生变化。请重新导入。");
                if (!configurations.Contains(requested))
                {
                    submission.StandardModel = standard;
                    submission.StudentModel = null;
                    submission.Grade = null;
                    submission.NeedsConfigurationChoice = true;
                    submission.State = TaskState.NeedsReview;
                    submission.StatusDetail = $"学生模型没有「{requested}」配置。请在结果区选择该学生的配置后重新批改。";
                    return;
                }

                var student = await reader.ReadAsync(submission.FilePath, requested, standard.SolidWorksVersion, diagnostics.Add);
                if (student?.OpenedSuccessfully != true || !student.SourceIntegrityVerified || student.DocumentHash != localHash)
                    throw new InvalidOperationException("未能验证学生文件的真实只读分析结果。");

                submission.StandardModel = standard;
                submission.StudentModel = student;
                submission.Grade = engine.Evaluate(assignment.Rules, standard, student, new GeometryEvidence
                {
                    Reason = "当前版本尚无通过真机验证的几何比较数据。请进行教师复核。"
                }, submission.ConfigurationOverride);
                submission.State = submission.Grade.NeedsReview ? TaskState.NeedsReview : TaskState.Completed;
                submission.StatusDetail = submission.Grade.NeedsReview ? "已有可评估证据，待核对未评估项。" : "已完成全部计分检查。";
            }
            catch (Exception exception)
            {
                submission.State = TaskState.Failed;
                submission.StatusDetail = exception.Message;
                attempt.Error = exception.Message;
            }
            finally
            {
                attempt.Diagnostics.AddRange(diagnostics);
                attempt.FinishedUtc = Clock.UtcNow();
                store.SaveSubmission(submission);
                changed?.Invoke(submission);
            }
        }
    }
}

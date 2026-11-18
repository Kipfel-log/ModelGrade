using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using SWGrade.Application;
using SWGrade.Domain;
using SWGrade.Desktop;
using SWGrade.Infrastructure;

namespace SWGrade.WorkflowChecks
{
    internal static class Program
    {
        private static async Task Main()
        {
            Console.WriteLine("MOCK TEST ONLY. This run contains no CAD reads, real part measurements, or live grades.");
            CheckRules();
            CheckAssignmentDraft();
            var root = Directory.CreateTempSubdirectory("swgrade-workflow-");
            try
            {
                await CheckWorkflow(root.FullName);
                await CheckConfigurationWorkflow(root.FullName);
            }
            finally
            {
                root.Delete(true);
            }
        }

        private static void CheckRules()
        {
            var tolerance = new MeasurementTolerance { Relative = 0.05, Absolute = 0.125 };
            Check(GradingEngine.CompareMeasurement("boundary", "m³", 10, 10.5, tolerance, 10).Awarded == 10, "inclusive tolerance boundary");
            Check(GradingEngine.CompareMeasurement("outside", "m³", 10, 10.51, tolerance, 10).Awarded == 0, "outside tolerance");
            var zero = GradingEngine.CompareMeasurement("zero", "m³", 0, 0.125, tolerance, 10);
            Check(zero.Awarded == 10 && zero.RelativeError == null && zero.EffectiveTolerance == 0.125, "zero reference absolute tolerance");
            Check(GradingEngine.CompareMeasurement("missing", "kg", 1, null, tolerance, 10).Awarded == null, "missing measurement unverified");

            var engine = new GradingEngine();
            var rules = new AssignmentRules();
            var reference = Model(null, "A");
            var student = Model(null, "A");
            rules.GeometryWeight = 61;
            Expect<ArgumentException>(rules.Validate, "reject invalid weight total");
            rules.GeometryWeight = 60;
            rules.AreaTolerance.Relative = double.NaN;
            Expect<ArgumentException>(rules.Validate, "reject nonfinite tolerance");
            rules.AreaTolerance.Relative = 0.001;

            CheckComparison(reference, student);

            var geometryUnavailable = engine.Evaluate(rules, reference, student, null);
            Check(geometryUnavailable.EarnedPoints == 25 && geometryUnavailable.EvaluatedPoints == 25 &&
                geometryUnavailable.ReviewPoints == 75 && geometryUnavailable.FinalAutoScore == null, "unverified geometry and unconfigured feature points withheld");

            student.OpenWarningCode = 2;
            Check(engine.Evaluate(rules, reference, student, null).EvaluatedPoints == 25, "read-only status is compatible with grading");
            student.ReviewOpenWarningCode = 32;
            Check(engine.Evaluate(rules, reference, student, null).EvaluatedPoints == 0, "non-read-only load warning requires review");
            student.ReviewOpenWarningCode = 0;

            var mismatch = engine.Evaluate(rules, reference, Model(null, "B"), null);
            Check(mismatch.EvaluatedPoints == 0 && mismatch.NeedsReview, "configuration mismatch blocks score");
            rules.StudentConfiguration = "B";
            var mapped = engine.Evaluate(rules, reference, Model(null, "B"), null);
            Check(mapped.EvaluatedPoints == 25, "explicit configuration mapping");
            rules.StudentConfiguration = null;

            rules.Mode = GradingMode.ProcessRequired;
            Expect<ArgumentException>(rules.Validate, "process mode requires explicit feature rules");
            rules.RequiredFeatureTypes.Add("Cut");
            student.FeatureCounts.Add("Cut", 1);
            var suppressed = engine.Evaluate(rules, reference, student, null);
            Check(suppressed.EvaluatedPoints == 40 && suppressed.EarnedPoints == 25, "suppressed feature does not earn points");
            student.UnverifiedFeatureTypes.Add("Cut");
            var unverified = engine.Evaluate(rules, reference, student, null);
            Check(unverified.EvaluatedPoints == 25, "unknown suppression state avoids deduction");
            student.ActiveFeatureCounts.Add("Cut", 1);
            var active = engine.Evaluate(rules, reference, student, null);
            Check(active.EvaluatedPoints == 40 && active.EarnedPoints == 40, "verified active feature earns configured points");

            rules.GeometryPolicy = GeometryPolicy.Bands;
            rules.GeometryBands.Add(new GeometryBand { MaximumRatio = 0.001, AwardFraction = 1 });
            rules.GeometryBands.Add(new GeometryBand { MaximumRatio = 0.02, AwardFraction = 0.5m });
            var syntheticGeometry = new GeometryEvidence
            {
                ValidatedComparison = true,
                ReferenceVolumeM3 = 1,
                MissingVolumeM3 = 0.01,
                ExtraVolumeM3 = 0
            };
            Check(engine.Evaluate(rules, reference, student, syntheticGeometry).FinalAutoScore == 70, "synthetic geometry band arithmetic");
            syntheticGeometry.MissingVolumeM3 = null;
            Check(engine.Evaluate(rules, reference, student, syntheticGeometry).FinalAutoScore == null, "incomplete geometry result withheld");
            Check(engine.Evaluate(rules, null, null, null).EvaluatedPoints == 0, "empty inspections withheld");
        }

        private static void CheckComparison(ModelInspectionResult reference, ModelInspectionResult current)
        {
            current.VolumeM3 = 2;
            var comparison = ModelComparison.Evaluate(reference, current, null, null);
            var difference = comparison.Checks.Single(check => check.Name == "体积");
            Check(difference.Status == CheckStatus.Fail && difference.AbsoluteError == 1 &&
                difference.StandardValue == 1 && difference.StudentValue == 2, "pair shows measured out-of-tolerance values");
            Check(comparison.Checks.All(check => check.Awarded == null && check.Weight == 0),
                "ad-hoc comparison carries no grading points");
            Check(comparison.Checks.Single(check => check.Name == "几何差异").Status == CheckStatus.Unverified,
                "comparison never infers geometric difference from measurements");
            Check(comparison.Checks.Single(check => check.Name == "质量").Status == CheckStatus.Unverified,
                "comparison reports absent mass without fabricating values");

            var swapped = ModelComparison.Evaluate(current, reference, null, null);
            Check(swapped.Checks.Single(check => check.Name == "体积").StandardValue == 2,
                "swapped comparison uses new reference");
            var mismatched = ModelComparison.Evaluate(reference, Model(null, "B"), null, null);
            Check(mismatched.Checks.Single(check => check.Name == "体积").Status == CheckStatus.Unverified,
                "ad-hoc mismatched configurations require a mapping");
            var mapped = ModelComparison.Evaluate(reference, Model(null, "B"), "A", "B");
            Check(mapped.Checks.Single(check => check.Name == "体积").Status == CheckStatus.Pass,
                "ad-hoc explicit different configurations are compared");
            current.VolumeM3 = 1;
        }

        private static void CheckAssignmentDraft()
        {
            var draft = new RulesDraft();
            Check(draft.Preset == "标准" && draft.Volume.Relative.Text == "0.1" && draft.Volume.Absolute.Text == "0.001" &&
                draft.Area.Absolute.Text == "0.01" && draft.Mass.Absolute.Text == "0.001", "assignment displays percentages and engineering units");
            Expect<ArgumentException>(() => draft.ToRules(), "teacher confirmation required for unavailable points");
            draft.ConfirmUnavailableWeights = true;
            var rules = draft.ToRules();
            Check(rules.GeometryWeight == 60 && rules.ParametersWeight == 25 && rules.FeaturesWeight == 15,
                "teacher confirmation retains original weights");

            draft.Preset = "严格";
            rules = draft.ToRules();
            Check(Close(rules.VolumeTolerance.Relative, 0.0001) && Close(rules.AreaTolerance.Relative, 0.0001) &&
                Close(rules.MassTolerance.Relative, 0.0001) && draft.Volume.Example.Contains("±0.01 cm³"),
                "strict preset applies to engine inputs and live example");
            draft.Preset = "宽松";
            rules = draft.ToRules();
            Check(Close(rules.VolumeTolerance.Relative, 0.005) && Close(rules.AreaTolerance.Relative, 0.005) &&
                Close(rules.MassTolerance.Relative, 0.005) && draft.Volume.Example.Contains("99.5 至 100.5 cm³"),
                "relaxed preset updates all three inputs and range");
            draft.Preset = "标准";
            draft.Volume.Relative.SliderValue = 0.2;
            draft.Volume.Absolute.Text = "0.6";
            draft.Area.Absolute.Text = "1.25";
            draft.Mass.Absolute.Text = "0.75";
            rules = draft.ToRules();
            Check(draft.Preset == "自定义" && draft.Volume.Relative.Text == "0.2" && Close(rules.VolumeTolerance.Relative, 0.002) &&
                Close(rules.VolumeTolerance.Absolute, 6e-7) && Close(rules.AreaTolerance.Absolute, 1.25e-4) &&
                Close(rules.MassTolerance.Absolute, 7.5e-4) && draft.Volume.Example.Contains("±0.6 cm³"),
                "custom slider and input units round trip with max-formula feedback");

            draft.ChangeStandard("part.SLDPRT");
            draft.SetConfigurations(new PartConfigurations("MOCK", "Saved active", new[] { "Other", "Saved active" }), null);
            Check(draft.StandardConfiguration == "Saved active" && draft.ConfigurationChoices.Count == 2 &&
                draft.ConfigurationChoices[1].Display.Contains("活动"), "configuration list selects saved active by default");
            draft.StandardConfiguration = "Other";
            draft.StudentConfiguration = "Class version";
            draft.Geometry.SliderValue = 59.25;
            draft.Parameters.Text = "25.75";
            Check(!draft.ConfirmUnavailableWeights && draft.WeightTotal == "合计 100 分",
                "editable weights sum exactly and invalidate previous confirmation");
            draft.ConfirmUnavailableWeights = true;
            rules = draft.ToRules();
            Check(rules.GeometryWeight == 59.25m && rules.ParametersWeight == 25.75m &&
                rules.StandardConfiguration == "Other" && rules.StudentConfiguration == "Class version",
                "weight and configuration changes reach saved rule model");
            draft.Parameters.Text = "25.755";
            Expect<ArgumentException>(() => draft.ToRules(), "reject excess weight precision");
            draft.FeatureTypes = "， , ";
            Check(draft.WeightNotice.Contains("尚未指定特征类型"), "separator-only feature input still requires review");
            draft.Redistribute();
            rules = draft.ToRules();
            Check(rules.GeometryWeight == 0 && rules.FeaturesWeight == 0 && rules.ParametersWeight == 100 &&
                !draft.HasUnavailableWeights, "explicit redistribution assigns unavailable points to parameters");

            var oldRules = new AssignmentRules { StandardConfiguration = "Legacy", StudentConfiguration = "Mapped" };
            oldRules.VolumeTolerance.Absolute = 0.000000001234567890123456;
            var reused = RulesDraft.FromAssignment(new Assignment { Rules = oldRules, StandardPath = "legacy.SLDPRT" });
            reused.ConfirmUnavailableWeights = true;
            var reusedRules = reused.ToRules();
            Check(reusedRules.StandardConfiguration == "Legacy" && reusedRules.StudentConfiguration == "Mapped" &&
                reusedRules.VolumeTolerance.Absolute == oldRules.VolumeTolerance.Absolute, "offline draft preserves legacy configuration and precise tolerance");
        }

        private static bool Close(double actual, double expected) => Math.Abs(actual - expected) <= 1e-14 * Math.Abs(expected);

        private static async Task CheckConfigurationWorkflow(string root)
        {
            string db = Path.Combine(root, "config-history");
            string reference = Input(root, "config-standard", "mock-configuration-reference");
            string student = Input(root, "manual-config", "mock-configuration-student");
            string jobId;
            string itemId;
            string legacyId;
            var reader = new MockReader();
            using (var store = new SqliteGradeStore(db))
            {
                var assignments = new Assignments(store);
                var rules = new AssignmentRules { StandardConfiguration = "A" };
                Expect<InvalidOperationException>(() => assignments.Create("stale", reference, rules, "old-hash"),
                    "reject changed standard when saving selected configuration");
                var job = assignments.Create("teacher configuration", reference, rules, PartFiles.Hash(reference));
                jobId = job.Id;
                itemId = assignments.Import(job, new[] { student }).Single().Id;
                var service = new BatchGrading(store, reader, "MOCK");
                await service.RunAsync(job, null, null);
                var pendingChoice = store.LoadSubmissions(jobId).Single();
                Check(pendingChoice.NeedsConfigurationChoice && pendingChoice.State == TaskState.NeedsReview &&
                    pendingChoice.Grade == null && pendingChoice.StudentModel == null && reader.ManualConfigReads == 0,
                    "unmatched student config pauses scoring without inspecting a different configuration");

                var choices = await service.ReadStudentConfigurationsAsync(job, pendingChoice);
                Expect<ArgumentException>(() => service.SetStudentConfiguration(pendingChoice, choices, "missing"),
                    "manual choice must be present in student model");
                service.SetStudentConfiguration(pendingChoice, choices, "B");
                var queued = store.LoadSubmissions(jobId).Single();
                Check(queued.State == TaskState.Pending && !queued.NeedsConfigurationChoice && queued.ConfigurationOverride == "B",
                    "manual choice persisted and queued for retry");

                var legacyRules = new AssignmentRules { StandardConfiguration = "A", StudentConfiguration = "A",
                    GeometryWeight = 0, ParametersWeight = 100, FeaturesWeight = 0 };
                var legacy = assignments.Create("legacy data", reference, legacyRules);
                legacyId = legacy.Id;
                assignments.Import(legacy, new[] { Input(root, "legacy", "mock-legacy-student") });
                await service.RunAsync(legacy, null, null);
            }
            RemoveNewFieldsFromLegacyPayload(db, legacyId);
            using (var store = new SqliteGradeStore(db))
            {
                var legacyJob = store.LoadAssignments().Single(item => item.Id == legacyId);
                var legacyRow = store.LoadSubmissions(legacyId).Single();
                Check(legacyJob.Rules.StudentConfiguration == "A" &&
                    legacyRow.ConfigurationOverride == null && !legacyRow.NeedsConfigurationChoice &&
                    legacyRow.FinalScore == 100, "legacy submission JSON without new fields still loads recorded results");

                var job = store.LoadAssignments().Single(item => item.Id == jobId);
                var service = new BatchGrading(store, reader, "MOCK");
                await service.RunAsync(job, null, null);
                var graded = store.LoadSubmissions(jobId).Single(item => item.Id == itemId);
                Check(graded.Attempts.Count == 2 && graded.StudentModel.Configuration == "B" &&
                    graded.Grade.EvaluatedPoints == 25 && graded.Grade.FinalAutoScore == null && reader.ManualConfigReads == 1,
                    "persisted override supplies the selected configuration after restart without fabricating geometry");
            }
        }

        private static void RemoveNewFieldsFromLegacyPayload(string db, string assignment)
        {
            using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
            {
                DataSource = Path.Combine(db, "swgrade.sqlite3"), Pooling = false
            }.ToString());
            connection.Open();
            using var command = connection.CreateCommand();
            command.Parameters.AddWithValue("$assignment", assignment);
            command.CommandText = "SELECT payload FROM submissions WHERE assignment_id = $assignment";
            var payload = JsonNode.Parse((string)command.ExecuteScalar()).AsObject();
            payload.Remove("ConfigurationOverride");
            payload.Remove("NeedsConfigurationChoice");
            command.CommandText = "UPDATE submissions SET payload = $payload WHERE assignment_id = $assignment";
            command.Parameters.AddWithValue("$payload", payload.ToJsonString());
            command.ExecuteNonQuery();
        }

        private static async Task CheckWorkflow(string root)
        {
            var db = Path.Combine(root, "history");
            var reference = Input(root, "reference", "mock-reference");
            var files = Enumerable.Range(1, 20).Select(index => Input(root, index == 2 ? "02-'student" :
                index.ToString("00"), index == 3 ? "1" : index.ToString())).ToList();

            string jobId;
            string interruptedId;
            var reader = new MockReader();
            using (var store = new SqliteGradeStore(db))
            {
                var jobs = new Assignments(store);
                var job = jobs.Create("<测试作业>", reference, new AssignmentRules());
                jobId = job.Id;
                var imported = jobs.Import(job, files);
                Check(imported.Count == 20 && imported[2].ImportNotice != null, "20 file imports with duplicate hash notice");
                Check(imported[1].StudentId == "02", "suggested student ID from filename");

                var service = new BatchGrading(store, reader, "MOCK");
                var paused = await service.RunAsync(job, item =>
                {
                    if (item.State == TaskState.Running)
                        service.RequestPause();
                }, null);
                Check(paused == BatchEnd.Paused && store.LoadSubmissions(job.Id).Count(item => item.State == TaskState.Pending) == 19,
                    "pause at file boundary");

                var complete = await service.RunAsync(job, null, null);
                var firstRun = store.LoadSubmissions(job.Id);
                Check(complete == BatchEnd.Finished && firstRun.Count(item => item.State == TaskState.NeedsReview) == 19 &&
                    firstRun.Count(item => item.State == TaskState.Failed) == 1, "one file failure isolated");
                Check(reader.ReferenceReads == 2, "reference read once per run with recorded snapshot");
                var failed = firstRun.Single(item => item.State == TaskState.Failed);
                service.ResetForRetry(failed);
                await service.RunAsync(job, null, null);
                var retried = store.LoadSubmissions(job.Id).Single(item => item.Id == failed.Id);
                Check(retried.State == TaskState.NeedsReview && retried.Attempts.Count == 2 &&
                    retried.Attempts[0].Error != null && retried.Attempts[1].Error == null, "failed file retried with full history");

                var reviewed = store.LoadSubmissions(job.Id)[0];
                reviewed.StudentId = "=1+1";
                reviewed.StudentName = "<script>alert(1)</script>";
                Expect<ArgumentException>(() => reviewed.ConfirmScore(90, ""), "manual reason required");
                reviewed.ConfirmScore(90, "教师现场复核");
                reviewed.ConfirmScore(91, "核对修订");
                store.SaveSubmission(reviewed);

                var extra = jobs.Import(job, files.Take(3));
                service.CancelPending(job.Id, null);
                Check(store.LoadSubmissions(job.Id).Count(item => item.State == TaskState.Canceled) == 3, "cancel remaining persisted");
                var later = jobs.Import(job, files.Take(3));
                var canceled = await service.RunAsync(job, item =>
                {
                    if (item.State == TaskState.Running)
                        service.RequestCancel();
                }, null);
                var canceledRows = store.LoadSubmissions(job.Id).Where(item => later.Any(input => input.Id == item.Id)).ToList();
                Check(canceled == BatchEnd.Canceled && canceledRows.Count(item => item.State == TaskState.Canceled) == 2 &&
                    canceledRows.Count(item => item.State == TaskState.NeedsReview) == 1, "running item retained when canceling remaining queue");
                var interrupted = extra[0];
                interruptedId = interrupted.Id;
                interrupted.State = TaskState.Running;
                interrupted.Attempts.Add(new Attempt { TaskId = "MOCK-INTERRUPTED", StartedUtc = Clock.UtcNow() });
                store.SaveSubmission(interrupted);
            }

            using (var reopened = new SqliteGradeStore(db))
            {
                var job = reopened.LoadAssignments().Single(item => item.Id == jobId);
                Check(job.Rules.GeometryWeight == 60 && job.Rules.VolumeTolerance.Absolute == 1e-9, "rule snapshot round trip");
                var service = new BatchGrading(reopened, reader, "MOCK");
                Check(service.RecoverInterrupted() == 1, "interrupted task detected on restart");
                var items = reopened.LoadSubmissions(job.Id);
                Check(items.Single(item => item.Id == interruptedId).State == TaskState.Failed, "interrupted task recoverable");
                var reviewed = items[0];
                Check(reviewed.Reviews.Count == 2 && reviewed.FinalScore == 91 && reviewed.Grade.FinalAutoScore == null &&
                    reviewed.Grade.EarnedPoints == 25 && reviewed.Grade.ReviewPoints == 75, "manual and automatic scores remain distinct after reopen");
                Check(reviewed.StudentModel.Configuration == "A" && reviewed.StudentModel.SourceIntegrityVerified &&
                    reviewed.StudentModel.DocumentHash == reviewed.FileHash, "inspection evidence round trip");

                var csv = Path.Combine(root, "MOCK.csv");
                var html = Path.Combine(root, "MOCK.html");
                ReportExporter.SaveCsv(csv, job, items);
                ReportExporter.SaveHtml(html, job, reviewed);
                Check(File.ReadAllBytes(csv).Take(3).SequenceEqual(new byte[] { 0xEF, 0xBB, 0xBF }), "CSV Excel BOM");
                Check(File.ReadAllText(csv).Contains("\"'=1+1\"") && File.ReadAllText(csv).Contains("\",\"91\","),
                    "CSV formula protection and manual score");
                Check(!File.ReadAllText(html).Contains("<script>") && File.ReadAllText(html).Contains("&lt;script&gt;") &&
                    File.ReadAllText(html).Contains("<dd>91</dd>"), "escaped HTML and same final score");
                Expect<IOException>(() => ReportExporter.SaveHtml(html, job, reviewed), "export preserves existing files");

                File.AppendAllText(reference, "modified");
                await ExpectAsync<InvalidOperationException>(() => service.InspectReferenceAsync(job, null), "changed reference rejected");
            }
        }

        private static string Input(string folder, string name, string content)
        {
            var path = Path.Combine(folder, name + ".SLDPRT");
            File.WriteAllText(path, content);
            return path;
        }

        private static ModelInspectionResult Model(string hash, string config) => new ModelInspectionResult
        {
            DocumentHash = hash,
            Configuration = config,
            SourceIntegrityVerified = true,
            OpenedSuccessfully = true,
            DocumentType = "Part",
            SolidWorksVersion = "2026",
            SolidWorksRevision = "MOCK",
            SolidBodyCount = 1,
            VolumeM3 = 1,
            SurfaceAreaM2 = 6,
            FeatureTraversalComplete = true
        };

        private sealed class MockReader : IModelReader
        {
            private bool failedOnce;
            internal int ReferenceReads { get; private set; }
            internal int ManualConfigReads { get; private set; }

            public Task<PartConfigurations> ReadConfigurationsAsync(string path, string cadYear, Action<string> report)
            {
                var name = Path.GetFileNameWithoutExtension(path) == "manual-config" ? "B" : "A";
                return Task.FromResult(new PartConfigurations(PartFiles.Hash(path), name, new[] { name }));
            }

            public Task<ModelInspectionResult> ReadAsync(string path, string configuration, string cadYear, Action<string> report)
            {
                report?.Invoke("MOCK only");
                string name = Path.GetFileNameWithoutExtension(path);
                if (name == "reference")
                    ReferenceReads++;
                if (name == "manual-config")
                    ManualConfigReads++;
                if (name == "05" && !failedOnce)
                {
                    failedOnce = true;
                    throw new InvalidDataException("MOCK transient read failure");
                }
                return Task.FromResult(Model(PartFiles.Hash(path), configuration ?? "A"));
            }
        }

        private static void Check(bool condition, string label)
        {
            if (!condition)
                throw new InvalidOperationException("FAIL " + label);
            Console.WriteLine("PASS " + label);
        }

        private static void Expect<T>(Action action, string label) where T : Exception
        {
            try { action(); }
            catch (T) { Check(true, label); return; }
            Check(false, label);
        }

        private static async Task ExpectAsync<T>(Func<Task> action, string label) where T : Exception
        {
            try { await action(); }
            catch (T) { Check(true, label); return; }
            Check(false, label);
        }
    }
}

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
            CheckNotifications();
            CheckRules();
            CheckSnapshotAndTrial();
            CheckSheetMetalRules();
            CheckMeshPreview();
            CheckAssignmentDraft();
            CheckSimpleAssignment();
            var root = Directory.CreateTempSubdirectory("swgrade-workflow-");
            try
            {
                await CheckWorkflow(root.FullName);
                await CheckConfigurationWorkflow(root.FullName);
                await CheckGeometryWorkflow(root.FullName);
            }
            finally
            {
                root.Delete(true);
            }
        }

        private static void CheckNotifications()
        {
            var now = new DateTimeOffset(2026, 11, 18, 14, 0, 0, TimeSpan.Zero);
            var notices = new NotificationCenter(() => now);
            notices.Publish("作业管理操作失败", "请先选择标准件。", NoticeKind.Error);
            var first = notices.Items.Single();
            now = now.AddSeconds(2);
            notices.Publish(first.Title, first.Message, NoticeKind.Error);
            var repeated = notices.Items.Last();
            Check(notices.Items.Count == 2 && !ReferenceEquals(first, repeated) &&
                notices.Items[0].CreatedAt < repeated.CreatedAt && first.DisplayTime != repeated.DisplayTime,
                "identical repeated failure appends its own dated entry");

            notices.Dismiss(first);
            Check(notices.Items.SequenceEqual(new[] { repeated }), "dismissing a card leaves later occurrences intact");
            notices.Publish("连接检测", "当前无连接。", NoticeKind.Warning);
            notices.Publish("已复制", "分析结果已复制。");
            now = now.AddSeconds(7);
            notices.RemoveExpired();
            Check(notices.Items.Count == 2 && notices.Items[0] == repeated && notices.Items[1].Kind == NoticeKind.Warning,
                "informational feedback expires and warnings and errors stay until dismissed");
            notices.Clear();
            Check(notices.Items.Count == 0, "clear all removes currently displayed notices");
            notices.Publish(first.Title, first.Message, NoticeKind.Error);
            Check(notices.Items.Count == 1 && notices.Items[0] != first && notices.Items[0] != repeated,
                "fresh failure can appear after clearing notifications");
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

        private static void CheckSnapshotAndTrial()
        {
            string hash = new string('a', 64);
            var reference = Model(hash, "A");
            var current = Model(hash, "A");
            reference.SolidFaceCount = current.SolidFaceCount = 6;
            var evidence = SavedGeometry.CompareIdenticalSnapshot(reference, current);
            Check(evidence.ValidatedComparison && evidence.MissingVolumeM3 == 0 && evidence.ExtraVolumeM3 == 0,
                "verified same snapshot and configuration give exact geometry evidence");

            var assignment = new Assignment { StandardHash = hash, Rules = new AssignmentRules { StandardConfiguration = "A" } };
            var trial = TrialGrading.Evaluate(assignment, reference, current);
            var grade = new GradingEngine().Evaluate(assignment.Rules, reference, current, evidence);
            Check(trial.EarnedPoints == 85 && trial.ReviewPoints == 15 && trial.FinalAutoScore == null &&
                trial.Checks.Select(check => check.Awarded).SequenceEqual(grade.Checks.Select(check => check.Awarded)),
                "trial uses saved weights, verified evidence and the batch engine together");
            Check(ModelComparison.Evaluate(reference, current, null, null).Checks.Single(check => check.Name == "实体面数")
                .Status == CheckStatus.NotApplicable, "face count is a topology observation without scoring");

            current.DocumentHash = new string('b', 64);
            trial = TrialGrading.Evaluate(assignment, reference, current);
            Check(!trial.Geometry.ValidatedComparison && trial.EarnedPoints == 25 &&
                trial.ReviewPoints == 75 && trial.FinalAutoScore == null, "equal volume and area do not prove geometry for different files");
            Expect<InvalidOperationException>(() => TrialGrading.Evaluate(assignment, current, reference),
                "trial rejects the wrong standard even when its measurements match");

            current.DocumentHash = hash;
            current.Configuration = "B";
            Check(!SavedGeometry.CompareIdenticalSnapshot(reference, current).ValidatedComparison,
                "same file with another configuration does not prove geometry");
            Expect<InvalidOperationException>(() => TrialGrading.Evaluate(assignment, reference, current),
                "trial asks for the saved student configuration");
            current.Configuration = "A";
            current.ReviewOpenWarningCode = 32;
            Check(!SavedGeometry.CompareIdenticalSnapshot(reference, current).ValidatedComparison,
                "rebuild warning prevents exact-match geometry evidence");
            current.ReviewOpenWarningCode = 0;
            current.SolidBodyCount = 2;
            Check(!SavedGeometry.CompareIdenticalSnapshot(reference, current).ValidatedComparison,
                "identical-snapshot proof is limited to a single solid");
        }

        private static void CheckSheetMetalRules()
        {
            string hash = new string('c', 64);
            var reference = Model(hash, "A");
            var student = Model(hash, "A");
            var rules = new AssignmentRules { GeometryWeight = 60, ParametersWeight = 40, FeaturesWeight = 0 };
            var engine = new GradingEngine();
            GradeResult Grade() => engine.Evaluate(rules, reference, student,
                SavedGeometry.CompareIdenticalSnapshot(reference, student));

            Check(Grade().Checks.All(check => check.Name != "钣金展开图") && Grade().FinalAutoScore == 100,
                "automatic mode leaves ordinary parts on the normal grading path");
            rules.SheetMetal = SheetMetalMode.RequireFlatPattern;
            Check(Grade().Checks.Single(check => check.Name == "钣金展开图").Status == CheckStatus.Unverified &&
                Grade().FinalAutoScore == null, "manual sheet-metal mode requires flat pattern evidence");
            rules.SheetMetal = SheetMetalMode.Automatic;
            reference.SheetMetal = new SheetMetalInspection
                { Detected = true, FoldedVerified = true, FlatVolumeM3 = 1, FlatSurfaceAreaM2 = 6 };
            student.SheetMetal = new SheetMetalInspection { Detected = true, FoldedVerified = false };
            Check(Grade().EvaluatedPoints == 0, "automatic detected sheet metal with unknown folded state leaves dimensions for review");
            student.SheetMetal.FoldedVerified = true;
            student.SheetMetal.FlatVolumeM3 = 1;
            student.SheetMetal.FlatSurfaceAreaM2 = 6;
            Check(Grade().FinalAutoScore == 100 && Grade().Checks.Single(check => check.Name == "钣金展开图").Status == CheckStatus.Pass,
                "same validated sheet snapshot receives geometry points after both flat reads");

            student.DocumentHash = new string('d', 64);
            Check(Grade().FinalAutoScore == null && Grade().ReviewPoints == 60 &&
                Grade().Checks.Single(check => check.Name == "钣金展开图").Status == CheckStatus.Unverified,
                "same flat area on different file contents does not prove equivalent outline or holes");
            student.SheetMetal.FlatSurfaceAreaM2 = 8;
            Check(Grade().FinalAutoScore == 40 &&
                Grade().Checks.Single(check => check.Name == "钣金展开图").Status == CheckStatus.Fail,
                "measured flat area outside tolerance fails the combined geometry check");
            rules.GeometryWeight = 0;
            rules.ParametersWeight = 100;
            Check(Grade().FinalAutoScore == null, "required flat check cannot be skipped for a final grade with zero geometry weight");

            rules.SheetMetal = SheetMetalMode.StandardPart;
            Check(Grade().FinalAutoScore == 100 && Grade().Checks.All(check => check.Name != "钣金展开图"),
                "manual ordinary-part mode intentionally omits the sheet requirement");
            var draft = RulesDraft.FromAssignment(new Assignment { Rules = rules });
            Check(draft.ToRules(acceptReview: true).SheetMetal == SheetMetalMode.StandardPart,
                "saved sheet recognition mode survives assignment reuse");
        }

        private static void CheckMeshPreview()
        {
            // Synthetic planar data in metres, independent of CAD.
            float[] reference = { 0, 0, 0, 0.01f, 0, 0, 0.01f, 0.01f, 0, 0, 0, 0, 0.01f, 0.01f, 0, 0, 0.01f, 0 };
            float[] otherDiagonal = { 0, 0, 0, 0.01f, 0, 0, 0, 0.01f, 0, 0.01f, 0, 0, 0.01f, 0.01f, 0, 0, 0.01f, 0 };
            var mesh = SurfacePreview.Prepare(reference, otherDiagonal);
            Check(mesh.Compared && mesh.Shared.Length == reference.Length && mesh.CurrentOnly.Length == 0 && mesh.ReferenceOnly.Length == 0,
                "flat face with another triangulation is drawn once in overlay");
            var shifted = (float[])otherDiagonal.Clone();
            for (var i = 2; i < shifted.Length; i += 3)
                shifted[i] = 0.005f;
            mesh = SurfacePreview.Prepare(reference, shifted);
            Check(mesh.Compared && mesh.Shared.Length == 0 && mesh.CurrentOnly.Length == shifted.Length &&
                mesh.ReferenceOnly.Length == reference.Length, "translated display surfaces remain distinct in their original axes");

            var extra = (float[])shifted.Take(9).ToArray();
            var changed = reference.Concat(extra).ToArray();
            mesh = SurfacePreview.Prepare(reference, changed);
            Check(mesh.Shared.Length == reference.Length && mesh.CurrentOnly.SequenceEqual(extra) && mesh.ReferenceOnly.Length == 0,
                "overlay isolates added display triangles with their original positions");
            var reversedFirst = reference.Skip(6).Take(3).Concat(reference.Skip(3).Take(3)).Concat(reference.Take(3));
            var duplicates = reference.Concat(reversedFirst).Concat(new float[9]).ToArray();
            mesh = SurfacePreview.Prepare(reference, duplicates);
            Check(mesh.Current.Length == reference.Length && mesh.Shared.Length == reference.Length,
                "renderer filters reversed coincident duplicates and degenerate triangles");
            Check(!SurfacePreview.Prepare(reference, null).Compared, "missing display mesh leaves overlay unavailable");
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
            Check(GeometryCoordinateChoice.Choices.Select(option => option.Value).SequenceEqual(
                new[] { GeometryCoordinates.Original, GeometryCoordinates.Datum }), "coordinate menus show only original and datum modes");
            var legacy = new Assignment { Rules = new AssignmentRules { GeometryCoordinates = GeometryCoordinates.Aligned } };
            var copied = RulesDraft.FromAssignment(legacy);
            Check(copied.NeedsCoordinates, "a copy of a legacy assignment starts with coordinates unselected");
            Expect<ArgumentException>(() => copied.ToRules(acceptReview: true), "saving a copy requires a supported coordinate mode");
            copied.GeometryCoordinates = GeometryCoordinates.Original;
            Check(copied.ToRules(acceptReview: true).GeometryCoordinates == GeometryCoordinates.Original &&
                legacy.Rules.GeometryCoordinates == GeometryCoordinates.Aligned, "the new draft does not change the stored old scoring policy");
            var draft = RulesDraft.FromAssignment(new Assignment { Rules = new AssignmentRules() });
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

        private static void CheckSimpleAssignment()
        {
            var draft = new RulesDraft();
            draft.ChangeStandard("支架.SLDPRT");
            var rules = draft.ToRules(acceptReview: true);
            Check(draft.Name == "支架" && draft.SaveLabel.Contains("待复核") &&
                rules.Mode == GradingMode.GeometryFirst && rules.GeometryWeight == 60 &&
                rules.ParametersWeight == 40 && rules.FeaturesWeight == 0 && !rules.CheckMass && !rules.CheckMaterial,
                "selecting standard alone gives a valid result-first draft with no unconfigured feature weight");
            draft.CheckProcess = true;
            Expect<ArgumentException>(() => draft.ToRules(acceptReview: true),
                "process draft asks for an operation before save");
            draft.FeatureChoices.Single(choice => choice.Label == "圆角").Selected = true;
            rules = draft.ToRules(acceptReview: true);
            Check(rules.Mode == GradingMode.ProcessRequired && rules.GeometryWeight == 60 && rules.ParametersWeight == 25 &&
                rules.FeaturesWeight == 15 && rules.RequiredFeatureTypes.SequenceEqual(new[] { "Fillet" }),
                "selecting a named operation configures real process weights and the CAD type");
            draft.FeatureTypes += "\nOther";
            draft.FeatureChoices.Single(choice => choice.Label == "圆角").Selected = false;
            Check(draft.FeatureTypes == "Other", "simple checkboxes and custom operation names share the same rule");
            draft.CheckProcess = false;
            rules = draft.ToRules(acceptReview: true);
            Check(rules.Mode == GradingMode.GeometryFirst && rules.FeaturesWeight == 0 && rules.ParametersWeight == 40,
                "turning off process grading returns those points to parameter grading");

            var legacy = new AssignmentRules { Mode = GradingMode.GeometryFirst };
            legacy.RequiredFeatureTypes.Add("Cut");
            var reused = RulesDraft.FromAssignment(new Assignment { Rules = legacy });
            rules = reused.ToRules(acceptReview: true);
            Check(reused.CheckProcess && rules.Mode == legacy.Mode &&
                rules.ParametersWeight == legacy.ParametersWeight && rules.FeaturesWeight == legacy.FeaturesWeight,
                "reusing a saved rule preserves its mode and weights until the teacher edits them");
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

        private static async Task CheckGeometryWorkflow(string root)
        {
            var reference = Input(root, "solid-reference", "mock geometry reference");
            var student = Input(root, "solid-student", "mock geometry student");
            using var store = new SqliteGradeStore(Path.Combine(root, "geometry"));
            var jobs = new Assignments(store);
            var assignment = jobs.Create("MOCK", reference, new AssignmentRules
            {
                GeometryCoordinates = GeometryCoordinates.Aligned,
                GeometryWeight = 60,
                ParametersWeight = 40,
                FeaturesWeight = 0
            });
            jobs.Import(assignment, new[] { reference, student });
            var reader = new MockGeometryReader();
            var service = new BatchGrading(store, reader, "MOCK");
            await service.RunAsync(assignment, null, null);
            var grades = store.LoadSubmissions(assignment.Id);
            Check(reader.Comparisons == 1 && grades[0].Grade.Geometry.Source == "IdenticalSnapshot" &&
                grades[1].Grade.Geometry.Source == "MOCK" && grades[1].Grade.Geometry.Coordinates == GeometryCoordinates.Aligned,
                "batch uses the saved coordinate policy and skips identical snapshots");
            var pair = grades[1];
            var trial = TrialGrading.Evaluate(assignment, pair.StandardModel, pair.StudentModel, pair.Grade.Geometry);
            Check(pair.Grade.FinalAutoScore == 40 && trial.FinalAutoScore == pair.Grade.FinalAutoScore &&
                pair.Grade.Geometry.Missing.ErrorCode == 0, "geometry evidence and identical calculation survive SQLite roundtrip");
            pair.Grade.Geometry.Coordinates = GeometryCoordinates.Original;
            trial = TrialGrading.Evaluate(assignment, pair.StandardModel, pair.StudentModel, pair.Grade.Geometry);
            Check(trial.FinalAutoScore == null && trial.ReviewPoints == 60, "trial with another coordinate strategy defers geometry");
            var datum = new AssignmentDatum
            {
                ReferenceHash = PartFiles.Hash(reference), Configuration = "A",
                Planes = new[]
                {
                    new DatumPlane { X = 1, AreaSquareMetres = 1 },
                    new DatumPlane { Y = 1, AreaSquareMetres = 1 },
                    new DatumPlane { Z = 1, AreaSquareMetres = 1 }
                }
            };
            var datumAssignment = jobs.Create("Datum", reference, new AssignmentRules
            {
                GeometryCoordinates = GeometryCoordinates.Datum, Datum = datum, StandardConfiguration = "A",
                GeometryWeight = 60, ParametersWeight = 40, FeaturesWeight = 0
            });
            jobs.Import(datumAssignment, new[] { student });
            var storedDatum = store.LoadAssignments().Single(job => job.Id == datumAssignment.Id);
            await service.RunAsync(storedDatum, null, null);
            var datumPair = store.LoadSubmissions(storedDatum.Id).Single();
            Check(datumPair.Grade.Geometry.Datum.SameDefinition(datum) && datumPair.Grade.FinalAutoScore == 40,
                "saved assignment datum travels through batch comparison and SQLite");
            datumPair.Grade.Geometry.Datum.Planes[0].OffsetMetres += 1;
            Check(TrialGrading.Evaluate(storedDatum, datumPair.StandardModel, datumPair.StudentModel,
                    datumPair.Grade.Geometry).ReviewPoints == 60,
                "trial rejects a comparison performed against a different datum definition");
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

        private class MockReader : IModelReader
        {
            private bool failedOnce;
            internal int ReferenceReads { get; private set; }
            internal int ManualConfigReads { get; private set; }

            public Task<PartConfigurations> ReadConfigurationsAsync(string path, string cadYear, Action<string> report)
            {
                var name = Path.GetFileNameWithoutExtension(path) == "manual-config" ? "B" : "A";
                return Task.FromResult(new PartConfigurations(PartFiles.Hash(path), name, new[] { name }));
            }

            public Task<ModelInspectionResult> ReadAsync(string path, string configuration, string cadYear, Action<string> report,
                SheetMetalMode sheetMetal = SheetMetalMode.Automatic)
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

        private sealed class MockGeometryReader : MockReader, IGeometryComparer
        {
            internal int Comparisons { get; private set; }

            public Task<GeometryEvidence> CompareAsync(ModelInspectionResult reference, ModelInspectionResult student,
                GeometryCoordinates coordinates, Action<string> report, AssignmentDatum datum = null)
            {
                Comparisons++;
                return Task.FromResult(new GeometryEvidence
                {
                    Source = "MOCK",
                    Coordinates = coordinates,
                    Datum = datum,
                    ValidatedComparison = true,
                    ReferenceVolumeM3 = 1,
                    MissingVolumeM3 = 0.01,
                    ExtraVolumeM3 = 0,
                    Missing = new GeometryOperationEvidence { ErrorCode = 0, VolumeM3 = 0.01 }
                });
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

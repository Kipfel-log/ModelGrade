using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace SWGrade.Domain
{
    public sealed class GradingEngine
    {
        public GradeResult Evaluate(AssignmentRules rules, ModelInspectionResult reference,
            ModelInspectionResult student, GeometryEvidence geometry, string studentConfigurationOverride = null)
        {
            if (rules == null)
                throw new ArgumentNullException(nameof(rules));
            rules.Validate();

            var grade = new GradeResult { Geometry = geometry };
            string evidenceProblem = EvidenceProblem(rules, reference, student, studentConfigurationOverride);
            GradeCheck flat = null;
            if (SheetMetalComparison.Required(rules.SheetMetal, reference, student))
            {
                if ((reference?.SheetMetal?.Detected == true && !reference.SheetMetal.FoldedVerified) ||
                    (student?.SheetMetal?.Detected == true && !student.SheetMetal.FoldedVerified))
                    evidenceProblem = evidenceProblem ?? "钣金的折弯态尚未核实，模型参数待复核。";
                flat = SheetMetalComparison.Check(rules, reference, student, evidenceProblem);
                if (flat.Status == CheckStatus.Fail && (rules.GeometryWeight == 0 || rules.GeometryPolicy == GeometryPolicy.TeacherReview))
                    flat.Status = CheckStatus.Unverified;
                grade.Checks.Add(flat);
            }
            grade.Checks.Add(Check("模型和配置", 0,
                evidenceProblem == null ? CheckStatus.Pass : CheckStatus.Unverified,
                evidenceProblem ?? "来源、CAD 版本、配置和重建状态已核对。"));
            grade.Checks.Add(GradeGeometry(rules, geometry, evidenceProblem, flat));
            grade.Checks.AddRange(GradeParameters(rules, reference, student, evidenceProblem));
            grade.Checks.AddRange(GradeFeatures(rules, student, evidenceProblem));
            return grade;
        }

        public static GradeCheck CompareMeasurement(string name, string unit, double? reference,
            double? student, MeasurementTolerance tolerance, decimal weight)
        {
            tolerance.Validate(name);
            var check = new GradeCheck
            {
                Name = name,
                Unit = unit,
                StandardValue = ValidMeasurement(reference),
                StudentValue = ValidMeasurement(student),
                Weight = weight,
                Status = weight == 0 ? CheckStatus.NotApplicable : CheckStatus.Unverified
            };
            if (!check.StandardValue.HasValue || !check.StudentValue.HasValue)
            {
                check.Reason = "标准值或学生测量值缺失，需复核。";
                return check;
            }

            double expected = check.StandardValue.Value;
            double absoluteError = Math.Abs(check.StudentValue.Value - expected);
            double threshold = Math.Max(tolerance.Absolute, tolerance.Relative * Math.Abs(expected));
            check.AbsoluteError = absoluteError;
            double relativeError = expected == 0 ? double.PositiveInfinity : absoluteError / expected;
            check.RelativeError = ValidMeasurement(relativeError);
            check.EffectiveTolerance = ValidMeasurement(threshold);
            if (!check.EffectiveTolerance.HasValue)
            {
                check.Reason = "有效容差超出可表示范围，需复核。";
                return check;
            }

            bool passes = absoluteError <= threshold;
            if (weight > 0)
            {
                check.Status = passes ? CheckStatus.Pass : CheckStatus.Fail;
                check.Awarded = passes ? weight : 0;
            }

            check.Reason = passes ? "绝对误差在有效容差内。" : "绝对误差超过有效容差。";
            return check;
        }

        private static IEnumerable<GradeCheck> GradeParameters(AssignmentRules rules, ModelInspectionResult reference,
            ModelInspectionResult student, string problem)
        {
            var inputs = new List<Func<decimal, GradeCheck>>
            {
                weight => CompareMeasurement("体积", "m³", reference?.VolumeM3, student?.VolumeM3, rules.VolumeTolerance, weight),
                weight => CompareMeasurement("实体表面积", "m²", reference?.SurfaceAreaM2, student?.SurfaceAreaM2, rules.AreaTolerance, weight)
            };

            if (rules.RequireSingleSolidBody)
                inputs.Add(weight => CompareMeasurement("学生实体数量", "个", 1,
                    student == null ? (double?)null : student.SolidBodyCount, new MeasurementTolerance(), weight));
            if (rules.CheckMass)
                inputs.Add(weight => CompareMeasurement("质量", "kg", reference?.MassKg, student?.MassKg, rules.MassTolerance, weight));
            if (rules.CheckMaterial)
                inputs.Add(weight => CompareMaterial(reference?.MaterialName, student?.MaterialName, weight));

            for (int index = 0; index < inputs.Count; index++)
            {
                decimal weight = SplitWeight(rules.ParametersWeight, inputs.Count, index);
                var check = inputs[index](weight);
                if (problem != null)
                {
                    check.Status = weight == 0 ? CheckStatus.NotApplicable : CheckStatus.Unverified;
                    check.Awarded = null;
                    check.Reason = problem;
                    check.AbsoluteError = null;
                    check.RelativeError = null;
                    check.EffectiveTolerance = null;
                }

                yield return check;
            }
        }

        private static GradeCheck CompareMaterial(string reference, string student, decimal weight)
        {
            CheckStatus status = CheckStatus.NotApplicable;
            if (weight > 0)
            {
                status = string.IsNullOrWhiteSpace(reference) || string.IsNullOrWhiteSpace(student)
                    ? CheckStatus.Unverified
                    : string.Equals(reference, student, StringComparison.OrdinalIgnoreCase) ? CheckStatus.Pass : CheckStatus.Fail;
            }
            return Check("零件级材料", weight, status,
                $"标准材料 {reference ?? "未取得"}，学生材料 {student ?? "未取得"}。");
        }

        private static IEnumerable<GradeCheck> GradeFeatures(AssignmentRules rules, ModelInspectionResult student, string problem)
        {
            if (rules.FeaturesWeight == 0 || rules.RequiredFeatureTypes.Count == 0)
            {
                yield return Check("指定特征", rules.FeaturesWeight,
                    rules.FeaturesWeight == 0 ? CheckStatus.NotApplicable : CheckStatus.Unverified,
                    rules.FeaturesWeight == 0 ? "教师未分配特征分值。" : "教师尚未配置需要验证的特征类型。");
                yield break;
            }

            for (int index = 0; index < rules.RequiredFeatureTypes.Count; index++)
            {
                var name = rules.RequiredFeatureTypes[index];
                decimal weight = SplitWeight(rules.FeaturesWeight, rules.RequiredFeatureTypes.Count, index);
                int count = 0;
                student?.ActiveFeatureCounts?.TryGetValue(name, out count);
                bool verifiedAbsence = student?.FeatureTraversalComplete == true &&
                    student.UnverifiedFeatureTypes?.Contains(name) == false;
                CheckStatus status = weight == 0 ? CheckStatus.NotApplicable :
                    problem != null ? CheckStatus.Unverified : count > 0 ? CheckStatus.Pass :
                    verifiedAbsence ? CheckStatus.Fail : CheckStatus.Unverified;
                var check = Check("特征类型 " + name, weight, status, problem ??
                    (status == CheckStatus.Unverified ? "当前配置无法核实是否存在有效特征。" : "检查未压缩的 API 特征类型是否存在。"));
                check.Unit = "个";
                check.StandardValue = 1;
                check.StudentValue = status == CheckStatus.Pass || status == CheckStatus.Fail ? (double?)count : null;
                yield return check;
            }
        }

        private static GradeCheck GradeGeometry(AssignmentRules rules, GeometryEvidence geometry, string problem, GradeCheck flat)
        {
            decimal weight = rules.GeometryWeight;
            if (weight == 0)
                return Check("几何差异", weight, CheckStatus.NotApplicable, "教师未分配几何分值。");
            if (problem != null)
                return Check("几何差异", weight, CheckStatus.Unverified, problem);
            if (rules.GeometryPolicy == GeometryPolicy.TeacherReview)
                return Check("几何差异", weight, CheckStatus.Unverified, "教师选择人工复核几何。");
            if (flat != null && flat.Status != CheckStatus.Pass)
                return Check("几何差异", weight, flat.Status, "几何分要求折弯态和展开图均符合。 " + flat.Reason);

            if (geometry != null && geometry.Coordinates != rules.GeometryCoordinates)
                return Check("几何差异", weight, CheckStatus.Unverified, "当前比较采用的坐标策略与作业不同，请按作业策略重新比较。");
            if (geometry?.ValidatedComparison == true && rules.GeometryCoordinates == GeometryCoordinates.Datum &&
                !rules.Datum.SameDefinition(geometry.Datum))
                return Check("几何差异", weight, CheckStatus.Unverified, "本次对齐使用的基准与作业设置不同，请按作业基准重新比较。");

            if (!(geometry?.DifferenceRatio is double ratio))
                return Check("几何差异", weight, CheckStatus.Unverified, geometry?.Reason ?? "当前环境未取得经验证的几何比较证据。");

            double tolerance = rules.GeometryTolerance;
            decimal fraction = ratio <= tolerance ? 1 : 0;
            if (rules.GeometryPolicy == GeometryPolicy.Bands)
            {
                var band = rules.GeometryBands.FirstOrDefault(item => ratio <= item.MaximumRatio);
                fraction = band?.AwardFraction ?? 0;
                tolerance = band?.MaximumRatio ?? rules.GeometryBands.Last().MaximumRatio;
            }
            var check = Check("几何差异", weight, fraction == 1 ? CheckStatus.Pass : CheckStatus.Fail,
                (geometry.Reason == null ? "" : geometry.Reason + " ") +
                "差异率按标准体积归一化。得分比例 " + fraction.ToString(CultureInfo.InvariantCulture) + "。");
            check.Awarded = decimal.Round(weight * fraction, 2, MidpointRounding.AwayFromZero);
            check.StudentValue = ratio;
            check.StandardValue = 0;
            check.AbsoluteError = ratio;
            check.EffectiveTolerance = tolerance;
            check.Unit = "比例";
            return check;
        }

        internal static string EvidenceProblem(AssignmentRules rules, ModelInspectionResult reference,
            ModelInspectionResult student, string studentConfigurationOverride = null)
        {
            if (reference?.OpenedSuccessfully != true || student?.OpenedSuccessfully != true ||
                !reference.SourceIntegrityVerified || !student.SourceIntegrityVerified ||
                reference.DocumentType != "Part" || student.DocumentType != "Part")
                return "标准件或学生件没有经过本次真实只读验证。";
            if (string.IsNullOrWhiteSpace(reference.SolidWorksRevision) ||
                !string.Equals(reference.SolidWorksRevision, student.SolidWorksRevision, StringComparison.Ordinal))
                return "标准件和学生件需在同一 SOLIDWORKS 修订版读取。";
            string standardConfiguration = rules.StandardConfiguration ?? reference.Configuration;
            string studentConfiguration = studentConfigurationOverride ?? rules.StudentConfiguration ?? reference.Configuration;
            if (string.IsNullOrWhiteSpace(standardConfiguration) || string.IsNullOrWhiteSpace(studentConfiguration) ||
                !string.Equals(standardConfiguration, reference.Configuration, StringComparison.Ordinal) ||
                !string.Equals(studentConfiguration, student.Configuration, StringComparison.Ordinal))
                return "配置映射未确认。请核对标准件与学生件的配置。";
            if (reference.RebuildStatusCode != 0 || student.RebuildStatusCode != 0 ||
                reference.ReviewOpenWarningCode != 0 || student.ReviewOpenWarningCode != 0)
                return "标准件或学生件有打开警告或待重建状态，需核查后比较。";
            return null;
        }

        private static GradeCheck Check(string name, decimal weight, CheckStatus status, string reason) =>
            new GradeCheck
            {
                Name = name,
                Weight = weight,
                Status = status,
                Awarded = status == CheckStatus.Pass ? (decimal?)weight : status == CheckStatus.Fail ? 0m : (decimal?)null,
                Reason = reason
            };

        private static double? ValidMeasurement(double? value) =>
            value.HasValue && Numeric.IsNonnegativeFinite(value.Value) ? value : null;

        private static decimal SplitWeight(decimal total, int count, int index)
        {
            decimal totalCents = total * 100;
            decimal baseCents = decimal.Floor(totalCents / count);
            bool receivesRemainder = index < totalCents - baseCents * count;
            return (baseCents + (receivesRemainder ? 1 : 0)) / 100;
        }
    }
}

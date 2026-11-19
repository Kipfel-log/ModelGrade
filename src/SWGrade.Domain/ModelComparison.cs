using System;
using System.Collections.Generic;
using System.Runtime.Serialization;

namespace SWGrade.Domain
{
    [DataContract]
    public sealed class ModelComparison
    {
        private ModelComparison(ModelInspectionResult reference, ModelInspectionResult current)
        {
            Reference = reference;
            Current = current;
        }

        [DataMember(Name = "reference")]
        public ModelInspectionResult Reference { get; private set; }

        [DataMember(Name = "current")]
        public ModelInspectionResult Current { get; private set; }

        [DataMember(Name = "checks")]
        private List<GradeCheck> Evidence { get; set; } = new List<GradeCheck>();

        public IReadOnlyList<GradeCheck> Checks => Evidence;

        [DataMember(Name = "geometry", EmitDefaultValue = false)]
        public GeometryEvidence Geometry { get; private set; }

        public static ModelComparison Evaluate(ModelInspectionResult reference, ModelInspectionResult current,
            string referenceConfiguration, string currentConfiguration, SheetMetalMode sheetMetal = SheetMetalMode.Automatic,
            GeometryEvidence geometry = null)
        {
            if (reference == null)
                throw new ArgumentNullException(nameof(reference));
            if (current == null)
                throw new ArgumentNullException(nameof(current));

            var rules = new AssignmentRules
            {
                StandardConfiguration = referenceConfiguration,
                StudentConfiguration = currentConfiguration
            };
            var report = new ModelComparison(reference, current);
            string problem = GradingEngine.EvidenceProblem(rules, reference, current);
            if (problem != null)
                report.Evidence.Add(Review("模型与配置", problem));

            report.AddMeasurement("体积", "m³", reference.VolumeM3, current.VolumeM3, rules.VolumeTolerance, problem);
            report.AddMeasurement("实体表面积", "m²", reference.SurfaceAreaM2, current.SurfaceAreaM2, rules.AreaTolerance, problem);
            report.AddMeasurement("质量", "kg", reference.MassKg, current.MassKg, rules.MassTolerance, problem);
            report.AddMeasurement("实体数量", "个", reference.SolidBodyCount, current.SolidBodyCount, new MeasurementTolerance(), problem);
            report.AddMeasurement("曲面体数量", "个", reference.SurfaceBodyCount, current.SurfaceBodyCount, new MeasurementTolerance(), problem);
            report.AddObservation("实体面数", reference.SolidFaceCount, current.SolidFaceCount);

            if (problem == null && !string.IsNullOrWhiteSpace(reference.MaterialName) && !string.IsNullOrWhiteSpace(current.MaterialName))
            {
                report.Evidence.Add(new GradeCheck
                {
                    Name = "零件级材料",
                    Status = string.Equals(reference.MaterialName, current.MaterialName, StringComparison.OrdinalIgnoreCase)
                        ? CheckStatus.Pass : CheckStatus.Fail,
                    Reason = $"参考件 {reference.MaterialName}，当前件 {current.MaterialName}。"
                });
            }
            else
            {
                report.Evidence.Add(Review("零件级材料", problem ?? "至少一个零件未取得零件级材料。"));
            }

            geometry = geometry ?? SavedGeometry.CompareIdenticalSnapshot(reference, current);
            report.Geometry = geometry;
            report.Evidence.Add(geometry.ValidatedComparison && problem == null
                ? new GradeCheck
                {
                    Name = "几何差异",
                    Status = geometry.MissingVolumeM3 == 0 && geometry.ExtraVolumeM3 == 0 ? CheckStatus.Pass : CheckStatus.Fail,
                    Reason = geometry.Reason
                }
                : Review("几何差异", problem ?? geometry.Reason));
            if (SheetMetalComparison.Required(sheetMetal, reference, current, adHoc: true))
                report.Evidence.Add(SheetMetalComparison.Check(rules, reference, current, problem));
            return report;
        }

        private void AddMeasurement(string name, string unit, double? reference, double? current,
            MeasurementTolerance tolerance, string problem)
        {
            var check = GradingEngine.CompareMeasurement(name, unit, reference, current, tolerance, 0);
            check.Status = CheckStatus.Unverified;
            if (problem != null)
            {
                check.AbsoluteError = null;
                check.RelativeError = null;
                check.EffectiveTolerance = null;
                check.Reason = problem;
            }
            else if (check.AbsoluteError.HasValue && check.EffectiveTolerance.HasValue)
            {
                check.Status = check.AbsoluteError.Value <= check.EffectiveTolerance.Value
                    ? CheckStatus.Pass : CheckStatus.Fail;
            }
            Evidence.Add(check);
        }

        private void AddObservation(string name, int? reference, int? current)
        {
            Evidence.Add(new GradeCheck
            {
                Name = name,
                Unit = "个",
                StandardValue = reference,
                StudentValue = current,
                Status = CheckStatus.NotApplicable,
                Reason = "面数用于核对拓扑。同一种形状可以有不同的面数。"
            });
        }

        private static GradeCheck Review(string name, string reason) =>
            new GradeCheck { Name = name, Status = CheckStatus.Unverified, Reason = reason };
    }
}

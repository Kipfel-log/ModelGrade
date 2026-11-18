using System;
using System.Runtime.Serialization;

namespace SWGrade.Domain
{
    public enum SheetMetalMode { Automatic, RequireFlatPattern, StandardPart }

    [DataContract]
    public sealed class SheetMetalInspection
    {
        [DataMember(Name = "detected", EmitDefaultValue = false)]
        public bool? Detected { get; internal set; }

        [DataMember(Name = "foldedVerified")]
        public bool FoldedVerified { get; internal set; }

        [DataMember(Name = "flatVolumeM3", EmitDefaultValue = false)]
        public double? FlatVolumeM3 { get; internal set; }

        [DataMember(Name = "flatSurfaceAreaM2", EmitDefaultValue = false)]
        public double? FlatSurfaceAreaM2 { get; internal set; }

        [DataMember(Name = "issue", EmitDefaultValue = false)]
        public string Issue { get; internal set; }

        public bool HasFlatMeasurements => FlatVolumeM3 > 0 && FlatSurfaceAreaM2 > 0 &&
            Numeric.IsNonnegativeFinite(FlatVolumeM3.Value) && Numeric.IsNonnegativeFinite(FlatSurfaceAreaM2.Value);
    }

    internal static class SheetMetalComparison
    {
        internal static bool Required(SheetMetalMode mode, ModelInspectionResult reference, ModelInspectionResult current,
            bool adHoc = false) => mode == SheetMetalMode.RequireFlatPattern ||
            (mode == SheetMetalMode.Automatic && (NeedsSheetReview(reference) || (adHoc && NeedsSheetReview(current))));

        private static bool NeedsSheetReview(ModelInspectionResult model) =>
            model?.SheetMetal?.Detected == true ||
            (model?.SheetMetal?.Detected == null && model?.SheetMetal?.Issue != null);

        internal static GradeCheck Check(AssignmentRules rules, ModelInspectionResult reference,
            ModelInspectionResult current, string blocker)
        {
            const string name = "钣金展开图";
            var referenceFlat = reference?.SheetMetal;
            var currentFlat = current?.SheetMetal;
            var check = new GradeCheck { Name = name, Status = CheckStatus.Unverified };
            if (blocker != null)
                check.Reason = blocker;
            else if (referenceFlat?.FoldedVerified != true || currentFlat?.FoldedVerified != true ||
                !referenceFlat.HasFlatMeasurements || !currentFlat.HasFlatMeasurements)
                check.Reason = "至少一件没有取得已核实的折弯态和展开态。请检查平板型式、配置和钣金实体。";
            else
            {
                var area = GradingEngine.CompareMeasurement(name, "m²", referenceFlat.FlatSurfaceAreaM2,
                    currentFlat.FlatSurfaceAreaM2, rules.AreaTolerance, 1);
                area.Weight = 0;
                area.Awarded = null;
                if (area.Status == CheckStatus.Fail)
                {
                    area.Reason = "展开态表面积超出作业容差。请同时核对展开轮廓和孔位。";
                    return area;
                }

                bool same = SavedGeometry.CompareIdenticalSnapshot(reference, current).ValidatedComparison &&
                    Math.Abs(referenceFlat.FlatVolumeM3.Value - currentFlat.FlatVolumeM3.Value) <= referenceFlat.FlatVolumeM3.Value * 1e-9 &&
                    Math.Abs(referenceFlat.FlatSurfaceAreaM2.Value - currentFlat.FlatSurfaceAreaM2.Value) <= referenceFlat.FlatSurfaceAreaM2.Value * 1e-9;
                area.Status = same ? CheckStatus.Pass : CheckStatus.Unverified;
                area.Reason = same ? "同一已验证文件和配置，展开实体的体积和面积一致。"
                    : "展开度量已读取。请用展开叠加核对外轮廓和孔位。";
                return area;
            }
            return check;
        }
    }
}

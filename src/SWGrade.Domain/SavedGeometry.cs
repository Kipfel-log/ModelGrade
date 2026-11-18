using System;
using System.Linq;

namespace SWGrade.Domain
{
    public static class SavedGeometry
    {
        public static GeometryEvidence CompareIdenticalSnapshot(ModelInspectionResult reference, ModelInspectionResult current)
        {
            // File identity is only useful for the same configuration and clean, reproducible CAD reads.
            string hash = reference?.DocumentHash;
            if (hash != null && hash.Length == 64 && hash.All(Uri.IsHexDigit) &&
                string.Equals(hash, current?.DocumentHash, StringComparison.OrdinalIgnoreCase) &&
                reference.SolidBodyCount == 1 && current.SolidBodyCount == 1 &&
                reference.SurfaceBodyCount == 0 && current.SurfaceBodyCount == 0 &&
                PositiveAndConsistent(reference.VolumeM3, current.VolumeM3) &&
                PositiveAndConsistent(reference.SurfaceAreaM2, current.SurfaceAreaM2) &&
                GradingEngine.EvidenceProblem(new AssignmentRules(), reference, current) == null)
            {
                return new GeometryEvidence
                {
                    ValidatedComparison = true,
                    ReferenceVolumeM3 = reference.VolumeM3,
                    MissingVolumeM3 = 0,
                    ExtraVolumeM3 = 0,
                    Reason = "两次只读验证的文件内容与配置相同，且实体度量一致。"
                };
            }

            return new GeometryEvidence
            {
                Reason = "没有可用于计分的实体差集证据，几何分值待教师复核。"
            };
        }

        private static bool PositiveAndConsistent(double? reference, double? current) =>
            reference > 0 && current > 0 && Numeric.IsNonnegativeFinite(reference.Value) &&
            Numeric.IsNonnegativeFinite(current.Value) &&
            Math.Abs(reference.Value - current.Value) <= reference.Value * 1e-9;
    }
}

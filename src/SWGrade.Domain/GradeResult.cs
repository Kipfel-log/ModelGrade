using System.Collections.Generic;
using System.Linq;

namespace SWGrade.Domain
{
    public enum CheckStatus { Pass, Fail, NotApplicable, Unverified, Error }

    public sealed class GradeCheck
    {
        public string Name { get; set; }
        public string Unit { get; set; }
        public CheckStatus Status { get; set; }
        public decimal Weight { get; set; }
        public decimal? Awarded { get; set; }
        public double? StandardValue { get; set; }
        public double? StudentValue { get; set; }
        public double? AbsoluteError { get; set; }
        public double? RelativeError { get; set; }
        public double? EffectiveTolerance { get; set; }
        public string Reason { get; set; }
    }

    public sealed class GeometryEvidence
    {
        public bool ValidatedComparison { get; set; }
        public string Source { get; set; }
        public GeometryCoordinates Coordinates { get; set; }
        public double? ReferenceVolumeM3 { get; set; }
        public double? StudentVolumeM3 { get; set; }
        public double? MissingVolumeM3 { get; set; }
        public double? ExtraVolumeM3 { get; set; }
        public double? OverlapVolumeM3 { get; set; }
        public string ReferenceHash { get; set; }
        public string StudentHash { get; set; }
        public string ReferenceConfiguration { get; set; }
        public string StudentConfiguration { get; set; }
        public string CadRevision { get; set; }
        public string AlignmentState { get; set; }
        public double[] StudentToReferenceTransform { get; set; }
        public GeometryOperationEvidence Missing { get; set; }
        public GeometryOperationEvidence Extra { get; set; }
        public GeometryOperationEvidence Overlap { get; set; }
        public string Reason { get; set; }

        public double? DifferenceRatio
        {
            get
            {
                if (!ValidatedComparison || !(ReferenceVolumeM3 > 0) ||
                    !Numeric.IsNonnegativeFinite(ReferenceVolumeM3.Value) ||
                    !MissingVolumeM3.HasValue || !Numeric.IsNonnegativeFinite(MissingVolumeM3.Value) ||
                    !ExtraVolumeM3.HasValue || !Numeric.IsNonnegativeFinite(ExtraVolumeM3.Value))
                    return null;
                double ratio = (MissingVolumeM3.Value + ExtraVolumeM3.Value) / ReferenceVolumeM3.Value;
                return Numeric.IsNonnegativeFinite(ratio) ? (double?)ratio : null;
            }
        }
    }

    public sealed class GeometryOperationEvidence
    {
        public string State { get; set; }
        public int? ErrorCode { get; set; }
        public string ErrorName { get; set; }
        public int? BodyCount { get; set; }
        public string ReturnedValue { get; set; }
        public double? VolumeM3 { get; set; }
        public string Reason { get; set; }
    }

    public sealed class GradeResult
    {
        public List<GradeCheck> Checks { get; set; } = new List<GradeCheck>();
        public GeometryEvidence Geometry { get; set; }
        public decimal EvaluatedPoints => Checks.Where(check => check.Awarded.HasValue).Sum(check => check.Weight);
        public decimal EarnedPoints => Checks.Sum(check => check.Awarded ?? 0);
        public decimal ReviewPoints => 100 - EvaluatedPoints;
        public bool NeedsReview => ReviewPoints > 0 || Checks.Any(check =>
            check.Status == CheckStatus.Unverified || check.Status == CheckStatus.Error);
        public decimal? FinalAutoScore => NeedsReview ? (decimal?)null : EarnedPoints;
    }
}

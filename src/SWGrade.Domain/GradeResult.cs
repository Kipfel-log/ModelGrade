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
        public double? ReferenceVolumeM3 { get; set; }
        public double? MissingVolumeM3 { get; set; }
        public double? ExtraVolumeM3 { get; set; }
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

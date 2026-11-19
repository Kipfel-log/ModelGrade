using System;
using System.Collections.Generic;
using System.Linq;

namespace SWGrade.Domain
{
    public enum GradingMode { GeometryFirst, ProcessRequired }
    public enum GeometryPolicy { PassFail, Bands, TeacherReview }
    public enum GeometryCoordinates { Original, Aligned }

    public sealed class MeasurementTolerance
    {
        public double Relative { get; set; }
        public double Absolute { get; set; }

        internal void Validate(string name)
        {
            if (!Numeric.IsNonnegativeFinite(Relative) || !Numeric.IsNonnegativeFinite(Absolute))
                throw new ArgumentException(name + "容差必须是非负有限数。");
        }
    }

    public sealed class GeometryBand
    {
        public double MaximumRatio { get; set; }
        public decimal AwardFraction { get; set; }
    }

    public sealed class AssignmentRules
    {
        public GradingMode Mode { get; set; } = GradingMode.GeometryFirst;
        public SheetMetalMode SheetMetal { get; set; }
        public decimal GeometryWeight { get; set; } = 60;
        public decimal ParametersWeight { get; set; } = 25;
        public decimal FeaturesWeight { get; set; } = 15;
        public MeasurementTolerance VolumeTolerance { get; set; } =
            new MeasurementTolerance { Relative = 0.001, Absolute = 1e-9 };
        public MeasurementTolerance AreaTolerance { get; set; } =
            new MeasurementTolerance { Relative = 0.001, Absolute = 1e-6 };
        public MeasurementTolerance MassTolerance { get; set; } =
            new MeasurementTolerance { Relative = 0.001, Absolute = 1e-6 };
        public GeometryPolicy GeometryPolicy { get; set; } = GeometryPolicy.PassFail;
        public GeometryCoordinates GeometryCoordinates { get; set; }
        public double GeometryTolerance { get; set; } = 0.001;
        public List<GeometryBand> GeometryBands { get; set; } = new List<GeometryBand>();
        public bool RequireSingleSolidBody { get; set; } = true;
        public bool CheckMass { get; set; }
        public bool CheckMaterial { get; set; }
        public List<string> RequiredFeatureTypes { get; set; } = new List<string>();
        public string StandardConfiguration { get; set; }
        public string StudentConfiguration { get; set; }
        public string ExpectedCadYear { get; set; }

        public void Validate()
        {
            var weights = new[] { GeometryWeight, ParametersWeight, FeaturesWeight };
            if (weights.Any(weight => weight < 0 || weight > 100 || decimal.Round(weight, 2) != weight) ||
                weights.Sum() != 100)
                throw new ArgumentException("三项权重必须分别为 0 至 100，合计 100，最多两位小数。");
            if (VolumeTolerance == null || AreaTolerance == null || MassTolerance == null)
                throw new ArgumentException("请填写各物理量的容差。");
            VolumeTolerance.Validate("体积");
            AreaTolerance.Validate("面积");
            MassTolerance.Validate("质量");
            if (!Numeric.IsNonnegativeFinite(GeometryTolerance))
                throw new ArgumentException("几何差异率容差必须是非负有限数。");
            if (!Enum.IsDefined(typeof(GradingMode), Mode) || !Enum.IsDefined(typeof(GeometryPolicy), GeometryPolicy) ||
                !Enum.IsDefined(typeof(SheetMetalMode), SheetMetal) ||
                !Enum.IsDefined(typeof(GeometryCoordinates), GeometryCoordinates))
                throw new ArgumentException("评分模式无效。");
            if (ExpectedCadYear != null && ExpectedCadYear != "2025" && ExpectedCadYear != "2026")
                throw new ArgumentException("可选择 SOLIDWORKS 2025、2026 或自动识别。");
            if (RequiredFeatureTypes == null ||
                RequiredFeatureTypes.Any(string.IsNullOrWhiteSpace) ||
                RequiredFeatureTypes.Distinct(StringComparer.Ordinal).Count() != RequiredFeatureTypes.Count)
                throw new ArgumentException("特征类型必须有效且互不重复。");
            if (Mode == GradingMode.ProcessRequired && (FeaturesWeight == 0 || RequiredFeatureTypes.Count == 0))
                throw new ArgumentException("指定过程模式需要特征规则和大于零的特征权重。");
            if (GeometryPolicy == GeometryPolicy.Bands)
                ValidateBands();
        }

        private void ValidateBands()
        {
            if (GeometryBands == null || GeometryBands.Count == 0)
                throw new ArgumentException("请提供几何分档。");
            double previousRatio = -1;
            decimal previousFraction = 1;
            foreach (var band in GeometryBands)
            {
                if (band == null || !Numeric.IsNonnegativeFinite(band.MaximumRatio) ||
                    band.MaximumRatio <= previousRatio || band.AwardFraction < 0 ||
                    band.AwardFraction > previousFraction)
                    throw new ArgumentException("几何分档的阈值须递增，得分比例须在 0 到 1 之间并按档递减或持平。");
                previousRatio = band.MaximumRatio;
                previousFraction = band.AwardFraction;
            }
        }
    }

    internal static class Numeric
    {
        internal static bool IsNonnegativeFinite(double value) =>
            value >= 0 && !double.IsInfinity(value);
    }
}

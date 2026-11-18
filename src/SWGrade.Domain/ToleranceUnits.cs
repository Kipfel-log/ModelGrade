namespace SWGrade.Domain
{
    public static class ToleranceUnits
    {
        public const double CubicCentimetresPerCubicMetre = 1000000;
        public const double SquareCentimetresPerSquareMetre = 10000;
        public const double GramsPerKilogram = 1000;

        public static double ToPercent(double ratio) => ratio * 100;
        public static double FromPercent(double percent) => percent / 100;
        public static double ToDisplay(double siValue, double unitsPerSi) => siValue * unitsPerSi;
        public static double FromDisplay(double displayValue, double unitsPerSi) => displayValue / unitsPerSi;
    }
}

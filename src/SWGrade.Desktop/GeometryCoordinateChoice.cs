using SWGrade.Domain;

namespace SWGrade.Desktop
{
    internal sealed class GeometryCoordinateChoice
    {
        private GeometryCoordinateChoice(GeometryCoordinates value, string label)
        {
            Value = value;
            Label = label;
        }

        public GeometryCoordinates Value { get; }
        public string Label { get; }

        internal static GeometryCoordinateChoice[] Choices { get; } =
        {
            new GeometryCoordinateChoice(GeometryCoordinates.Original, "原始 CAD 坐标"),
            new GeometryCoordinateChoice(GeometryCoordinates.Datum, "按基准对齐"),
            new GeometryCoordinateChoice(GeometryCoordinates.Aligned, "仅验证完全重合")
        };
    }
}

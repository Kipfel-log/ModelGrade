using SWGrade.Domain;

namespace SWGrade.Desktop
{
    internal sealed class SheetMetalOption
    {
        private SheetMetalOption(SheetMetalMode mode, string label)
        {
            Mode = mode;
            Label = label;
        }

        public SheetMetalMode Mode { get; }
        public string Label { get; }

        internal static SheetMetalOption[] Choices { get; } = new[]
        {
            new SheetMetalOption(SheetMetalMode.Automatic, "自动识别钣金"),
            new SheetMetalOption(SheetMetalMode.RequireFlatPattern, "按钣金检查展开图"),
            new SheetMetalOption(SheetMetalMode.StandardPart, "按普通零件处理")
        };
    }
}

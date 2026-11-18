using System;
using System.ComponentModel;
using System.Globalization;
using SWGrade.Domain;

namespace SWGrade.Desktop
{
    internal sealed class RuleNumber : INotifyPropertyChanged
    {
        private const NumberStyles InputStyles = NumberStyles.AllowDecimalPoint | NumberStyles.AllowLeadingSign |
            NumberStyles.AllowLeadingWhite | NumberStyles.AllowTrailingWhite;

        private string text;

        internal RuleNumber(string label, double value, double sliderMaximum, string unit)
        {
            Label = label;
            text = Format(value);
            SliderMaximum = sliderMaximum;
            Unit = unit;
        }

        public string Label { get; }
        public string Unit { get; }
        public double SliderMaximum { get; }
        public double SliderStep => SliderMaximum == 100 ? 1 : 0.01;

        public string Text
        {
            get => text;
            set
            {
                if (text == value)
                    return;
                text = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Text)));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SliderValue)));
                Changed?.Invoke(this, EventArgs.Empty);
            }
        }

        public double SliderValue
        {
            get => TryValue(out var value) ? Math.Max(0, Math.Min(SliderMaximum, (double)value)) : 0;
            set => Text = Format(Math.Round(value, SliderMaximum == 100 ? 2 : 3));
        }

        internal bool TryValue(out decimal value) => decimal.TryParse(Text, InputStyles, CultureInfo.InvariantCulture, out value);

        internal decimal Read()
        {
            if (!TryValue(out var value) || value < 0)
                throw new ArgumentException(Label + "请填写非负数，小数使用点号。");
            return value;
        }

        internal static string Format(double value) =>
            value.ToString("0.###########################", CultureInfo.InvariantCulture);

        internal event EventHandler Changed;
        public event PropertyChangedEventHandler PropertyChanged;
    }

    internal sealed class ToleranceDraft : INotifyPropertyChanged
    {
        private readonly MeasurementTolerance original;
        private readonly string originalPercent;
        private readonly string originalAbsolute;
        private readonly double unitsPerSi;
        private readonly double exampleValue;
        private readonly string parameter;
        private readonly string unit;

        internal ToleranceDraft(string parameter, string unit, double unitsPerSi,
            double exampleValue, MeasurementTolerance original)
        {
            this.parameter = parameter;
            this.unit = unit;
            this.unitsPerSi = unitsPerSi;
            this.exampleValue = exampleValue;
            this.original = original;
            Relative = new RuleNumber(parameter + "相对容差", ToleranceUnits.ToPercent(original.Relative), 5, "%");
            Absolute = new RuleNumber(parameter + "绝对容差", ToleranceUnits.ToDisplay(original.Absolute, unitsPerSi), 0, unit);
            originalPercent = Relative.Text;
            originalAbsolute = Absolute.Text;
            Relative.Changed += InputChanged;
            Absolute.Changed += InputChanged;
        }

        public RuleNumber Relative { get; }
        public RuleNumber Absolute { get; }

        public string Example
        {
            get
            {
                try
                {
                    double sample = ToleranceUnits.FromDisplay(exampleValue, unitsPerSi);
                    var check = GradingEngine.CompareMeasurement(parameter, unit, sample, sample, ToTolerance(), 0);
                    if (!check.EffectiveTolerance.HasValue)
                        return "请输入有效容差。";
                    double limit = ToleranceUnits.ToDisplay(check.EffectiveTolerance.Value, unitsPerSi);
                    return $"参考{parameter}为 {RuleNumber.Format(exampleValue)} {unit} 时，允许偏差 ±{RuleNumber.Format(limit)} {unit}" +
                           $"（{RuleNumber.Format(Math.Max(0, exampleValue - limit))} 至 {RuleNumber.Format(exampleValue + limit)} {unit}）。";
                }
                catch (ArgumentException)
                {
                    return "请输入有效容差后查看示例。";
                }
            }
        }

        internal MeasurementTolerance ToTolerance() => new MeasurementTolerance
        {
            Relative = Relative.Text == originalPercent ? original.Relative : ToleranceUnits.FromPercent((double)Relative.Read()),
            Absolute = Absolute.Text == originalAbsolute ? original.Absolute : ToleranceUnits.FromDisplay((double)Absolute.Read(), unitsPerSi)
        };

        private void InputChanged(object sender, EventArgs args) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Example)));

        public event PropertyChangedEventHandler PropertyChanged;
    }
}

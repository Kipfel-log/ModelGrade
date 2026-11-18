using System;
using System.ComponentModel;
using System.IO;
using System.Linq;
using SWGrade.Application;
using SWGrade.Domain;
using SWGrade.Infrastructure;

namespace SWGrade.Desktop
{
    internal sealed class SubmissionRow : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;
        internal Submission Item { get; private set; }
        public int Number { get; }
        public string Student => string.Join(" ", new[] { Item.StudentId, Item.StudentName }
            .Where(value => !string.IsNullOrWhiteSpace(value)));
        public string File => Path.GetFileName(Item.FilePath);
        public string Status => StateText.Task(Item.State);
        public string Automatic => ReportExporter.Summary(Item.Grade);
        public string Final => ReportExporter.Score(Item.FinalScore);
        public int Errors => Item.Attempts.Count(attempt => attempt.Error != null) + (Item.StudentModel?.Errors.Count ?? 0);
        public string Notice => Item.ImportNotice;

        internal SubmissionRow(int number, Submission item)
        {
            Number = number;
            Item = item;
        }

        internal void Update(Submission item)
        {
            Item = item;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(string.Empty));
        }
    }

    internal sealed class ComparisonRow
    {
        private readonly GradeCheck check;

        internal ComparisonRow(GradeCheck check) { this.check = check; }

        public string Name => check.Name;
        public string Standard => Format(check.StandardValue);
        public string Student => Format(check.StudentValue);
        public string Absolute => Format(check.AbsoluteError);
        public string Relative => Percent(check.RelativeError);
        public string Tolerance => Format(check.EffectiveTolerance);
        public string Conclusion => StateText.Check(check.Status);
        public string Score => check.Weight == 0 ? Conclusion : (check.Awarded.HasValue ? ReportExporter.Score(check.Awarded) : "待复核") +
            " / " + ReportExporter.Score(check.Weight);
        public string Reason => check.Reason;
        public string Measurements => check.AbsoluteError.HasValue
            ? $"标准 {Standard} · 当前 {Student} · 实际差值 {Absolute} · 允许 {Tolerance}"
            : check.StandardValue.HasValue || check.StudentValue.HasValue ? $"标准 {Standard} · 当前 {Student}" : "";

        private string Format(double? value)
        {
            switch (check.Unit)
            {
                case "m³": return ReportExporter.Measurement(value * ToleranceUnits.CubicCentimetresPerCubicMetre, "cm³");
                case "m²": return ReportExporter.Measurement(value * ToleranceUnits.SquareCentimetresPerSquareMetre, "cm²");
                case "kg": return ReportExporter.Measurement(value * ToleranceUnits.GramsPerKilogram, "g");
                case "比例": return Percent(value);
                default: return ReportExporter.Measurement(value, check.Unit);
            }
        }

        private static string Percent(double? value) => ReportExporter.Measurement(value * 100, "%");
    }
}

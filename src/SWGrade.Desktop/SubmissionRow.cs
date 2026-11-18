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
        public string Standard => ReportExporter.Measurement(check.StandardValue, check.Unit);
        public string Student => ReportExporter.Measurement(check.StudentValue, check.Unit);
        public string Absolute => ReportExporter.Measurement(check.AbsoluteError, check.Unit);
        public string Relative => ReportExporter.Measurement(check.RelativeError, null);
        public string Tolerance => ReportExporter.Measurement(check.EffectiveTolerance, check.Unit);
        public string Conclusion => StateText.Check(check.Status);
        public string Score => ReportExporter.Score(check.Awarded) + " / " + ReportExporter.Score(check.Weight);
        public string Reason => check.Reason;
    }
}

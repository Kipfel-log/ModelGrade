using System.Collections.Generic;
using System.Linq;
using SWGrade.Domain;

namespace SWGrade.Teacher
{
    internal sealed class ClassroomStatistics
    {
        private static readonly int[] Lower = { 0, 60, 70, 80, 90 };
        private static readonly int[] Upper = { 59, 69, 79, 89, 100 };

        internal int Submitted { get; }
        internal int Pending { get; }
        internal double? Average { get; }
        internal int? Best { get; }
        internal ScoreColumn[] Distribution { get; }
        internal StepBar[] Breakdown { get; }

        internal ClassroomStatistics(IEnumerable<Part3Report> reports)
        {
            var results = reports.ToArray();
            var final = results.Where(report => report.Pending == 0).Select(report => report.Earned).ToArray();
            Submitted = results.Length;
            Pending = Submitted - final.Length;
            if (final.Length > 0)
            {
                Average = final.Average();
                Best = final.Max();
            }

            Distribution = Lower.Select((min, index) =>
                new ScoreColumn(min, Upper[index], final.Count(score => score >= min && score <= Upper[index]))).ToArray();
            int peak = Distribution.Max(column => column.Count);
            foreach (var column in Distribution)
                column.Height = peak == 0 ? 0 : 145.0 * column.Count / peak;

            Breakdown = results.SelectMany(report => report.Lines
                    .Where(line => line.Points.HasValue && line.Points.Value < line.Maximum)
                    .GroupBy(line => line.Step)
                    .Select(step => new { Step = step.Key, Lost = step.Sum(line => line.Maximum - line.Points.Value) }))
                .GroupBy(item => item.Step)
                .Select(step => new StepBar(step.Key, step.Count(), step.Sum(item => item.Lost)))
                .OrderByDescending(step => step.Count).ThenByDescending(step => step.Lost).ThenBy(step => step.Step).ToArray();
            if (Breakdown.Length > 0)
                foreach (var bar in Breakdown)
                    bar.Share = (double)bar.Count / Submitted;
        }

        public sealed class ScoreColumn
        {
            internal ScoreColumn(int min, int max, int count)
            {
                Range = min + "–" + max;
                Count = count;
            }

            public string Range { get; }
            public int Count { get; }
            public double Height { get; internal set; }
        }

        public sealed class StepBar
        {
            internal StepBar(string step, int count, int lost)
            {
                Step = step;
                Count = count;
                Lost = lost;
            }

            public string Step { get; }
            public int Count { get; }
            public int Lost { get; }
            public double Share { get; internal set; }
            public string Description => $"{Count} 人 · 共扣 {Lost} 分";
        }
    }
}

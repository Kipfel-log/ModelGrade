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
            var scores = results.Select(report => new { Earned = report.Earned, NeedsReview = report.Pending > 0 }).ToArray();
            var final = scores.Where(score => !score.NeedsReview).Select(score => score.Earned).ToArray();
            var pending = scores.Where(score => score.NeedsReview).Select(score => score.Earned).ToArray();
            Submitted = results.Length;
            Pending = pending.Length;
            if (scores.Length > 0)
            {
                Average = scores.Average(score => score.Earned);
                Best = scores.Max(score => score.Earned);
            }

            Distribution = Lower.Select((min, index) =>
            {
                int max = Upper[index];
                return new ScoreColumn(min, max, final.Count(score => score >= min && score <= max),
                    pending.Count(score => score >= min && score <= max));
            }).ToArray();
            int peak = Distribution.Max(column => column.Count);
            double heightPerStudent = peak == 0 ? 0 : 145.0 / peak;
            foreach (var column in Distribution)
            {
                column.FinalHeight = heightPerStudent * column.FinalCount;
                column.PendingHeight = heightPerStudent * column.PendingCount;
            }

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
            internal ScoreColumn(int min, int max, int finalCount, int pendingCount)
            {
                Range = min + "–" + max;
                FinalCount = finalCount;
                PendingCount = pendingCount;
            }

            public string Range { get; }
            public int FinalCount { get; }
            public int PendingCount { get; }
            public int Count => FinalCount + PendingCount;
            public double FinalHeight { get; internal set; }
            public double PendingHeight { get; internal set; }
            public string Description => $"{Range} 分 · 完整成绩 {FinalCount} 人 · 待复核 {PendingCount} 人";
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

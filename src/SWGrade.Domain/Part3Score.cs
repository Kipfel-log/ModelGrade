using System;
using System.Collections.Generic;
using System.Linq;

namespace SWGrade.Domain
{
    public sealed class Part3Report
    {
        public const string Version = "part3-v1";
        public string Rubric { get; set; } = Version;
        public string StudentId { get; set; }
        public string StudentName { get; set; }
        public string ModelName { get; set; }
        public string ModelHash { get; set; }
        public DateTime CreatedUtc { get; set; }
        public Part3ScoreLine[] Lines { get; set; }
        public Part3Difference Shell { get; set; }
        public Part3Difference Flat { get; set; }
        public int Earned => Lines.Sum(line => line.Points ?? 0);
        public int Pending => Lines.Where(line => line.Points == null).Sum(line => line.Maximum);
        public string Score => Pending == 0 ? $"{Earned} / 100" : $"{Earned} 分 · {Pending} 分待复核";
    }

    public sealed class Part3ScoreLine
    {
        public string Step { get; set; }
        public string Criterion { get; set; }
        public int Maximum { get; set; }
        public int? Points { get; set; }
        public string Detail { get; set; }
        public string Display => Points.HasValue ? $"{Points} / {Maximum}" : $"{Maximum} 分待复核";
    }

    public sealed class Part3Difference
    {
        public double ExtraMm3 { get; set; }
        public double MissingMm3 { get; set; }
        public double Ratio { get; set; }
        public string Issue { get; set; }
        public string Display => Issue ?? $"多料 {ExtraMm3:G7} mm³ · 缺料 {MissingMm3:G7} mm³（{Ratio:P3}）";
    }
}

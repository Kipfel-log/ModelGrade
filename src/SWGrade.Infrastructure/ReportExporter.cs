using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using SWGrade.Application;
using SWGrade.Domain;

namespace SWGrade.Infrastructure
{
    public static class ReportExporter
    {
        public static void SaveCsv(string path, Assignment assignment, IEnumerable<Submission> submissions)
        {
            using (var writer = OpenNew(path, ".csv", new UTF8Encoding(true)))
            {
                Row(writer, "作业", "学号", "姓名", "文件", "状态", "自动已得分", "已评估满分",
                    "待复核分值", "自动确定分", "最终成绩", "人工原因", "复核时间 UTC", "配置", "CAD 修订版",
                    "SHA-256", "任务 ID", "说明");
                foreach (var item in submissions)
                {
                    var grade = item.Grade;
                    var review = item.Reviews.LastOrDefault();
                    Row(writer, assignment.Name, item.StudentId, item.StudentName, Path.GetFileName(item.FilePath),
                        StateText.Task(item.State), Score(grade?.EarnedPoints), Score(grade?.EvaluatedPoints),
                        Score(grade?.ReviewPoints), Score(grade?.FinalAutoScore), Score(item.FinalScore),
                        review?.Reason, review?.TimeUtc, item.StudentModel?.Configuration,
                        item.StudentModel?.SolidWorksRevision, item.FileHash, item.Attempts.LastOrDefault()?.TaskId,
                        item.StatusDetail);
                }
            }
        }

        public static void SaveHtml(string path, Assignment assignment, Submission item)
        {
            using (var writer = OpenNew(path, ".html", new UTF8Encoding(false)))
            {
                writer.WriteLine("<!doctype html><html lang=\"zh-CN\"><head><meta charset=\"utf-8\">");
                writer.WriteLine("<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">");
                writer.WriteLine("<title>SWGrade 单件报告</title><style>body{font:16px system-ui,sans-serif;max-width:76rem;margin:2rem auto;padding:0 1rem;color:#172b38}table{border-collapse:collapse;width:100%}th,td{border:1px solid #d5dee4;padding:.45rem;text-align:left;overflow-wrap:anywhere}th{background:#f1f5f6}dd,li{overflow-wrap:anywhere}</style></head><body>");
                writer.WriteLine("<h1>SWGrade 单件报告</h1><dl>");
                Field(writer, "作业", assignment.Name);
                Field(writer, "学号 / 姓名", $"{item.StudentId} / {item.StudentName}");
                Field(writer, "文件", Path.GetFileName(item.FilePath));
                Field(writer, "文件 SHA-256", item.FileHash);
                Field(writer, "标准件", Path.GetFileName(assignment.StandardPath));
                Field(writer, "标准件 SHA-256", assignment.StandardHash);
                Field(writer, "状态", StateText.Task(item.State));
                Field(writer, "说明", item.StatusDetail);
                Field(writer, "最终成绩", Score(item.FinalScore));
                Field(writer, "CAD 修订版", item.StudentModel?.SolidWorksRevision);
                Field(writer, "标准配置 / 学生配置", $"{item.StandardModel?.Configuration} / {item.StudentModel?.Configuration}");
                Field(writer, "几何 / 参数 / 特征权重", $"{assignment.Rules.GeometryWeight} / {assignment.Rules.ParametersWeight} / {assignment.Rules.FeaturesWeight}");
                if (item.Grade != null)
                {
                    Field(writer, "自动评分", Summary(item.Grade));
                    Field(writer, "自动确定分", Score(item.Grade.FinalAutoScore));
                    Field(writer, "几何证据", item.Grade.Geometry?.Reason);
                }
                writer.WriteLine("</dl>");

                if (item.Grade != null)
                {
                    writer.WriteLine("<h2>逐项比较</h2><table><thead><tr><th>检查项</th><th>标准值</th><th>学生值</th><th>绝对误差</th><th>相对误差</th><th>有效容差</th><th>结论</th><th>得分 / 分值</th><th>依据</th></tr></thead><tbody>");
                    foreach (var check in item.Grade.Checks)
                    {
                        writer.Write("<tr>");
                        Cell(writer, check.Name);
                        Cell(writer, Measurement(check.StandardValue, check.Unit));
                        Cell(writer, Measurement(check.StudentValue, check.Unit));
                        Cell(writer, Measurement(check.AbsoluteError, check.Unit));
                        Cell(writer, Measurement(check.RelativeError, null));
                        Cell(writer, Measurement(check.EffectiveTolerance, check.Unit));
                        Cell(writer, StateText.Check(check.Status));
                        Cell(writer, $"{Score(check.Awarded)} / {Score(check.Weight)}");
                        Cell(writer, check.Reason);
                        writer.WriteLine("</tr>");
                    }
                    writer.WriteLine("</tbody></table>");
                }

                if (item.Reviews.Count > 0)
                {
                    writer.WriteLine("<h2>教师复核历史</h2><ul>");
                    foreach (var review in item.Reviews)
                        Item(writer, $"{review.TimeUtc} · {Score(review.Score)} · {review.Reason}");
                    writer.WriteLine("</ul>");
                }

                if (item.Attempts.Count > 0)
                {
                    writer.WriteLine("<h2>执行记录</h2><ul>");
                    foreach (var attempt in item.Attempts)
                        Item(writer, $"{attempt.TaskId} · {attempt.StartedUtc} 至 {attempt.FinishedUtc} · SWGrade {attempt.ExecutableVersion} {attempt.Error}");
                    writer.WriteLine("</ul>");
                }
                writer.WriteLine("</body></html>");
            }
        }

        public static string Summary(GradeResult grade) => grade == null ? "—" :
            $"{Score(grade.EarnedPoints)}/{Score(grade.EvaluatedPoints)}，另 {Score(grade.ReviewPoints)} 分待复核";

        public static string Score(decimal? value) => value?.ToString("0.##", CultureInfo.InvariantCulture) ?? "";

        public static string Measurement(double? value, string unit) =>
            value.HasValue ? value.Value.ToString("G10", CultureInfo.InvariantCulture) +
                (string.IsNullOrEmpty(unit) ? "" : " " + unit) : "—";

        private static StreamWriter OpenNew(string path, string extension, Encoding encoding)
        {
            if (!string.Equals(Path.GetExtension(path), extension, StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("导出文件名必须以 " + extension + " 结尾。");
            return new StreamWriter(File.Open(path, FileMode.CreateNew, FileAccess.Write, FileShare.None), encoding);
        }

        private static void Row(TextWriter writer, params string[] values) =>
            writer.WriteLine(string.Join(",", values.Select(CsvCell)));

        private static string CsvCell(string value)
        {
            value = value ?? "";
            string trimmed = value.TrimStart();
            if ((value.Length > 0 && (value[0] == '\t' || value[0] == '\r' || value[0] == '\n')) ||
                (trimmed.Length > 0 && "=+-@＝＋－＠\uFEFF".IndexOf(trimmed[0]) >= 0))
                value = "'" + value;
            return "\"" + value.Replace("\"", "\"\"") + "\"";
        }

        private static void Field(TextWriter writer, string name, string value) =>
            writer.WriteLine("<dt>" + WebUtility.HtmlEncode(name) + "</dt><dd>" + WebUtility.HtmlEncode(value ?? "") + "</dd>");
        private static void Cell(TextWriter writer, string value) =>
            writer.Write("<td>" + WebUtility.HtmlEncode(value ?? "") + "</td>");
        private static void Item(TextWriter writer, string value) =>
            writer.WriteLine("<li>" + WebUtility.HtmlEncode(value ?? "") + "</li>");
    }
}

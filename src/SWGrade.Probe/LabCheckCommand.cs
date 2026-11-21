using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using SWGrade.SolidWorks;

namespace SWGrade.Probe
{
    internal static class LabCheckCommand
    {
        private const string Year = "2025";
        private const int Failed = 4;

        internal static int Run()
        {
            var log = new List<string>();
            string reference = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "reference", "零件3.SLDPRT");
            Console.WriteLine("SWGrade 机房一键检测 · " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            try
            {
                string referenceIssue = CheckReference(reference);
                if (referenceIssue != null)
                    log.Add(referenceIssue);

                var connection = CadAnalysis.Detect(Year, log.Add);
                if (!connection.CanAnalyze)
                {
                    Console.WriteLine("【连接失败】" + connection.Message);
                    Console.WriteLine("【怎么处理】" + connection.RecommendedAction);
                }
                else
                    Console.WriteLine("【连接通过】SOLIDWORKS " + connection.ConnectedYear + " · " + connection.Revision);

                if (referenceIssue != null)
                {
                    Console.WriteLine("【标准件检查失败】" + referenceIssue);
                    Console.WriteLine("【怎么处理】把机房试用包完整解压到本地目录，保留 reference 目录中的原始标准件。");
                }
                else
                    Console.WriteLine("【标准件核对通过】文件与发布版本一致。");

                if (!connection.CanAnalyze || referenceIssue != null)
                {
                    Console.WriteLine("【判分自检】等连接和标准件两项通过后再运行。");
                    return Failed;
                }

                var score = CadAnalysis.GradePart3(reference, reference, Year, log.Add);
                Console.WriteLine("【判分自检】" + score.Score);
                Console.WriteLine("抽壳后 · " + score.Shell.Display);
                Console.WriteLine("展开后 · " + score.Flat.Display);
                if (score.Earned == 100 && score.Pending == 0 && score.Shell.Issue == null && score.Flat.Issue == null &&
                    score.Shell.Ratio == 0 && score.Flat.Ratio == 0)
                {
                    Console.WriteLine("【全部通过】可以打开学生端，使用该标准件判分。");
                    return 0;
                }

                foreach (var line in score.Lines)
                    if (line.Points != line.Maximum)
                        Console.WriteLine("待确认 · " + line.Step + " · " + line.Detail);
                Console.WriteLine("【怎么处理】自比有差异或待复核项。保留本报告和 CAD 版本，由开发端核对 CAD 读取结果。");
                return Failed;
            }
            catch (Exception error)
            {
                Console.WriteLine("【自检中断】" + CadAnalysis.DescribeError(error));
                Console.WriteLine("【怎么处理】用 SOLIDWORKS 2025 确认随包标准件能打开。若仍失败，请保留完整报告。");
                log.Add(error.ToString());
                return Failed;
            }
            finally
            {
                Console.WriteLine();
                Console.WriteLine("—— 详细记录 ——");
                foreach (string line in log)
                    Console.WriteLine(line);
            }
        }

        private static string CheckReference(string path)
        {
            try
            {
                using (var stream = File.OpenRead(path))
                using (var sha = SHA256.Create())
                {
                    string hash = BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "");
                    return string.Equals(hash, CadAnalysis.Part3ReferenceHash, StringComparison.OrdinalIgnoreCase)
                        ? null : "随包标准件已被改动或替换。";
                }
            }
            catch (Exception error) when (error is IOException || error is UnauthorizedAccessException)
            {
                return "无法读取 reference 目录里的标准件。 " + CadAnalysis.DescribeError(error);
            }
        }
    }
}

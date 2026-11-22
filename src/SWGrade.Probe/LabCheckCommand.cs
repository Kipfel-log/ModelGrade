using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using SWGrade.SolidWorks;

namespace SWGrade.Probe
{
    internal static class LabCheckCommand
    {
        private const string Year = "2025";
        private const int Failed = 4;

        internal static int RunAsAdministrator()
        {
            try
            {
                if (IsAdministrator())
                    return RunToReport();

                Console.WriteLine("请确认 Windows 的管理员权限提示。");
                using (var worker = Process.Start(new ProcessStartInfo
                {
                    FileName = typeof(LabCheckCommand).Assembly.Location,
                    Arguments = "--lab-check-report",
                    WorkingDirectory = AppDomain.CurrentDomain.BaseDirectory,
                    UseShellExecute = true,
                    Verb = "runas",
                    WindowStyle = ProcessWindowStyle.Hidden
                }))
                {
                    if (worker == null)
                        throw new InvalidOperationException("Windows 未启动机房检测工具。");
                    worker.WaitForExit();
                    return worker.ExitCode;
                }
            }
            catch (Win32Exception error) when (error.NativeErrorCode == 1223)
            {
                Console.Error.WriteLine("管理员请求已取消。请重新打开检测工具并确认 Windows 提示。");
                return 5;
            }
            catch (Exception error)
            {
                Console.Error.WriteLine("无法以管理员权限启动检测。 " + CadAnalysis.DescribeError(error));
                return 5;
            }
        }

        internal static int RunToReport()
        {
            try
            {
                string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "SWGrade-Compatibility.txt");
                using (var output = new StreamWriter(path, false, new UTF8Encoding(false)))
                    return Run(output);
            }
            catch (Exception error) when (error is IOException || error is UnauthorizedAccessException)
            {
                Console.Error.WriteLine("无法保存检测报告。请将整包解压到可写的本地目录。 " + CadAnalysis.DescribeError(error));
                return 6;
            }
        }

        internal static int Run(TextWriter output)
        {
            var log = new List<string>();
            string reference = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "reference", "零件3.SLDPRT");
            output.WriteLine("SWGrade 机房一键检测 · " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            try
            {
                output.WriteLine("当前权限 · " + (IsAdministrator() ? "管理员" : "普通用户"));
                string referenceIssue = CheckReference(reference);
                if (referenceIssue != null)
                    log.Add(referenceIssue);

                var connection = CadAnalysis.Detect(Year, log.Add);
                if (!connection.CanAnalyze)
                {
                    output.WriteLine("【连接失败】" + connection.Message);
                    output.WriteLine("【怎么处理】" + connection.RecommendedAction);
                }
                else
                    output.WriteLine("【连接通过】SOLIDWORKS " + connection.ConnectedYear + " · " + connection.Revision);

                if (referenceIssue != null)
                {
                    output.WriteLine("【标准件检查失败】" + referenceIssue);
                    output.WriteLine("【怎么处理】把机房试用包完整解压到本地目录，保留 reference 目录中的原始标准件。");
                }
                else
                    output.WriteLine("【标准件核对通过】文件与发布版本一致。");

                if (!connection.CanAnalyze || referenceIssue != null)
                {
                    output.WriteLine("【判分自检】等连接和标准件两项通过后再运行。");
                    return Failed;
                }

                var score = CadAnalysis.GradePart3(reference, reference, Year, log.Add);
                output.WriteLine("【判分自检】" + score.Score);
                output.WriteLine("抽壳后 · " + score.Shell.Display);
                output.WriteLine("展开后 · " + score.Flat.Display);
                if (score.Earned == 100 && score.Pending == 0 && score.Shell.Issue == null && score.Flat.Issue == null &&
                    score.Shell.Ratio == 0 && score.Flat.Ratio == 0)
                {
                    output.WriteLine("【全部通过】可以打开学生端，使用该标准件判分。");
                    return 0;
                }

                foreach (var line in score.Lines)
                    if (line.Points != line.Maximum)
                        output.WriteLine("待确认 · " + line.Step + " · " + line.Detail);
                output.WriteLine("【怎么处理】自比有差异或待复核项。保留本报告和 CAD 版本，由开发端核对 CAD 读取结果。");
                return Failed;
            }
            catch (Exception error)
            {
                output.WriteLine("【自检中断】" + CadAnalysis.DescribeError(error));
                output.WriteLine("【怎么处理】用 SOLIDWORKS 2025 确认随包标准件能打开。若仍失败，请保留完整报告。");
                log.Add(error.ToString());
                return Failed;
            }
            finally
            {
                output.WriteLine();
                output.WriteLine("—— 详细记录 ——");
                foreach (string line in log)
                    output.WriteLine(line);
            }
        }

        private static bool IsAdministrator()
        {
            using (var identity = WindowsIdentity.GetCurrent())
                return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
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

using System;
using System.Collections.Generic;
using SWGrade.SolidWorks;

namespace SWGrade.Probe
{
    internal static class GeometryProbeCommand
    {
        internal static int Run(string[] args)
        {
            if (!TryRead(args, out var options, out bool mesh, out var alignment))
            {
                Console.Error.WriteLine("[参数错误] 请提供双份零件、目标 CAD 版本及有效选项。");
                PrintUsage();
                return 2;
            }

            if (Environment.OSVersion.Platform != PlatformID.Win32NT)
            {
                Console.Error.WriteLine("[平台不可用] 实体布尔实验需要 Windows x64 及运行中的 SOLIDWORKS。");
                return 3;
            }

            Console.WriteLine("SWGrade 阶段 A · SOLIDWORKS 实体布尔实验 · " + typeof(Program).Assembly.GetName().Version);
            var result = CadAnalysis.ProbeGeometry(options["--reference"], options["--student"],
                Read(options, "--reference-config"), Read(options, "--student-config"), options["--expect"],
                alignment, mesh, Console.WriteLine, Read(options, "--transform-report"));

            Console.WriteLine("CAD 修订版：{0}；对齐：{1}；状态：{2}",
                result.CadRevision ?? "未取得", result.Alignment, result.AlignmentState);
            if (result.StudentToReferenceTransform != null)
                Console.WriteLine("学生→参考变换：{0}", string.Join(", ", result.StudentToReferenceTransform));
            if (result.PlacementSource != null)
                Console.WriteLine("放置变换来源：{0}；来源 CAD 修订版：{1}",
                    result.PlacementSource.ReportPath, result.PlacementSource.CadRevision);

            var output = Read(options, "--json");
            if (output != null)
                InspectionJson.SaveNew(output, InspectionJson.SerializeGeometryProbe(result));

            if (!result.Measured)
            {
                Console.Error.WriteLine("[待复核] " + result.ReviewReason);
                return 4;
            }

            Console.WriteLine("参考 {0:G17} m³；学生 {1:G17} m³；缺失 {2:G17} m³；多余 {3:G17} m³；重叠 {4:G17} m³。",
                result.ReferenceVolumeM3, result.StudentVolumeM3, result.Missing.VolumeM3, result.Extra.VolumeM3, result.Overlap.VolumeM3);
            Console.WriteLine("差异率：{0:G17}（无量纲）。", result.DifferenceRatio);
            return 0;
        }

        private static string Read(Dictionary<string, string> values, string key) =>
            values.TryGetValue(key, out string value) ? value : null;

        private static bool TryRead(string[] args, out Dictionary<string, string> values, out bool mesh,
            out GeometryProbeAlignment alignment)
        {
            values = new Dictionary<string, string>(StringComparer.Ordinal);
            mesh = false;
            alignment = GeometryProbeAlignment.OriginalCoordinates;
            var keys = new HashSet<string>(StringComparer.Ordinal)
            {
                "--reference", "--student", "--expect", "--reference-config", "--student-config",
                "--alignment", "--transform-report", "--json"
            };

            for (int index = 1; index < args.Length; index++)
            {
                string key = args[index];
                if (key == "--mesh" && !mesh)
                {
                    mesh = true;
                    continue;
                }
                if (!keys.Contains(key) || values.ContainsKey(key) || ++index >= args.Length ||
                    string.IsNullOrWhiteSpace(args[index]) || args[index].StartsWith("--", StringComparison.Ordinal))
                    return false;
                values.Add(key, args[index]);
            }

            string year = Read(values, "--expect");
            bool hasPlacement = values.ContainsKey("--transform-report");
            string mode = Read(values, "--alignment") ?? (hasPlacement ? "reuse" : "original");
            switch (mode)
            {
                case "original": break;
                case "coincident": alignment = GeometryProbeAlignment.CoincidentShape; break;
                case "reuse": alignment = GeometryProbeAlignment.ReusePlacement; break;
                default: return false;
            }
            return (year == "2025" || year == "2026") && (hasPlacement == (alignment == GeometryProbeAlignment.ReusePlacement)) &&
                values.ContainsKey("--reference") && values.ContainsKey("--student") && (!mesh || values.ContainsKey("--json"));
        }

        internal static void PrintUsage()
        {
            Console.WriteLine("SWGrade.Probe.exe --geometry --expect 2025|2026 --reference <标准件.SLDPRT> --student <学生件.SLDPRT>");
            Console.WriteLine("    [--reference-config <配置>] [--student-config <配置>] [--alignment original|coincident|reuse] [--json <结果.json> [--mesh]]");
            Console.WriteLine("    --transform-report <已验证的对齐报告.json> 启用 reuse；变更后的学生件应保留报告中的放置方式。");
        }
    }
}

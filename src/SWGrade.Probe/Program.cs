using System;
using System.Globalization;
using SWGrade.SolidWorks;
using System.Text;

namespace SWGrade.Probe
{
    internal static class Program
    {
        [STAThread]
        private static int Main(string[] args)
        {
            Console.OutputEncoding = new UTF8Encoding(false);
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;

            if (args.Length == 1 && (args[0] == "--help" || args[0] == "-h"))
            {
                PrintUsage();
                return 0;
            }

            if (args.Length == 1 && args[0] == "--lab-check")
                return LabCheckCommand.Run(Console.Out);
            if (args.Length == 1 && args[0] == "--lab-check-admin")
                return LabCheckCommand.RunAsAdministrator();
            if (args.Length == 1 && args[0] == "--lab-check-report")
                return LabCheckCommand.RunToReport();

            if (args.Length > 0 && args[0] == "--geometry")
            {
                try
                {
                    return GeometryProbeCommand.Run(args);
                }
                catch (Exception exception)
                {
                    Console.Error.WriteLine("[几何实验未完成] " + CadAnalysis.DescribeError(exception));
                    return 4;
                }
            }

            if (!TryParse(args, out var model, out var configuration, out var expectedYear, out var jsonPath,
                out var active, out var list))
            {
                Console.Error.WriteLine("[参数错误] 参数缺失、无效或重复。");
                PrintUsage();
                return 2;
            }

            Console.WriteLine("SWGrade 阶段 0 探针 " + typeof(Program).Assembly.GetName().Version);
            if (Environment.OSVersion.Platform != PlatformID.Win32NT)
            {
                Console.Error.WriteLine("[平台不可用] 当前系统为 {0}。SOLIDWORKS COM 需要 Windows x64。",
                    Environment.OSVersion.Platform);
                return 3;
            }

            try
            {
                if (list)
                {
                    var parts = CadAnalysis.ListOpenParts(expectedYear, Console.WriteLine);
                    Console.WriteLine("已打开的可见零件：{0}", parts.Count);
                    foreach (var part in parts)
                        Console.WriteLine("{0} | {1} | {2} | {3}", part.Title, part.Configuration, part.SavedState, part.Path);
                    return 0;
                }

                var result = active
                    ? CadAnalysis.ReadActivePart(configuration, expectedYear, Console.WriteLine)
                    : CadAnalysis.Run(model, configuration, expectedYear, true, Console.WriteLine);
                if (result == null)
                    return 0;
                if (jsonPath != null)
                {
                    InspectionJson.SaveNew(jsonPath, InspectionJson.Serialize(result));
                    Console.WriteLine("分析 JSON 已导出。");
                }
                if (result.VolumeM3 == null || result.SurfaceAreaM2 == null)
                {
                    Console.Error.WriteLine("[待复核] 未取得完整的实体度量。");
                    return 4;
                }
                Console.WriteLine("模型探测完成。");
                return 0;
            }
            catch (Exception exception)
            {
                Console.Error.WriteLine("[探测未完成] " + CadAnalysis.DescribeError(exception));
                return 4;
            }
        }

        private static bool TryParse(string[] args, out string model, out string config, out string year,
            out string jsonPath, out bool active, out bool list)
        {
            model = config = year = jsonPath = null;
            active = list = false;
            for (int index = 0; index < args.Length; index++)
            {
                string option = args[index];
                if (option == "--active" && !active)
                {
                    active = true;
                    continue;
                }
                if (option == "--list-open" && !list)
                {
                    list = true;
                    continue;
                }
                if (index + 1 >= args.Length || string.IsNullOrWhiteSpace(args[index + 1]) ||
                    args[index + 1].StartsWith("--", StringComparison.Ordinal))
                    return false;

                string value = args[++index];
                switch (option)
                {
                    case "--model" when model == null:
                        model = value;
                        break;
                    case "--json" when jsonPath == null:
                        jsonPath = value;
                        break;
                    case "--config" when config == null:
                        config = value;
                        break;
                    case "--expect" when year == null && (value == "2025" || value == "2026"):
                        year = value;
                        break;
                    default:
                        return false;
                }
            }

            if (list)
                return !active && model == null && config == null && jsonPath == null;
            if (active)
                return model == null;

            return ((config == null && jsonPath == null) || model != null) && (model == null || year != null);
        }

        private static void PrintUsage()
        {
            Console.WriteLine("SWGrade.Probe.exe [--expect 2025|2026]");
            Console.WriteLine("SWGrade.Probe.exe --lab-check");
            Console.WriteLine("SWGrade.Probe.exe --lab-check-admin");
            Console.WriteLine("SWGrade.Probe.exe --expect 2025|2026 --model \"C:\\样本\\零件.SLDPRT\" [--config \"配置名\"] [--json \"结果.json\"]");
            Console.WriteLine("SWGrade.Probe.exe --list-open [--expect 2025|2026]");
            Console.WriteLine("SWGrade.Probe.exe --active [--expect 2025|2026] [--config \"配置名\"] [--json \"结果.json\"]");
            GeometryProbeCommand.PrintUsage();
            Console.WriteLine("模型探测使用当前用户会话中唯一已启动的 SOLIDWORKS 实例。");
        }
    }
}

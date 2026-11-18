using System;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using SWGrade.SolidWorks;
using SWGrade.Domain;

namespace SWGrade.Probe
{
    internal static class Program
    {
        private static void Main()
        {
            string folder = Directory.CreateTempSubdirectory("swgrade-checks-").FullName;
            try
            {
                Require(ModelUnits.CubicMetresToCubicCentimetres(0.00025) == 250, "m³ → cm³");
                Require(ModelUnits.CubicMetresToCubicCentimetres(0) == 0, "零值单位换算");

                // Ordinary bytes for file-system checks only. CAD never opens this file.
                string source = Path.Combine(folder, "文件系统测试.SLDPRT");
                byte[] bytes = { 0, 17, 204, 255 };
                File.WriteAllBytes(source, bytes);
                DateTime writeTime = File.GetLastWriteTimeUtc(source);
                string workPath;
                using (var editor = File.Open(source, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite))
                using (var copy = ModelCopy.Create(source))
                {
                    Require(editor.CanWrite, "读取可共享的已打开源文件");
                    workPath = copy.WorkingPath;
                    Require(Path.GetFullPath(source) != workPath && File.Exists(workPath), "独立工作副本");
                    Require(copy.Hash == Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(),
                        "源文件 SHA-256");
                    Require(File.ReadAllBytes(workPath).AsSpan().SequenceEqual(bytes), "副本字节一致");
                    copy.VerifySource();
                }
                Require(!File.Exists(workPath) && !Directory.Exists(Path.GetDirectoryName(workPath)), "清理工作副本");
                Require(File.GetLastWriteTimeUtc(source) == writeTime && File.ReadAllBytes(source).AsSpan().SequenceEqual(bytes),
                    "源文件内容和最后写入时间保留");

                using (var copy = ModelCopy.Create(source))
                {
                    File.WriteAllBytes(source, new byte[] { 1, 2, 3, 4 });
                    Expect<IOException>(copy.VerifySource, "源文件变动被识别");
                }

                Expect<ArgumentException>(() => ModelCopy.Create(Path.Combine(folder, "assembly.SLDASM")), "拒绝装配体");
                Expect<FileNotFoundException>(() => ModelCopy.Create(Path.Combine(folder, "missing.SLDPRT")), "报告缺失文件");
                CheckOpenPartSelection();
                CheckJson(folder);
                Console.WriteLine("云端纯逻辑检查通过。未执行 SOLIDWORKS COM。");
            }
            finally
            {
                Directory.Delete(folder, true);
            }
        }

        private static void Expect<TException>(Action action, string name) where TException : Exception
        {
            try
            {
                action();
            }
            catch (TException)
            {
                Console.WriteLine("PASS " + name);
                return;
            }

            throw new InvalidOperationException("FAIL " + name);
        }

        private static void Require(bool condition, string name)
        {
            if (!condition)
                throw new InvalidOperationException("FAIL " + name);

            Console.WriteLine("PASS " + name);
        }

        private static void CheckOpenPartSelection()
        {
            var unsaved = new OpenPartInfo(10, "零件1", "", "Default", true);
            Require(!unsaved.CanRead && unsaved.SavedState == "尚未保存", "新建未保存零件不可读取");
            var dirty = new OpenPartInfo(10, "零件1", "示例.sldprt", "活动配置", true);
            Require(dirty.CanRead && dirty.SavedState == "CAD 提示需保存", "有磁盘版本的零件保留需保存提示");
            Require(dirty.ConfigurationForRead(null) == "活动配置" &&
                dirty.ConfigurationForRead("指定配置") == "指定配置", "指定配置优先，留空使用打开文档配置");
            var other = new OpenPartInfo(10, "零件2", "示例.step", null, false);
            Require(!other.CanRead, "读取已打开零件需要原生 SLDPRT 磁盘版本");
        }

        private static void CheckJson(string folder)
        {
            // Synthetic values exercise the serializer only.
            var fixture = new ModelInspectionResult
            {
                SourcePath = "脱敏序列化测试.SLDPRT",
                DocumentType = "Part",
                OpenedSuccessfully = true,
                SourceIntegrityVerified = true,
                VolumeM3 = 0.00025,
                FromOpenCadDocument = true,
                OpenCadDocumentNeedsSave = true
            };
            fixture.FeatureCounts.Add("Boss", 2);
            fixture.WarningMessages.Add("无材料");
            string json = InspectionJson.Serialize(fixture);
            using (var doc = JsonDocument.Parse(json))
            {
                var root = doc.RootElement;
                Require(root.GetProperty("fromOpenCadDocument").GetBoolean() &&
                    root.GetProperty("openCadDocumentNeedsSave").GetBoolean(), "JSON 保留已打开零件来源和需保存状态");
                Require(root.GetProperty("schemaVersion").GetInt32() == 1, "JSON 架构版本");
                Require(root.GetProperty("volumeM3").GetDouble() == 0.00025, "JSON 体积保持 SI");
                Require(root.GetProperty("massKg").ValueKind == JsonValueKind.Null, "无材料保留空质量");
                Require(root.GetProperty("featureTypes").GetProperty("Boss").GetInt32() == 2, "JSON 特征类型摘要");
                Require(root.GetProperty("sourcePath").GetString() == fixture.SourcePath, "JSON 中文文件名");
            }

            var comparison = ModelComparison.Evaluate(fixture, fixture, null, null);
            using (var comparisonJson = JsonDocument.Parse(InspectionJson.SerializeComparison(comparison)))
            {
                var root = comparisonJson.RootElement;
                Require(root.GetProperty("reference").GetProperty("volumeM3").GetDouble() == 0.00025 &&
                    root.GetProperty("current").GetProperty("volumeM3").GetDouble() == 0.00025 &&
                    root.GetProperty("checks").GetArrayLength() > 0 && !root.TryGetProperty("score", out _),
                    "双件对照 JSON 保留测量证据和检查项");
            }

            string target = Path.Combine(folder, "分析.json");
            InspectionJson.SaveNew(target, json);
            Require(File.ReadAllText(target) == json, "UTF-8 JSON 导出");
            Expect<IOException>(() => InspectionJson.SaveNew(target, "{}"), "JSON 禁止覆盖已有文件");
            Require(File.ReadAllText(target) == json, "覆盖失败时保留已有文件");
            Expect<ArgumentException>(() => InspectionJson.SaveNew(Path.Combine(folder, "part.SLDPRT"), json),
                "JSON 导出拒绝零件扩展名");
        }
    }
}

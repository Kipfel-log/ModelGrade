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

                CheckDeferredCleanup(source);

                using (var copy = ModelCopy.Create(source))
                {
                    File.WriteAllBytes(source, new byte[] { 1, 2, 3, 4 });
                    Expect<IOException>(copy.VerifySource, "源文件变动被识别");
                }

                Expect<ArgumentException>(() => ModelCopy.Create(Path.Combine(folder, "assembly.SLDASM")), "拒绝装配体");
                Expect<FileNotFoundException>(() => ModelCopy.Create(Path.Combine(folder, "missing.SLDPRT")), "报告缺失文件");
                CheckOpenPartSelection();
                CheckJson(folder);
                CheckGeometryProbeValidation();
                CheckSavedPlacement(folder);
                Console.WriteLine("云端纯逻辑检查通过。未执行 SOLIDWORKS COM。");
            }
            finally
            {
                Directory.Delete(folder, true);
            }
        }

        private static void CheckDeferredCleanup(string source)
        {
            var logs = new System.Collections.Generic.List<string>();
            string leftover;
            string workingPath;
            using (var copy = ModelCopy.Create(source, logs.Add))
            {
                workingPath = copy.WorkingPath;
                leftover = Path.Combine(Path.GetDirectoryName(workingPath), "unrelated.tmp");
                File.WriteAllText(leftover, "test file");
                copy.VerifySource();
            }
            try
            {
                Require(!File.Exists(workingPath) && File.Exists(leftover) && logs.Exists(line => line.Contains("清理已延后")),
                    "目录暂时非空时保留其他文件，不将已验证结果标为失败");
                using (var repeated = ModelCopy.Create(source, logs.Add))
                {
                    Require(repeated.WorkingPath != workingPath, "后续分析使用新副本，不受上一次清理影响");
                    repeated.VerifySource();
                }
            }
            finally
            {
                File.Delete(leftover);
            }
            using (var repeated = ModelCopy.Create(source, logs.Add))
            {
                Require(!Directory.Exists(Path.GetDirectoryName(workingPath)) && logs.Exists(line => line.Contains("已补清理")),
                    "目录可以删除后自动补清理");
            }

            string preserved;
            using (var copy = ModelCopy.Create(source))
            {
                preserved = copy.WorkingPath;
                copy.PreserveForCad();
            }
            try
            {
                using var repeated = ModelCopy.Create(source);
                Require(File.Exists(preserved), "关闭状态不明时保留 CAD 工作副本");
            }
            finally
            {
                File.Delete(preserved);
                Directory.Delete(Path.GetDirectoryName(preserved));
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

        private static void CheckGeometryProbeValidation()
        {
            // Synthetic statistics exercise the decision logic only, not SOLIDWORKS or Boolean geometry.
            GeometryProbeOperation Empty() => new GeometryProbeOperation { State = "EmptyUnconfirmed", ErrorCode = 0 };
            GeometryProbeOperation Bodies(double volume) => new GeometryProbeOperation
                { State = "BodiesMeasured", ErrorCode = 0, VolumeM3 = volume };

            var missing = Empty();
            var extra = Empty();
            var overlap = Bodies(2e-6);
            Require(GeometryProbeValidation.ResolveVolumes(2e-6, 2e-6, missing, extra, overlap) == null &&
                missing.State == "ConfirmedEmpty" && extra.State == "ConfirmedEmpty" && missing.VolumeM3 == 0,
                "诊断逻辑：差集为空需要独立交集证实");

            missing = Empty();
            extra = Empty();
            Require(GeometryProbeValidation.ResolveVolumes(2e-6, 2e-6, missing, extra, Empty()) != null &&
                missing.VolumeM3 == null, "诊断逻辑：三次空结果不能给出零差异");

            var failure = new GeometryProbeOperation { State = "Failed", ErrorCode = 6 };
            Require(GeometryProbeValidation.ResolveVolumes(2e-6, 2e-6, failure, Empty(), overlap) != null &&
                failure.VolumeM3 == null, "诊断逻辑：布尔错误保留待复核");

            var noIntersection = new GeometryProbeOperation { State = "NoIntersectionReported", ErrorCode = 1067 };
            Require(GeometryProbeValidation.ResolveVolumes(2e-6, 3e-6, Bodies(2e-6), Bodies(3e-6), noIntersection) == null &&
                noIntersection.VolumeM3 == 0 && noIntersection.ErrorCode == 1067,
                "诊断逻辑：两次有体积的差集可证实无交集，保留原始状态码");
            Require(GeometryProbeValidation.ResolveVolumes(2e-6, 3e-6, Bodies(1e-6), Bodies(2e-6), Bodies(0.5e-6)) != null,
                "诊断逻辑：不接受不守恒的实体结果");
            extra = Empty();
            Require(GeometryProbeValidation.ResolveVolumes(3e-6, 2e-6, Bodies(1e-6), extra, Bodies(2e-6)) == null &&
                extra.VolumeM3 == 0 && extra.State == "ConfirmedEmpty",
                "诊断逻辑：单侧缺料时用交集证实多余项为空");

            var rotation = new double[] { 0, 1, 0, -1, 0, 0, 0, 0, 1, 0.025, 0, 0, 1, 0, 0, 0 };
            Require(GeometryProbeValidation.IsProperRigidTransform(rotation), "诊断逻辑：允许正交旋转及有限平移");
            rotation[8] = -1;
            Require(!GeometryProbeValidation.IsProperRigidTransform(rotation), "诊断逻辑：拒绝镜像矩阵");
            rotation[8] = 1;
            rotation[12] = 2;
            Require(!GeometryProbeValidation.IsProperRigidTransform(rotation), "诊断逻辑：拒绝尺度变换");

            string json = InspectionJson.SerializeGeometryProbe(new GeometryProbeReport { Missing = failure, ReviewReason = "未核实" });
            using (var output = JsonDocument.Parse(json))
                Require(!output.RootElement.GetProperty("Measured").GetBoolean() &&
                    output.RootElement.GetProperty("Missing").GetProperty("VolumeM3").ValueKind == JsonValueKind.Null &&
                    output.RootElement.GetProperty("DifferenceRatio").ValueKind == JsonValueKind.Null,
                    "实验 JSON 保留失败与空差异数值");
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

        private static void CheckSavedPlacement(string folder)
        {
            // Synthetic report used to test the saved placement rules without COM.
            var matrix = new double[] { 0, 1, 0, 1, 0, 0, 0, 0, -1, -0.1485, 0, 0.105, 1, 0, 0, 0 };
            var calibration = new GeometryProbeReport
            {
                Alignment = GeometryProbeAlignment.CoincidentShape,
                AlignmentState = "CoincidenceVerified",
                Measured = true,
                DifferenceRatio = 0,
                ReferenceVolumeM3 = 2e-6,
                StudentVolumeM3 = 2e-6,
                CadRevision = "34.2.1",
                Missing = new GeometryProbeOperation { VolumeM3 = 0 },
                Extra = new GeometryProbeOperation { VolumeM3 = 0 },
                Overlap = new GeometryProbeOperation { VolumeM3 = 2e-6 },
                StudentToReferenceTransform = matrix,
                Reference = new ModelInspectionResult
                {
                    SourceIntegrityVerified = true, DocumentHash = new string('a', 64), Configuration = "参考"
                },
                Student = new ModelInspectionResult
                {
                    SourceIntegrityVerified = true, DocumentHash = new string('b', 64), Configuration = "学生"
                }
            };

            string reportPath = Path.Combine(folder, "calibration.json");
            InspectionJson.SaveNew(reportPath, InspectionJson.SerializeGeometryProbe(calibration));
            var imported = InspectionJson.ReadGeometryProbe(reportPath);
            var source = GeometryProbeValidation.ReadPlacementSource(reportPath, imported);
            Require(source.ReportPath == reportPath && source.ReferenceHash == calibration.Reference.DocumentHash &&
                source.StudentHash == calibration.Student.DocumentHash && source.CadRevision == calibration.CadRevision &&
                GeometryProbeValidation.SameRigidTransform(matrix, imported.StudentToReferenceTransform),
                "复用对齐：读取报告及放置来源");

            foreach (var snapshot in new[] { imported.Reference, imported.Student })
            {
                snapshot.SolidWorksRevision = imported.CadRevision;
                snapshot.VolumeM3 = 2e-6;
            }
            var bound = imported.EvidenceFor(imported.Reference, imported.Student);
            Require(bound.ValidatedComparison && bound.Coordinates == GeometryCoordinates.Aligned &&
                bound.Overlap.VolumeM3 == 2e-6, "几何证据：仅使用与两侧分析快照一致的结果");
            Require(!imported.EvidenceFor(imported.Reference, imported.Reference).ValidatedComparison,
                "几何证据：拒绝不同的学生文件或配置");

            GeometryProbeValidation.CheckPlacementReference(source, imported.Reference);
            imported.Student.DocumentHash = new string('c', 64);
            GeometryProbeValidation.CheckPlacementStudent(source, imported.Student);
            Require(source.StudentHash != imported.Student.DocumentHash, "复用对齐：允许同配置的编辑后学生件");

            imported.Reference.DocumentHash = new string('d', 64);
            Expect<InvalidOperationException>(() => GeometryProbeValidation.CheckPlacementReference(source, imported.Reference),
                "复用对齐：拒绝变更后的参考件");
            imported.Student.Configuration = "另一配置";
            Expect<InvalidOperationException>(() => GeometryProbeValidation.CheckPlacementStudent(source, imported.Student),
                "复用对齐：拒绝另一个学生配置");

            imported.Measured = false;
            Expect<InvalidOperationException>(() => GeometryProbeValidation.ReadPlacementSource(reportPath, imported),
                "复用对齐：拒绝待复核的源报告");
            imported.Measured = true;
            imported.Overlap.VolumeM3 = 1e-6;
            Expect<InvalidOperationException>(() => GeometryProbeValidation.ReadPlacementSource(reportPath, imported),
                "复用对齐：拒绝无完整重合证据的报告");

            var other = (double[])matrix.Clone();
            other[9] += 0.001;
            Require(!GeometryProbeValidation.SameRigidTransform(matrix, other), "复用对齐：检测重建矩阵的平移差异");
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

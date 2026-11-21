using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using SWGrade.Domain;

namespace SWGrade.SolidWorks
{
    public static partial class CadAnalysis
    {
        public const string Part3ReferenceHash = "a86b4f6304ff5ca2aff405c990b3ad6b620068230f77d2b8a88e4d4dce3e820b";

        public static Part3Report GradePart3(string referencePath, string studentPath, string expectedYear, Action<string> log)
        {
            return WithSession(expectedYear, log, (cad, session) =>
            {
                var probe = new GeometryProbe(cad, session.App);
                Part3Report result = null;
                ModelInspectionResult reference = null;
                ReadSavedCopy(cad, session, referencePath, null, sheetMetal: SheetMetalMode.RequireFlatPattern,
                    recordInspection: model =>
                    {
                        if (model.DocumentHash != Part3ReferenceHash)
                            throw new InvalidOperationException("请选择发布的零件 3 标准件。该文件与本题标准版本不一致。");
                        reference = model;
                    },
                    inspectModel: model =>
                    {
                        var standard = Part3Evidence.Read(cad, probe, model, reference);
                        if (standard.Shell == null || standard.Flat == null || standard.Steps.Any(step => step == null || !step.Healthy))
                            throw new InvalidOperationException("标准件未能完整重建两个阶段或读取所有指定步骤。");
                        ModelInspectionResult student = null;
                        ReadSavedCopy(cad, session, studentPath, null, sheetMetal: SheetMetalMode.RequireFlatPattern,
                            recordInspection: inspection => student = inspection,
                            inspectModel: submitted =>
                            {
                                var evidence = Part3Evidence.Read(cad, probe, submitted, student);
                                if (!evidence.IsExercise(standard))
                                    throw new InvalidOperationException("当前件不符合零件 3 的基础轮廓和特征结构。请选择这道题的原生零件文件。");
                                var shell = Part3Evidence.Compare(probe, standard.Shell, evidence.Shell, GeometryProbeAlignment.OriginalCoordinates, null);
                                var flat = Part3Evidence.Compare(probe, standard.Flat, evidence.Flat, GeometryProbeAlignment.OriginalCoordinates, null);
                                result = new Part3Report
                                {
                                    ModelName = Path.GetFileName(studentPath),
                                    ModelHash = student.DocumentHash,
                                    CreatedUtc = DateTime.UtcNow,
                                    Shell = shell,
                                    Flat = flat,
                                    Lines = evidence.Score(standard, shell, flat)
                                };
                            });
                    });
                return result;
            });
        }
    }

    internal sealed class Part3Evidence
    {
        private const string Doc = "IModelDoc2";
        private const string Feature = "IFeature";
        private static readonly string[] Kinds = { "Extrusion", "Fillet", "Fillet", "Shell", "CutThin", "RefPlane",
            "CutThin", "MirrorPattern", "MirrorPattern", "SheetMetal", "FlattenBends", "ProcessBends", "UnFold", "FlatPattern" };
        internal static readonly string[] Labels = { "草图1", "凸台拉伸", "圆角1", "圆角2", "抽壳",
            "薄壁切除1", "基准面", "薄壁切除2", "镜向1", "镜向2", "钣金转换", "展平折弯", "加工折弯", "展开", "平板型式" };
        private static readonly int[] Points = { 8, 8, 6, 6, 8, 8, 4, 8, 5, 5, 10, 2, 2, 4, 4 };
        internal readonly Step[] Steps = new Step[Labels.Length];
        internal object Shell;
        internal object Flat;

        internal static Part3Evidence Read(CadInterop cad, GeometryProbe probe, object model, ModelInspectionResult read)
        {
            var evidence = new Part3Evidence();
            if (read.SheetMetal?.FlatVolumeM3 > 0)
                evidence.Flat = probe.CaptureSolid(model, allowSheetMetal: true);

            var features = new List<(object Handle, string Type)>();
            for (var feature = cad.Call(Doc, model, "FirstFeature"); feature != null;
                feature = cad.Call(Feature, feature, "GetNextFeature"))
            {
                if (features.Count == 1500)
                    throw new InvalidOperationException("特征树超出本题读取范围。");
                features.Add((feature, (string)cad.Call(Feature, feature, "GetTypeName2")));
            }
            int cursor = 0;
            object shell = null;
            for (int index = 0; index < Kinds.Length; index++)
            {
                var search = features.FindIndex(cursor, item => item.Type == Kinds[index] ||
                    index == 0 && item.Type == "Boss");
                if (search < 0)
                    continue;
                // A later repeated feature must not fill both rubric slots.
                cursor = search + 1;
                var feature = features[search].Handle;
                evidence.Steps[index + 1] = ReadStep(cad, feature);
                if (index == 0)
                    for (var sub = cad.Call(Feature, feature, "GetFirstSubFeature"); sub != null;
                        sub = cad.Call(Feature, sub, "GetNextSubFeature"))
                        if ((string)cad.Call(Feature, sub, "GetTypeName2") == "ProfileFeature")
                        {
                            evidence.Steps[0] = ReadStep(cad, sub);
                            break;
                        }
                if (Kinds[index] == "Shell")
                    shell = feature;
            }
            if (shell != null)
            {
                var manager = cad.Get(Doc, model, "FeatureManager");
                int after = cad.Constant("swMoveRollbackBarTo_e", "swMoveRollbackBarToAfterFeature");
                string name = (string)cad.Get(Feature, shell, "Name");
                if ((bool)cad.Call("IFeatureManager", manager, "EditRollback", after, name) &&
                    (bool)cad.Call(Doc, model, "ForceRebuild3", false) &&
                    !(bool)cad.Call(Feature, shell, "IsRolledBack"))
                {
                    var nextGeometry = features.SkipWhile(item => !ReferenceEquals(item.Handle, shell)).Skip(1)
                        .FirstOrDefault(item => Kinds.Contains(item.Type)).Handle;
                    if (nextGeometry == null || (bool)cad.Call(Feature, nextGeometry, "IsRolledBack"))
                        evidence.Shell = probe.CaptureSolid(model, allowSheetMetal: true);
                }
            }
            return evidence;
        }

        private static Step ReadStep(CadInterop cad, object feature)
        {
            object[] warning = { false };
            int error = (int)cad.Call(Feature, feature, "GetErrorCode2", warning);
            double[] values = null;
            try
            {
                var dimensions = new List<double>();
                var display = cad.Call(Feature, feature, "GetFirstDisplayDimension");
                while (display != null && dimensions.Count < 64)
                {
                    var dimension = cad.Call("IDisplayDimension", display, "GetDimension2", 0);
                    if (dimension == null)
                        break;
                    dimensions.Add((double)cad.Get("IDimension", dimension, "SystemValue"));
                    display = cad.Call(Feature, feature, "GetNextDisplayDimension", display);
                }
                if (display == null && dimensions.All(value => !double.IsNaN(value) && !double.IsInfinity(value)))
                    values = dimensions.OrderBy(value => value).ToArray();
            }
            catch (Exception exception)
            {
                cad.Log("特征尺寸读取待复核：" + CadAnalysis.DescribeError(exception));
            }
            return new Step { Healthy = error == 0 || (bool)warning[0], Values = values };
        }

        internal bool IsExercise(Part3Evidence standard)
        {
            var student = Steps[0]?.Values;
            var reference = standard.Steps[0]?.Values;
            bool similar = student != null && reference?.Length == 2 && student.Length == 2 &&
                student.Zip(reference, (a, b) => a > b * .5 && a < b * 1.5).All(match => match);
            return Steps[1] != null && (similar || Steps.Skip(2).Count(step => step != null) >= 5);
        }

        internal static Part3Difference Compare(GeometryProbe probe, object reference, object student,
            GeometryProbeAlignment mode, double[] placement)
        {
            if (reference == null || student == null)
                return new Part3Difference { Issue = "未能重建并提取此阶段的单实体。" };
            try
            {
                var measured = new GeometryProbeReport { Alignment = mode };
                probe.Compare(measured, reference, student, false, placement, false);
                return new Part3Difference { ExtraMm3 = measured.Extra.VolumeM3.Value * 1e9,
                    MissingMm3 = measured.Missing.VolumeM3.Value * 1e9, Ratio = measured.DifferenceRatio.Value };
            }
            catch (Exception exception)
            {
                return new Part3Difference { Issue = "布尔差集待复核：" + CadAnalysis.DescribeError(exception) };
            }
        }

        internal Part3ScoreLine[] Score(Part3Evidence standard, Part3Difference shell, Part3Difference flat)
        {
            var lines = new List<Part3ScoreLine>();
            for (int i = 0; i < Steps.Length; i++)
            {
                var expected = standard.Steps[i];
                var actual = Steps[i];
                if (i == 0 && expected.Values?.Length == 2 && actual?.Values?.Length == 2)
                {
                    lines.Add(Line(i, "轮廓用于凸台", 3, actual.Healthy ? (int?)3 : null, actual.Healthy ? "草图由首个凸台使用。" : "草图重建待复核。"));
                    for (int dim = 0; dim < 2; dim++)
                    {
                        int maximum = dim == 1 ? 3 : 2;
                        bool valid = Near(expected.Values[dim], actual.Values[dim]);
                        lines.Add(Line(i, dim == 1 ? "主轮廓长边" : "主轮廓短边", maximum, valid ? maximum : 0,
                            $"标准 {expected.Values[dim] * 1000:G7} mm，实测 {actual.Values[dim] * 1000:G7} mm，容差 ±0.05 mm。"));
                    }
                }
                else if (actual == null)
                    lines.Add(Line(i, "步骤", Points[i], 0, "未找到本题顺序中的对应特征。"));
                else if (!actual.Healthy || expected.Values == null || actual.Values == null)
                    lines.Add(Line(i, "步骤", Points[i], null, "特征未能可靠重建或读取。"));
                else if (i == Steps.Length - 1)
                {
                    int? points = Flat != null ? Points[i] : lines.Any(line => line.Points != line.Maximum) ? (int?)null : 0;
                    lines.Add(Line(i, "成功展平", Points[i], points,
                        Flat != null ? "取得展开实体。" : "未能提取展开实体。"));
                }
                else if (actual.Values.Length != expected.Values.Length)
                    lines.Add(Line(i, "参数", Points[i], null, "尺寸数量改变，需要核对特征定义。"));
                else
                {
                    bool pass = expected.Values.Zip(actual.Values, Near).All(match => match);
                    lines.Add(Line(i, "步骤及可读参数", Points[i], pass ? Points[i] : 0,
                        expected.Values.Length == 0 ? "已核对特征顺序与重建状态。" :
                            "标准：" + Format(expected.Values) + "；实测：" + Format(actual.Values) + "（CAD 系统单位）。"));
                }
                if (i == 4)
                    lines.Add(Checkpoint(i, "抽壳后实体偏差", 4, shell, lines));
                if (i == Steps.Length - 1)
                    lines.Add(Checkpoint(i, "展开后实体偏差", 8, flat, lines));
            }
            return lines.ToArray();
        }

        private static Part3ScoreLine Checkpoint(int index, string name, int maximum, Part3Difference difference,
            List<Part3ScoreLine> prior)
        {
            if (difference.Issue != null)
                return Line(index, name, maximum, null, difference.Issue);
            if (difference.Ratio <= .0001)
                return Line(index, name, maximum, maximum, difference.Display);
            if (prior.Any(line => line.Points != line.Maximum))
                return Line(index, name, maximum, null, difference.Display + "，含可能由前序步骤传递的偏差，待确认归因。");
            return Line(index, name, maximum, 0, difference.Display);
        }

        private static bool Near(double a, double b) => Math.Abs(a - b) <= .00005;
        private static string Format(double[] values) => string.Join(" / ", values.Select(value => value.ToString("G7")));
        private static Part3ScoreLine Line(int index, string criterion, int max, int? score, string detail) =>
            new Part3ScoreLine { Step = Labels[index], Criterion = criterion, Maximum = max, Points = score, Detail = detail };

        internal sealed class Step
        {
            internal bool Healthy;
            internal double[] Values;
        }
    }
}

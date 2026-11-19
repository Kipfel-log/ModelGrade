using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using SWGrade.Domain;

namespace SWGrade.SolidWorks
{
    internal sealed class PartProbe
    {
        private const string App = "ISldWorks";
        private const string Model = "IModelDoc2";
        private const string Part = "IPartDoc";
        private const string Extension = "IModelDocExtension";
        private const string Mass = "IMassProperty2";
        private const string Feature = "IFeature";

        private readonly CadInterop cad;
        private readonly object app;
        private readonly ModelInspectionResult result;

        internal PartProbe(CadInterop cad, object app, ModelInspectionResult result)
        {
            this.cad = cad;
            this.app = app;
            this.result = result;
        }

        internal void Read(string path, string requestedConfiguration, SheetMetalMode mode, CadPartPreview preview = null,
            Action<object> inspectModel = null)
        {
            int partType = cad.Constant("swDocumentTypes_e", "swDocPART");
            int options = cad.Constant("swOpenDocOptions_e", "swOpenDocOptions_Silent") |
                          cad.Constant("swOpenDocOptions_e", "swOpenDocOptions_ReadOnly");

            if (cad.Call(App, app, "GetOpenDocumentByName", path) != null)
                throw new InvalidOperationException("该临时副本已打开，无法取得独占关闭权。");
            var dependencies = (Array)cad.Call(App, app, "GetDocumentDependencies2", path, false, false, false);
            if (dependencies != null && dependencies.Length > 0)
                throw new InvalidOperationException("该零件引用其他文档。请选择无外部引用的独立零件。");

            // OpenDoc6 returns the document and fills both integer output flags.
            // https://help.solidworks.com/2025/English/api/sldworksapi/SolidWorks.Interop.sldworks~SolidWorks.Interop.sldworks.ISldWorks~OpenDoc6.html
            object[] args = { path, partType, options, requestedConfiguration ?? "", 0, 0 };
            object model = cad.Call(App, app, "OpenDoc6", args);
            int errors = (int)args[4];
            int warnings = (int)args[5];
            result.OpenWarningCode = warnings;
            int readOnlyWarning = cad.Constant("swFileLoadWarning_e", "swFileLoadWarning_ReadOnly");
            result.ReviewOpenWarningCode = warnings & ~readOnlyWarning;
            if (result.ReviewOpenWarningCode != 0)
                result.WarningMessages.Add("打开警告：" + cad.Flags("swFileLoadWarning_e", result.ReviewOpenWarningCode));

            cad.Log("OpenDoc6 错误：0x{0:X8} {1}", errors,
                errors == 0 ? "无" : cad.Flags("swFileLoadError_e", errors));
            cad.Log("OpenDoc6 警告：0x{0:X8} {1}", warnings,
                warnings == 0 ? "无" : cad.Flags("swFileLoadWarning_e", warnings));
            if (model == null)
                throw new InvalidOperationException("SOLIDWORKS 未能打开零件。请检查文件、版本、引用及 CAD 许可。");

            var openedPath = (string)cad.Call(Model, model, "GetPathName");
            if (!string.Equals(path, openedPath, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("SOLIDWORKS 返回的文档路径与临时副本不同，已停止操作。");

            try
            {
                if (errors != 0)
                    throw new InvalidOperationException("打开零件时有错误，无法确认几何读取可靠。");
                if (!(bool)cad.Call(Model, model, "IsOpenedReadOnly"))
                    throw new InvalidOperationException("SOLIDWORKS 未确认只读模式，停止读取。");
                if ((bool)cad.Call(Model, model, "IsOpenedViewOnly"))
                    throw new InvalidOperationException("SOLIDWORKS 以仅查看模式打开零件，无法可靠读取实体。");
                if (!cad.Interface(Part).IsInstanceOfType(model))
                    throw new InvalidOperationException("打开的文档未提供零件接口。");

                result.OpenedSuccessfully = true;
                result.DocumentType = "Part";
                ReadConfigurationsAndGeometry(model, requestedConfiguration, mode, preview);
                inspectModel?.Invoke(model);
            }
            finally
            {
                CloseOwnedDocument(model, path);
            }
        }

        private void CloseOwnedDocument(object model, string path)
        {
            var opened = cad.Call(App, app, "GetOpenDocumentByName", path);
            if (opened == null)
                return;
            if (!(bool)cad.Get(App, app, "UserControl"))
                throw new InvalidOperationException("SOLIDWORKS 目前无法关闭分析副本。请确认 CAD 主窗口可用后再试。");

            var title = (string)cad.Call(Model, model, "GetTitle");
            if (!ReferenceEquals(opened, model) ||
                !(string.Equals(title, Path.GetFileName(path), StringComparison.OrdinalIgnoreCase) ||
                  string.Equals(title, Path.GetFileNameWithoutExtension(path), StringComparison.OrdinalIgnoreCase)))
                throw new InvalidOperationException("无法确认分析副本的关闭状态。请检查 SOLIDWORKS 是否有等待处理的提示。");

            cad.Call(App, app, "CloseDoc", title);
            if (cad.Call(App, app, "GetOpenDocumentByName", path) != null)
                throw new InvalidOperationException("SOLIDWORKS 仍在占用分析副本。请检查 CAD 是否有等待处理的提示，再重新分析。");

            cad.Log("探针打开的临时文档已关闭。");
        }

        private void ReadConfigurationsAndGeometry(object model, string requestedConfiguration, SheetMetalMode mode, CadPartPreview preview)
        {
            var extension = cad.Get(Model, model, "Extension");
            if ((bool)cad.Call(Extension, extension, "IsFutureVersion"))
                throw new InvalidOperationException("当前 SOLIDWORKS 识别到较新版本的零件，停止模型度量。");

            // Verify the opened configuration before changing the representation of the disposable document.
            string configuration = ReadConfiguration(model, requestedConfiguration);
            object flatPattern = null;
            int configOption = 0;
            if (mode != SheetMetalMode.StandardPart)
            {
                result.SheetMetal = new SheetMetalInspection();
                try
                {
                    var bodies = GetBodies(model, "swSolidBody");
                    var types = EnumerateFeatures(model).Select(feature =>
                        (Feature: feature, Kind: (string)cad.Call(Feature, feature, "GetTypeName2"))).ToArray();
                    var flat = types.Where(item => item.Kind == "FlatPattern").ToArray();
                    configOption = cad.Constant("swInConfigurationOpts_e", "swThisConfiguration");
                    bool sheetBody = bodies != null &&
                        bodies.Cast<object>().Any(body => (bool)cad.Call("IBody2", body, "IsSheetMetal"));
                    bool activeSheetFeature = types.Any(item =>
                        (item.Kind == "SheetMetal" || item.Kind == "SMBaseFlange" || item.Kind == "EdgeFlange" ||
                            item.Kind == "Fold" || item.Kind == "UnFold") &&
                        ReadSuppression(item.Feature, configOption) == false);
                    result.SheetMetal.Detected = sheetBody || activeSheetFeature ||
                        flat.Any(item => ReadSuppression(item.Feature, configOption) == false);
                    if (result.SheetMetal.Detected == true || mode == SheetMetalMode.RequireFlatPattern)
                    {
                        if (flat.Length != 1)
                            result.SheetMetal.Issue = "单实体钣金需要一个可用的平板型式。请核对当前配置的特征树。";
                        else if (bodies?.Length != 1)
                            result.SheetMetal.Issue = "当前展开检查支持单实体钣金。这个配置需要人工复核。";
                        else
                        {
                            if (!(bool)cad.Call(Feature, flat[0].Feature, "IsRolledBack"))
                            {
                                SetFlatState(model, flat[0].Feature, configOption, false, configuration);
                                result.SheetMetal.FoldedVerified = true;
                            }
                            else
                                cad.Log("平板型式处于回滚状态，折弯读数使用保存状态。");
                            flatPattern = flat[0].Feature;
                        }
                    }
                }
                catch (Exception exception)
                {
                    result.SheetMetal.Issue = "未能核实钣金的折弯态。请检查平板型式和重建状态。";
                    cad.Log("钣金识别或折弯失败：" + CadAnalysis.DescribeError(exception));
                }
            }

            ReadModel(model, extension, configuration);
            if (preview != null)
            {
                if (flatPattern != null)
                {
                    var folded = GetBodies(model, "swSolidBody");
                    preview.ReadSheetMesh(cad, folded?.Length == 1 ? folded.GetValue(folded.GetLowerBound(0)) : null, false);
                }
                else
                    preview.ReadMesh(cad, model);
            }

            if (flatPattern == null)
                return;

            try
            {
                RollForwardToFlatPattern(model, flatPattern);
                SetFlatState(model, flatPattern, configOption, true, configuration);
                RequireRebuilt(extension, "展开态");
                object[] warning = { false };
                int featureError = (int)cad.Call(Feature, flatPattern, "GetErrorCode2", warning);
                if (featureError != 0)
                    throw new InvalidOperationException("平板型式报告特征错误：" + cad.EnumName("swFeatureError_e", featureError));
                var bodies = GetBodies(model, "swSolidBody");
                if (bodies?.Length != 1)
                    throw new InvalidOperationException("展开后未取得一个有效实体。");

                object body = bodies.GetValue(bodies.GetLowerBound(0));
                var dimensions = ReadBodyMeasurements(cad, body);
                result.SheetMetal.FlatVolumeM3 = dimensions.Volume;
                result.SheetMetal.FlatSurfaceAreaM2 = dimensions.Area;
                cad.Log("钣金展开态：体积 {0:G17} m³，实体表面积 {1:G17} m²。", dimensions.Volume, dimensions.Area);
                preview?.ReadSheetMesh(cad, body, true);
            }
            catch (Exception exception)
            {
                cad.Log("钣金展开失败：" + CadAnalysis.DescribeError(exception));
                result.SheetMetal.Issue = exception is InvalidOperationException ? exception.Message :
                    "SOLIDWORKS 未能生成可核实的展开实体。";
                var failures = ReadRebuildFailures(model);
                if (failures.Count > 0)
                    result.SheetMetal.Issue += " 重建失败的特征：" + string.Join("、", failures) + "。";
            }
        }

        private void RollForwardToFlatPattern(object model, object feature)
        {
            if (!(bool)cad.Call(Feature, feature, "IsRolledBack"))
                return;

            // EditRollback can report success before the target feature is restored.
            var manager = cad.Get(Model, model, "FeatureManager");
            int end = cad.Constant("swMoveRollbackBarTo_e", "swMoveRollbackBarToEnd");
            bool movedToEnd = (bool)cad.Call("IFeatureManager", manager, "EditRollback", end, "");
            if (!movedToEnd || (bool)cad.Call(Feature, feature, "IsRolledBack"))
            {
                int after = cad.Constant("swMoveRollbackBarTo_e", "swMoveRollbackBarToAfterFeature");
                var name = (string)cad.Get(Feature, feature, "Name");
                if ((bool)cad.Call("IFeatureManager", manager, "EditRollback", after, name))
                    movedToEnd = (bool)cad.Call("IFeatureManager", manager, "EditRollback", end, "");
            }

            bool rebuilt = (bool)cad.Call(Model, model, "ForceRebuild3", false);
            if (!rebuilt && ReferenceEquals(cad.Get(App, app, "ActiveDoc"), model))
                rebuilt = (bool)cad.Call(Model, model, "EditRebuild3");
            if (!movedToEnd || (bool)cad.Call(Feature, feature, "IsRolledBack"))
                throw new InvalidOperationException("自动恢复特征树未完成，平板型式仍处于回滚状态。");
            if (!rebuilt)
                throw new InvalidOperationException("已恢复特征树，但 SOLIDWORKS 未能重建零件。");

            cad.Log("已将只读副本的回滚条移至特征树末尾并重建。");
        }

        private IReadOnlyList<string> ReadRebuildFailures(object model)
        {
            var failures = new List<string>();
            try
            {
                foreach (object feature in EnumerateFeatures(model))
                {
                    object[] warning = { false };
                    int code = (int)cad.Call(Feature, feature, "GetErrorCode2", warning);
                    if (code == 0 || (bool)warning[0])
                        continue;

                    var name = (string)cad.Get(Feature, feature, "Name");
                    cad.Log("重建错误：{0} ({1}, {2})", name, cad.EnumName("swFeatureError_e", code), code);
                    if (failures.Count < 3 && !string.IsNullOrWhiteSpace(name) && !failures.Contains(name))
                        failures.Add(name);
                }
            }
            catch (Exception exception)
            {
                cad.Log("读取特征重建错误失败：" + CadAnalysis.DescribeError(exception));
            }

            return failures;
        }

        private void RequireRebuilt(object extension, string state)
        {
            int rebuild = (int)cad.Get(Extension, extension, "NeedsRebuild2");
            if (rebuild != cad.Constant("swModelRebuildStatus_e", "swModelRebuildStatus_FullyRebuilt"))
                throw new InvalidOperationException(state + "仍有待重建内容：" +
                    cad.EnumName("swModelRebuildStatus_e", rebuild));
        }

        private void SetFlatState(object model, object feature, int configurationOption, bool flat, string configuration)
        {
            ReadConfiguration(model, configuration);
            bool desiredSuppression = !flat;
            bool? suppressed = ReadSuppression(feature, configurationOption);
            if (!suppressed.HasValue)
                throw new InvalidOperationException("SOLIDWORKS 未返回平板型式的压缩状态。");
            if (suppressed.Value == desiredSuppression)
                return;

            // This model has been verified as the uniquely named read-only working copy.
            // https://help.solidworks.com/2023/English/api/sldworksapi/SOLIDWORKS.Interop.sldworks~SOLIDWORKS.Interop.sldworks.IFeature~SetSuppression2.html
            int action = cad.Constant("swFeatureSuppressionAction_e", flat ? "swUnSuppressFeature" : "swSuppressFeature");
            if (!(bool)cad.Call(Feature, feature, "SetSuppression2", action, configurationOption, null) ||
                !(bool)cad.Call(Model, model, "ForceRebuild3", false) ||
                ReadSuppression(feature, configurationOption) != desiredSuppression)
                throw new InvalidOperationException("SOLIDWORKS 未确认平板型式切换及重建。");
            ReadConfiguration(model, configuration);
            cad.Log(flat ? "临时副本已展开（未保存）。" : "临时副本已折弯（未保存）。");
        }

        private string ReadConfiguration(object model, string requestedConfiguration)
        {
            var manager = cad.Get(Model, model, "ConfigurationManager");
            var config = cad.Get("IConfigurationManager", manager, "ActiveConfiguration");
            var name = (string)cad.Get("IConfiguration", config, "Name");
            if (string.IsNullOrWhiteSpace(name) || (requestedConfiguration != null &&
                !string.Equals(requestedConfiguration, name, StringComparison.Ordinal)))
                throw new InvalidOperationException("未打开指定配置。CAD 当前配置：" + name);

            return name;
        }

        private void ReadModel(object model, object extension, string name)
        {
            result.Configuration = name;
            int rebuild = (int)cad.Get(Extension, extension, "NeedsRebuild2");
            result.RebuildStatusCode = rebuild;
            result.RebuildState = cad.EnumName("swModelRebuildStatus_e", rebuild);
            if (rebuild != cad.Constant("swModelRebuildStatus_e", "swModelRebuildStatus_FullyRebuilt"))
                Warn("模型重建状态需复核。");

            cad.Log("文档：Part（只读临时副本）");
            cad.Log("配置：{0}", name);
            cad.Log("读数时的重建状态：{0} ({1})。", result.RebuildState, rebuild);

            var solids = GetBodies(model, "swSolidBody");
            var surfaces = GetBodies(model, "swSheetBody");
            result.SolidBodyCount = solids?.Length ?? 0;
            result.SurfaceBodyCount = surfaces?.Length ?? 0;
            cad.Log("实体数（含隐藏）：{0}；曲面体数（含隐藏）：{1}",
                result.SolidBodyCount, result.SurfaceBodyCount);

            result.SolidFaceCount = ReadFaceCount(solids, "实体面数");
            result.SurfaceFaceCount = ReadFaceCount(surfaces, "曲面体面数");
            ReadMaterial(model, name);

            if (result.SolidBodyCount > 0)
            {
                object documentProperties = null;
                try
                {
                    ReadSolidMeasurements(solids);
                }
                catch (Exception exception)
                {
                    cad.Log("实体度量接口回退：" + CadAnalysis.DescribeError(exception));
                    try
                    {
                        documentProperties = GetDocumentMassProperties(extension, solids);
                        SetSolidMeasurements((double)cad.Get(Mass, documentProperties, "Volume"),
                            (double)cad.Get(Mass, documentProperties, "SurfaceArea"));
                    }
                    catch (Exception fallbackException)
                    {
                        RecordError("体积或实体表面积读取失败", fallbackException);
                    }
                }

                if (result.MaterialName != null && result.SolidBodyCount == 1)
                {
                    try
                    {
                        ReadMassProperties(documentProperties ?? GetDocumentMassProperties(extension, solids));
                    }
                    catch (Exception exception)
                    {
                        RecordError("质量待复核", exception);
                    }
                }
                else if (result.MaterialName != null)
                    Warn("多实体的材料与覆盖状态待复核。");
            }
            else
                Warn("未取得实体。体积、实体表面积和质量待复核。");

            try
            {
                PrintFeatures(model);
            }
            catch (Exception exception)
            {
                result.FeatureTraversalComplete = false;
                RecordError("特征清单未读完整", exception);
            }
        }

        private Array GetBodies(object model, string bodyType)
        {
            return (Array)cad.Call(Part, model, "GetBodies2",
                cad.Constant("swBodyType_e", bodyType), false);
        }

        private int? ReadFaceCount(Array bodies, string label)
        {
            try
            {
                int count = 0;
                if (bodies != null)
                {
                    foreach (object body in bodies)
                    {
                        // https://help.solidworks.com/2018/English/api/sldworksapi/SolidWorks.Interop.sldworks~SolidWorks.Interop.sldworks.IBody2~GetFaceCount.html
                        int faces = (int)cad.Call("IBody2", body, "GetFaceCount");
                        if (faces <= 0)
                            throw new InvalidOperationException("CAD 未返回有效的面数。");
                        count = checked(count + faces);
                    }
                }
                cad.Log("{0}（含隐藏体）：{1}", label, count);
                return count;
            }
            catch (Exception exception)
            {
                RecordError(label + "待复核", exception);
                return null;
            }
        }

        private void ReadSolidMeasurements(Array solids)
        {
            double volume = 0, area = 0;
            foreach (object body in solids)
            {
                var dimensions = ReadBodyMeasurements(cad, body);
                volume += dimensions.Volume;
                area += dimensions.Area;
            }
            SetSolidMeasurements(volume, area);
        }

        internal static (double Volume, double Area) ReadBodyMeasurements(CadInterop cad, object body)
        {
            // IBody2 returns geometric volume and area at indices 3 and 4, in system units.
            // The supplied density affects the returned mass, which is intentionally discarded.
            // https://help.solidworks.com/2024/english/api/sldworksapi/SolidWorks.Interop.sldworks~SolidWorks.Interop.sldworks.IBody2~GetMassProperties.html
            var values = cad.Call("IBody2", body, "GetMassProperties", 1d) as Array;
            if (values?.Rank != 1 || values.Length != 12)
                throw new InvalidOperationException("CAD 未返回完整的实体几何度量。");
            int first = values.GetLowerBound(0);
            if (!(values.GetValue(first + 3) is double bodyVolume) || !IsPositiveFinite(bodyVolume) ||
                !(values.GetValue(first + 4) is double bodyArea) || !IsPositiveFinite(bodyArea))
                throw new InvalidOperationException("CAD 返回了无效的体积或实体表面积。");
            return (bodyVolume, bodyArea);
        }

        private void SetSolidMeasurements(double volume, double area)
        {
            if (!IsPositiveFinite(volume) || !IsPositiveFinite(area))
                throw new InvalidOperationException("CAD 未返回有效的体积与实体表面积。");

            result.VolumeM3 = volume;
            result.SurfaceAreaM2 = area;
            cad.Log("体积：{0:G17} m³ / {1:G17} cm³", volume, ModelUnits.CubicMetresToCubicCentimetres(volume));
            cad.Log("实体表面积：{0:G17} m²", area);
        }

        private object GetDocumentMassProperties(object extension, Array solids)
        {
            object properties = cad.Call(Extension, extension, "CreateMassProperty2");
            cad.Set(Mass, properties, "UseSystemUnits", true);
            cad.Set(Mass, properties, "IncludeHiddenBodiesOrComponents", true);
            cad.Set(Mass, properties, "SelectedItems", solids);
            if (!(bool)cad.Call(Mass, properties, "Recalculate"))
                throw new InvalidOperationException("SOLIDWORKS 无法计算选中实体的质量属性。");
            return properties;
        }

        private void ReadMassProperties(object properties)
        {
            object overrides = cad.Call(Mass, properties, "GetOverrideOptions");
            if (overrides == null || (bool)cad.Get("IMassPropertyOverrideOptions", overrides, "OverrideMass"))
            {
                Warn("质量已覆盖或覆盖状态未确认。");
                return;
            }

            double kilograms = (double)cad.Get(Mass, properties, "Mass");
            double density = (double)cad.Get(Mass, properties, "Density");
            if (!IsPositiveFinite(kilograms) || !IsPositiveFinite(density))
                throw new InvalidOperationException("质量或密度的 CAD 返回值异常。");

            result.MassKg = kilograms;
            result.DensityKgPerM3 = density;
            cad.Log("质量：{0:G17} kg；有效密度：{1:G17} kg/m³", kilograms, density);
        }

        private void ReadMaterial(object model, string config)
        {
            try
            {
                object[] args = { config, "" };
                string material = (string)cad.Call(Part, model, "GetMaterialPropertyName2", args);
                if (string.IsNullOrWhiteSpace(material))
                {
                    Warn("当前配置未读取到零件级材料，质量和密度待复核。");
                    return;
                }

                result.MaterialName = material;
                cad.Log("零件级材料：{0}", material);
            }
            catch (Exception exception)
            {
                RecordError("材料待复核", exception);
            }
        }

        private IEnumerable<object> EnumerateFeatures(object model)
        {
            var visitedTop = new HashSet<object>();
            var visitedSub = new HashSet<object>();
            var yielded = new HashSet<object>();
            var pending = new Stack<(object Item, bool IsSubFeature)>();
            pending.Push((cad.Call(Model, model, "FirstFeature"), false));
            while (pending.Count > 0)
            {
                var node = pending.Pop();
                var visited = node.IsSubFeature ? visitedSub : visitedTop;
                if (node.Item == null || !visited.Add(node.Item))
                    continue;
                if (yielded.Add(node.Item))
                    yield return node.Item;
                pending.Push((cad.Call(Feature, node.Item,
                    node.IsSubFeature ? "GetNextSubFeature" : "GetNextFeature"), node.IsSubFeature));
                pending.Push((cad.Call(Feature, node.Item, "GetFirstSubFeature"), true));
            }
        }

        private void PrintFeatures(object model)
        {
            int current = cad.Constant("swInConfigurationOpts_e", "swThisConfiguration");
            result.FeatureTraversalComplete = true;
            foreach (object feature in EnumerateFeatures(model))
            {
                var kind = (string)cad.Call(Feature, feature, "GetTypeName2");
                if (string.IsNullOrEmpty(kind))
                {
                    result.FeatureTraversalComplete = false;
                    kind = "(未识别)";
                }
                string underlying = kind == "ICE" ? InstantFeatureType(feature) : null;
                CountFeature(feature, current, kind, underlying);
            }

            cad.Log("特征类型摘要（API 原始类型，包含子特征）：");
            foreach (var pair in new SortedDictionary<string, int>(result.FeatureCounts, StringComparer.Ordinal))
                cad.Log("  {0}: {1}", pair.Key, pair.Value);
        }

        private string InstantFeatureType(object feature)
        {
            // IFeature.GetTypeName2 explicitly sends Instant3D features to GetTypeName for the underlying kind.
            // https://help.solidworks.com/2025/English/api/sldworksapi/SolidWorks.Interop.sldworks~SolidWorks.Interop.sldworks.IFeature~GetTypeName2.html
            try
            {
                string kind = (string)cad.Call(Feature, feature, "GetTypeName");
                if (!string.IsNullOrWhiteSpace(kind) && kind != "ICE")
                    return kind;
            }
            catch (Exception exception)
            {
                cad.Log("Instant3D 类型未识别：" + CadAnalysis.DescribeError(exception));
            }
            result.FeatureTraversalComplete = false;
            return null;
        }

        private void CountFeature(object feature, int configuration, string kind, string underlying)
        {
            bool? suppressed = ReadSuppression(feature, configuration);
            // Retain the wrapper name for existing rules and expose the actual operation for new rules.
            RecordFeature(kind, suppressed);
            if (underlying != null)
                RecordFeature(underlying, suppressed);
        }

        private void RecordFeature(string kind, bool? suppressed)
        {
            result.FeatureCounts.TryGetValue(kind, out int count);
            result.FeatureCounts[kind] = count + 1;
            if (suppressed == false)
            {
                result.ActiveFeatureCounts.TryGetValue(kind, out int activeCount);
                result.ActiveFeatureCounts[kind] = activeCount + 1;
            }
            else if (!suppressed.HasValue)
            {
                result.UnverifiedFeatureTypes.Add(kind);
            }
        }

        private bool? ReadSuppression(object feature, int configuration)
        {
            // https://help.solidworks.com/2025/English/api/sldworksapi/SolidWorks.Interop.sldworks~SolidWorks.Interop.sldworks.IFeature~IsSuppressed2.html
            try
            {
                var states = cad.Call(Feature, feature, "IsSuppressed2", configuration, null) as Array;
                if (states?.Length == 1 && states.GetValue(states.GetLowerBound(0)) is bool suppressed)
                    return suppressed;
            }
            catch (Exception)
            {
                // An unverified negative must never be used as a missing-feature deduction.
            }
            return null;
        }

        private void Warn(string message)
        {
            result.WarningMessages.Add(message);
            cad.Log(message);
        }

        private void RecordError(string label, Exception exception)
        {
            string message = "[" + label + "] " + CadAnalysis.DescribeError(exception);
            result.ErrorMessages.Add(message);
            cad.Log(message);
        }

        private static bool IsPositiveFinite(double value)
        {
            return value > 0 && !double.IsInfinity(value);
        }
    }
}

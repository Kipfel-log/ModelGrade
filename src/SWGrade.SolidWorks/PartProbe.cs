using System;
using System.Collections.Generic;
using System.IO;

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

        internal void Read(string path, string requestedConfiguration, Action<object> readPreview = null)
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
                ReadModel(model, requestedConfiguration);
                readPreview?.Invoke(model);
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
                throw new InvalidOperationException("CAD 已进入后台，停止关闭操作。请在 CAD 中手动关闭副本：" + path);

            var title = (string)cad.Call(Model, model, "GetTitle");
            if (!ReferenceEquals(opened, model) ||
                !(string.Equals(title, Path.GetFileName(path), StringComparison.OrdinalIgnoreCase) ||
                  string.Equals(title, Path.GetFileNameWithoutExtension(path), StringComparison.OrdinalIgnoreCase)))
                throw new InvalidOperationException("无法核实临时文档的关闭权，请在 CAD 中手动关闭副本：" + path);

            cad.Call(App, app, "CloseDoc", title);
            if (cad.Call(App, app, "GetOpenDocumentByName", path) != null)
                throw new InvalidOperationException("SOLIDWORKS 中仍可找到临时文档：" + path);

            cad.Log("探针打开的临时文档已关闭。");
        }

        private void ReadModel(object model, string requestedConfiguration)
        {
            var extension = cad.Get(Model, model, "Extension");
            if ((bool)cad.Call(Extension, extension, "IsFutureVersion"))
                throw new InvalidOperationException("当前 SOLIDWORKS 识别到较新版本的零件，停止模型度量。");

            var manager = cad.Get(Model, model, "ConfigurationManager");
            var config = cad.Get("IConfigurationManager", manager, "ActiveConfiguration");
            var name = (string)cad.Get("IConfiguration", config, "Name");
            if (string.IsNullOrWhiteSpace(name) || (requestedConfiguration != null &&
                !string.Equals(requestedConfiguration, name, StringComparison.Ordinal)))
                throw new InvalidOperationException("未打开指定配置。CAD 当前配置：" + name);

            result.Configuration = name;
            int rebuild = (int)cad.Get(Extension, extension, "NeedsRebuild2");
            result.RebuildStatusCode = rebuild;
            result.RebuildState = cad.EnumName("swModelRebuildStatus_e", rebuild);
            if (rebuild != cad.Constant("swModelRebuildStatus_e", "swModelRebuildStatus_FullyRebuilt"))
                Warn("模型重建状态需复核。");

            cad.Log("文档：Part（只读临时副本）");
            cad.Log("配置：{0}", name);
            cad.Log("重建状态：{0} ({1})；探针未主动重建。", result.RebuildState, rebuild);

            var solids = GetBodies(model, "swSolidBody");
            var surfaces = GetBodies(model, "swSheetBody");
            result.SolidBodyCount = solids?.Length ?? 0;
            result.SurfaceBodyCount = surfaces?.Length ?? 0;
            cad.Log("实体数（含隐藏）：{0}；曲面体数（含隐藏）：{1}",
                result.SolidBodyCount, result.SurfaceBodyCount);

            PrintFeatures(model);
            ReadMaterial(model, name);
            if (result.SolidBodyCount == 0)
            {
                Warn("未取得实体。体积、实体表面积和质量待复核。");
                return;
            }

            try
            {
                ReadMeasurements(extension, solids);
            }
            catch (Exception exception)
            {
                RecordError("实体度量待复核", exception);
            }
        }

        private Array GetBodies(object model, string bodyType)
        {
            return (Array)cad.Call(Part, model, "GetBodies2",
                cad.Constant("swBodyType_e", bodyType), false);
        }

        private void ReadMeasurements(object extension, Array solids)
        {
            object properties = cad.Call(Extension, extension, "CreateMassProperty2");
            cad.Set(Mass, properties, "UseSystemUnits", true);
            cad.Set(Mass, properties, "IncludeHiddenBodiesOrComponents", true);
            cad.Set(Mass, properties, "SelectedItems", solids);
            if (!(bool)cad.Call(Mass, properties, "Recalculate"))
                throw new InvalidOperationException("SOLIDWORKS 无法计算选中实体的质量属性。");

            double volume = (double)cad.Get(Mass, properties, "Volume");
            double area = (double)cad.Get(Mass, properties, "SurfaceArea");
            if (!IsPositiveFinite(volume) || !IsPositiveFinite(area))
                throw new InvalidOperationException("CAD 返回了无效体积或实体表面积，需复核。");

            result.VolumeM3 = volume;
            result.SurfaceAreaM2 = area;
            cad.Log("体积：{0:G17} m³ / {1:G17} cm³", volume, ModelUnits.CubicMetresToCubicCentimetres(volume));
            cad.Log("实体表面积：{0:G17} m²", area);
            ReadMass(properties);
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

        private void ReadMass(object properties)
        {
            if (result.MaterialName == null)
                return;
            if (result.SolidBodyCount != 1)
            {
                Warn("多实体的材料与覆盖状态待复核。");
                return;
            }

            try
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
                {
                    Warn("质量和密度的 CAD 返回值异常，待复核。");
                    return;
                }

                result.MassKg = kilograms;
                result.DensityKgPerM3 = density;
                cad.Log("质量：{0:G17} kg；有效密度：{1:G17} kg/m³", kilograms, density);
            }
            catch (Exception exception)
            {
                RecordError("质量待复核", exception);
            }
        }

        private void PrintFeatures(object model)
        {
            var visitedTop = new HashSet<object>();
            var visitedSub = new HashSet<object>();
            var counted = new HashSet<object>();
            int current = cad.Constant("swInConfigurationOpts_e", "swThisConfiguration");
            result.FeatureTraversalComplete = true;
            var pending = new Stack<(object Item, bool IsSubFeature)>();
            pending.Push((cad.Call(Model, model, "FirstFeature"), false));

            while (pending.Count > 0)
            {
                var node = pending.Pop();
                var visited = node.IsSubFeature ? visitedSub : visitedTop;
                if (node.Item == null || !visited.Add(node.Item))
                    continue;

                if (counted.Add(node.Item))
                {
                    var kind = (string)cad.Call(Feature, node.Item, "GetTypeName2");
                    if (string.IsNullOrEmpty(kind))
                    {
                        result.FeatureTraversalComplete = false;
                        kind = "(未识别)";
                    }

                    result.FeatureCounts.TryGetValue(kind, out int count);
                    result.FeatureCounts[kind] = count + 1;
                    RecordSuppression(node.Item, current, kind);
                }

                pending.Push((cad.Call(Feature, node.Item,
                    node.IsSubFeature ? "GetNextSubFeature" : "GetNextFeature"), node.IsSubFeature));
                pending.Push((cad.Call(Feature, node.Item, "GetFirstSubFeature"), true));
            }

            cad.Log("特征类型摘要（API 原始类型，包含子特征）：");
            foreach (var pair in new SortedDictionary<string, int>(result.FeatureCounts, StringComparer.Ordinal))
                cad.Log("  {0}: {1}", pair.Key, pair.Value);
        }

        private void RecordSuppression(object feature, int configuration, string kind)
        {
            // https://help.solidworks.com/2025/English/api/sldworksapi/SolidWorks.Interop.sldworks~SolidWorks.Interop.sldworks.IFeature~IsSuppressed2.html
            try
            {
                var states = cad.Call(Feature, feature, "IsSuppressed2", configuration, null) as Array;
                if (states?.Length == 1 && states.GetValue(states.GetLowerBound(0)) is bool suppressed)
                {
                    if (!suppressed)
                    {
                        result.ActiveFeatureCounts.TryGetValue(kind, out int count);
                        result.ActiveFeatureCounts[kind] = count + 1;
                    }

                    return;
                }
            }
            catch (Exception)
            {
                // An unverified negative must never be used as a missing-feature deduction.
            }

            result.UnverifiedFeatureTypes.Add(kind);
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

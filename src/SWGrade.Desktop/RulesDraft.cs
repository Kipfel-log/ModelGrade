using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
using SWGrade.Application;
using SWGrade.Domain;

namespace SWGrade.Desktop
{
    internal sealed class RulesDraft : INotifyPropertyChanged
    {
        private const string StandardPreset = "标准";
        private const string CustomPreset = "自定义";

        private string preset;
        private string standardPath;
        private string standardConfiguration;
        private string studentConfiguration;
        private string featureTypes;
        private bool checkProcess;
        private bool checkMass;
        private decimal? previousProcessWeight;
        private GradingMode mode;
        private string name;
        private string configurationStatus = "选择标准模型后读取配置。";
        private IReadOnlyList<ConfigurationChoice> configurationChoices = Array.Empty<ConfigurationChoice>();
        private bool confirmed;
        private bool applyingPreset;
        private readonly double originalGeometryTolerance;
        private readonly string originalGeometryText;
        private readonly string originalBands;
        private readonly IReadOnlyList<GeometryBand> savedBands;

        internal RulesDraft() : this(new AssignmentRules { ParametersWeight = 40, FeaturesWeight = 0 }) { }

        private RulesDraft(AssignmentRules rules)
        {
            standardConfiguration = rules.StandardConfiguration;
            studentConfiguration = rules.StudentConfiguration;
            CadYear = rules.ExpectedCadYear ?? "自动识别";
            mode = rules.Mode;
            SheetMetal = rules.SheetMetal;
            GeometryCoordinates = rules.GeometryCoordinates;
            checkProcess = mode == GradingMode.ProcessRequired ||
                (rules.FeaturesWeight > 0 && rules.RequiredFeatureTypes.Count > 0);
            GeometryPolicy = rules.GeometryPolicy == Domain.GeometryPolicy.Bands ? "分档" :
                rules.GeometryPolicy == Domain.GeometryPolicy.TeacherReview ? "教师复核" : "通过 / 不通过";
            Geometry = new RuleNumber("几何", (double)rules.GeometryWeight, 100, "分");
            Parameters = new RuleNumber("参数", (double)rules.ParametersWeight, 100, "分");
            Features = new RuleNumber("特征", (double)rules.FeaturesWeight, 100, "分");
            Volume = new ToleranceDraft("体积", "cm³", ToleranceUnits.CubicCentimetresPerCubicMetre, 100, rules.VolumeTolerance);
            Area = new ToleranceDraft("表面积", "cm²", ToleranceUnits.SquareCentimetresPerSquareMetre, 100, rules.AreaTolerance);
            Mass = new ToleranceDraft("质量", "g", ToleranceUnits.GramsPerKilogram, 100, rules.MassTolerance);
            originalGeometryTolerance = rules.GeometryTolerance;
            originalGeometryText = GeometryTolerancePercent = RuleNumber.Format(ToleranceUnits.ToPercent(originalGeometryTolerance));
            savedBands = rules.GeometryBands ?? new List<GeometryBand>();
            originalBands = Bands = string.Join(Environment.NewLine, savedBands.Select(band =>
                RuleNumber.Format(ToleranceUnits.ToPercent(band.MaximumRatio)) + " " +
                (band.AwardFraction * 100).ToString("0.############################", CultureInfo.InvariantCulture)));
            featureTypes = string.Join(Environment.NewLine, rules.RequiredFeatureTypes);
            FeatureChoices = new[]
            {
                new FeatureChoice(this, "拉伸凸台", "Boss"),
                new FeatureChoice(this, "切除拉伸", "Cut"),
                new FeatureChoice(this, "圆角", "Fillet"),
                new FeatureChoice(this, "倒角", "Chamfer"),
                new FeatureChoice(this, "异型孔", "HoleWzd"),
                new FeatureChoice(this, "钣金基体法兰", "SMBaseFlange"),
                new FeatureChoice(this, "钣金边线法兰", "EdgeFlange")
            };
            RequireSingleSolidBody = rules.RequireSingleSolidBody;
            CheckMass = rules.CheckMass;
            CheckMaterial = rules.CheckMaterial;

            foreach (var weight in new[] { Geometry, Parameters, Features })
                weight.Changed += (_, __) => RefreshWeights();
            foreach (var tolerance in new[] { Volume, Area, Mass })
                tolerance.Relative.Changed += (_, __) => SyncPreset();
            SyncPreset();

        }

        public string Name { get => name; set { name = value; Notify(); } }
        internal string ConfigurationFileHash { get; private set; }
        public string StandardPath
        {
            get => standardPath;
            private set { standardPath = value; Notify(); Notify(nameof(StandardFileName)); }
        }

        public string StandardFileName => System.IO.Path.GetFileName(StandardPath) ?? "还未选择标准件";

        public string StandardConfiguration
        {
            get => standardConfiguration;
            set { standardConfiguration = value; Notify(); Notify(nameof(StandardConfigurationSummary)); Notify(nameof(HasMultipleConfigurations)); }
        }

        public string StandardConfigurationSummary => StandardPath == null ? "" :
            StandardConfiguration != null ? "用于批改的配置：" + StandardConfiguration :
            ConfigurationFileHash == null ? "默认使用文件的活动配置。" : "请选择用于批改的配置。";
        public string StudentConfiguration
        {
            get => studentConfiguration;
            set { studentConfiguration = value; Notify(); Notify(nameof(StudentMatchingSummary)); }
        }

        public string StudentMatchingSummary => string.IsNullOrWhiteSpace(StudentConfiguration)
            ? "优先查找与标准模型同名的配置。没有同名配置时暂停该学生的判分，提示教师选择。"
            : $"优先查找学生模型的「{StudentConfiguration}」配置。找不到时由教师选择。";

        public IReadOnlyList<ConfigurationChoice> ConfigurationChoices
        {
            get => configurationChoices;
            private set { configurationChoices = value; Notify(); Notify(nameof(HasMultipleConfigurations)); }
        }

        public bool HasMultipleConfigurations => ConfigurationChoices.Count > 1 ||
            (ConfigurationChoices.Count == 1 && string.IsNullOrWhiteSpace(StandardConfiguration));

        public string ConfigurationStatus
        {
            get => configurationStatus;
            internal set { configurationStatus = value; Notify(); }
        }

        public string CadYear { get; set; }
        public string ExpectedYear => CadYear == "自动识别" ? null : CadYear;
        public bool CheckProcess
        {
            get => checkProcess;
            set
            {
                if (checkProcess == value)
                    return;
                if (Features.TryValue(out var features) && Parameters.TryValue(out var parameters) &&
                    features >= 0 && parameters >= 0 && parameters + features <= 100)
                {
                    if (value && features == 0 && parameters > 0)
                    {
                        decimal allocation = Math.Min(parameters, previousProcessWeight ?? 15);
                        Parameters.Text = (parameters - allocation).ToString(CultureInfo.InvariantCulture);
                        Features.Text = allocation.ToString(CultureInfo.InvariantCulture);
                    }
                    else if (!value && features > 0)
                    {
                        previousProcessWeight = features;
                        Parameters.Text = (parameters + features).ToString(CultureInfo.InvariantCulture);
                        Features.Text = "0";
                    }
                }
                checkProcess = value;
                mode = value ? GradingMode.ProcessRequired : GradingMode.GeometryFirst;
                Notify();
                RefreshWeights();
            }
        }
        public string GeometryPolicy { get; set; }
        public GeometryCoordinates GeometryCoordinates { get; set; }
        public GeometryCoordinateChoice[] GeometryCoordinateChoices => GeometryCoordinateChoice.Choices;
        public SheetMetalMode SheetMetal { get; set; }
        public RuleNumber Geometry { get; }
        public RuleNumber Parameters { get; }
        public RuleNumber Features { get; }
        public ToleranceDraft Volume { get; }
        public ToleranceDraft Area { get; }
        public ToleranceDraft Mass { get; }
        public string GeometryTolerancePercent { get; set; }
        public string Bands { get; set; }
        public IReadOnlyList<FeatureChoice> FeatureChoices { get; }

        public string FeatureTypes
        {
            get => featureTypes;
            set
            {
                if (featureTypes == value)
                    return;
                featureTypes = value;
                Notify();
                foreach (var choice in FeatureChoices)
                    choice.Refresh();
                RefreshWeights();
            }
        }

        internal bool HasFeature(string key) => FeatureTypeNames().Contains(key, StringComparer.Ordinal);
        internal void SetFeature(string key, bool selected)
        {
            var features = FeatureTypeNames().ToList();
            if (selected)
                features.Add(key);
            else
                features.RemoveAll(existing => existing == key);
            FeatureTypes = string.Join(Environment.NewLine, features);
        }
        public bool RequireSingleSolidBody { get; set; }
        public bool CheckMass
        {
            get => checkMass;
            set { checkMass = value; Notify(); }
        }
        public bool CheckMaterial { get; set; }
        public string[] Presets { get; } = new[] { "严格", StandardPreset, "宽松", CustomPreset };

        public string Preset
        {
            get => preset;
            set
            {
                if (preset == value || value == null)
                    return;
                preset = value;
                if (value != CustomPreset)
                {
                    applyingPreset = true;
                    try
                    {
                        string amount = value == "严格" ? "0.01" : value == "宽松" ? "0.5" : "0.1";
                        Volume.Relative.Text = Area.Relative.Text = Mass.Relative.Text = amount;
                    }
                    finally { applyingPreset = false; }
                }
                Notify();
            }
        }

        public bool ConfirmUnavailableWeights { get => confirmed; set { confirmed = value; Notify(); } }
        private bool GeometryPending => Geometry.TryValue(out var value) && value > 0;
        private bool FeaturesPending => Features.TryValue(out var value) && value > 0 && !FeatureTypeNames().Any();
        public bool HasUnavailableWeights => GeometryPending || FeaturesPending;
        public string SaveLabel => HasUnavailableWeights ? "保存作业并保留待复核分" : "保存作业";

        public string ReviewSummary => HasUnavailableWeights
            ? "无法自动确认的分值会留给老师复核。"
            : "将按此规则评估可读的参数和已选项目。";
        public string WeightNotice
        {
            get
            {
                var messages = new List<string>();
                if (GeometryPending)
                    messages.Add("实体比较或对齐无法验证时，几何分值将留给人工复核。");
                if (FeaturesPending)
                    messages.Add("尚未指定特征类型，特征权重将留给人工复核。");
                return string.Join(" ", messages);
            }
        }

        public string WeightTotal
        {
            get
            {
                if (!Geometry.TryValue(out var a) || !Parameters.TryValue(out var b) || !Features.TryValue(out var c))
                    return "请填写这三项分值。";
                if (new[] { a, b, c }.Any(value => value < 0 || value > 100))
                    return "每项须在 0 至 100 分之间。";
                if (new[] { a, b, c }.Any(value => decimal.Round(value, 2) != value))
                    return "分值最多填写两位小数。";
                decimal total = a + b + c;
                return total == 100 ? "合计 100 分" :
                    $"合计 {total:0.##} / 100 分，{(total < 100 ? "还差" : "超出")} {Math.Abs(100 - total):0.##} 分。";
            }
        }

        public string RedistributeLabel => HasUnavailableWeights ? "将无法自动评分的分值转给参数" : "用参数分补足 100 分";

        internal static RulesDraft FromAssignment(Assignment assignment)
        {
            var draft = new RulesDraft(assignment.Rules) { Name = assignment.Name, StandardPath = assignment.StandardPath };
            if (draft.StandardConfiguration != null)
            {
                draft.ConfigurationChoices = new[] { new ConfigurationChoice(draft.StandardConfiguration, "（已保存）") };
                draft.ConfigurationStatus = "已载入原作业的选择。可重新读取文件配置。";
            }
            return draft;
        }

        internal void ChangeStandard(string path)
        {
            string previousSuggestion = System.IO.Path.GetFileNameWithoutExtension(StandardPath);
            StandardPath = path;
            if (string.IsNullOrWhiteSpace(Name) || Name == previousSuggestion)
                Name = System.IO.Path.GetFileNameWithoutExtension(path);
            Notify(nameof(StandardConfigurationSummary));
            ConfigurationFileHash = null;
            ConfigurationChoices = Array.Empty<ConfigurationChoice>();
            StandardConfiguration = null;
            ConfigurationStatus = "正在读取保存的配置…";
        }

        internal void SetConfigurations(PartConfigurations catalog, string prefer)
        {
            ConfigurationFileHash = catalog.DocumentHash;
            ConfigurationChoices = catalog.Names.Select(name => new ConfigurationChoice(name,
                name == catalog.ActiveName ? "（文件活动配置）" : "")).ToArray();
            StandardConfiguration = catalog.Contains(prefer) ? prefer : catalog.ActiveName;
            if (prefer != null && !catalog.Contains(prefer))
                ConfigurationStatus = $"原选项「{prefer}」已不存在，请核对当前选择。";
            else
                ConfigurationStatus = "此处选中的配置就是批改时使用的标准模型版本。";
        }

        internal AssignmentRules ToRules(bool acceptReview = false)
        {
            if (mode == GradingMode.ProcessRequired && !FeatureTypeNames().Any())
                throw new ArgumentException("请在「还要检查指定建模操作」下至少选一种操作，或关闭此项。");
            if (mode == GradingMode.ProcessRequired && Features.TryValue(out var featureWeight) && featureWeight == 0)
                throw new ArgumentException("请在高级设置为建模操作分配分值，或关闭「还要检查指定建模操作」。");

            if (ConfigurationFileHash != null && !ConfigurationChoices.Any(choice => choice.Name == StandardConfiguration))
                throw new ArgumentException("请选择标准模型用于批改的配置。");
            var policy = GeometryPolicy == "分档" ? Domain.GeometryPolicy.Bands :
                GeometryPolicy == "教师复核" ? Domain.GeometryPolicy.TeacherReview : Domain.GeometryPolicy.PassFail;
            var rules = new AssignmentRules
            {
                StandardConfiguration = Optional(StandardConfiguration),
                StudentConfiguration = Optional(StudentConfiguration),
                ExpectedCadYear = ExpectedYear,
                Mode = mode,
                SheetMetal = SheetMetal,
                GeometryPolicy = policy,
                GeometryCoordinates = GeometryCoordinates,
                GeometryWeight = Geometry.Read(),
                ParametersWeight = Parameters.Read(),
                FeaturesWeight = Features.Read(),
                GeometryTolerance = GeometryTolerancePercent == originalGeometryText ? originalGeometryTolerance :
                    ToleranceUnits.FromPercent(ParseNumber(GeometryTolerancePercent, "几何差异率百分比")),
                VolumeTolerance = Volume.ToTolerance(),
                AreaTolerance = Area.ToTolerance(),
                MassTolerance = Mass.ToTolerance(),
                RequiredFeatureTypes = FeatureTypeNames().ToList(),
                RequireSingleSolidBody = RequireSingleSolidBody,
                CheckMass = CheckMass,
                CheckMaterial = CheckMaterial
            };
            if (policy == Domain.GeometryPolicy.Bands)
                rules.GeometryBands = Bands == originalBands ? savedBands.ToList() : ReadBands();

            rules.Validate();
            if (HasUnavailableWeights && !ConfirmUnavailableWeights && !acceptReview)
                throw new ArgumentException("请确认保留人工复核分值，或先将无法自动评分的分值转给参数。");
            return rules;
        }

        private IEnumerable<string> FeatureTypeNames() => (FeatureTypes ?? "").Split(new[] { '\r', '\n', ',', '，' },
            StringSplitOptions.RemoveEmptyEntries).Select(item => item.Trim()).Where(item => item.Length > 0);

        private List<GeometryBand> ReadBands()
        {
            var result = new List<GeometryBand>();
            foreach (string line in (Bands ?? "").Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var values = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                if (values.Length != 2)
                    throw new ArgumentException("每行填写差异率上限和给分比例，均用百分数。");
                result.Add(new GeometryBand
                {
                    MaximumRatio = ToleranceUnits.FromPercent(ParseNumber(values[0], "差异率上限")),
                    AwardFraction = ParseScore(values[1], "给分比例") / 100
                });
            }
            return result;
        }

        internal bool CanRedistribute() =>
            Geometry.TryValue(out decimal geometry) && Features.TryValue(out decimal features) &&
            geometry >= 0 && features >= 0 && (GeometryPending ? 0 : geometry) + (FeaturesPending ? 0 : features) <= 100;

        internal void Redistribute()
        {
            if (GeometryPending)
                Geometry.Text = "0";
            if (FeaturesPending)
                Features.Text = "0";
            Parameters.Text = (100 - Geometry.Read() - Features.Read()).ToString(CultureInfo.InvariantCulture);
        }

        private void RefreshWeights()
        {
            ConfirmUnavailableWeights = false;
            Notify(nameof(WeightTotal));
            Notify(nameof(HasUnavailableWeights));
            Notify(nameof(WeightNotice));
            Notify(nameof(RedistributeLabel));
            Notify(nameof(SaveLabel));
            Notify(nameof(ReviewSummary));
        }

        private void SyncPreset()
        {
            if (applyingPreset)
                return;
            if (Volume.Relative.TryValue(out var volume) && Area.Relative.TryValue(out var area) &&
                Mass.Relative.TryValue(out var mass) && volume == area && area == mass)
            {
                preset = volume == 0.01m ? "严格" : volume == 0.1m ? StandardPreset :
                    volume == 0.5m ? "宽松" : CustomPreset;
            }
            else
                preset = CustomPreset;
            Notify(nameof(Preset));
        }

        internal static decimal ParseScore(string value, string field)
        {
            if (!decimal.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var score))
                throw new ArgumentException(field + "请填写数字，小数使用点号。");
            return score;
        }

        private static double ParseNumber(string value, string field) => (double)ParseScore(value, field);
        private static string Optional(string value) => string.IsNullOrWhiteSpace(value) ? null : value;

        public event PropertyChangedEventHandler PropertyChanged;
        private void Notify([CallerMemberName] string property = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(property));
    }

    internal sealed class FeatureChoice : INotifyPropertyChanged
    {
        private readonly RulesDraft draft;
        private readonly string key;

        internal FeatureChoice(RulesDraft draft, string label, string key)
        {
            this.draft = draft;
            Label = label;
            this.key = key;
        }

        public string Label { get; }
        public bool Selected
        {
            get => draft.HasFeature(key);
            set
            {
                if (Selected != value)
                    draft.SetFeature(key, value);
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;
        internal void Refresh() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Selected)));
    }

    internal sealed class ConfigurationChoice
    {
        internal ConfigurationChoice(string name, string details)
        {
            Name = name;
            Display = name + details;
        }

        public string Name { get; }
        public string Display { get; }
    }
}

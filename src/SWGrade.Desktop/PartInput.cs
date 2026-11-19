using System;
using System.ComponentModel;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using Microsoft.Win32;
using SWGrade.SolidWorks;
using SWGrade.Domain;

namespace SWGrade.Desktop
{
    internal sealed class PartInput : INotifyPropertyChanged
    {
        private readonly Window owner;
        private readonly AnalysisViewModel analysis;
        private string configuration;

        internal PartInput(Window owner, AnalysisViewModel analysis)
        {
            this.owner = owner;
            this.analysis = analysis;
            BrowseCommand = new RelayCommand(Browse, () => analysis.CanEdit);
            OpenCommand = new RelayCommand(async () => await analysis.SelectAsync(this, false), () => analysis.CanEdit);
            ActiveCommand = new RelayCommand(async () => await analysis.SelectAsync(this, true), () => analysis.CanEdit);
            ClearCommand = new RelayCommand(() => Set(null, null, null), () => analysis.CanEdit && HasPart);
        }

        public ICommand BrowseCommand { get; }
        public ICommand OpenCommand { get; }
        public ICommand ActiveCommand { get; }
        public ICommand ClearCommand { get; }

        public string SourcePath { get; private set; }
        internal OpenPartInfo OpenPart { get; private set; }
        public bool HasPart => SourcePath != null;
        public string FileName => HasPart ? Path.GetFileName(SourcePath) : "尚未选择零件";
        public string SourceDetail => OpenPart == null ? (HasPart ? "磁盘文件" : "") :
            $"CAD 中已打开 · {OpenPart.Configuration ?? "配置未取得"} · {OpenPart.SavedState}";

        public string Configuration
        {
            get => configuration;
            set
            {
                if (configuration == value)
                    return;
                configuration = value;
                analysis.UpdateInputs();
            }
        }

        internal string RequestedConfiguration => string.IsNullOrWhiteSpace(Configuration) ? null : Configuration;

        internal void Set(string path, OpenPartInfo part, string config)
        {
            SourcePath = path;
            OpenPart = part;
            configuration = config;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(string.Empty));
            analysis.UpdateInputs();
        }

        internal Task<CadPartPreview> ReadAsync(string version, Action<string> report, SheetMetalMode sheetMetal)
        {
            string path = SourcePath;
            string config = RequestedConfiguration;
            var part = OpenPart;
            return StaWorker.Run(() => part == null
                ? CadAnalysis.PreviewFile(path, config, version, report, sheetMetal)
                : CadAnalysis.PreviewOpenPart(part, config, version, report, sheetMetal));
        }

        private void Browse()
        {
            var dialog = new OpenFileDialog
            {
                Title = "选择 SOLIDWORKS 零件",
                Filter = "SOLIDWORKS 零件 (*.SLDPRT)|*.SLDPRT",
                CheckFileExists = true
            };
            if (dialog.ShowDialog(owner) == true)
                Set(dialog.FileName, null, null);
        }

        public event PropertyChangedEventHandler PropertyChanged;
    }
}

using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using Microsoft.Win32;
using SWGrade.SolidWorks;

namespace SWGrade.Desktop
{
    internal sealed class ConnectionViewModel : INotifyPropertyChanged
    {
        private const string Auto = "自动识别";
        private readonly Window owner;
        private string version = Auto;
        private string state = "等待检测";
        private bool isBusy;
        private bool connected;
        private CadDetection detection;
        private string checkError;

        internal ConnectionViewModel(Window owner)
        {
            this.owner = owner;
            CheckCommand = new RelayCommand(async () => await CheckAsync(), () => !IsBusy);
            ExportCommand = new RelayCommand(Export, () => !IsBusy && Lines.Count > 0);
        }

        public ObservableCollection<string> Lines { get; } = new ObservableCollection<string>();
        public string[] Versions { get; } = new[] { Auto, "2025", "2026" };
        public ICommand CheckCommand { get; }
        public ICommand ExportCommand { get; }
        public string ExpectedYear => SelectedVersion == Auto ? null : SelectedVersion;

        public string SelectedVersion
        {
            get => version;
            set
            {
                if (version == value)
                    return;
                version = value;
                State = "等待检测";
                detection = null;
                checkError = null;
                NotifyDetection();
                IsConnected = false;
                Lines.Clear();
                Notify();
                CommandManager.InvalidateRequerySuggested();
            }
        }

        public bool IsConnected
        {
            get => connected;
            private set
            {
                connected = value;
                Notify();
                Notify(nameof(ShowConnectionHelp));
            }
        }

        public bool ShowConnectionHelp => !IsConnected && !IsBusy;
        public string DetectedVersion => detection?.ConnectedYear != null ? "SOLIDWORKS " + detection.ConnectedYear :
            !string.IsNullOrWhiteSpace(detection?.Revision) ? "Revision " + detection.Revision :
            detection == null && checkError == null ? "未检测" : "未确认";

        public string InstallationSummary => detection?.InstallationChecked != true ? "安装情况未确认" :
            detection.InstalledYears.Count == 0 ? "本机安装：未确认 2025 / 2026" :
            "本机安装：" + string.Join(" / ", detection.InstalledYears);

        public string EnvironmentSummary => detection?.Windows == null ? "待检测" :
            detection.Windows + (detection.IsX64 ? " · x64" : " · 非 x64");

        public string FrameworkSummary => detection?.Windows == null ? "" :
            detection.HasFramework48 ? ".NET Framework 4.8 或更新版本" : "需 .NET Framework 4.8 或更新版本";

        public string ConnectionHelp => checkError ?? detection?.Message ?? "请用当前账号启动 SOLIDWORKS，然后重新检测。";

        private void NotifyDetection()
        {
            Notify(nameof(DetectedVersion));
            Notify(nameof(InstallationSummary));
            Notify(nameof(EnvironmentSummary));
            Notify(nameof(FrameworkSummary));
            Notify(nameof(ConnectionHelp));
        }

        public string State { get => state; private set { state = value; Notify(); } }
        public bool IsBusy
        {
            get => isBusy;
            private set { isBusy = value; Notify(); Notify(nameof(ShowConnectionHelp)); }
        }

        internal async Task CheckAsync()
        {
            if (IsBusy)
                return;
            IsBusy = true;
            CommandManager.InvalidateRequerySuggested();
            Lines.Clear();
            State = "正在检测…";
            checkError = null;
            string expected = ExpectedYear;
            IProgress<string> progress = new Progress<string>(Lines.Add);
            try
            {
                detection = await StaWorker.Run(() => CadAnalysis.Detect(expected, progress.Report));
                IsConnected = detection.CanAnalyze;
                State = detection.CanAnalyze ? "已连接" : detection.IsComConnected ? "已连接，分析受限" :
                    detection.IsRunning == true ? "已运行，连接失败" : detection.IsRunning == false ? "未运行" : "未连接";
                NotifyDetection();
            }
            catch (Exception exception)
            {
                Lines.Add(CadAnalysis.DescribeError(exception));
                detection = null;
                checkError = "无法完成检测。请在设置中查看详细诊断，再重新检测。";
                IsConnected = false;
                State = "检测失败";
                NotifyDetection();
            }
            finally
            {
                IsBusy = false;
                CommandManager.InvalidateRequerySuggested();
            }
        }

        private void Export()
        {
            try
            {
                var dialog = new SaveFileDialog
                {
                    Title = "导出环境诊断",
                    Filter = "文本文件 (*.txt)|*.txt",
                    DefaultExt = ".txt",
                    AddExtension = true,
                    OverwritePrompt = false,
                    FileName = "SWGrade-环境诊断.txt"
                };
                if (dialog.ShowDialog(owner) != true)
                    return;
                if (!string.Equals(Path.GetExtension(dialog.FileName), ".txt", StringComparison.OrdinalIgnoreCase))
                    throw new ArgumentException("诊断文件名必须以 .txt 结尾。");
                using (var output = File.Open(dialog.FileName, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                using (var writer = new StreamWriter(output, new UTF8Encoding(false)))
                    foreach (var line in Lines)
                        writer.WriteLine(line);
            }
            catch (Exception exception)
            {
                MessageBox.Show(owner, exception.Message, "诊断导出失败", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;
        private void Notify([CallerMemberName] string property = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(property));
    }
}

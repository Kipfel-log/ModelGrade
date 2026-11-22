using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
using System.Windows;
using Microsoft.Win32;
using SWGrade.Classroom;
using SWGrade.Domain;
using SWGrade.SolidWorks;

namespace SWGrade.Desktop
{
    public partial class MainWindow : Window
    {
        private string reference;
        private string student;
        private string cadYear;
        private bool grading;
        private bool sending;
        private Part3Report report;
        private List<string> connectionLog;

        public MainWindow()
        {
            new AppearanceSettings().ApplyPalette();
            InitializeComponent();
            Loaded += Detect;
        }

        private async void Detect(object sender, RoutedEventArgs args)
        {
            if (grading)
                return;
            Loaded -= Detect;
            grading = true;
            UpdateActions();
            ConnectionStatus.Text = "正在检测 SOLIDWORKS…";
            ExportConnectionLogButton.IsEnabled = false;
            var lines = new List<string>();
            try
            {
                var detection = await StaWorker.Run(() => CadAnalysis.Detect(null, lines.Add));
                cadYear = detection.CanAnalyze ? detection.ConnectedYear : null;
                ConnectionStatus.Text = cadYear == null ? detection.Message : "已连接 SOLIDWORKS " + cadYear;
                Status.Text = cadYear == null ? detection.RecommendedAction : "请选择标准件和你的模型。";
            }
            catch (Exception exception)
            {
                string reason = CadAnalysis.DescribeError(exception);
                lines.Add(reason);
                ConnectionStatus.Text = reason;
                Status.Text = "";
                cadYear = null;
            }
            finally
            {
                connectionLog = lines;
                ExportConnectionLogButton.IsEnabled = lines.Count > 0;
                grading = false;
                UpdateActions();
            }
        }

        private void ExportConnectionLog(object sender, RoutedEventArgs args)
        {
            if (connectionLog == null)
                return;
            var dialog = new SaveFileDialog { Filter = "文本文件 (*.txt)|*.txt", FileName = "SWGrade-Compatibility.txt" };
            if (dialog.ShowDialog(this) != true)
                return;
            try
            {
                File.WriteAllLines(dialog.FileName, connectionLog, System.Text.Encoding.UTF8);
                Status.Text = "连接日志已导出。";
            }
            catch (Exception exception) { Status.Text = CadAnalysis.DescribeError(exception); }
        }

        private void UpdateActions()
        {
            ModelForm.IsEnabled = cadYear != null && !grading;
            AnalyzeButton.IsEnabled = ModelForm.IsEnabled && reference != null && student != null;
        }

        private void PickReference(object sender, RoutedEventArgs args)
        {
            var path = SelectFile("选择本次评分的标准件", "SOLIDWORKS 零件 (*.SLDPRT)|*.SLDPRT");
            if (path == null)
                return;
            reference = path;
            ReferenceName.Text = Path.GetFileName(path);
            ClearResult();
        }

        private void PickStudent(object sender, RoutedEventArgs args)
        {
            var path = SelectFile("选择学生模型", "SOLIDWORKS 零件 (*.SLDPRT)|*.SLDPRT");
            if (path != null)
                SetStudent(path);
        }

        private async void UseActive(object sender, RoutedEventArgs args)
        {
            grading = true;
            UpdateActions();
            try
            {
                var part = await StaWorker.Run(() => CadAnalysis.GetActivePart(cadYear, null));
                if (part?.CanRead != true || part.NeedsSave)
                    throw new InvalidOperationException("请先在 SOLIDWORKS 中保存当前零件。");
                SetStudent(part.Path);
            }
            catch (Exception exception) { Status.Text = CadAnalysis.DescribeError(exception); }
            finally { grading = false; UpdateActions(); }
        }

        private void SetStudent(string path)
        {
            student = path;
            StudentModelName.Text = Path.GetFileName(path);
            ClearResult();
        }

        private void ClearResult()
        {
            report = null;
            ResultPanel.Visibility = Visibility.Collapsed;
            Status.Text = "";
            UpdateActions();
        }

        private async void Analyze(object sender, RoutedEventArgs args)
        {
            if (!AnalyzeButton.IsEnabled)
                return;
            grading = true;
            ClearResult();
            Status.Text = "正在提取建模步骤，比较抽壳和展开后的实体…";
            try
            {
                report = await StaWorker.Run(() => CadAnalysis.GradePart3(reference, student, cadYear, null));
                Score.Text = report.Score;
                ShellSummary.Text = "抽壳后 · " + report.Shell.Display;
                FlatSummary.Text = "平板型式后 · " + report.Flat.Display;
                Steps.ItemsSource = report.Lines;
                ResultPanel.Visibility = Visibility.Visible;
                Status.Text = "判分完成。可填写学号和姓名后导出或提交。";
            }
            catch (Exception exception) { Status.Text = CadAnalysis.DescribeError(exception); }
            finally { grading = false; UpdateActions(); }
        }

        private Part3Report Submission()
        {
            if (report == null)
                throw new InvalidOperationException("请先完成判分。");
            report.StudentId = StudentId.Text.Trim();
            report.StudentName = StudentName.Text.Trim();
            if (report.StudentId.Length == 0 || report.StudentName.Length == 0)
                throw new ArgumentException("请先填写学号和姓名。");
            return report;
        }

        private void Export(object sender, RoutedEventArgs args)
        {
            try
            {
                var data = ClassroomFiles.Encode(Submission());
                var dialog = new SaveFileDialog { Filter = "零件 3 成绩 (*.part3.json)|*.part3.json", FileName = "零件3-成绩.part3.json" };
                if (dialog.ShowDialog(this) != true)
                    return;
                File.WriteAllBytes(dialog.FileName, data);
                Status.Text = "成绩文件已导出。";
            }
            catch (Exception exception) { Status.Text = exception.Message; }
        }

        private async void FindTeacher(object sender, RoutedEventArgs args)
        {
            if (sending)
                return;
            sending = true;
            Status.Text = "正在查找同一局域网的教师大屏…";
            try
            {
                var addresses = await ClassroomTransport.FindAsync();
                var typedAddress = TeacherAddress.Text;
                TeacherAddress.ItemsSource = addresses.Select(address => address.ToString()).ToArray();
                if (addresses.Length == 1 && string.IsNullOrWhiteSpace(typedAddress))
                    TeacherAddress.SelectedIndex = 0;
                else
                    TeacherAddress.Text = typedAddress;
                Status.Text = addresses.Length == 0 ? "未发现大屏。可填写教师大屏上显示的 IP，或交成绩文件。" : "请选择教师 IP 并填写课堂码。";
            }
            catch (SocketException) { Status.Text = "自动发现暂不可用。可填写教师大屏上的 IP 和课堂码，或导出成绩文件。"; }
            catch (Exception exception) { Status.Text = exception.Message; }
            finally { sending = false; }
        }

        private async void Submit(object sender, RoutedEventArgs args) => await SendAsync(() => Submission());

        private async void Upload(object sender, RoutedEventArgs args)
        {
            var path = SelectFile("选择已导出的成绩文件", "零件 3 成绩 (*.part3.json)|*.part3.json");
            if (path != null)
                await SendAsync(() => ClassroomFiles.Read(path));
        }

        private async Task SendAsync(Func<Part3Report> read)
        {
            if (sending)
                return;
            sending = true;
            try
            {
                if (!IPAddress.TryParse(TeacherAddress.Text.Trim(), out var address) || address.AddressFamily != AddressFamily.InterNetwork)
                    throw new ArgumentException("请先选择或填写教师大屏的 IPv4 地址。");
                await ClassroomTransport.SendAsync(address, ClassCode.Text.Trim(), read());
                Status.Text = "教师大屏已接收成绩。";
            }
            catch (Exception exception) { Status.Text = "发送失败 · " + exception.Message; }
            finally { sending = false; }
        }

        private string SelectFile(string title, string filter)
        {
            var dialog = new OpenFileDialog { Title = title, Filter = filter, CheckFileExists = true };
            return dialog.ShowDialog(this) == true ? dialog.FileName : null;
        }
    }
}

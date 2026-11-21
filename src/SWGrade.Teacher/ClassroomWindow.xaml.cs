using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Windows;
using System.Windows.Input;
using Microsoft.Win32;
using SWGrade.Classroom;
using SWGrade.Domain;

namespace SWGrade.Teacher
{
    public partial class ClassroomWindow : Window
    {
        private readonly ObservableCollection<ClassroomRow> rows = new ObservableCollection<ClassroomRow>();
        private ClassroomTransport transport;
        private bool fullScreen;

        public ClassroomWindow()
        {
            InitializeComponent();
            Scores.ItemsSource = rows;
            RefreshDashboard();
            Loaded += (sender, args) => StartReception();
            Closed += (sender, args) => transport?.Dispose();
        }

        private void StartReception()
        {
            try
            {
                transport = new ClassroomTransport(report => Dispatcher.Invoke(() => Receive(report)));
                ClassroomCode.Text = "课堂码  " + transport.Code;
                var addresses = NetworkInterface.GetAllNetworkInterfaces()
                    .Where(network => network.OperationalStatus == OperationalStatus.Up &&
                        network.NetworkInterfaceType != NetworkInterfaceType.Loopback)
                    .SelectMany(network => network.GetIPProperties().UnicastAddresses)
                    .Select(entry => entry.Address)
                    .Where(address => address.AddressFamily == AddressFamily.InterNetwork)
                    .Select(address => address.ToString()).Distinct().ToArray();
                Connection.Text = addresses.Length > 0
                    ? "学生可发现大屏，或填写本机地址 " + string.Join("  /  ", addresses)
                    : "学生可在本机发现大屏，离线成绩可通过文件导入。";
            }
            catch (NetworkInformationException)
            {
                Connection.Text = "学生可通过「发现大屏」查找教师端，离线成绩可通过文件导入。";
            }
            catch (SocketException ex)
            {
                ClassroomCode.Text = "文件导入模式";
                Connection.Text = "局域网接收未启动（" + ex.SocketErrorCode + "）。可继续导入成绩文件。";
            }
        }

        private bool Receive(Part3Report report)
        {
            var previous = rows.FirstOrDefault(row =>
                string.Equals(row.Report.StudentId, report.StudentId, StringComparison.OrdinalIgnoreCase));
            if (previous != null)
            {
                if (previous.Report.CreatedUtc >= report.CreatedUtc)
                    return false;
                rows.Remove(previous);
            }
            rows.Insert(0, new ClassroomRow(report));
            RefreshDashboard();
            return true;
        }

        private void RefreshDashboard()
        {
            var stats = new ClassroomStatistics(rows.Select(row => row.Report));
            ReceivedCount.Text = stats.Submitted.ToString();
            Average.Text = stats.Average.HasValue ? stats.Average.Value.ToString("0.#") : "—";
            Best.Text = stats.Best?.ToString() ?? "—";
            Pending.Text = stats.Pending.ToString();
            Distribution.ItemsSource = stats.Distribution;
            Breakdown.ItemsSource = stats.Breakdown;
            DistributionState.Text = stats.Submitted == 0 ? "收到提交后显示分布" : "待复核成绩按当前已得分统计";
            BreakdownState.Text = stats.Breakdown.Length == 0 ? "暂无已确认扣分" : "按涉及人数排序";
            BreakdownState.Visibility = stats.Breakdown.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        private void ImportFiles(object sender, RoutedEventArgs args)
        {
            var picker = new OpenFileDialog { Multiselect = true, Filter = "零件 3 成绩文件 (*.part3.json)|*.part3.json|JSON 文件 (*.json)|*.json" };
            if (picker.ShowDialog(this) != true)
                return;
            var rejected = new List<string>();
            int accepted = 0;
            foreach (var file in picker.FileNames)
                try
                {
                    if (Receive(ClassroomFiles.Read(file)))
                        accepted++;
                }
                catch (Exception)
                {
                    rejected.Add(System.IO.Path.GetFileName(file));
                }
            int skipped = picker.FileNames.Length - accepted - rejected.Count;
            Feedback.Text = $"导入 {accepted} 份，重复或较早 {skipped} 份，无法读取 {rejected.Count} 份。" +
                (rejected.Count == 0 ? "" : "  " + string.Join("、", rejected.Take(4)));
        }

        private void ToggleFullscreen(object sender, RoutedEventArgs args) => SetFullscreen(!fullScreen);

        private void OnKeyDown(object sender, KeyEventArgs args)
        {
            if (args.Key == Key.Escape && fullScreen)
                SetFullscreen(false);
        }

        private void SetFullscreen(bool value)
        {
            fullScreen = value;
            WindowState = WindowState.Normal;
            WindowStyle = value ? WindowStyle.None : WindowStyle.SingleBorderWindow;
            ResizeMode = value ? ResizeMode.NoResize : ResizeMode.CanResize;
            WindowState = WindowState.Maximized;
            FullscreenButton.Content = value ? "退出全屏" : "全屏";
        }

        public sealed class ClassroomRow
        {
            internal ClassroomRow(Part3Report report)
            {
                Report = report;
                Received = DateTime.Now;
                Issues = report.Lines.Where(line => line.Points != line.Maximum).ToArray();
            }

            public Part3Report Report { get; }
            public DateTime Received { get; }
            public Part3ScoreLine[] Issues { get; }
            public int Deductions => 100 - Report.Earned - Report.Pending;
        }
    }
}

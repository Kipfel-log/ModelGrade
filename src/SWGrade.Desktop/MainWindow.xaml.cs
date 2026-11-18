using System;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Wpf.Ui.Controls;

namespace SWGrade.Desktop
{
    public partial class MainWindow : Window
    {
        private const int OverviewIndex = 0;
        private const int AnalysisIndex = 1;
        private const int ManageIndex = 2;
        private const int BatchIndex = 3;
        private const int RecordsIndex = 4;
        private const int SettingsIndex = 5;

        private readonly AppearanceSettings appearance = new AppearanceSettings();
        private readonly ConnectionViewModel connection;
        private readonly AnalysisViewModel analysis;
        private readonly TeacherViewModel teacher;
        private readonly HistoryViewModel history;
        private readonly PageLink[] links;
        private bool applyingNavigation;
        private int activePage = -1;
        private bool? sidebarPreference;
        private bool? sidebarCollapsed;

        public MainWindow()
        {
            appearance.ApplyPalette();
            InitializeComponent();

            var notices = new NotificationCenter();
            Notifications.Connect(notices);
            connection = new ConnectionViewModel(this, notices);
            var workspace = new GradingWorkspace();
            teacher = workspace.Connect(this, notices);
            analysis = new AnalysisViewModel(this, () => connection.ExpectedYear, teacher.Jobs, notices);
            var analyzer = new AnalysisWorkspace { DataContext = analysis };
            history = new HistoryViewModel(teacher, () => GoTo(BatchIndex), notices);

            var overview = new OverviewPage
            {
                DataContext = new OverviewViewModel(connection, teacher, history,
                    () => OpenAnalysis(analysis.Reference.BrowseCommand),
                    OpenStudentImport,
                    () => OpenAnalysis(analysis.AnalyzeCommand),
                    () => GoTo(RecordsIndex),
                    () => GoTo(SettingsIndex))
            };

            var settings = new SettingsPage { DataContext = new SettingsViewModel(appearance, connection, teacher, analysis) };
            var records = new HistoryPage { DataContext = history };
            links = new[]
            {
                new PageLink("概览", SymbolRegular.AppsList24, overview),
                new PageLink("模型分析", SymbolRegular.CubeMultiple24, analyzer),
                new PageLink("作业管理", SymbolRegular.FolderOpen24, workspace),
                new PageLink("批量批改", SymbolRegular.ClipboardTaskListLtr24, workspace),
                new PageLink("批改记录", SymbolRegular.ClipboardClock24, records),
                new PageLink("设置", SymbolRegular.ClipboardSettings24, settings)
            };

            teacher.PropertyChanged += TeacherChanged;
            appearance.Changed += AppearanceChanged;
            SystemParameters.StaticPropertyChanged += SystemAppearanceChanged;
            Pages.ItemsSource = links.Take(SettingsIndex).ToArray();
            Pages.SelectedIndex = OverviewIndex;
            ApplyNavigationWidth();
            Loaded += DetectOnStartup;
        }

        private async void DetectOnStartup(object sender, RoutedEventArgs args)
        {
            Loaded -= DetectOnStartup;
            await connection.CheckAsync(notify: false);
        }

        private void Navigate(object sender, SelectionChangedEventArgs args)
        {
            if (ReferenceEquals(args.Source, Pages) && links != null && Pages.SelectedIndex >= 0)
                ShowPage(Pages.SelectedIndex);
        }

        private void OpenSettings(object sender, RoutedEventArgs args) => GoTo(SettingsIndex);

        private void GoTo(int index)
        {
            Pages.SelectedIndex = index == SettingsIndex ? -1 : index;
            ShowPage(index);
        }

        private void ShowPage(int index)
        {
            if (activePage == index)
                return;
            activePage = index;
            if (index == SettingsIndex)
                SettingsSelection.SetResourceReference(BackgroundProperty, "AccentSoft");
            else
                SettingsSelection.ClearValue(BackgroundProperty);

            applyingNavigation = true;
            try
            {
                if (index == ManageIndex || index == BatchIndex)
                    teacher.WorkspaceTab = index == ManageIndex ? 1 : 0;
                PageContent.Content = links[index].View;
            }
            finally
            {
                applyingNavigation = false;
            }

            if (SystemParameters.ClientAreaAnimation)
            {
                var transition = new DoubleAnimation(0.86, 1, TimeSpan.FromMilliseconds(140))
                {
                    EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
                };
                PageContent.BeginAnimation(OpacityProperty, transition);
            }

            if (index == OverviewIndex || index == RecordsIndex)
                _ = history.RefreshAsync();
        }

        private void TeacherChanged(object sender, PropertyChangedEventArgs args)
        {
            if (args.PropertyName == nameof(TeacherViewModel.IsBusy) && !teacher.IsBusy &&
                (activePage == OverviewIndex || activePage == RecordsIndex))
                _ = history.RefreshAsync();

            if (args.PropertyName != nameof(TeacherViewModel.WorkspaceTab) || applyingNavigation)
                return;
            if (activePage == ManageIndex || activePage == BatchIndex)
                GoTo(teacher.WorkspaceTab == 1 ? ManageIndex : BatchIndex);
        }

        private void OpenAnalysis(System.Windows.Input.ICommand action)
        {
            GoTo(AnalysisIndex);
            if (action.CanExecute(null))
                action.Execute(null);
        }

        private void OpenStudentImport()
        {
            if (teacher.Current == null)
            {
                GoTo(ManageIndex);
                if (teacher.NewDraftCommand.CanExecute(null))
                    teacher.NewDraftCommand.Execute(null);
                return;
            }

            GoTo(BatchIndex);
            if (teacher.ImportFilesCommand.CanExecute(null))
                teacher.ImportFilesCommand.Execute(null);
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            var handle = new WindowInteropHelper(this).Handle;
            ApplyWindowAppearance();
            var source = HwndSource.FromHwnd(handle);
            if (source?.CompositionTarget == null)
                return;

            var workArea = System.Windows.Forms.Screen.FromHandle(handle).WorkingArea;
            var logicalSize = source.CompositionTarget.TransformFromDevice.Transform(new Vector(workArea.Width, workArea.Height));
            Width = Math.Max(MinWidth, Math.Min(Width, logicalSize.X - 12));
            Height = Math.Max(MinHeight, Math.Min(Height, logicalSize.Y - 12));
        }

        private void WindowResized(object sender, SizeChangedEventArgs args) => ApplyNavigationWidth();

        private void ToggleSidebar(object sender, RoutedEventArgs args)
        {
            sidebarPreference = !(sidebarCollapsed ?? false);
            ApplyNavigationWidth();
        }

        private void ApplyNavigationWidth()
        {
            if (NavigationPane == null || SidebarToggle == null || SettingsLabel == null)
                return;

            double width = ActualWidth > 0 ? ActualWidth : Width;
            bool collapse = sidebarPreference ?? width < 1090;
            if (sidebarCollapsed == collapse)
                return;
            sidebarCollapsed = collapse;

            var visibility = collapse ? Visibility.Collapsed : Visibility.Visible;
            Pages.Tag = visibility;
            SidebarToggle.Tag = visibility.ToString();
            SettingsButton.Tag = visibility.ToString();
            SettingsLabel.Visibility = visibility;
            string action = collapse ? "展开导航" : "折叠导航";
            SidebarToggle.ToolTip = action;
            AutomationProperties.SetName(SidebarToggle, action);

            double current = NavigationPane.Width;
            double target = collapse ? 60 : 232;
            NavigationPane.BeginAnimation(WidthProperty, null);
            NavigationPane.Width = target;
            if (IsLoaded && SystemParameters.ClientAreaAnimation)
            {
                NavigationPane.BeginAnimation(WidthProperty,
                    new DoubleAnimation(current, target, TimeSpan.FromMilliseconds(170))
                    {
                        EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut },
                        FillBehavior = FillBehavior.Stop
                    });
            }
        }

        private void AppearanceChanged(object sender, EventArgs args)
        {
            appearance.ApplyPalette();
            ApplyWindowAppearance();
        }

        private void ApplyWindowAppearance()
        {
            var source = HwndSource.FromHwnd(new WindowInteropHelper(this).Handle);
            if (source?.CompositionTarget == null)
                return;

            bool mica = NativeWindowAppearance.Apply(source.Handle,
                appearance.IsDark && !SystemParameters.HighContrast, appearance.CanUseMica());
            if (mica)
            {
                Background = Brushes.Transparent;
                source.CompositionTarget.BackgroundColor = Colors.Transparent;
            }
            else
            {
                SetResourceReference(BackgroundProperty, "Canvas");
                if (Background is SolidColorBrush canvas)
                    source.CompositionTarget.BackgroundColor = canvas.Color;
            }
            NavigationPane.SetResourceReference(BackgroundProperty, mica ? "NavigationMica" : "NavigationSurface");
            appearance.MaterialState = mica ? "原生 Mica 已启用" : "不透明 Fluent 背景";
        }

        private void SystemAppearanceChanged(object sender, PropertyChangedEventArgs args)
        {
            if (args.PropertyName == nameof(SystemParameters.HighContrast))
                Dispatcher.InvokeAsync(() => AppearanceChanged(this, EventArgs.Empty));
        }

        protected override void OnClosing(CancelEventArgs e)
        {
            if (analysis.IsBusy)
            {
                e.Cancel = true;
                analysis.ShowCloseMessage();
            }
            if (connection.IsBusy)
                e.Cancel = true;
            if (teacher.IsBusy)
            {
                e.Cancel = true;
                teacher.ShowCloseMessage();
            }
            base.OnClosing(e);
        }

        protected override void OnClosed(EventArgs e)
        {
            SystemParameters.StaticPropertyChanged -= SystemAppearanceChanged;
            appearance.Changed -= AppearanceChanged;
            teacher.PropertyChanged -= TeacherChanged;
            teacher.Dispose();
            base.OnClosed(e);
        }

        private sealed class PageLink
        {
            internal PageLink(string label, SymbolRegular icon, UserControl view)
            {
                Label = label;
                Icon = icon;
                View = view;
            }

            public string Label { get; }
            public SymbolRegular Icon { get; }
            internal UserControl View { get; }
        }
    }
}

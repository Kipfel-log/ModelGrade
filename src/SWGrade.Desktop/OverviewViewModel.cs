using System;
using System.Windows.Input;

namespace SWGrade.Desktop
{
    internal sealed class OverviewViewModel
    {
        internal OverviewViewModel(ConnectionViewModel connection, TeacherViewModel teacher, HistoryViewModel history,
            Action importReference, Action importStudent, Action analyze, Action showRecords, Action showSettings)
        {
            Connection = connection;
            History = history;
            ImportReferenceCommand = new RelayCommand(importReference, () => !connection.IsBusy);
            ImportStudentCommand = new RelayCommand(importStudent, () => teacher.CanEdit);
            AnalyzeCommand = new RelayCommand(analyze, () => !connection.IsBusy);
            OpenRecordsCommand = new RelayCommand(showRecords, () => true);
            SettingsCommand = new RelayCommand(showSettings, () => true);
        }

        public ConnectionViewModel Connection { get; }
        public HistoryViewModel History { get; }
        public ICommand ImportReferenceCommand { get; }
        public ICommand ImportStudentCommand { get; }
        public ICommand AnalyzeCommand { get; }
        public ICommand OpenRecordsCommand { get; }
        public ICommand SettingsCommand { get; }
    }

    internal sealed class SettingsViewModel
    {
        internal SettingsViewModel(AppearanceSettings appearance, ConnectionViewModel connection, TeacherViewModel teacher,
            AnalysisViewModel analysis)
        {
            Appearance = appearance;
            Connection = connection;
            Analysis = analysis;
            OpenFolderCommand = teacher.OpenFolderCommand;
        }

        public AppearanceSettings Appearance { get; }
        public AnalysisViewModel Analysis { get; }
        public ConnectionViewModel Connection { get; }
        public ICommand OpenFolderCommand { get; }
    }
}

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows.Input;
using SWGrade.Application;
using SWGrade.Infrastructure;

namespace SWGrade.Desktop
{
    internal sealed class HistoryViewModel : INotifyPropertyChanged
    {
        private readonly TeacherViewModel teacher;
        private IReadOnlyList<HistoryEntry> entries = Array.Empty<HistoryEntry>();
        private HistoryEntry selected;
        private bool isLoading;
        private string error;

        internal HistoryViewModel(TeacherViewModel teacher, Action openRecord)
        {
            this.teacher = teacher;
            RefreshCommand = new RelayCommand(async () => await RefreshAsync(), () => !IsLoading);
            OpenCommand = new RelayCommand(() =>
            {
                teacher.OpenHistoryEntry(Selected);
                openRecord();
            }, () => Selected != null && teacher.CanEdit);
        }

        public IReadOnlyList<HistoryEntry> Entries => entries;
        public IEnumerable<HistoryEntry> Recent => Entries.Take(4);
        public HistoryEntry Latest => Entries.FirstOrDefault();
        public bool IsEmpty => Entries.Count == 0 && !IsLoading && string.IsNullOrEmpty(Error);
        public bool HasEntries => Entries.Count > 0;
        public string LatestStatus => Latest?.Status ?? (IsLoading ? "正在读取" : Error == null ? "尚无记录" : "读取失败");
        public string LatestLabel => Latest?.FileName ?? "本机批改记录";

        public HistoryEntry Selected
        {
            get => selected;
            set
            {
                selected = value;
                Notify();
                CommandManager.InvalidateRequerySuggested();
            }
        }

        public bool IsLoading { get => isLoading; private set { isLoading = value; Notify(); } }
        public string Error { get => error; private set { error = value; Notify(); } }
        public bool HasError => !string.IsNullOrEmpty(Error);
        public ICommand RefreshCommand { get; }
        public ICommand OpenCommand { get; }

        internal async Task RefreshAsync()
        {
            if (IsLoading)
                return;
            IsLoading = true;
            Error = null;
            Notify(nameof(IsEmpty));
            Notify(nameof(HasError));
            Notify(nameof(LatestStatus));
            CommandManager.InvalidateRequerySuggested();
            try
            {
                entries = await teacher.ReadHistoryAsync();
                if (Selected != null)
                    Selected = entries.FirstOrDefault(entry => entry.SubmissionId == Selected.SubmissionId);
            }
            catch (Exception exception)
            {
                Error = "读取本地批改记录失败。 " + exception.Message;
            }
            finally
            {
                IsLoading = false;
                Notify(nameof(HasError));
                Notify(nameof(Entries));
                Notify(nameof(HasEntries));
                Notify(nameof(IsEmpty));
                Notify(nameof(Recent));
                Notify(nameof(Latest));
                Notify(nameof(LatestLabel));
                Notify(nameof(LatestStatus));
                CommandManager.InvalidateRequerySuggested();
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;
        private void Notify([CallerMemberName] string property = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(property));
    }

    internal sealed class HistoryEntry
    {
        internal HistoryEntry(Assignment assignment, Submission submission)
        {
            AssignmentId = assignment.Id;
            SubmissionId = submission.Id;
            AssignmentName = assignment.Name;
            FileName = Path.GetFileName(submission.FilePath);
            Status = StateText.Task(submission.State);
            Score = submission.FinalScore.HasValue ? ReportExporter.Score(submission.FinalScore) : "—";
            var attempt = submission.Attempts.Last();
            var review = submission.Reviews.LastOrDefault();
            Timestamp = ReadTime(review?.TimeUtc ?? attempt.FinishedUtc ?? attempt.StartedUtc);
        }

        internal string AssignmentId { get; }
        internal string SubmissionId { get; }
        internal DateTimeOffset Timestamp { get; }
        public string AssignmentName { get; }
        public string FileName { get; }
        public string Status { get; }
        public string Score { get; }
        public string DisplayTime => Timestamp == DateTimeOffset.MinValue ? "—" :
            Timestamp.ToLocalTime().ToString("MM/dd HH:mm", CultureInfo.CurrentCulture);

        private static DateTimeOffset ReadTime(string value) =>
            DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var when)
                ? when : DateTimeOffset.MinValue;
    }
}

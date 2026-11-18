using System;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;

namespace SWGrade.Desktop
{
    internal enum NoticeKind { Information, Warning, Error }

    internal sealed class Notice
    {
        internal Notice(string title, string message, NoticeKind kind, DateTimeOffset createdAt)
        {
            Title = title;
            Message = message;
            Kind = kind;
            CreatedAt = createdAt;
        }

        public string Title { get; }
        public string Message { get; }
        public NoticeKind Kind { get; }
        public string DisplayTime => CreatedAt.ToLocalTime().ToString("HH:mm:ss", CultureInfo.CurrentCulture);
        public string DismissLabel => "关闭通知：" + Title;
        internal DateTimeOffset CreatedAt { get; }
    }

    internal sealed class NotificationCenter
    {
        private static readonly TimeSpan InformationLifetime = TimeSpan.FromSeconds(7);
        private readonly ObservableCollection<Notice> notices = new ObservableCollection<Notice>();
        private readonly Func<DateTimeOffset> clock;

        internal NotificationCenter(Func<DateTimeOffset> clock = null)
        {
            this.clock = clock ?? (() => DateTimeOffset.Now);
            Items = new ReadOnlyObservableCollection<Notice>(notices);
        }

        public ReadOnlyObservableCollection<Notice> Items { get; }

        internal void Publish(string title, string message, NoticeKind kind = NoticeKind.Information)
        {
            if (!string.IsNullOrWhiteSpace(message))
                notices.Add(new Notice(title, message, kind, clock()));
        }

        internal void Dismiss(Notice notice) => notices.Remove(notice);
        internal void Clear() => notices.Clear();

        internal void RemoveExpired()
        {
            DateTimeOffset cutoff = clock() - InformationLifetime;
            foreach (var notice in notices.Where(item =>
                item.Kind == NoticeKind.Information && item.CreatedAt <= cutoff).ToArray())
                notices.Remove(notice);
        }
    }
}

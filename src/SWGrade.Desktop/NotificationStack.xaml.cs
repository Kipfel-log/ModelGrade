using System;
using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace SWGrade.Desktop
{
    public partial class NotificationStack : UserControl
    {
        private readonly DispatcherTimer expiry = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        private NotificationCenter center;
        private bool followLatest = true;

        public NotificationStack()
        {
            InitializeComponent();
            Loaded += (_, __) => expiry.Start();
            Unloaded += (_, __) => expiry.Stop();
            expiry.Tick += (_, __) =>
            {
                if (!Tray.IsExpanded || (!IsMouseOver && !IsKeyboardFocusWithin))
                    center?.RemoveExpired();
            };
        }

        internal void Connect(NotificationCenter notifications)
        {
            center = notifications;
            DataContext = notifications;
            ((INotifyCollectionChanged)notifications.Items).CollectionChanged += OnNoticesChanged;
        }

        private void OnNoticesChanged(object sender, NotifyCollectionChangedEventArgs args)
        {
            if (args.Action != NotifyCollectionChangedAction.Add)
                return;

            followLatest = true;
            Tray.IsExpanded = true;
            NoticeScroll.ScrollToEnd();
        }

        private void KeepLatestVisible(object sender, ScrollChangedEventArgs args)
        {
            if (args.ExtentHeightChange != 0 || args.ViewportHeightChange != 0)
            {
                if (followLatest)
                    NoticeScroll.ScrollToEnd();
            }
            else if (args.VerticalChange != 0)
            {
                followLatest = args.VerticalOffset >= NoticeScroll.ScrollableHeight - 1;
            }
        }

        private void AnimateArrival(object sender, RoutedEventArgs args)
        {
            var card = (FrameworkElement)sender;
            if (!SystemParameters.ClientAreaAnimation)
                return;

            var duration = TimeSpan.FromMilliseconds(190);
            card.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, duration) { FillBehavior = FillBehavior.Stop });
        }

        private void Dismiss(object sender, RoutedEventArgs args)
        {
            if (((Button)sender).DataContext is Notice notice)
                center.Dismiss(notice);
        }

        private void ClearAll(object sender, RoutedEventArgs args) => center.Clear();
    }
}

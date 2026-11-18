using System.Windows;
using System.Windows.Controls;

namespace SWGrade.Desktop
{
    public partial class OverviewPage : UserControl
    {
        public OverviewPage()
        {
            InitializeComponent();
        }

        private void DashboardResized(object sender, SizeChangedEventArgs args)
        {
            if (Metrics == null || Actions == null)
                return;
            double width = args.NewSize.Width;
            Metrics.Columns = width >= 880 ? 4 : width >= 456 ? 2 : 1;
            Actions.Columns = width >= 665 ? 3 : width >= 456 ? 2 : 1;
        }
    }
}

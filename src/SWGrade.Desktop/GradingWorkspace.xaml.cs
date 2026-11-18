using System.Windows;
using System.Windows.Controls;

namespace SWGrade.Desktop
{
    public partial class GradingWorkspace : UserControl
    {
        public GradingWorkspace()
        {
            InitializeComponent();
        }

        internal TeacherViewModel Connect(Window owner)
        {
            var model = new TeacherViewModel(owner);
            DataContext = model;
            return model;
        }
    }
}

using System.Linq;
using System.Windows;
using SWGrade.Domain;

namespace SWGrade.Desktop
{
    public partial class StudentConfigurationPicker : Window
    {
        internal StudentConfigurationPicker(Window owner, PartConfigurations options)
        {
            InitializeComponent();
            Owner = owner;
            Choices.ItemsSource = options.Names.Select(name => new ConfigurationChoice(name,
                name == options.ActiveName ? "（文件活动配置）" : "")).ToArray();
            Choices.SelectedValue = options.ActiveName;
        }

        internal string SelectedConfiguration => Choices.SelectedValue as string;

        private void Accept(object sender, RoutedEventArgs args)
        {
            if (SelectedConfiguration != null)
                DialogResult = true;
        }
    }
}

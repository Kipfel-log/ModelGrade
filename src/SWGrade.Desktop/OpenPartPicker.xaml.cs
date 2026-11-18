using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using SWGrade.SolidWorks;

namespace SWGrade.Desktop
{
    public partial class OpenPartPicker : Wpf.Ui.Controls.FluentWindow
    {
        private OpenPartPicker(Window owner, IReadOnlyList<OpenPartInfo> parts)
        {
            InitializeComponent();
            Owner = owner;
            Parts.ItemsSource = parts;
            Parts.SelectedItem = parts.FirstOrDefault(part => part.CanRead);
        }

        internal static async Task<OpenPartInfo> PickAsync(Window owner, string expectedYear, Action<string> report)
        {
            var parts = await StaWorker.Run(() => CadAnalysis.ListOpenParts(expectedYear, report));
            if (parts.Count == 0)
                throw new InvalidOperationException("未找到可见的已打开 SOLIDWORKS 零件。请先在 CAD 中打开一个零件。");

            var dialog = new OpenPartPicker(owner, parts);
            return dialog.ShowDialog() == true ? dialog.Parts.SelectedItem as OpenPartInfo : null;
        }

        private void Choose(object sender, RoutedEventArgs e)
        {
            if (Parts.SelectedItem is OpenPartInfo part && part.CanRead)
                DialogResult = true;
        }
    }
}

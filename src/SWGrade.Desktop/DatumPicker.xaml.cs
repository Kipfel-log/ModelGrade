using System;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using SWGrade.Domain;
using SWGrade.SolidWorks;

namespace SWGrade.Desktop
{
    public partial class DatumPicker : Window
    {
        private readonly CadPartPreview part;
        private readonly ComboBox[] choices;
        private readonly PlaneChoice[] planes;
        private AssignmentDatum result;

        internal DatumPicker(Window owner, CadPartPreview part, AssignmentDatum previous)
        {
            this.part = part;
            InitializeComponent();
            if (owner?.IsLoaded == true)
                Owner = owner;
            choices = new[] { Primary, Secondary, Tertiary };
            planes = part.DatumPlanes.Select((plane, i) => new PlaneChoice(plane, i + 1)).ToArray();
            foreach (var choice in choices)
            {
                choice.ItemsSource = planes;
                choice.DisplayMemberPath = nameof(PlaneChoice.Label);
            }
            if (previous?.Planes?.Length == 3 && previous.AppliesTo(part.Inspection.DocumentHash, part.Inspection.Configuration))
                for (int i = 0; i < choices.Length; i++)
                    choices[i].SelectedItem = planes.FirstOrDefault(item => item.Plane.SameLocation(previous.Planes[i], 1e-8));
            Preview.DatumPlanePicked += PickPlane;
            Preview.Scene = new AnalysisScene(null, part, SheetMetalMode.StandardPart, GeometryCoordinates.Original);
            UpdateSelection();
        }

        internal static async Task<AssignmentDatum> PickAsync(Window owner, string path, string configuration,
            string year, AssignmentDatum previous)
        {
            var part = await StaWorker.Run(() => CadAnalysis.PreviewDatum(path, configuration, year, null));
            var picker = new DatumPicker(owner, part, previous);
            return picker.ShowDialog() == true ? picker.result : null;
        }

        private void PickPlane(DatumPlane plane)
        {
            var item = planes.First(candidate => ReferenceEquals(plane, candidate.Plane));
            if (choices.Any(choice => ReferenceEquals(choice.SelectedItem, item)))
                return;
            var next = choices.FirstOrDefault(choice => choice.SelectedItem == null);
            if (next != null)
                next.SelectedItem = item;
            else
                Status.Text = "已选择三面，可用上方列表替换任一基准。";
        }

        private void ChangeSelection(object sender, SelectionChangedEventArgs e) => UpdateSelection();

        private void UpdateSelection()
        {
            if (Preview == null || choices == null)
                return;
            Preview.SetDatumHighlights(SelectedPlanes());
            Status.Text = "";
        }

        private DatumPlane[] SelectedPlanes() => choices.Select(choice => (choice.SelectedItem as PlaneChoice)?.Plane).ToArray();

        private void Accept(object sender, RoutedEventArgs e)
        {
            var datum = new AssignmentDatum
            {
                ReferenceHash = part.Inspection.DocumentHash,
                Configuration = part.Inspection.Configuration,
                Planes = SelectedPlanes()
            };
            try { datum.Validate(); }
            catch (ArgumentException exception)
            {
                Status.Text = exception.Message;
                return;
            }
            result = datum;
            DialogResult = true;
        }

        private sealed class PlaneChoice
        {
            internal PlaneChoice(DatumPlane plane, int number)
            {
                Plane = plane;
                Label = $"平面 {number} · {plane.X:0.##}, {plane.Y:0.##}, {plane.Z:0.##} · {plane.OffsetMetres * 1000:0.###} mm";
            }

            internal DatumPlane Plane { get; }
            public string Label { get; }
        }
    }
}

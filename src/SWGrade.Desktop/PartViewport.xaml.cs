using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Media3D;

namespace SWGrade.Desktop
{
    public partial class PartViewport : UserControl
    {
        public static readonly DependencyProperty SceneProperty = DependencyProperty.Register(
            nameof(Scene), typeof(AnalysisScene), typeof(PartViewport),
            new PropertyMetadata(null, SceneChanged));

        private GeometryModel3D referenceModel;
        private GeometryModel3D currentModel;
        private bool showingReference;
        private bool preparing;
        private string prepareIssue;
        private MouseButton? dragButton;
        private Point lastPoint;
        private double yaw;
        private double pitch;
        private double zoom;
        private Vector3D pan;

        public PartViewport()
        {
            InitializeComponent();
            ResetCamera();
            Display();
        }

        public AnalysisScene Scene
        {
            get => (AnalysisScene)GetValue(SceneProperty);
            set => SetValue(SceneProperty, value);
        }

        private static void SceneChanged(DependencyObject target, DependencyPropertyChangedEventArgs args) =>
            ((PartViewport)target).PrepareAsync((AnalysisScene)args.NewValue);

        private async void PrepareAsync(AnalysisScene scene)
        {
            referenceModel = currentModel = null;
            showingReference = false;
            prepareIssue = null;
            preparing = scene != null;
            ResetCamera();
            Display();
            if (scene == null)
                return;

            try
            {
                var models = await Task.Run(() => BuildModels(scene));
                if (ReferenceEquals(scene, Scene))
                {
                    referenceModel = models.Reference;
                    currentModel = models.Current;
                }
            }
            catch (Exception exception)
            {
                if (ReferenceEquals(scene, Scene))
                    prepareIssue = "三维预览不可用。 " + exception.Message;
            }
            finally
            {
                if (ReferenceEquals(scene, Scene))
                {
                    preparing = false;
                    Display();
                }
            }
        }

        private static (GeometryModel3D Reference, GeometryModel3D Current) BuildModels(AnalysisScene scene)
        {
            float[] reference = scene.Reference?.TriangleCoordinates;
            float[] current = scene.Current?.TriangleCoordinates;
            var bounds = Rect3D.Empty;
            IncludeBounds(ref bounds, reference);
            IncludeBounds(ref bounds, current);
            if (bounds.IsEmpty)
                return (null, null);

            // Both parts share one origin offset and one scale for the camera.
            var center = new Point3D(bounds.X + bounds.SizeX / 2,
                bounds.Y + bounds.SizeY / 2, bounds.Z + bounds.SizeZ / 2);
            double extent = Math.Max(bounds.SizeX, Math.Max(bounds.SizeY, bounds.SizeZ));
            if (extent <= 0)
                return (null, null);

            return (
                BuildMesh(reference, center, 2 / extent, Color.FromRgb(43, 130, 117)),
                BuildMesh(current, center, 2 / extent, Color.FromRgb(77, 129, 175)));
        }

        private static void IncludeBounds(ref Rect3D bounds, float[] points)
        {
            if (points == null)
                return;
            for (int i = 0; i < points.Length; i += 3)
                bounds.Union(new Point3D(points[i], points[i + 1], points[i + 2]));
        }

        private static GeometryModel3D BuildMesh(float[] coordinates, Point3D center, double scale, Color color)
        {
            if (coordinates == null)
                return null;
            var points = new Point3DCollection(coordinates.Length / 3);
            var indices = new Int32Collection(coordinates.Length / 3);
            for (int i = 0; i < coordinates.Length; i += 3)
            {
                indices.Add(points.Count);
                points.Add(new Point3D((coordinates[i] - center.X) * scale,
                    (coordinates[i + 1] - center.Y) * scale, (coordinates[i + 2] - center.Z) * scale));
            }
            var mesh = new MeshGeometry3D { Positions = points, TriangleIndices = indices };
            var material = new DiffuseMaterial(new SolidColorBrush(color));
            var model = new GeometryModel3D(mesh, material) { BackMaterial = material };
            model.Freeze();
            return model;
        }

        private void Display()
        {
            if (PartVisual == null)
                return;
            var item = showingReference ? Scene?.Reference : Scene?.Current;
            var geometry = showingReference ? referenceModel : currentModel;
            PartVisual.Content = geometry;
            GeometryNotice.Visibility = Scene?.Reference == null ? Visibility.Collapsed : Visibility.Visible;
            CurrentButton.IsEnabled = Scene?.Current != null && showingReference && !preparing;
            ReferenceButton.IsEnabled = Scene?.Reference != null && !showingReference && !preparing;
            ResetButton.IsEnabled = geometry != null;
            string role = showingReference ? "参考件" : "当前件";
            VisibleName.Text = item == null ? "" : role + " · " + Path.GetFileName(item.Inspection.SourcePath);
            EmptyMessage.Visibility = geometry == null ? Visibility.Visible : Visibility.Collapsed;
            EmptyMessage.Text = preparing ? "正在准备 SOLIDWORKS 三维网格…" :
                prepareIssue ?? item?.PreviewIssue ?? "未取得可显示的模型网格。";
        }

        private void ShowCurrent(object sender, RoutedEventArgs args)
        {
            showingReference = false;
            Display();
        }

        private void ShowReference(object sender, RoutedEventArgs args)
        {
            showingReference = true;
            Display();
        }

        private void ResetView(object sender, RoutedEventArgs args) => ResetCamera();

        private void ResetCamera()
        {
            yaw = 0.7;
            pitch = 0.5;
            zoom = 1;
            pan = new Vector3D();
            UpdateCamera();
        }

        private Vector3D Direction() =>
            new Vector3D(Math.Cos(pitch) * Math.Sin(yaw), Math.Sin(pitch), Math.Cos(pitch) * Math.Cos(yaw));

        private static Vector3D Right(Vector3D direction)
        {
            var right = Vector3D.CrossProduct(new Vector3D(0, 1, 0), direction);
            right.Normalize();
            return right;
        }

        private void UpdateCamera()
        {
            if (ViewCamera == null || Surface == null)
                return;
            var direction = Direction();
            var offset = direction * 8;
            ViewCamera.Position = new Point3D(pan.X + offset.X, pan.Y + offset.Y, pan.Z + offset.Z);
            ViewCamera.LookDirection = -offset;
            ViewCamera.UpDirection = Vector3D.CrossProduct(direction, Right(direction));
            ViewCamera.Width = 4 * Math.Max(1, Surface.ActualWidth / Math.Max(1, Surface.ActualHeight)) / Math.Max(0.2, zoom);
        }

        private void BeginMove(object sender, MouseButtonEventArgs args)
        {
            if (PartVisual.Content == null || (args.ChangedButton != MouseButton.Left && args.ChangedButton != MouseButton.Right))
                return;
            dragButton = args.ChangedButton;
            lastPoint = args.GetPosition(Surface);
            Surface.CaptureMouse();
            args.Handled = true;
        }

        private void Move(object sender, MouseEventArgs args)
        {
            if (!dragButton.HasValue)
                return;
            Point point = args.GetPosition(Surface);
            Vector delta = point - lastPoint;
            lastPoint = point;
            if (dragButton == MouseButton.Left)
            {
                yaw -= delta.X * 0.01;
                pitch = Math.Max(-1.54, Math.Min(1.54, pitch + delta.Y * 0.01));
            }
            else
            {
                var direction = Direction();
                var right = Right(direction);
                var up = Vector3D.CrossProduct(direction, right);
                double unit = ViewCamera.Width / Math.Max(1, Surface.ActualWidth);
                pan += right * (-delta.X * unit) + up * (delta.Y * unit);
            }
            UpdateCamera();
        }

        private void EndMove(object sender, MouseButtonEventArgs args)
        {
            if (dragButton == args.ChangedButton)
                Surface.ReleaseMouseCapture();
        }

        private void CancelMove(object sender, MouseEventArgs args) => dragButton = null;

        private void Zoom(object sender, MouseWheelEventArgs args)
        {
            if (PartVisual.Content == null)
                return;
            zoom = Math.Max(0.2, Math.Min(20, zoom * Math.Pow(1.15, args.Delta / 120d)));
            UpdateCamera();
            args.Handled = true;
        }

        private void Resize(object sender, SizeChangedEventArgs args) => UpdateCamera();
    }
}

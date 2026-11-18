using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using SWGrade.Domain;
using SWGrade.SolidWorks;

namespace SWGrade.Desktop
{
    public partial class PartViewport : UserControl
    {
        public static readonly DependencyProperty SceneProperty = DependencyProperty.Register(
            nameof(Scene), typeof(AnalysisScene), typeof(PartViewport),
            new PropertyMetadata(null, SceneChanged));

        public bool IsFlatPattern { get; set; }

        private GeometryModel3D referenceModel;
        private GeometryModel3D currentModel;
        private Model3D overlayModel;
        private Model3D differencesModel;
        private SurfacePreview preview;
        private ViewMode mode;
        private enum ViewMode { Overlay, Current, Reference }
        private static readonly Color ReferenceColor = Color.FromRgb(200, 100, 35);
        private static readonly Color CurrentColor = Color.FromRgb(46, 108, 182);
        private static readonly Color SharedColor = Color.FromRgb(142, 155, 164);
        private bool preparing;
        private string prepareIssue;
        private MouseButton? dragButton;
        private Point lastPoint;
        private double yaw;
        private double pitch;
        private double zoom;
        private Vector3D pan;
        private Vector3D? flatNormal;

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
            overlayModel = differencesModel = null;
            DifferencesOnly.IsChecked = false;
            preview = null;
            flatNormal = null;
            mode = scene?.Reference == null ? ViewMode.Current : ViewMode.Overlay;
            prepareIssue = null;
            preparing = scene != null;
            ResetCamera();
            Display();
            if (scene == null || (IsFlatPattern && !scene.HasSheetMetal))
            {
                preparing = false;
                Display();
                return;
            }

            try
            {
                bool flat = IsFlatPattern;
                var models = await Task.Run(() => BuildModels(scene, flat));
                if (ReferenceEquals(scene, Scene))
                {
                    referenceModel = models.Reference;
                    currentModel = models.Current;
                    overlayModel = models.Overlay;
                    differencesModel = models.Differences;
                    preview = models.Preview;
                    flatNormal = models.Normal;
                    ResetCamera();
                    if (overlayModel == null)
                        mode = currentModel == null && referenceModel != null ? ViewMode.Reference : ViewMode.Current;
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

        private static (GeometryModel3D Reference, GeometryModel3D Current, Model3D Overlay, Model3D Differences, SurfacePreview Preview,
            Vector3D? Normal) BuildModels(AnalysisScene scene, bool flat)
        {
            var preview = SurfacePreview.Prepare(
                flat ? scene.Reference?.FlatTriangleCoordinates : scene.Reference?.TriangleCoordinates,
                flat ? scene.Current?.FlatTriangleCoordinates : scene.Current?.TriangleCoordinates);
            var bounds = Rect3D.Empty;
            IncludeBounds(ref bounds, preview.Reference);
            IncludeBounds(ref bounds, preview.Current);
            if (bounds.IsEmpty)
                return (null, null, null, null, preview, null);

            // Both parts share one origin offset and one scale for the camera.
            var center = new Point3D(bounds.X + bounds.SizeX / 2,
                bounds.Y + bounds.SizeY / 2, bounds.Z + bounds.SizeZ / 2);
            double extent = Math.Max(bounds.SizeX, Math.Max(bounds.SizeY, bounds.SizeZ));
            if (extent <= 0)
                return (null, null, null, null, preview, null);

            double scale = 2 / extent;
            Model3DGroup overlay = null;
            Model3DGroup differences = null;
            if (preview.Compared)
            {
                var shared = BuildMesh(preview.Shared, center, scale, SharedColor);
                var a = BuildMesh(preview.CurrentOnly, center, scale, CurrentColor);
                var b = BuildMesh(preview.ReferenceOnly, center, scale, ReferenceColor);
                overlay = Group(shared, a, b);
                differences = Group(a, b);
            }
            return (BuildMesh(preview.Reference, center, scale, ReferenceColor),
                BuildMesh(preview.Current, center, scale, CurrentColor), overlay, differences, preview,
                flat ? BroadFaceNormal(preview.Reference.Length > 0 ? preview.Reference : preview.Current) : null);
        }

        private static Vector3D? BroadFaceNormal(float[] coordinates)
        {
            // Orient the camera towards the broad face. Coordinates for comparison remain untouched.
            var best = new Vector3D();
            for (int i = 0; i < coordinates.Length; i += 9)
            {
                var a = new Vector3D((double)coordinates[i + 3] - coordinates[i], (double)coordinates[i + 4] - coordinates[i + 1],
                    (double)coordinates[i + 5] - coordinates[i + 2]);
                var b = new Vector3D((double)coordinates[i + 6] - coordinates[i], (double)coordinates[i + 7] - coordinates[i + 1],
                    (double)coordinates[i + 8] - coordinates[i + 2]);
                var normal = Vector3D.CrossProduct(a, b);
                if (normal.LengthSquared > best.LengthSquared)
                    best = normal;
            }
            if (best.LengthSquared == 0)
                return null;
            if (best.Z < 0 || (best.Z == 0 && (best.Y < 0 || (best.Y == 0 && best.X < 0))))
                best = -best;
            best.Normalize();
            return best;
        }

        private static Model3DGroup Group(params Model3D[] models)
        {
            var group = new Model3DGroup();
            foreach (var model in models)
                if (model != null)
                    group.Children.Add(model);
            if (group.Children.Count == 0)
                return null;
            group.Freeze();
            return group;
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
            if (coordinates == null || coordinates.Length == 0)
                return null;
            var points = new Point3DCollection(coordinates.Length / 3);
            var indices = new Int32Collection(coordinates.Length / 3);
            for (int i = 0; i < coordinates.Length; i += 3)
            {
                indices.Add(points.Count);
                points.Add(new Point3D((coordinates[i] - center.X) * scale,
                    (coordinates[i + 1] - center.Y) * scale, (coordinates[i + 2] - center.Z) * scale));
            }
            var normals = new Vector3DCollection(points.Count);
            for (int i = 0; i < points.Count; i += 3)
            {
                var normal = Vector3D.CrossProduct(points[i + 1] - points[i], points[i + 2] - points[i]);
                normal.Normalize();
                normals.Add(normal);
                normals.Add(normal);
                normals.Add(normal);
            }
            var mesh = new MeshGeometry3D { Positions = points, TriangleIndices = indices, Normals = normals };
            var material = new DiffuseMaterial(new SolidColorBrush(color));
            var model = new GeometryModel3D(mesh, material) { BackMaterial = material };
            model.Freeze();
            return model;
        }

        private void Display()
        {
            if (PartVisual == null)
                return;
            var item = mode == ViewMode.Reference ? Scene?.Reference : Scene?.Current;
            Model3D geometry = mode == ViewMode.Overlay ? (DifferencesOnly.IsChecked == true ? differencesModel : overlayModel) :
                mode == ViewMode.Reference ? referenceModel : currentModel;
            PartVisual.Content = geometry;
            bool pair = Scene?.Reference != null;
            OverlayButton.Visibility = ReferenceButton.Visibility = pair ? Visibility.Visible : Visibility.Collapsed;
            GeometryNotice.Visibility = pair && !preparing ? Visibility.Visible : Visibility.Collapsed;
            Legend.Visibility = mode == ViewMode.Overlay && geometry != null ? Visibility.Visible : Visibility.Collapsed;
            DifferencesOnly.Visibility = mode == ViewMode.Overlay && differencesModel != null ? Visibility.Visible : Visibility.Collapsed;
            OverlayButton.IsEnabled = overlayModel != null && mode != ViewMode.Overlay && !preparing;
            CurrentButton.IsEnabled = currentModel != null && mode != ViewMode.Current && !preparing;
            ReferenceButton.IsEnabled = referenceModel != null && mode != ViewMode.Reference && !preparing;
            ResetButton.IsEnabled = geometry != null;
            ResetButton.Content = IsFlatPattern ? "正视展开" : "重置视角";
            VisibleName.Text = mode == ViewMode.Overlay ? (IsFlatPattern ? "展开叠加 · 使用零件原点与固定面" : "叠加差异 · 按零件原点对齐") :
                item == null ? "" : (mode == ViewMode.Reference ? "参考件" : "当前件") +
                " · " + Path.GetFileName(item.Inspection.SourcePath);
            GeometryNotice.Text = preview?.Compared == true
                ? (preview.CurrentOnly.Length == 0 && preview.ReferenceOnly.Length == 0 && mode == ViewMode.Overlay
                    ? "显示网格未发现明显差异。"
                    : "着色显示采样发现相距超过约 0.02 mm 的三角片。") +
                    " 网格供目视复核。"
                : preview?.Issue ?? "未取得两份网格，无法显示叠加。";
            if (IsFlatPattern && pair && !preparing && (currentModel == null || referenceModel == null))
            {
                string currentIssue = currentModel == null ? "当前件：" + FlatIssue(Scene.Current) : null;
                string referenceIssue = referenceModel == null ? "参考件：" + FlatIssue(Scene.Reference) : null;
                GeometryNotice.Text = currentIssue == null ? referenceIssue : referenceIssue == null ? currentIssue :
                    currentIssue + Environment.NewLine + referenceIssue;
            }
            EmptyMessage.Visibility = geometry == null ? Visibility.Visible : Visibility.Collapsed;
            string meshIssue = IsFlatPattern ? FlatIssue(item)
                : item?.PreviewIssue ?? "未取得可显示的模型网格。";
            EmptyMessage.Text = preparing ? "正在准备模型和叠加差异…" : prepareIssue ?? meshIssue;
        }

        private static string FlatIssue(CadPartPreview item) =>
            item?.FlatPreviewIssue ?? item?.Inspection.SheetMetal?.Issue ?? "未读取到钣金展开网格。";

        private void ToggleShared(object sender, RoutedEventArgs args) => Display();

        private void ShowOverlay(object sender, RoutedEventArgs args) => Show(ViewMode.Overlay);
        private void ShowCurrent(object sender, RoutedEventArgs args) => Show(ViewMode.Current);
        private void ShowReference(object sender, RoutedEventArgs args) => Show(ViewMode.Reference);

        private void Show(ViewMode view)
        {
            mode = view;
            Display();
        }

        private void ResetView(object sender, RoutedEventArgs args) => ResetCamera();

        private void ResetCamera()
        {
            yaw = flatNormal.HasValue ? Math.Atan2(flatNormal.Value.X, flatNormal.Value.Z) : 0.7;
            pitch = flatNormal.HasValue ? Math.Max(-1.56, Math.Min(1.56,
                Math.Asin(Math.Max(-1, Math.Min(1, flatNormal.Value.Y))))) : 0.5;
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

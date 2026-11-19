using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
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
        public bool IsDeviationView { get; set; }

        private GeometryModel3D referenceModel;
        private GeometryModel3D currentModel;
        private Model3D overlayModel;
        private Model3D differencesModel;
        private SolidViewportModels solid;
        private SurfacePreview preview;
        private ViewMode mode;
        private enum ViewMode { Overlay, Current, Reference, Extra, Missing }
        private static readonly Color ReferenceColor = Color.FromRgb(37, 103, 188);
        private static readonly Color CurrentColor = Color.FromRgb(186, 56, 47);
        private static readonly Color SharedColor = Color.FromRgb(142, 155, 164);
        private bool preparing;
        private string prepareIssue;
        private MouseButton? dragButton;
        private Point lastPoint;
        private Point dragStart;
        private double yaw;
        private double pitch;
        private double zoom;
        private Vector3D pan;
        private Vector3D? flatNormal;

        public PartViewport()
        {
            InitializeComponent();
            InitializeDeviationSettings();
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
            if (IsDeviationView)
            {
                await PrepareDeviationAsync(scene, resetCamera: true);
                return;
            }
            referenceModel = currentModel = null;
            overlayModel = differencesModel = null;
            solid = null;
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
                    solid = models.Solid;
                    UpdateReferenceOpacity();
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
            Vector3D? Normal, SolidViewportModels Solid) BuildModels(AnalysisScene scene, bool flat)
        {
            var reference = flat ? scene.Reference?.FlatTriangleCoordinates : scene.Reference?.TriangleCoordinates;
            var current = flat ? scene.Current?.FlatTriangleCoordinates : scene.Current?.TriangleCoordinates;
            var native = !flat && scene.Geometry?.ValidatedComparison == true ? scene.SolidComparison : null;
            if (native?.StudentToReferenceTransform != null)
                current = TransformTriangles(current, native.StudentToReferenceTransform);
            bool hasSolidMeshes = native != null && CanDisplay(native.Missing) && CanDisplay(native.Extra) && CanDisplay(native.Overlap);
            var preview = hasSolidMeshes ? null : SurfacePreview.Prepare(reference, current);
            reference = preview?.Reference ?? reference;
            current = preview?.Current ?? current;
            var bounds = Rect3D.Empty;
            IncludeBounds(ref bounds, reference);
            IncludeBounds(ref bounds, current);
            if (hasSolidMeshes)
            {
                foreach (var operation in new[] { native.Missing, native.Extra, native.Overlap })
                    foreach (var body in operation.TrianglesMetres ?? Array.Empty<float[]>())
                        IncludeBounds(ref bounds, body);
            }
            if (bounds.IsEmpty)
                return (null, null, null, null, preview, null, null);

            // Both parts share one origin offset and one scale for the camera.
            var center = new Point3D(bounds.X + bounds.SizeX / 2,
                bounds.Y + bounds.SizeY / 2, bounds.Z + bounds.SizeZ / 2);
            double extent = Math.Max(bounds.SizeX, Math.Max(bounds.SizeY, bounds.SizeZ));
            if (extent <= 0)
                return (null, null, null, null, preview, null, null);

            double scale = 2 / extent;
            var referenceModel = BuildMesh(reference, center, scale, ReferenceColor);
            var currentModel = BuildMesh(current, center, scale, CurrentColor);
            Model3DGroup overlay = null;
            Model3DGroup differences = null;
            SolidViewportModels solid = null;
            if (hasSolidMeshes)
            {
                var extra = BuildOperation(native.Extra, center, scale, CurrentColor);
                var missing = BuildOperation(native.Missing, center, scale, ReferenceColor);
                var overlap = BuildOperation(native.Overlap, center, scale, SharedColor);
                var ghostOverlap = overlap == null ? null : Group(overlap.Children.OfType<GeometryModel3D>()
                    .Select(model => (Model3D)Translucent(model, SharedColor)).ToArray());
                solid = new SolidViewportModels(extra, missing, Translucent(referenceModel, SharedColor), ghostOverlap);
                overlay = Group(overlap, extra, missing);
                differences = Group(extra, missing);
            }
            else if (preview.Compared)
            {
                var shared = BuildMesh(preview.Shared, center, scale, SharedColor);
                var a = BuildMesh(preview.CurrentOnly, center, scale, CurrentColor);
                var b = BuildMesh(preview.ReferenceOnly, center, scale, ReferenceColor);
                overlay = Group(shared, a, b);
                differences = Group(a, b);
            }
            var normal = flat ? scene.Reference?.FlatNormal ?? scene.Current?.FlatNormal : null;
            return (referenceModel, currentModel, overlay, differences, preview,
                normal == null ? (Vector3D?)null : new Vector3D(normal[0], normal[1], normal[2]), solid);
        }

        private static bool CanDisplay(GeometryProbeOperation operation) =>
            operation?.VolumeM3 == 0 || operation?.TrianglesMetres?.Length > 0;

        private static Model3DGroup BuildOperation(GeometryProbeOperation operation, Point3D center, double scale, Color color) =>
            Group((operation.TrianglesMetres ?? Array.Empty<float[]>())
                .Select(points => (Model3D)BuildMesh(points, center, scale, color)).ToArray());

        private static float[] TransformTriangles(float[] points, double[] matrix)
        {
            if (points == null)
                return null;
            var transform = new Matrix3D(matrix[0], matrix[1], matrix[2], 0, matrix[3], matrix[4], matrix[5], 0,
                matrix[6], matrix[7], matrix[8], 0, matrix[9], matrix[10], matrix[11], 1);
            var transformed = new float[points.Length];
            for (int i = 0; i < points.Length; i += 3)
            {
                var point = transform.Transform(new Point3D(points[i], points[i + 1], points[i + 2]));
                transformed[i] = (float)point.X;
                transformed[i + 1] = (float)point.Y;
                transformed[i + 2] = (float)point.Z;
            }
            return transformed;
        }

        private static GeometryModel3D Translucent(GeometryModel3D model, Color color, double opacity = .25)
        {
            if (model == null)
                return null;
            color.A = (byte)Math.Round(opacity * 255);
            var ghost = new GeometryModel3D(model.Geometry, new DiffuseMaterial(new SolidColorBrush(color)));
            ghost.Freeze();
            return ghost;
        }

        private sealed class SolidViewportModels
        {
            internal SolidViewportModels(Model3D extra, Model3D missing, Model3D transparentReference, Model3D transparentOverlap)
            {
                Extra = extra;
                Missing = missing;
                TransparentReference = transparentReference;
                TransparentOverlap = transparentOverlap;
            }

            internal Model3D Extra { get; }
            internal Model3D Missing { get; }
            internal Model3D TransparentReference { get; set; }
            internal Model3D TransparentOverlap { get; set; }
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
            if (IsDeviationView)
            {
                DisplayDeviation();
                return;
            }
            var item = mode == ViewMode.Reference ? Scene?.Reference : Scene?.Current;
            Model3D geometry = mode == ViewMode.Overlay ? (DifferencesOnly.IsChecked == true ? differencesModel : overlayModel) :
                mode == ViewMode.Reference ? referenceModel : mode == ViewMode.Extra ? solid?.Extra :
                mode == ViewMode.Missing ? solid?.Missing : currentModel;
            if (TransparentReference.IsChecked == true && solid != null)
            {
                if (mode == ViewMode.Reference)
                    geometry = solid.TransparentReference;
                else if (mode == ViewMode.Extra)
                    geometry = Group(geometry, solid.TransparentReference);
                else if (mode == ViewMode.Missing)
                    geometry = Group(geometry, solid.TransparentOverlap);
                else if (mode == ViewMode.Overlay && DifferencesOnly.IsChecked != true)
                    geometry = Group(differencesModel, solid.TransparentOverlap);
            }
            PartVisual.Content = geometry;
            bool showHighlights = solid != null && mode == ViewMode.Overlay && DifferencesOnly.IsChecked != true &&
                differencesModel != null;
            HighlightDifferences.Visibility = showHighlights ? Visibility.Visible : Visibility.Collapsed;
            // Share the camera but use a separate depth buffer so overlap cannot hide the exact difference meshes.
            bool highlight = showHighlights && HighlightDifferences.IsChecked == true;
            HighlightedDifferenceVisual.Content = highlight ? differencesModel : null;
            DifferenceViewport.Visibility = highlight ? Visibility.Visible : Visibility.Collapsed;
            bool pair = Scene?.Reference != null;
            OverlayButton.Visibility = ReferenceButton.Visibility = pair ? Visibility.Visible : Visibility.Collapsed;
            GeometryNotice.Visibility = pair && !preparing ? Visibility.Visible : Visibility.Collapsed;
            Legend.Visibility = (mode == ViewMode.Overlay || mode == ViewMode.Missing || mode == ViewMode.Extra) &&
                geometry != null ? Visibility.Visible : Visibility.Collapsed;
            SolidControls.Visibility = solid == null ? Visibility.Collapsed : Visibility.Visible;
            TransparentReference.IsEnabled = solid != null &&
                (mode == ViewMode.Reference || mode == ViewMode.Extra ? solid.TransparentReference != null :
                    mode != ViewMode.Current && solid.TransparentOverlap != null && DifferencesOnly.IsChecked != true);
            ReferenceOpacity.IsEnabled = TransparentReference.IsEnabled && TransparentReference.IsChecked == true;
            DifferencesOnly.Visibility = mode == ViewMode.Overlay && differencesModel != null ? Visibility.Visible : Visibility.Collapsed;
            SetButton(OverlayButton, overlayModel != null, mode == ViewMode.Overlay);
            SetButton(CurrentButton, currentModel != null, mode == ViewMode.Current);
            SetButton(ReferenceButton, referenceModel != null, mode == ViewMode.Reference);
            SetButton(ExtraButton, solid != null, mode == ViewMode.Extra);
            SetButton(MissingButton, solid != null, mode == ViewMode.Missing);
            ResetButton.IsEnabled = StandardViews.IsEnabled = geometry != null;
            ResetButton.Content = IsFlatPattern && flatNormal.HasValue ? "正视展开" : "重置视角";
            VisibleName.Text = mode == ViewMode.Overlay ? (IsFlatPattern ? "展开叠加" : "模型叠加") :
                mode == ViewMode.Extra ? "多余实体" : mode == ViewMode.Missing ? "缺少实体" :
                item == null ? "" : (mode == ViewMode.Reference ? "参考件" : "当前件") +
                " · " + Path.GetFileName(item.Inspection.SourcePath);
            GeometryNotice.Text = DescribeGeometry(pair);
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
            if (solid != null && (mode == ViewMode.Extra || mode == ViewMode.Missing || DifferencesOnly.IsChecked == true))
                meshIssue = mode == ViewMode.Extra ? "没有多余实体。" : mode == ViewMode.Missing ? "没有缺少实体。" : "没有实体差异。";
            EmptyMessage.Text = preparing ? "正在准备模型和叠加差异…" : prepareIssue ?? meshIssue;
        }

        private string DescribeGeometry(bool pair)
        {
            var evidence = Scene?.Geometry;
            if (!IsFlatPattern && evidence?.DifferenceRatio is double ratio && !Scene.HasSheetMetal)
            {
                string coordinates = evidence.Coordinates == GeometryCoordinates.Original ? "原始 CAD 坐标" : "已验证刚体重合";
                string figures = $"缺少 {evidence.MissingVolumeM3 * 1e9:G7} mm³ · 多余 {evidence.ExtraVolumeM3 * 1e9:G7} mm³ · 差异率 {ratio:P4}";
                return solid != null ? coordinates + " · 已验证实体差集\n" + figures :
                    coordinates + " · " + figures + "\n当前视图为近似网格预览，不用于自动评分。";
            }
            if (pair && preview?.Compared == true)
                return (IsFlatPattern ? "" : (evidence?.Reason ?? "实体比较待复核。") + "\n") + "近似网格预览，不用于自动评分。";
            if (pair)
                return IsFlatPattern ? preview?.Issue ?? "未取得两份网格，无法显示叠加。" :
                    evidence?.Reason ?? preview?.Issue ?? "未取得两份网格，无法显示叠加。";
            return "";
        }

        private void SetButton(Button button, bool available, bool selected)
        {
            button.IsEnabled = available && !preparing;
            if (selected && button.IsEnabled)
                button.SetResourceReference(BackgroundProperty, "AccentSoft");
            else
                button.ClearValue(BackgroundProperty);
            AutomationProperties.SetItemStatus(button, selected && button.IsEnabled ? "当前视图" : "");
        }

        private static string FlatIssue(CadPartPreview item) =>
            item?.FlatPreviewIssue ?? item?.Inspection.SheetMetal?.Issue ?? "未读取到钣金展开网格。";

        private void ToggleShared(object sender, RoutedEventArgs args) => Display();

        private void ChangeReferenceOpacity(object sender, RoutedPropertyChangedEventArgs<double> args)
        {
            UpdateReferenceOpacity();
            Display();
        }

        private void UpdateReferenceOpacity()
        {
            if (solid == null || ReferenceOpacity == null)
                return;
            double opacity = ReferenceOpacity.Value;
            solid.TransparentReference = Translucent(solid.TransparentReference as GeometryModel3D, SharedColor, opacity);
            if (solid.TransparentOverlap is Model3DGroup overlap)
                solid.TransparentOverlap = Group(overlap.Children.OfType<GeometryModel3D>()
                    .Select(model => (Model3D)Translucent(model, SharedColor, opacity)).ToArray());
        }

        private void ShowOverlay(object sender, RoutedEventArgs args) => Show(ViewMode.Overlay);
        private void ShowCurrent(object sender, RoutedEventArgs args) => Show(ViewMode.Current);
        private void ShowReference(object sender, RoutedEventArgs args) => Show(ViewMode.Reference);
        private void ShowExtra(object sender, RoutedEventArgs args) => Show(ViewMode.Extra);
        private void ShowMissing(object sender, RoutedEventArgs args) => Show(ViewMode.Missing);

        private void Show(ViewMode view)
        {
            if (IsDeviationView && mode != view)
                LocalDeviation.Text = "";
            bool wasFocused = mode == ViewMode.Extra || mode == ViewMode.Missing;
            mode = view;
            if (wasFocused || view == ViewMode.Extra || view == ViewMode.Missing)
            {
                FitCameraToMode();
                UpdateCamera();
            }
            Display();
        }

        private void ResetView(object sender, RoutedEventArgs args) => ResetCamera();

        private void InitializeStandardViews(object sender, RoutedEventArgs args) => StandardViews.SelectedIndex = 0;

        private void SelectStandardView(object sender, SelectionChangedEventArgs args)
        {
            int view = StandardViews.SelectedIndex;
            if (view < 1)
                return;
            ResetCamera();
            switch (view)
            {
                case 1: yaw = 0; pitch = 0; break;
                case 2: yaw = 0; pitch = Math.PI / 2; break;
                case 3: yaw = Math.PI / 2; pitch = 0; break;
                case 4: yaw = Math.PI / 4; pitch = Math.Atan(1 / Math.Sqrt(2)); break;
            }
            UpdateCamera();
            StandardViews.SelectedIndex = 0;
        }

        private void ResetCamera()
        {
            yaw = flatNormal.HasValue ? Math.Atan2(flatNormal.Value.X, flatNormal.Value.Z) : 0.7;
            pitch = flatNormal.HasValue ? Math.Max(-1.56, Math.Min(1.56,
                Math.Asin(Math.Max(-1, Math.Min(1, flatNormal.Value.Y))))) : 0.5;
            FitCameraToMode();
            UpdateCamera();
        }

        private void FitCameraToMode()
        {
            zoom = 1;
            pan = new Vector3D();
            var difference = mode == ViewMode.Extra ? solid?.Extra : mode == ViewMode.Missing ? solid?.Missing : null;
            if (difference == null || difference.Bounds.IsEmpty)
                return;

            var bounds = difference.Bounds;
            pan = new Vector3D(bounds.X + bounds.SizeX / 2, bounds.Y + bounds.SizeY / 2, bounds.Z + bounds.SizeZ / 2);
            double size = Math.Max(bounds.SizeX, Math.Max(bounds.SizeY, bounds.SizeZ));
            if (size > 0)
                zoom = Math.Min(20, 2 / size);
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
            dragStart = lastPoint;
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
            {
                if (IsDeviationView && dragButton == MouseButton.Left &&
                    (args.GetPosition(Surface) - dragStart).Length < 4)
                    ReportDeviationAt(args.GetPosition(ModelViewport));
                Surface.ReleaseMouseCapture();
            }
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

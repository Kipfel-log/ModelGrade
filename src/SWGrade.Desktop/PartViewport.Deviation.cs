using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Media.Media3D;
using SWGrade.SolidWorks;

namespace SWGrade.Desktop
{
    public partial class PartViewport
    {
        private CloudMesh studentCloud;
        private CloudMesh referenceCloud;
        private int cloudGeneration;
        private double toleranceMillimetres = 0.05;
        private double rangeMillimetres = 1;
        private string scaleIssue;
        private static readonly Color NegativeColor = Color.FromRgb(39, 99, 212);
        private static readonly Color ToleranceColor = Color.FromRgb(41, 147, 86);
        private static readonly Color PositiveColor = Color.FromRgb(217, 50, 45);
        private static readonly AmbientLight CloudLight = CreateCloudLight();

        private void InitializeDeviationSettings()
        {
            DeviationTolerance.Text = toleranceMillimetres.ToString(CultureInfo.CurrentCulture);
            DeviationRange.Text = rangeMillimetres.ToString(CultureInfo.CurrentCulture);
        }

        private async void ApplyDeviationScale(object sender, RoutedEventArgs args)
        {
            if (!double.TryParse(DeviationTolerance.Text, NumberStyles.Float, CultureInfo.CurrentCulture, out double tolerance) ||
                !double.TryParse(DeviationRange.Text, NumberStyles.Float, CultureInfo.CurrentCulture, out double range) ||
                !(tolerance >= 0) || !(range > tolerance) || double.IsInfinity(range))
            {
                scaleIssue = "请输入非负容差，以及大于容差的有限色带范围。";
                DisplayDeviation();
                return;
            }

            toleranceMillimetres = tolerance;
            rangeMillimetres = range;
            scaleIssue = null;
            await PrepareDeviationAsync(Scene, resetCamera: false);
        }

        private async Task PrepareDeviationAsync(AnalysisScene scene, bool resetCamera)
        {
            int generation = ++cloudGeneration;
            studentCloud = referenceCloud = null;
            LocalDeviation.Text = "";
            prepareIssue = null;
            if (resetCamera)
            {
                mode = ViewMode.Current;
                scaleIssue = null;
                ResetCamera();
            }
            var deviation = scene?.Deviation;
            preparing = deviation != null;
            DisplayDeviation();
            if (deviation == null)
                return;

            double range = rangeMillimetres;
            double tolerance = toleranceMillimetres;
            try
            {
                var pair = await Task.Run(() =>
                {
                    var bounds = Rect3D.Empty;
                    IncludeBounds(ref bounds, deviation.Student.TrianglesMetres);
                    IncludeBounds(ref bounds, deviation.Reference.TrianglesMetres);
                    double extent = Math.Max(bounds.SizeX, Math.Max(bounds.SizeY, bounds.SizeZ));
                    if (bounds.IsEmpty || !(extent > 0))
                        throw new InvalidOperationException("无法缩放表面采样网格。");
                    var center = new Point3D(bounds.X + bounds.SizeX / 2, bounds.Y + bounds.SizeY / 2,
                        bounds.Z + bounds.SizeZ / 2);
                    var palette = DeviationTexture(tolerance / range);
                    return (Student: BuildCloud(deviation.Student, center, 2 / extent, range, palette),
                        Reference: BuildCloud(deviation.Reference, center, 2 / extent, range, palette));
                });
                if (generation == cloudGeneration)
                {
                    studentCloud = pair.Student;
                    referenceCloud = pair.Reference;
                }
            }
            catch (Exception exception)
            {
                if (generation == cloudGeneration)
                    prepareIssue = "偏差云图无法显示。 " + exception.Message;
            }
            finally
            {
                if (generation == cloudGeneration)
                {
                    preparing = false;
                    DisplayDeviation();
                }
            }
        }

        private static LinearGradientBrush DeviationPalette(double tolerance, double range)
        {
            var brush = new LinearGradientBrush
            {
                StartPoint = new Point(0, .5),
                EndPoint = new Point(1, .5),
                ColorInterpolationMode = ColorInterpolationMode.SRgbLinearInterpolation
            };
            brush.GradientStops.Add(new GradientStop(NegativeColor, 0));
            brush.GradientStops.Add(new GradientStop(ToleranceColor, .5 - .5 * tolerance / range));
            brush.GradientStops.Add(new GradientStop(ToleranceColor, .5 + .5 * tolerance / range));
            brush.GradientStops.Add(new GradientStop(PositiveColor, 1));
            brush.Freeze();
            return brush;
        }

        private const int PaletteWidth = 1024;

        private static ImageBrush DeviationTexture(double tolerance)
        {
            const int height = 8;
            var pixels = new byte[PaletteWidth * height * 4];
            for (int x = 0; x < PaletteWidth; x++)
            {
                double value = 2d * x / (PaletteWidth - 1) - 1;
                var end = value < 0 ? NegativeColor : PositiveColor;
                double amount = Math.Max(0, (Math.Abs(value) - tolerance) / (1 - tolerance));
                for (int y = 0; y < height; y++)
                {
                    int index = (y * PaletteWidth + x) * 4;
                    pixels[index] = Blend(ToleranceColor.B, end.B, amount);
                    pixels[index + 1] = Blend(ToleranceColor.G, end.G, amount);
                    pixels[index + 2] = Blend(ToleranceColor.R, end.R, amount);
                    pixels[index + 3] = 255;
                }
            }
            var image = BitmapSource.Create(PaletteWidth, height, 96, 96, PixelFormats.Bgra32, null, pixels, PaletteWidth * 4);
            image.Freeze();
            var brush = new ImageBrush(image)
            {
                ViewportUnits = BrushMappingMode.Absolute,
                Viewport = new Rect(0, 0, 1, 1)
            };
            brush.Freeze();
            return brush;
        }

        private static byte Blend(byte start, byte end, double amount) => (byte)Math.Round(start + (end - start) * amount);

        private static AmbientLight CreateCloudLight()
        {
            var light = new AmbientLight(Colors.White);
            light.Freeze();
            return light;
        }

        private static CloudMesh BuildCloud(DeviationSurface data, Point3D center, double scale, double range, Brush palette)
        {
            var points = new Point3DCollection();
            var unknown = new Point3DCollection();
            var uv = new PointCollection();
            var distances = new List<float>();
            for (int face = 0; face < data.SignedMillimetres.Length; face += 3)
            {
                bool valid = Finite(data.SignedMillimetres[face]) && Finite(data.SignedMillimetres[face + 1]) &&
                    Finite(data.SignedMillimetres[face + 2]);
                for (int vertex = face; vertex < face + 3; vertex++)
                {
                    int xyz = vertex * 3;
                    var point = new Point3D((data.TrianglesMetres[xyz] - center.X) * scale,
                        (data.TrianglesMetres[xyz + 1] - center.Y) * scale,
                        (data.TrianglesMetres[xyz + 2] - center.Z) * scale);
                    if (!valid)
                    {
                        unknown.Add(point);
                        continue;
                    }
                    float value = data.SignedMillimetres[vertex];
                    points.Add(point);
                    distances.Add(value);
                    double fraction = .5 + .5 * Math.Min(1, Math.Max(-1, value / range));
                    // Sample texel centers. Repeated palette rows let every triangle span a vertical interval.
                    uv.Add(new Point((.5 + fraction * (PaletteWidth - 1)) / PaletteWidth, .25 + .25 * (vertex - face)));
                }
            }
            var verified = CloudGeometry(points, uv, palette);
            var unavailable = CloudGeometry(unknown, null, Brushes.SlateGray);
            return new CloudMesh(Group(verified, unavailable), verified, distances.ToArray(), unknown.Count / 3);
        }

        private static GeometryModel3D CloudGeometry(Point3DCollection points, PointCollection uv, Brush brush)
        {
            if (points.Count == 0)
                return null;
            var mesh = new MeshGeometry3D { Positions = points, TriangleIndices = new Int32Collection(Enumerable.Range(0, points.Count)) };
            if (uv != null)
                mesh.TextureCoordinates = uv;
            var material = new DiffuseMaterial(brush);
            var model = new GeometryModel3D(mesh, material) { BackMaterial = material };
            model.Freeze();
            return model;
        }

        private void DisplayDeviation()
        {
            var cloud = mode == ViewMode.Reference ? referenceCloud : studentCloud;
            Lighting.Content = CloudLight;
            PartVisual.Content = cloud?.Geometry;
            OverlayButton.Visibility = DifferenceControls.Visibility = Legend.Visibility = Visibility.Collapsed;
            DeviationControls.Visibility = DeviationLegend.Visibility = cloud == null ? Visibility.Collapsed : Visibility.Visible;
            SetButton(CurrentButton, studentCloud != null, mode == ViewMode.Current);
            SetButton(ReferenceButton, referenceCloud != null, mode == ViewMode.Reference);
            ReferenceButton.Visibility = referenceCloud == null ? Visibility.Collapsed : Visibility.Visible;
            ReferenceButton.Content = "参考件侧";
            ResetButton.IsEnabled = StandardViews.IsEnabled = cloud?.Geometry != null;
            VisibleName.Text = mode == ViewMode.Reference ? "参考件侧 · 查看缺料区域" : "当前件表面偏差";
            DeviationColorBar.Background = DeviationPalette(toleranceMillimetres, rangeMillimetres);
            NegativeLimit.Text = $"-{rangeMillimetres:G5} mm";
            PositiveLimit.Text = $"+{rangeMillimetres:G5} mm";
            ToleranceBand.Text = $"容差 ±{toleranceMillimetres:G5} mm";
            GeometryNotice.Visibility = Visibility.Visible;
            GeometryNotice.Text = scaleIssue ?? (cloud == null ? "" :
                "SOLIDWORKS 曲面采样，面片内插值。点击查看局部估计，云图不用于评分。" +
                (cloud.UnknownFaces > 0 ? $" {cloud.UnknownFaces} 个灰色面片未核实。" : ""));
            EmptyMessage.Visibility = cloud?.Geometry == null ? Visibility.Visible : Visibility.Collapsed;
            EmptyMessage.Text = preparing ? "正在准备偏差色带…" : prepareIssue ??
                (Scene?.Geometry?.ValidatedComparison == true ? Scene.DeviationIssue ??
                    "点击「计算偏差云图」获取两侧的表面偏差。" :
                    Scene?.Geometry?.Reason ?? "未取得可验证的实体差集，无法计算偏差云图。");
        }

        private void ReportDeviationAt(Point location)
        {
            var cloud = mode == ViewMode.Reference ? referenceCloud : studentCloud;
            if (cloud == null)
                return;
            RayMeshGeometry3DHitTestResult nearest = null;
            VisualTreeHelper.HitTest(ModelViewport, null, hit =>
            {
                if (hit is RayMeshGeometry3DHitTestResult candidate &&
                    (nearest == null || candidate.DistanceToRayOrigin < nearest.DistanceToRayOrigin))
                    nearest = candidate;
                return HitTestResultBehavior.Continue;
            }, new PointHitTestParameters(location));
            if (nearest == null)
                LocalDeviation.Text = "";
            else if (!ReferenceEquals(nearest.ModelHit, cloud.Verified))
                LocalDeviation.Text = "此处偏差未核实。";
            else
            {
                double value = cloud.Distances[nearest.VertexIndex1] * nearest.VertexWeight1 +
                    cloud.Distances[nearest.VertexIndex2] * nearest.VertexWeight2 +
                    cloud.Distances[nearest.VertexIndex3] * nearest.VertexWeight3;
                LocalDeviation.Text = $"局部偏差约 {value:+0.0000;-0.0000;0.0000} mm";
            }
        }

        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);

        private sealed class CloudMesh
        {
            internal CloudMesh(Model3D geometry, GeometryModel3D verified, float[] distances, int unknownFaces)
            {
                Geometry = geometry;
                Verified = verified;
                Distances = distances;
                UnknownFaces = unknownFaces;
            }

            internal Model3D Geometry { get; }
            internal GeometryModel3D Verified { get; }
            internal float[] Distances { get; }
            internal int UnknownFaces { get; }
        }
    }
}

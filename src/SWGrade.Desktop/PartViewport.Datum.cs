using System;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using SWGrade.Domain;

namespace SWGrade.Desktop
{
    public partial class PartViewport
    {
        internal event Action<DatumPlane> DatumPlanePicked;
        private DatumPlane[] datumHighlights;

        internal void SetDatumHighlights(DatumPlane[] planes)
        {
            datumHighlights = planes;
            DrawDatumHighlights();
        }

        private void PickDatum(Point location)
        {
            if (DatumPlanePicked == null || currentModel == null)
                return;
            RayMeshGeometry3DHitTestResult nearest = null;
            VisualTreeHelper.HitTest(ModelViewport, null, hit =>
            {
                if (hit is RayMeshGeometry3DHitTestResult found && found.ModelHit == currentModel &&
                    (nearest == null || nearest.DistanceToRayOrigin > found.DistanceToRayOrigin))
                    nearest = found;
                return HitTestResultBehavior.Continue;
            }, new PointHitTestParameters(location));
            if (nearest == null)
                return;
            var coordinates = Scene.Current.TriangleCoordinates;
            var plane = Scene.Current.DatumPlanes.FirstOrDefault(candidate =>
                OnPlane(candidate, coordinates, nearest.VertexIndex1 * 3) &&
                OnPlane(candidate, coordinates, nearest.VertexIndex2 * 3) &&
                OnPlane(candidate, coordinates, nearest.VertexIndex3 * 3));
            if (plane != null)
                DatumPlanePicked(plane);
        }

        private void DrawDatumHighlights()
        {
            if (datumHighlights == null || Scene?.Current?.TriangleCoordinates == null ||
                !(currentModel?.Geometry is MeshGeometry3D mesh))
                return;
            var coordinates = Scene.Current.TriangleCoordinates;
            var colors = new[] { Colors.OrangeRed, Colors.DodgerBlue, Colors.SeaGreen };
            var highlights = new Model3D[datumHighlights.Length];
            for (int i = 0; i < datumHighlights.Length; i++)
            {
                if (datumHighlights[i] == null)
                    continue;
                var points = new Point3DCollection();
                for (int index = 0; index < coordinates.Length; index += 9)
                {
                    if (!OnPlane(datumHighlights[i], coordinates, index) ||
                        !OnPlane(datumHighlights[i], coordinates, index + 3) ||
                        !OnPlane(datumHighlights[i], coordinates, index + 6))
                        continue;
                    int vertex = index / 3;
                    points.Add(mesh.Positions[vertex]);
                    points.Add(mesh.Positions[vertex + 1]);
                    points.Add(mesh.Positions[vertex + 2]);
                }
                if (points.Count == 0)
                    continue;
                var material = new DiffuseMaterial(new SolidColorBrush(colors[i]));
                var geometry = new MeshGeometry3D { Positions = points };
                var plane = datumHighlights[i];
                highlights[i] = new GeometryModel3D(geometry, material)
                {
                    BackMaterial = material,
                    Transform = new TranslateTransform3D(plane.X * .0001, plane.Y * .0001, plane.Z * .0001)
                };
                highlights[i].Freeze();
            }
            // The comparison viewport keeps its ordinary depth ordering for the selected planes.
            PartVisual.Content = Group(new Model3D[] { currentModel, Group(highlights) });
        }

        private static bool OnPlane(DatumPlane plane, float[] xyz, int index) =>
            Math.Abs(plane.Project(xyz[index], xyz[index + 1], xyz[index + 2]) - plane.OffsetMetres) <= 1e-7;
    }
}

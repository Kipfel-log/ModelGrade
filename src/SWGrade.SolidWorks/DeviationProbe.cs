using System;
using System.Collections.Generic;
using System.Linq;

namespace SWGrade.SolidWorks
{
    internal sealed class DeviationProbe
    {
        private const string Face = "IFace2";
        private const double NearSurfaceMetres = 1e-8;
        private const int MaximumSamplesPerSide = 4500;
        private const int MaximumFaceQueries = 48000;
        private const int MaximumDisplayVertices = 120000;
        private readonly CadInterop cad;

        private DeviationProbe(CadInterop cad)
        {
            this.cad = cad;
        }

        internal static SurfaceDeviation Read(CadInterop cad, object reference, object student,
            GeometryProbeOperation missing, GeometryProbeOperation extra)
        {
            int checkpoint = cad.ReferenceCheckpoint;
            try
            {
                var probe = new DeviationProbe(cad);
                var regions = new[] { missing, extra }.Where(operation => operation.VolumeM3 > 0)
                    .SelectMany(operation => operation.DeviationBounds ??
                        throw new InvalidOperationException("未取得差集范围，无法细化局部偏差采样。")).ToArray();
                return new SurfaceDeviation(probe.Sample(student, reference, 1, regions), probe.Sample(reference, student, -1, regions));
            }
            finally
            {
                cad.ReleaseAfter(checkpoint);
            }
        }

        private DeviationSurface Sample(object source, object target, int outsideSign, double[][] boundsOfDifferences)
        {
            var targetFaces = cad.Call("IBody2", target, "GetFaces") as Array;
            var bounds = cad.Call("IBody2", source, "GetBodyBox") as double[];
            if (targetFaces == null || targetFaces.Length == 0 || bounds?.Length != 6 || !bounds.All(Finite))
                throw new InvalidOperationException("未取得偏差计算所需的 CAD 曲面。");
            double size = Math.Max(bounds[3] - bounds[0], Math.Max(bounds[4] - bounds[1], bounds[5] - bounds[2]));
            if (!(size > 0))
                throw new InvalidOperationException("模型尺度不可用，无法建立偏差采样网格。");

            var faces = targetFaces.Cast<object>().Select(ReadFace).ToArray();
            var regions = boundsOfDifferences.Select(box => new DeviationRegion(box, size)).ToArray();
            var unique = new Dictionary<(float, float, float), float>();
            var coordinates = new List<float>();
            var values = new List<float>();
            var candidates = new List<(TargetFace Face, double[] Point, double Distance)>();
            void Record(object face, double[] point)
            {
                if (values.Count >= MaximumDisplayVertices)
                    throw new InvalidOperationException("偏差云图网格超出显示预算。");
                var key = ((float)point[0], (float)point[1], (float)point[2]);
                if (!unique.TryGetValue(key, out float value))
                {
                    if (unique.Count >= MaximumSamplesPerSide || (long)(unique.Count + 1) * faces.Length > MaximumFaceQueries)
                        throw new InvalidOperationException("模型超出当前云图采样预算，偏差待复核。");
                    var projected = Closest(face, point);
                    value = projected == null ? float.NaN :
                        (float)(Distance(projected, faces, candidates) * outsideSign * 1000);
                    unique.Add(key, value);
                }
                values.Add(value);
                coordinates.Add(key.Item1);
                coordinates.Add(key.Item2);
                coordinates.Add(key.Item3);
            }

            // The native planar tessellation can skip an entire small defect. Refine locally
            // using the verified solid differences, then measure each new site on the CAD face.
            CadPartPreview.TessellateBody(cad, source, size / 18,
                (face, a, b, c) => Refine(regions, a, b, c, (x, y, z) =>
                {
                    Record(face, x);
                    Record(face, y);
                    Record(face, z);
                }));

            int checkedPoints = unique.Values.Count(value => Finite(value));
            if (checkedPoints == 0)
                throw new InvalidOperationException("SOLIDWORKS 未能核实任何表面偏差采样点。");
            cad.Log("偏差云图：已核对 {0}/{1} 个采样点。", checkedPoints, unique.Count);
            return new DeviationSurface(coordinates.ToArray(), values.ToArray());
        }

        private static void Refine(DeviationRegion[] regions, double[] a, double[] b, double[] c,
            Action<double[], double[], double[]> triangle)
        {
            double ab = SquaredLength(a, b), bc = SquaredLength(b, c), ca = SquaredLength(c, a);
            double longest = Math.Max(ab, Math.Max(bc, ca));
            if (!regions.Any(region => longest > region.StepSquared && region.Touches(a, b, c)))
            {
                triangle(a, b, c);
                return;
            }

            // Rotate the vertex order to put the longest edge first without reversing the face.
            if (bc == longest && ab != longest)
                (a, b, c) = (b, c, a);
            else if (ca == longest && ab != longest)
                (a, b, c) = (c, a, b);
            var midpoint = new[] { (a[0] + b[0]) * .5, (a[1] + b[1]) * .5, (a[2] + b[2]) * .5 };
            Refine(regions, a, midpoint, c, triangle);
            Refine(regions, midpoint, b, c, triangle);
        }

        private static double SquaredLength(double[] a, double[] b)
        {
            double x = a[0] - b[0], y = a[1] - b[1], z = a[2] - b[2];
            return x * x + y * y + z * z;
        }

        private sealed class DeviationRegion
        {
            private readonly double[] bounds;
            private readonly double padding;

            internal DeviationRegion(double[] bounds, double modelSize)
            {
                if (bounds?.Length != 6 || !bounds.All(Finite))
                    throw new InvalidOperationException("差集范围不可用，无法核对局部偏差。");
                this.bounds = bounds;
                double size = Math.Max(bounds[3] - bounds[0], Math.Max(bounds[4] - bounds[1], bounds[5] - bounds[2]));
                if (!(size > 0))
                    throw new InvalidOperationException("差集范围不可用，无法核对局部偏差。");
                double step = Math.Max(modelSize / 256, Math.Min(modelSize / 18, size / 5));
                StepSquared = step * step;
                padding = step * .5;
            }

            internal double StepSquared { get; }

            internal bool Touches(double[] a, double[] b, double[] c)
            {
                for (int axis = 0; axis < 3; axis++)
                    if (Math.Max(a[axis], Math.Max(b[axis], c[axis])) < bounds[axis] - padding ||
                        Math.Min(a[axis], Math.Min(b[axis], c[axis])) > bounds[axis + 3] + padding)
                        return false;
                return true;
            }
        }

        private TargetFace ReadFace(object face)
        {
            var surface = cad.Call(Face, face, "GetSurface");
            var planarNormal = (bool)cad.Call("ISurface", surface, "IsPlane")
                ? cad.Get(Face, face, "Normal") as double[] : null;
            bool reversed = planarNormal == null && (bool)cad.Call(Face, face, "FaceInSurfaceSense");
            return new TargetFace(face, surface, planarNormal, reversed);
        }

        private double Distance(double[] origin, TargetFace[] faces,
            List<(TargetFace Face, double[] Point, double Distance)> candidates)
        {
            candidates.Clear();
            double minimum = double.PositiveInfinity;
            foreach (var face in faces)
            {
                var point = Closest(face.Face, origin);
                if (point == null)
                    return double.NaN;
                double dx = origin[0] - point[0];
                double dy = origin[1] - point[1];
                double dz = origin[2] - point[2];
                double distance = Math.Sqrt(dx * dx + dy * dy + dz * dz);
                if (distance <= NearSurfaceMetres)
                    return 0;
                if (distance < minimum - NearSurfaceMetres)
                    candidates.Clear();
                if (distance <= minimum + NearSurfaceMetres)
                    candidates.Add((face, point, distance));
                minimum = Math.Min(minimum, distance);
            }

            int direction = 0;
            bool normalProjection = false;
            foreach (var hit in candidates)
            {
                if (hit.Distance > minimum + NearSurfaceMetres)
                    continue;
                var normal = hit.Face.PlanarNormal ?? (cad.Call("ISurface", hit.Face.Surface,
                    "EvaluateAtPoint", hit.Point[0], hit.Point[1], hit.Point[2]) as double[]);
                if (normal == null || normal.Length < 3)
                    return double.NaN;
                double length = Math.Sqrt(normal[0] * normal[0] + normal[1] * normal[1] + normal[2] * normal[2]);
                if (!Finite(length) || !(length > 0))
                    return double.NaN;
                double dot = ((origin[0] - hit.Point[0]) * normal[0] + (origin[1] - hit.Point[1]) * normal[1] +
                    (origin[2] - hit.Point[2]) * normal[2]) / (length * hit.Distance);
                if (hit.Face.Reversed)
                    dot = -dot;
                if (!Finite(dot))
                    return double.NaN;
                // At a shared trim edge another face can be tangent to a valid normal
                // projection. A tangential normal does not determine the sign.
                if (Math.Abs(dot) <= 0.0001)
                    continue;
                normalProjection |= Math.Abs(dot) >= 0.9999;
                var sign = Math.Sign(dot);
                if (direction != 0 && direction != sign)
                    return double.NaN;
                direction = sign;
            }
            return normalProjection && direction != 0 ? direction * minimum : double.NaN;
        }

        private double[] Closest(object face, double[] point)
        {
            if (face == null)
                return null;
            var result = cad.Call(Face, face, "GetClosestPointOn", point[0], point[1], point[2]) as double[];
            return result?.Length >= 3 && Finite(result[0]) && Finite(result[1]) && Finite(result[2]) ? result : null;
        }

        private static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);

        private sealed class TargetFace
        {
            internal TargetFace(object face, object surface, double[] planarNormal, bool reversed)
            {
                Face = face;
                Surface = surface;
                PlanarNormal = planarNormal;
                Reversed = reversed;
            }

            internal object Face { get; }
            internal object Surface { get; }
            internal double[] PlanarNormal { get; }
            internal bool Reversed { get; }
        }
    }
}

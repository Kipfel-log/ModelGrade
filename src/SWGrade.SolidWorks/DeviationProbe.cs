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

        internal static SurfaceDeviation Read(CadInterop cad, object reference, object student)
        {
            int checkpoint = cad.ReferenceCheckpoint;
            try
            {
                var probe = new DeviationProbe(cad);
                return new SurfaceDeviation(probe.Sample(student, reference, 1), probe.Sample(reference, student, -1));
            }
            finally
            {
                cad.ReleaseAfter(checkpoint);
            }
        }

        private DeviationSurface Sample(object source, object target, int outsideSign)
        {
            var targetFaces = cad.Call("IBody2", target, "GetFaces") as Array;
            var bounds = cad.Call("IBody2", source, "GetBodyBox") as double[];
            if (targetFaces == null || targetFaces.Length == 0 || bounds?.Length != 6 || !bounds.All(Finite))
                throw new InvalidOperationException("未取得偏差计算所需的 CAD 曲面。");
            double size = Math.Max(bounds[3] - bounds[0], Math.Max(bounds[4] - bounds[1], bounds[5] - bounds[2]));
            if (!(size > 0))
                throw new InvalidOperationException("模型尺度不可用，无法建立偏差采样网格。");

            var faces = targetFaces.Cast<object>().Select(ReadFace).ToArray();
            var unique = new Dictionary<(float, float, float), float>();
            var values = new List<float>();
            var candidates = new List<(TargetFace Face, double[] Point, double Distance)>();
            // Tessellation supplies display sites. The native trimmed face supplies the actual source point.
            var triangles = CadPartPreview.TessellateBody(cad, source, size / 18, (face, point) =>
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
            });

            int checkedPoints = unique.Values.Count(value => Finite(value));
            if (checkedPoints == 0)
                throw new InvalidOperationException("SOLIDWORKS 未能核实任何表面偏差采样点。");
            cad.Log("偏差云图：已核对 {0}/{1} 个采样点。", checkedPoints, unique.Count);
            return new DeviationSurface(triangles, values.ToArray());
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

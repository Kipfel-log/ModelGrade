using System;
using System.Collections.Generic;
using System.Linq;
using SWGrade.Domain;

namespace SWGrade.SolidWorks
{
    internal static class DatumRegistration
    {
        internal const double PlaneTolerance = 0.00002; // 0.02 mm
        private const double AngularTolerance = 0.001; // radians, approximately 0.057 degrees

        internal static (double[][] Matrices, int MatchedPlanes) Find(
            AssignmentDatum datum, IReadOnlyList<DatumPlane> reference, IReadOnlyList<DatumPlane> student)
        {
            if (student.Count < 4 || reference.Count < 4)
                throw new InvalidOperationException("缺少可独立核对姿态的外侧平面，请人工复核。");
            if (student.Count > 48 || reference.Count > 48)
                throw new InvalidOperationException("外侧平面太多，当前无法可靠自动匹配作业基准。");

            var matches = new List<double[]>();
            var standard = datum.Planes.Select(p => new Vector(p)).ToArray();
            var targetFrame = Frame(standard[0], standard[1]);
            double ab = standard[0].Dot(standard[1]), ac = standard[0].Dot(standard[2]), bc = standard[1].Dot(standard[2]);
            var normals = student.Select(plane => new Vector(plane)).ToArray();
            int minimum = Math.Max(4, (int)Math.Ceiling(Math.Min(reference.Count, student.Count) * .6));
            int best = minimum - 1;
            for (int i = 0; i < student.Count; i++)
            for (int j = 0; j < student.Count; j++)
            {
                if (i == j || Math.Abs(normals[i].Dot(normals[j]) - ab) > AngularTolerance)
                    continue;
                var sourceFrame = Frame(normals[i], normals[j]);
                var rotation = Rotation(sourceFrame, targetFrame);
                for (int k = 0; k < student.Count; k++)
                {
                    if (k == i || k == j || Math.Abs(normals[i].Dot(normals[k]) - ac) > AngularTolerance ||
                        Math.Abs(normals[j].Dot(normals[k]) - bc) > AngularTolerance)
                        continue;
                    var translation = Translate(standard, datum.Planes[0].OffsetMetres - student[i].OffsetMetres,
                        datum.Planes[1].OffsetMetres - student[j].OffsetMetres, datum.Planes[2].OffsetMetres - student[k].OffsetMetres);
                    if (!Matches(datum.Planes[0], student[i], Rotate(normals[i], rotation), translation) ||
                        !Matches(datum.Planes[1], student[j], Rotate(normals[j], rotation), translation) ||
                        !Matches(datum.Planes[2], student[k], Rotate(normals[k], rotation), translation))
                        continue;
                    int count = CountMatches(reference, student, normals, rotation, translation);
                    if (count < minimum || count < best)
                        continue;
                    if (count > best)
                    {
                        matches.Clear();
                        best = count;
                    }
                    var matrix = (double[])rotation.Clone();
                    matrix[9] = translation.X;
                    matrix[10] = translation.Y;
                    matrix[11] = translation.Z;
                    if (!matches.Any(existing => GeometryProbeValidation.SameRigidTransform(existing, matrix)))
                        matches.Add(matrix);
                }
            }
            if (matches.Count == 0)
                throw new InvalidOperationException("没有找到可用其他外侧平面核对的作业基准，请人工复核。");
            return (matches.ToArray(), best);
        }

        private static int CountMatches(IReadOnlyList<DatumPlane> reference, IReadOnlyList<DatumPlane> student,
            Vector[] normals, double[] rotation, Vector translation)
        {
            var seen = new HashSet<int>();
            for (int i = 0; i < student.Count; i++)
            {
                var normal = Rotate(normals[i], rotation);
                for (int j = 0; j < reference.Count; j++)
                {
                    if (Matches(reference[j], student[i], normal, translation))
                    {
                        seen.Add(j);
                        break;
                    }
                }
            }
            return seen.Count;
        }

        private static bool Matches(DatumPlane reference, DatumPlane student, Vector transformedNormal, Vector translation) =>
            transformedNormal.Dot(new Vector(reference)) >= 1 - AngularTolerance * AngularTolerance / 2 &&
            Math.Abs(student.OffsetMetres + transformedNormal.Dot(translation) - reference.OffsetMetres) <= PlaneTolerance;

        private static Vector[] Frame(Vector a, Vector b)
        {
            var second = (b - a * a.Dot(b)).Unit();
            return new[] { a, second, a.Cross(second) };
        }

        private static double[] Rotation(Vector[] from, Vector[] to)
        {
            var matrix = new double[16];
            for (int row = 0; row < 3; row++)
                for (int col = 0; col < 3; col++)
                    for (int axis = 0; axis < 3; axis++)
                        matrix[row * 3 + col] += from[axis][row] * to[axis][col];
            matrix[12] = 1;
            return matrix;
        }

        private static Vector Rotate(Vector v, double[] m) => new Vector(
            v.X * m[0] + v.Y * m[3] + v.Z * m[6],
            v.X * m[1] + v.Y * m[4] + v.Z * m[7],
            v.X * m[2] + v.Y * m[5] + v.Z * m[8]);

        private static Vector Translate(Vector[] n, double a, double b, double c) =>
            (n[1].Cross(n[2]) * a + n[2].Cross(n[0]) * b + n[0].Cross(n[1]) * c) * (1 / n[0].Dot(n[1].Cross(n[2])));

        private readonly struct Vector
        {
            internal readonly double X, Y, Z;

            internal Vector(DatumPlane plane) : this(plane.X, plane.Y, plane.Z) { }
            internal Vector(double x, double y, double z) { X = x; Y = y; Z = z; }
            internal double this[int axis] => axis == 0 ? X : axis == 1 ? Y : Z;
            internal double Dot(Vector b) => X * b.X + Y * b.Y + Z * b.Z;
            internal Vector Cross(Vector b) => new Vector(Y * b.Z - Z * b.Y, Z * b.X - X * b.Z, X * b.Y - Y * b.X);
            internal Vector Unit() => this * (1 / Math.Sqrt(Dot(this)));
            public static Vector operator +(Vector a, Vector b) => new Vector(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
            public static Vector operator -(Vector a, Vector b) => new Vector(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
            public static Vector operator *(Vector a, double factor) => new Vector(a.X * factor, a.Y * factor, a.Z * factor);
        }
    }
}

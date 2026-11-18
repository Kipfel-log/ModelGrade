using System;
using System.Collections.Generic;

namespace SWGrade.Domain
{
    /// <summary>Locates display-mesh regions for visual review in the original part coordinates.</summary>
    public sealed class SurfacePreview
    {
        public const double VisualToleranceMetres = 0.00002;
        private const int CoordinatesPerTriangle = 9;
        private const int MaximumComparisonTriangles = 100000;

        private SurfacePreview(float[] reference, float[] current)
        {
            Reference = reference;
            Current = current;
        }

        public float[] Reference { get; }
        public float[] Current { get; }
        public float[] Shared { get; private set; }
        public float[] ReferenceOnly { get; private set; }
        public float[] CurrentOnly { get; private set; }
        public bool Compared { get; private set; }
        public string Issue { get; private set; }

        public static SurfacePreview Prepare(float[] reference, float[] current)
        {
            var cleanReference = Clean(reference);
            var cleanCurrent = ReferenceEquals(reference, current) ? cleanReference : Clean(current);
            var preview = new SurfacePreview(cleanReference, cleanCurrent);
            if (cleanReference.Length == 0 || cleanCurrent.Length == 0)
            {
                preview.Issue = "叠加需要两份可读的显示网格。可切换到单件查看。";
                return preview;
            }

            if (ReferenceEquals(cleanReference, cleanCurrent))
            {
                preview.Shared = cleanCurrent;
                preview.ReferenceOnly = preview.CurrentOnly = Array.Empty<float>();
                preview.Compared = true;
                return preview;
            }

            if (Math.Max(cleanReference.Length, cleanCurrent.Length) / CoordinatesPerTriangle > MaximumComparisonTriangles)
            {
                preview.Issue = "至少一个模型的显示网格过大，未计算叠加差异。可切换到单件查看。";
                return preview;
            }

            var referenceIndex = BuildIndex(cleanReference);
            var currentIndex = BuildIndex(cleanCurrent);
            preview.CurrentOnly = Classify(cleanCurrent, referenceIndex, out var shared);
            preview.Shared = shared;
            preview.ReferenceOnly = Classify(cleanReference, currentIndex, out _, collectShared: false);
            preview.Compared = true;
            return preview;
        }

        private static float[] Clean(float[] coordinates)
        {
            if (coordinates == null || coordinates.Length == 0 || coordinates.Length % CoordinatesPerTriangle != 0)
                return Array.Empty<float>();

            var clean = new List<float>(coordinates.Length);
            var seen = new HashSet<(Point A, Point B, Point C)>();
            for (int i = 0; i < coordinates.Length; i += CoordinatesPerTriangle)
            {
                var a = new Point(coordinates, i);
                var b = new Point(coordinates, i + 3);
                var c = new Point(coordinates, i + 6);
                if (!a.IsFinite || !b.IsFinite || !c.IsFinite || Point.Cross(b - a, c - a).LengthSquared == 0)
                    continue;

                if (a.CompareTo(b) > 0) Swap(ref a, ref b);
                if (b.CompareTo(c) > 0) Swap(ref b, ref c);
                if (a.CompareTo(b) > 0) Swap(ref a, ref b);
                if (seen.Add((a, b, c)))
                    AppendTriangle(clean, coordinates, i);
            }
            return clean.Count == coordinates.Length ? coordinates : clean.ToArray();
        }

        private static void Swap(ref Point a, ref Point b)
        {
            var value = a;
            a = b;
            b = value;
        }

        private static float[] Classify(float[] coordinates, IndexNode target, out float[] shared, bool collectShared = true)
        {
            var different = new List<float>();
            var matching = collectShared ? new List<float>() : null;
            double limit = VisualToleranceMetres * VisualToleranceMetres;
            for (int i = 0; i < coordinates.Length; i += CoordinatesPerTriangle)
            {
                var a = new Point(coordinates, i);
                var b = new Point(coordinates, i + 3);
                var c = new Point(coordinates, i + 6);
                bool overlaps = target.Near((a + b + c) * (1d / 3d), limit) &&
                    target.Near(a, limit) && target.Near(b, limit) && target.Near(c, limit);
                var output = overlaps ? matching : different;
                if (output != null)
                    AppendTriangle(output, coordinates, i);
            }
            shared = matching?.ToArray();
            return different.ToArray();
        }

        private static void AppendTriangle(List<float> target, float[] source, int start)
        {
            for (int i = 0; i < CoordinatesPerTriangle; i++)
                target.Add(source[start + i]);
        }

        private static IndexNode BuildIndex(float[] coordinates)
        {
            var triangles = new Triangle[coordinates.Length / CoordinatesPerTriangle];
            for (int i = 0; i < triangles.Length; i++)
                triangles[i] = new Triangle(coordinates, i * CoordinatesPerTriangle);
            return new IndexNode(triangles, 0, triangles.Length);
        }

        private sealed class IndexNode
        {
            private readonly Triangle[] triangles;
            private readonly int start;
            private readonly int count;
            private readonly Point min;
            private readonly Point max;
            private readonly IndexNode left;
            private readonly IndexNode right;

            internal IndexNode(Triangle[] triangles, int start, int count)
            {
                this.triangles = triangles;
                this.start = start;
                this.count = count;
                min = triangles[start].Min;
                max = triangles[start].Max;
                for (int i = start + 1; i < start + count; i++)
                {
                    min = Point.Min(min, triangles[i].Min);
                    max = Point.Max(max, triangles[i].Max);
                }

                if (count <= 12)
                    return;

                var span = max - min;
                int axis = span.X >= span.Y && span.X >= span.Z ? 0 : span.Y >= span.Z ? 1 : 2;
                Array.Sort(triangles, start, count, Comparer<Triangle>.Create((a, b) =>
                    (a.Min.Axis(axis) + a.Max.Axis(axis)).CompareTo(b.Min.Axis(axis) + b.Max.Axis(axis))));
                int half = count / 2;
                left = new IndexNode(triangles, start, half);
                right = new IndexNode(triangles, start + half, count - half);
            }

            internal bool Near(Point point, double limit)
            {
                var nearest = Point.Min(Point.Max(min, point), max);
                if ((point - nearest).LengthSquared > limit)
                    return false;
                if (left != null)
                    return left.Near(point, limit) || right.Near(point, limit);
                for (int i = start; i < start + count; i++)
                    if (triangles[i].DistanceSquared(point) <= limit)
                        return true;
                return false;
            }
        }

        private readonly struct Triangle
        {
            private readonly Point a;
            private readonly Point b;
            private readonly Point c;
            private readonly Point normal;

            internal Triangle(float[] coordinates, int i)
            {
                a = new Point(coordinates, i);
                b = new Point(coordinates, i + 3);
                c = new Point(coordinates, i + 6);
                normal = Point.Cross(b - a, c - a);
                Min = Point.Min(a, Point.Min(b, c));
                Max = Point.Max(a, Point.Max(b, c));
            }

            internal Point Min { get; }
            internal Point Max { get; }

            internal double DistanceSquared(Point point)
            {
                double planeDot = Point.Dot(point - a, normal);
                double normalLength = normal.LengthSquared;
                var projected = point - normal * (planeDot / normalLength);
                if (InsideEdge(a, b, projected) && InsideEdge(b, c, projected) && InsideEdge(c, a, projected))
                    return planeDot * planeDot / normalLength;

                return Math.Min(EdgeDistanceSquared(a, b, point),
                    Math.Min(EdgeDistanceSquared(b, c, point), EdgeDistanceSquared(c, a, point)));
            }

            private bool InsideEdge(Point from, Point to, Point projected) =>
                Point.Dot(Point.Cross(to - from, projected - from), normal) >= 0;

            private static double EdgeDistanceSquared(Point from, Point to, Point point)
            {
                var edge = to - from;
                double fraction = Math.Max(0, Math.Min(1, Point.Dot(edge, point - from) / edge.LengthSquared));
                return (point - (from + edge * fraction)).LengthSquared;
            }
        }

        private readonly struct Point : IEquatable<Point>, IComparable<Point>
        {
            internal readonly double X, Y, Z;
            internal Point(float[] a, int i) : this(a[i], a[i + 1], a[i + 2]) { }
            private Point(double x, double y, double z) { X = x; Y = y; Z = z; }
            internal double LengthSquared => Dot(this, this);
            internal bool IsFinite => !double.IsNaN(X + Y + Z) && !double.IsInfinity(X + Y + Z);
            internal double Axis(int axis) => axis == 0 ? X : axis == 1 ? Y : Z;
            public static Point operator +(Point a, Point b) => new Point(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
            public static Point operator -(Point a, Point b) => new Point(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
            public static Point operator *(Point a, double k) => new Point(a.X * k, a.Y * k, a.Z * k);
            internal static double Dot(Point a, Point b) => a.X * b.X + a.Y * b.Y + a.Z * b.Z;
            internal static Point Cross(Point a, Point b) => new Point(a.Y * b.Z - a.Z * b.Y, a.Z * b.X - a.X * b.Z, a.X * b.Y - a.Y * b.X);
            internal static Point Min(Point a, Point b) => new Point(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), Math.Min(a.Z, b.Z));
            internal static Point Max(Point a, Point b) => new Point(Math.Max(a.X, b.X), Math.Max(a.Y, b.Y), Math.Max(a.Z, b.Z));

            public int CompareTo(Point other)
            {
                int x = X.CompareTo(other.X);
                if (x != 0) return x;
                int y = Y.CompareTo(other.Y);
                return y != 0 ? y : Z.CompareTo(other.Z);
            }

            public bool Equals(Point other) => X == other.X && Y == other.Y && Z == other.Z;
            public override bool Equals(object obj) => obj is Point other && Equals(other);
            public override int GetHashCode() => unchecked(((X.GetHashCode() * 397) ^ Y.GetHashCode()) * 397 ^ Z.GetHashCode());
        }
    }
}

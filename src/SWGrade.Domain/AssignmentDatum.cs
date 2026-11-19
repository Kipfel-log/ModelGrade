using System;
using System.Linq;

namespace SWGrade.Domain
{
    public sealed class DatumPlane
    {
        public double X { get; set; }
        public double Y { get; set; }
        public double Z { get; set; }
        public double OffsetMetres { get; set; }
        public double AreaSquareMetres { get; set; }

        public double Dot(DatumPlane other) => X * other.X + Y * other.Y + Z * other.Z;
        public double Project(double x, double y, double z) => X * x + Y * y + Z * z;

        internal bool IsValid => Math.Abs(Dot(this) - 1) < 1e-6 &&
            Numeric.IsNonnegativeFinite(Math.Abs(OffsetMetres)) &&
            Numeric.IsNonnegativeFinite(AreaSquareMetres) && AreaSquareMetres > 0;

        public bool SameLocation(DatumPlane other, double toleranceMetres) => other != null &&
            Dot(other) >= 1 - 1e-10 && Math.Abs(OffsetMetres - other.OffsetMetres) <= toleranceMetres;
    }

    public sealed class AssignmentDatum
    {
        public string ReferenceHash { get; set; }
        public string Configuration { get; set; }
        public DatumPlane[] Planes { get; set; }

        public bool AppliesTo(string hash, string configuration) => !string.IsNullOrEmpty(hash) &&
            string.Equals(hash, ReferenceHash, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(configuration, Configuration, StringComparison.Ordinal);

        public bool SameDefinition(AssignmentDatum other) => other != null &&
            AppliesTo(other.ReferenceHash, other.Configuration) && Planes?.Length == 3 && other.Planes?.Length == 3 &&
            Planes.Zip(other.Planes, (a, b) => a?.SameLocation(b, 1e-9) == true).All(equal => equal);

        public void Validate()
        {
            if (ReferenceHash?.Length != 64 || !ReferenceHash.All(Uri.IsHexDigit) ||
                string.IsNullOrWhiteSpace(Configuration) || Planes?.Length != 3 || Planes.Any(p => p?.IsValid != true))
                throw new ArgumentException("请选择标准件上三个有效的外侧平面作为作业基准。");
            var a = Planes[0];
            var b = Planes[1];
            var c = Planes[2];
            double determinant = a.Project(b.Y * c.Z - b.Z * c.Y, b.Z * c.X - b.X * c.Z, b.X * c.Y - b.Y * c.X);
            if (Math.Abs(determinant) < .1)
                throw new ArgumentException("三个基准面需要同时确定姿态和原点。请选择三个法向独立的外侧平面。");
        }
    }
}

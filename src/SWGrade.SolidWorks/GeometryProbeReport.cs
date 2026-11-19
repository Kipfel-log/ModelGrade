using System;
using System.Linq;
using System.Runtime.Serialization;
using SWGrade.Domain;

namespace SWGrade.SolidWorks
{
    public enum GeometryProbeAlignment { OriginalCoordinates, CoincidentShape, ReusePlacement, AssignmentDatum }

    [DataContract]
    public sealed class GeometryProbeReport
    {
        public const string NoCoincidence = "NoCoincidence";
        [DataMember] public string ReferencePath { get; internal set; }
        [DataMember] public string StudentPath { get; internal set; }
        [DataMember] public string RequestedReferenceConfiguration { get; internal set; }
        [DataMember] public string RequestedStudentConfiguration { get; internal set; }
        [DataMember] public string CadRevision { get; internal set; }
        [DataMember] public GeometryProbeAlignment Alignment { get; internal set; }
        [DataMember] public string AlignmentState { get; internal set; }
        [DataMember(EmitDefaultValue = false)] public AssignmentDatum Datum { get; internal set; }
        [DataMember(EmitDefaultValue = false)] public int? DatumMatchedPlanes { get; internal set; }
        [DataMember] public double[] StudentToReferenceTransform { get; internal set; }
        [DataMember(EmitDefaultValue = false)] public GeometryPlacementSource PlacementSource { get; internal set; }
        [DataMember] public ModelInspectionResult Reference { get; internal set; }
        [DataMember] public ModelInspectionResult Student { get; internal set; }
        [DataMember] public double? ReferenceVolumeM3 { get; internal set; }
        [DataMember] public double? StudentVolumeM3 { get; internal set; }
        [DataMember] public GeometryProbeOperation Missing { get; internal set; }
        [DataMember] public GeometryProbeOperation Extra { get; internal set; }
        [DataMember] public GeometryProbeOperation Overlap { get; internal set; }
        [DataMember] public double? DifferenceRatio { get; internal set; }
        [DataMember] public bool Measured { get; internal set; }
        [DataMember] public string ReviewReason { get; internal set; }
        [IgnoreDataMember] public SurfaceDeviation Deviation { get; internal set; }
        [IgnoreDataMember] public string DeviationIssue { get; internal set; }

        public GeometryEvidence EvidenceFor(ModelInspectionResult reference, ModelInspectionResult student)
        {
            bool matches = SameSnapshot(Reference, reference) && SameSnapshot(Student, student);
            bool verified = Measured && matches &&
                (Alignment == GeometryProbeAlignment.OriginalCoordinates || AlignmentState == "CoincidenceVerified" ||
                    Alignment == GeometryProbeAlignment.AssignmentDatum && AlignmentState == "DatumVerified");
            return new GeometryEvidence
            {
                ValidatedComparison = verified,
                Source = "SolidWorksBRep",
                Coordinates = Alignment == GeometryProbeAlignment.AssignmentDatum ? GeometryCoordinates.Datum :
                    Alignment == GeometryProbeAlignment.OriginalCoordinates ? GeometryCoordinates.Original : GeometryCoordinates.Aligned,
                Datum = Datum,
                DatumMatchedPlanes = DatumMatchedPlanes,
                ReferenceVolumeM3 = ReferenceVolumeM3,
                StudentVolumeM3 = StudentVolumeM3,
                MissingVolumeM3 = Missing?.VolumeM3,
                ExtraVolumeM3 = Extra?.VolumeM3,
                OverlapVolumeM3 = Overlap?.VolumeM3,
                ReferenceHash = Reference?.DocumentHash,
                StudentHash = Student?.DocumentHash,
                ReferenceConfiguration = Reference?.Configuration,
                StudentConfiguration = Student?.Configuration,
                CadRevision = CadRevision,
                AlignmentState = AlignmentState,
                StudentToReferenceTransform = StudentToReferenceTransform,
                Missing = CopyOperation(Missing),
                Extra = CopyOperation(Extra),
                Overlap = CopyOperation(Overlap),
                Reason = verified ? "SOLIDWORKS 实体差集与交集已通过体积守恒核对。" :
                    ReviewReason ?? "实体比较与本次读取的文件、配置或 CAD 修订版不一致。"
            };
        }

        private static bool SameSnapshot(ModelInspectionResult fromComparison, ModelInspectionResult fromInspection) =>
            fromComparison?.SourceIntegrityVerified == true && fromInspection?.SourceIntegrityVerified == true &&
            fromComparison.DocumentHash?.Length == 64 && !string.IsNullOrWhiteSpace(fromComparison.Configuration) &&
            !string.IsNullOrWhiteSpace(fromComparison.SolidWorksRevision) &&
            string.Equals(fromComparison.DocumentHash, fromInspection.DocumentHash, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(fromComparison.Configuration, fromInspection.Configuration, StringComparison.Ordinal) &&
            string.Equals(fromComparison.SolidWorksRevision, fromInspection.SolidWorksRevision, StringComparison.Ordinal) &&
            fromComparison.VolumeM3.HasValue && fromInspection.VolumeM3.HasValue &&
            GeometryProbeValidation.EqualVolumes(fromComparison.VolumeM3.Value, fromInspection.VolumeM3.Value);

        private static GeometryOperationEvidence CopyOperation(GeometryProbeOperation operation) => operation == null ? null :
            new GeometryOperationEvidence
            {
                State = operation.State,
                ErrorCode = operation.ErrorCode,
                ErrorName = operation.ErrorName,
                ReturnedValue = operation.ReturnedValue,
                BodyCount = operation.BodyCount,
                VolumeM3 = operation.VolumeM3,
                Reason = operation.Reason
            };
    }

    [DataContract]
    public sealed class GeometryPlacementSource
    {
        [DataMember] public string ReportPath { get; internal set; }
        [DataMember] public string CadRevision { get; internal set; }
        [DataMember] public string ReferenceHash { get; internal set; }
        [DataMember] public string ReferenceConfiguration { get; internal set; }
        [DataMember] public string StudentHash { get; internal set; }
        [DataMember] public string StudentConfiguration { get; internal set; }
    }

    [DataContract]
    public sealed class GeometryProbeOperation
    {
        [DataMember] public int? ErrorCode { get; internal set; }
        [DataMember] public string ErrorName { get; internal set; }
        [DataMember] public string ReturnedValue { get; internal set; }
        [DataMember] public int? BodyCount { get; internal set; }
        [DataMember] public string State { get; internal set; }
        [DataMember] public string Reason { get; internal set; }
        [DataMember] public double? VolumeM3 { get; internal set; }
        [DataMember(EmitDefaultValue = false)] public float[][] TrianglesMetres { get; internal set; }
        [DataMember(EmitDefaultValue = false)] public string MeshIssue { get; internal set; }
        [IgnoreDataMember] internal double[][] DeviationBounds { get; set; }
    }

    internal static class GeometryProbeValidation
    {
        internal static GeometryPlacementSource ReadPlacementSource(string path, GeometryProbeReport calibration)
        {
            if (calibration == null || calibration.Alignment != GeometryProbeAlignment.CoincidentShape ||
                calibration.AlignmentState != "CoincidenceVerified" || !calibration.Measured ||
                calibration.DifferenceRatio != 0 || calibration.Missing?.VolumeM3 != 0 || calibration.Extra?.VolumeM3 != 0 ||
                calibration.Overlap?.VolumeM3 == null || !calibration.ReferenceVolumeM3.HasValue || !calibration.StudentVolumeM3.HasValue ||
                !EqualVolumes(calibration.ReferenceVolumeM3.Value, calibration.Overlap.VolumeM3.Value) ||
                !EqualVolumes(calibration.StudentVolumeM3.Value, calibration.Overlap.VolumeM3.Value) ||
                string.IsNullOrWhiteSpace(calibration.CadRevision) ||
                !HasPlacementIdentity(calibration.Reference) || !HasPlacementIdentity(calibration.Student) ||
                !IsProperRigidTransform(calibration.StudentToReferenceTransform))
                throw new InvalidOperationException("请指定一份对齐成功且零差异的 CoincidenceVerified 报告。");

            return new GeometryPlacementSource
            {
                ReportPath = path,
                CadRevision = calibration.CadRevision,
                ReferenceHash = calibration.Reference.DocumentHash,
                ReferenceConfiguration = calibration.Reference.Configuration,
                StudentHash = calibration.Student.DocumentHash,
                StudentConfiguration = calibration.Student.Configuration
            };
        }

        private static bool HasPlacementIdentity(ModelInspectionResult inspection) =>
            inspection != null && inspection.SourceIntegrityVerified &&
            inspection.DocumentHash?.Length == 64 && !string.IsNullOrWhiteSpace(inspection.Configuration);

        internal static void CheckPlacementReference(GeometryPlacementSource source, ModelInspectionResult reference)
        {
            if (!string.Equals(source.ReferenceHash, reference.DocumentHash, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(source.ReferenceConfiguration, reference.Configuration, StringComparison.Ordinal))
                throw new InvalidOperationException("参考件的文件内容或配置与对齐报告不同，请重新验证放置方式。");
        }

        internal static void CheckPlacementStudent(GeometryPlacementSource source, ModelInspectionResult student)
        {
            if (!string.Equals(source.StudentConfiguration, student.Configuration, StringComparison.Ordinal))
                throw new InvalidOperationException("学生件的配置与对齐报告不同，请重新验证放置方式。");
        }

        // Numerical consistency guard for the experiment, not a grading tolerance.
        internal static bool EqualVolumes(double expected, double actual) =>
            expected > 0 && !double.IsInfinity(expected) && actual >= 0 && !double.IsInfinity(actual) &&
            Math.Abs(expected - actual) <= Math.Max(expected * 1e-8, 1e-24);

        internal static string ResolveVolumes(double reference, double student, GeometryProbeOperation missing,
            GeometryProbeOperation extra, GeometryProbeOperation overlap)
        {
            if (missing.State == "Failed" || extra.State == "Failed" || overlap.State == "Failed")
                return "至少一项 SOLIDWORKS 布尔运算失败。";

            double? common = overlap.VolumeM3;
            if (!common.HasValue && (overlap.State == "EmptyUnconfirmed" || overlap.State == "NoIntersectionReported") &&
                missing.VolumeM3.HasValue && extra.VolumeM3.HasValue &&
                EqualVolumes(reference, missing.VolumeM3.Value) && EqualVolumes(student, extra.VolumeM3.Value))
                common = 0;

            double? missingVolume = ResolveCut(reference, missing, common);
            double? extraVolume = ResolveCut(student, extra, common);
            if (!common.HasValue || !missingVolume.HasValue || !extraVolume.HasValue ||
                !EqualVolumes(reference, common.Value + missingVolume.Value) ||
                !EqualVolumes(student, common.Value + extraVolume.Value))
                return "布尔结果为空或体积不守恒，无法核实差集。";

            ConfirmIfEmpty(overlap, common.Value);
            ConfirmIfEmpty(missing, missingVolume.Value);
            ConfirmIfEmpty(extra, extraVolume.Value);
            return null;
        }

        private static double? ResolveCut(double minuend, GeometryProbeOperation operation, double? common) =>
            operation.VolumeM3 ?? (operation.State == "EmptyUnconfirmed" && common > 0 &&
                EqualVolumes(minuend, common.Value) ? (double?)0 : null);

        private static void ConfirmIfEmpty(GeometryProbeOperation operation, double volume)
        {
            if (!operation.VolumeM3.HasValue)
            {
                operation.VolumeM3 = volume;
                operation.State = "ConfirmedEmpty";
            }
        }

        internal static bool IsProperRigidTransform(double[] matrix)
        {
            if (matrix == null || matrix.Length != 16 || matrix.Any(value => double.IsNaN(value) || double.IsInfinity(value)) ||
                Math.Abs(matrix[12] - 1) > 1e-10)
                return false;

            for (int row = 0; row < 3; row++)
                for (int other = row; other < 3; other++)
                {
                    double dot = matrix[row * 3] * matrix[other * 3] + matrix[row * 3 + 1] * matrix[other * 3 + 1] +
                        matrix[row * 3 + 2] * matrix[other * 3 + 2];
                    if (Math.Abs(dot - (row == other ? 1 : 0)) > 1e-9)
                        return false;
                }

            double determinant = matrix[0] * (matrix[4] * matrix[8] - matrix[5] * matrix[7]) -
                matrix[1] * (matrix[3] * matrix[8] - matrix[5] * matrix[6]) +
                matrix[2] * (matrix[3] * matrix[7] - matrix[4] * matrix[6]);
            return Math.Abs(determinant - 1) <= 1e-9;
        }

        internal static bool SameRigidTransform(double[] requested, double[] returned)
        {
            if (!IsProperRigidTransform(requested) || !IsProperRigidTransform(returned))
                return false;

            // The last three components of SOLIDWORKS' array are unused.
            for (int i = 0; i < 13; i++)
                if (Math.Abs(requested[i] - returned[i]) > 1e-9)
                    return false;
            return true;
        }
    }
}

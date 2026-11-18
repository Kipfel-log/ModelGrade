using System.Collections.Generic;
using System.Runtime.Serialization;

namespace SWGrade.Domain
{
    [DataContract]
    public sealed class ModelInspectionResult
    {
        [DataMember(Name = "schemaVersion")]
        public int SchemaVersion { get; private set; } = 1;

        [DataMember(Name = "sourcePath")]
        public string SourcePath { get; internal set; }

        [DataMember(Name = "fromOpenCadDocument", EmitDefaultValue = false)]
        public bool FromOpenCadDocument { get; internal set; }

        [DataMember(Name = "openCadDocumentNeedsSave", EmitDefaultValue = false)]
        public bool? OpenCadDocumentNeedsSave { get; internal set; }

        [DataMember(Name = "documentHash")]
        public string DocumentHash { get; internal set; }

        [DataMember(Name = "documentType")]
        public string DocumentType { get; internal set; }

        [DataMember(Name = "solidWorksVersion")]
        public string SolidWorksVersion { get; internal set; }

        [DataMember(Name = "solidWorksRevision")]
        public string SolidWorksRevision { get; internal set; }

        [DataMember(Name = "configuration")]
        public string Configuration { get; internal set; }

        [DataMember(Name = "openedSuccessfully")]
        public bool OpenedSuccessfully { get; internal set; }

        [DataMember(Name = "openWarningCode")]
        public int OpenWarningCode { get; internal set; }

        [DataMember(Name = "reviewOpenWarningCode")]
        public int ReviewOpenWarningCode { get; internal set; }

        [DataMember(Name = "rebuildState")]
        public string RebuildState { get; internal set; }

        [DataMember(Name = "rebuildStatusCode")]
        public int RebuildStatusCode { get; internal set; }

        [DataMember(Name = "solidBodyCount")]
        public int SolidBodyCount { get; internal set; }

        [DataMember(Name = "surfaceBodyCount")]
        public int SurfaceBodyCount { get; internal set; }

        [DataMember(Name = "sheetMetal", EmitDefaultValue = false)]
        public SheetMetalInspection SheetMetal { get; internal set; }

        [DataMember(Name = "solidFaceCount", EmitDefaultValue = false)]
        public int? SolidFaceCount { get; internal set; }

        [DataMember(Name = "surfaceFaceCount", EmitDefaultValue = false)]
        public int? SurfaceFaceCount { get; internal set; }

        [DataMember(Name = "volumeM3")]
        public double? VolumeM3 { get; internal set; }

        [DataMember(Name = "surfaceAreaM2")]
        public double? SurfaceAreaM2 { get; internal set; }

        [DataMember(Name = "massKg")]
        public double? MassKg { get; internal set; }

        [DataMember(Name = "materialName")]
        public string MaterialName { get; internal set; }

        [DataMember(Name = "densityKgPerM3")]
        public double? DensityKgPerM3 { get; internal set; }

        [DataMember(Name = "featureTypes")]
        internal Dictionary<string, int> FeatureCounts { get; private set; } = new Dictionary<string, int>();

        public IReadOnlyDictionary<string, int> FeatureTypes => FeatureCounts;

        [DataMember(Name = "activeFeatureTypes")]
        internal Dictionary<string, int> ActiveFeatureCounts { get; private set; } = new Dictionary<string, int>();

        public IReadOnlyDictionary<string, int> ActiveFeatureTypes => ActiveFeatureCounts;

        public bool FeatureCountsComplete => FeatureTraversalComplete;

        public bool IsActiveFeatureCountVerified(string type) =>
            FeatureTraversalComplete && UnverifiedFeatureTypes?.Contains(type) == false;

        [DataMember(Name = "unverifiedFeatureTypes")]
        internal HashSet<string> UnverifiedFeatureTypes { get; private set; } = new HashSet<string>();

        [DataMember(Name = "featureTraversalComplete")]
        internal bool FeatureTraversalComplete { get; set; }


        [DataMember(Name = "warnings")]
        internal List<string> WarningMessages { get; private set; } = new List<string>();

        public IReadOnlyList<string> Warnings => WarningMessages;

        [DataMember(Name = "errors")]
        internal List<string> ErrorMessages { get; private set; } = new List<string>();

        public IReadOnlyList<string> Errors => ErrorMessages;

        [DataMember(Name = "sourceIntegrityVerified")]
        public bool SourceIntegrityVerified { get; internal set; }
    }
}

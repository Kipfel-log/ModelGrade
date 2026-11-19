using System;
using System.Threading.Tasks;
using SWGrade.Domain;
using SWGrade.SolidWorks;

namespace SWGrade.Desktop
{
    internal static class CadGeometryComparison
    {
        internal static Task<GeometryProbeReport> CompareAsync(ModelInspectionResult reference,
            ModelInspectionResult student, GeometryCoordinates coordinates, bool includeMesh, Action<string> report,
            bool includeDeviation = false) =>
            StaWorker.Run(() => CadAnalysis.ProbeGeometry(reference.SourcePath, student.SourcePath,
                reference.Configuration, student.Configuration, reference.SolidWorksVersion,
                coordinates == GeometryCoordinates.Aligned ? GeometryProbeAlignment.CoincidentShape :
                    GeometryProbeAlignment.OriginalCoordinates, includeMesh, report, includeDeviation: includeDeviation));
    }
}

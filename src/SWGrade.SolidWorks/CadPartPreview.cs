using System;
using SWGrade.Domain;

namespace SWGrade.SolidWorks
{
    public sealed class CadPartPreview
    {
        public ModelInspectionResult Inspection { get; internal set; }
        public float[] TriangleCoordinates { get; internal set; }
        public string PreviewIssue { get; internal set; }

        internal void ReadMesh(CadInterop interop, object part)
        {
            try
            {
                // Display tessellation only. True requests the system units (metres).
                // https://help.solidworks.com/2025/english/api/sldworksapi/SolidWorks.Interop.sldworks~SolidWorks.Interop.sldworks.IPartDoc~GetTessTriangles.html
                var coordinates = interop.Call("IPartDoc", part, "GetTessTriangles", true) as float[];
                if (!IsTriangleArray(coordinates))
                {
                    PreviewIssue = "SOLIDWORKS 未返回可显示的三角网格。";
                    return;
                }
                TriangleCoordinates = coordinates;
            }
            catch (Exception exception)
            {
                PreviewIssue = "三维预览不可用。 " + CadAnalysis.DescribeError(exception);
            }
        }

        private static bool IsTriangleArray(float[] coordinates)
        {
            if (coordinates == null || coordinates.Length == 0 || coordinates.Length % 9 != 0)
                return false;
            foreach (float coordinate in coordinates)
            {
                if (float.IsInfinity(coordinate) || float.IsNaN(coordinate))
                    return false;
            }
            return true;
        }
    }
}

using System;
using System.Collections.Generic;
using SWGrade.Domain;

namespace SWGrade.SolidWorks
{
    public sealed class CadPartPreview
    {
        public ModelInspectionResult Inspection { get; internal set; }
        public float[] TriangleCoordinates { get; internal set; }
        public string PreviewIssue { get; internal set; }
        public float[] FlatTriangleCoordinates { get; private set; }
        public string FlatPreviewIssue { get; private set; }

        internal void ReadMesh(CadInterop interop, object part)
        {
            try
            {
                // Display tessellation only. True requests the system units (metres).
                // https://help.solidworks.com/2025/english/api/sldworksapi/SolidWorks.Interop.sldworks~SolidWorks.Interop.sldworks.IPartDoc~GetTessTriangles.html
                int triangleCount = (int)interop.Call("IPartDoc", part, "GetTessTriangleCount");
                var coordinates = interop.Call("IPartDoc", part, "GetTessTriangles", true) as float[];
                if (!IsTriangleArray(coordinates) || triangleCount != coordinates.Length / 9)
                {
                    PreviewIssue = "SOLIDWORKS 未返回完整的显示网格。请在 CAD 中确认零件可见，再重新分析。";
                    interop.Log("显示网格未通过校验：三角形计数 {0}，坐标数组长度 {1}。", triangleCount, coordinates?.Length ?? 0);
                    return;
                }
                TriangleCoordinates = coordinates;
            }
            catch (Exception exception)
            {
                PreviewIssue = "三维预览不可用。请在 CAD 中确认零件可见，再重新分析。";
                interop.Log("读取显示网格失败：" + CadAnalysis.DescribeError(exception));
            }
        }

        internal void ReadSheetMesh(CadInterop cad, object body, bool flat)
        {
            try
            {
                var coordinates = Tessellate(cad, body);
                if (flat)
                    FlatTriangleCoordinates = coordinates;
                else
                    TriangleCoordinates = coordinates;
            }
            catch (Exception exception)
            {
                const string message = "已取得钣金度量，暂时无法显示该状态的预览。";
                if (flat)
                    FlatPreviewIssue = message;
                else
                    PreviewIssue = message;
                cad.Log("读取钣金网格失败：" + CadAnalysis.DescribeError(exception));
            }
        }

        private static float[] Tessellate(CadInterop cad, object body)
        {
            // Use the rebuilt body, not the cached display triangles for the other state.
            // https://help.solidworks.com/2020/English/api/sldworksapi/Tessellate_a_Body_Example_CSharp.htm
            const string api = "ITessellation";
            object tessellation = cad.Call("IBody2", body, "GetTessellation", (object)null);
            if (!(bool)cad.Call(api, tessellation, "Tessellate"))
                throw new InvalidOperationException("CAD 无法对钣金实体生成网格。");

            int count = (int)cad.Call(api, tessellation, "GetFacetCount");
            if (count <= 0 || count > 250000)
                throw new InvalidOperationException("钣金网格的大小超出可预览范围。");

            var coordinates = new float[count * 9];
            var vertices = new Dictionary<int, double[]>();
            var ids = new List<int>(3);
            for (int facet = 0; facet < count; facet++)
            {
                ids.Clear();
                var fins = cad.Call(api, tessellation, "GetFacetFins", facet) as int[];
                if (fins?.Length != 3)
                    throw new InvalidOperationException("钣金网格包含无法读取的三角面。");

                foreach (int fin in fins)
                {
                    var edge = cad.Call(api, tessellation, "GetFinVertices", fin) as int[];
                    if (edge?.Length != 2)
                        throw new InvalidOperationException("钣金网格包含无法读取的边。");
                    foreach (int vertexId in edge)
                        if (!ids.Contains(vertexId))
                            ids.Add(vertexId);
                }
                if (ids.Count != 3)
                    throw new InvalidOperationException("CAD 返回的三角面顶点数量异常。");

                for (int index = 0; index < ids.Count; index++)
                {
                    int vertexId = ids[index];
                    if (!vertices.TryGetValue(vertexId, out var point))
                    {
                        point = cad.Call(api, tessellation, "GetVertexPoint", vertexId) as double[];
                        if (point?.Length != 3)
                            throw new InvalidOperationException("钣金网格缺少顶点坐标。");
                        vertices.Add(vertexId, point);
                    }
                    for (int axis = 0; axis < 3; axis++)
                        coordinates[facet * 9 + index * 3 + axis] = (float)point[axis];
                }
            }
            if (!IsTriangleArray(coordinates))
                throw new InvalidOperationException("CAD 返回的钣金网格坐标无效。");
            return coordinates;
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

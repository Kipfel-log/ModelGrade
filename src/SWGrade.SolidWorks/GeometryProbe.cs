using System;
using System.Collections.Generic;
using System.Linq;

namespace SWGrade.SolidWorks
{
    internal sealed class GeometryProbe
    {
        private const string Body = "IBody2";
        private readonly CadInterop cad;
        private readonly object app;
        private readonly int solid;
        private readonly int success;
        private readonly int noIntersect;

        internal GeometryProbe(CadInterop cad, object app)
        {
            this.cad = cad;
            this.app = app;
            solid = cad.Constant("swBodyType_e", "swSolidBody");
            success = cad.Constant("swBodyOperationError_e", "swBodyOperationNoError");
            noIntersect = cad.Constant("swBodyOperationError_e", "swBodyOperationNoIntersect");
        }

        internal object CaptureSolid(object part)
        {
            var solids = cad.Call("IPartDoc", part, "GetBodies2", solid, false) as Array;
            int sheet = cad.Constant("swBodyType_e", "swSheetBody");
            var sheets = cad.Call("IPartDoc", part, "GetBodies2", sheet, false) as Array;
            if (solids?.Length != 1 || sheets?.Length > 0)
                throw new InvalidOperationException("几何探针要求恰好一个实体，且没有曲面体。");

            object body = solids.GetValue(solids.GetLowerBound(0));
            if ((bool)cad.Call(Body, body, "IsSheetMetal"))
                throw new InvalidOperationException("钣金折弯及展开配置需要单独核实，本阶段几何探针不比较钣金。");
            ValidateTopology(body);
            return CopySolid(body);
        }

        internal void Compare(GeometryProbeReport report, object reference, object student, bool includeMesh, double[] placement,
            bool includeDeviation)
        {
            double referenceVolume = PartProbe.ReadBodyMeasurements(cad, reference).Volume;
            double studentVolume = PartProbe.ReadBodyMeasurements(cad, student).Volume;
            report.ReferenceVolumeM3 = referenceVolume;
            report.StudentVolumeM3 = studentVolume;

            if (report.Alignment == GeometryProbeAlignment.CoincidentShape)
                AlignStudent(report, reference, student, studentVolume);
            else if (report.Alignment == GeometryProbeAlignment.ReusePlacement)
                ApplySavedPlacement(report, student, studentVolume, placement);

            int cut = cad.Constant("swBodyOperationType_e", "SWBODYCUT");
            int intersect = cad.Constant("swBodyOperationType_e", "SWBODYINTERSECT");
            report.Missing = Operate("参考 − 学生", reference, student, cut, false, includeMesh, includeDeviation);
            report.Extra = Operate("学生 − 参考", student, reference, cut, false, includeMesh, includeDeviation);
            report.Overlap = Operate("参考 ∩ 学生", reference, student, intersect, true, includeMesh, false);

            var reason = GeometryProbeValidation.ResolveVolumes(referenceVolume, studentVolume,
                report.Missing, report.Extra, report.Overlap);
            if (reason != null)
                throw new InvalidOperationException(reason);

            double difference = report.Missing.VolumeM3.Value + report.Extra.VolumeM3.Value;
            if (report.Alignment == GeometryProbeAlignment.CoincidentShape)
            {
                if (!GeometryProbeValidation.EqualVolumes(referenceVolume, referenceVolume + difference) ||
                    !GeometryProbeValidation.EqualVolumes(studentVolume, report.Overlap.VolumeM3.Value))
                    throw new InvalidOperationException("候选变换未通过双向差集与交集验证。");
                report.AlignmentState = "CoincidenceVerified";
            }

            double ratio = difference / referenceVolume;
            if (double.IsNaN(ratio) || double.IsInfinity(ratio))
                throw new InvalidOperationException("差异率超出可表示范围。");
            report.DifferenceRatio = ratio;
        }

        private void AlignStudent(GeometryProbeReport report, object reference, object student, double studentVolume)
        {
            // The returned matrix acts on the input body, so reference is the receiver.
            object[] args = { student, null };
            bool coincides;
            try
            {
                coincides = (bool)cad.Call(Body, reference, "GetCoincidenceTransform2", args);
            }
            finally
            {
                cad.Track(args[1]);
            }

            if (!coincides || args[1] == null)
            {
                report.AlignmentState = GeometryProbeReport.NoCoincidence;
                throw new InvalidOperationException("SOLIDWORKS 未找到可验证的形状重合变换。");
            }

            ApplyStudentTransform(report, student, studentVolume, args[1]);
            report.AlignmentState = "CandidateApplied";
        }

        private void ApplySavedPlacement(GeometryProbeReport report, object student, double studentVolume, double[] placement)
        {
            // SOLIDWORKS may correct invalid rotations at creation. Check the calibration before and after the call.
            if (!GeometryProbeValidation.IsProperRigidTransform(placement))
                throw new InvalidOperationException("对齐报告没有可复用的刚体旋转／平移。");

            var utility = cad.Call("ISldWorks", app, "GetMathUtility");
            if (utility == null)
                throw new InvalidOperationException("SOLIDWORKS 未提供数学工具，无法复用放置变换。");
            var transform = cad.Call("IMathUtility", utility, "CreateTransform", (object)placement);
            ApplyStudentTransform(report, student, studentVolume, transform, placement);
            report.AlignmentState = "PlacementReused";
        }

        private void ApplyStudentTransform(GeometryProbeReport report, object student, double studentVolume,
            object transform, double[] requested = null)
        {
            if (transform == null)
                throw new InvalidOperationException("SOLIDWORKS 未生成放置变换。");

            var matrix = cad.Get("IMathTransform", transform, "ArrayData") as double[];
            if (!GeometryProbeValidation.IsProperRigidTransform(matrix) ||
                (requested != null && !GeometryProbeValidation.SameRigidTransform(requested, matrix)) ||
                !(bool)cad.Call(Body, student, "ApplyTransform", transform) ||
                !GeometryProbeValidation.EqualVolumes(studentVolume, PartProbe.ReadBodyMeasurements(cad, student).Volume))
                throw new InvalidOperationException("学生件变换未通过刚体旋转／平移或体积核对。");

            report.StudentToReferenceTransform = matrix;
        }

        private GeometryProbeOperation Operate(string label, object master, object tool, int operation, bool intersect,
            bool mesh, bool deviation)
        {
            var result = new GeometryProbeOperation { State = "Failed" };
            int checkpoint = cad.ReferenceCheckpoint;
            try
            {
                // Operations2 invalidates its operands. Create fresh copies for every invocation.
                object left = CopySolid(master);
                object right = CopySolid(tool);
                if (ReferenceEquals(left, right) || ReferenceEquals(left, tool) || ReferenceEquals(right, master))
                    throw new InvalidOperationException("SOLIDWORKS 未返回两份独立布尔操作数。");

                object[] args = { operation, right, int.MinValue };
                object raw = cad.Call(Body, left, "Operations2", args);
                int error = (int)args[2];
                result.ErrorCode = error;
                result.ErrorName = cad.EnumName("swBodyOperationError_e", error);
                var bodies = raw as Array;
                result.ReturnedValue = raw == null ? "Null" : bodies == null ? "UnexpectedType" : "Array";
                result.BodyCount = bodies?.Length;
                bool empty = raw == null || (bodies?.Rank == 1 && bodies.Length == 0);
                if (error != success)
                {
                    if (intersect && empty && error == noIntersect)
                        result.State = "NoIntersectionReported";
                    else
                        result.Reason = "SOLIDWORKS 报告布尔错误。";
                    return result;
                }
                if (empty)
                {
                    result.State = "EmptyUnconfirmed";
                    return result;
                }
                if (bodies == null || bodies.Rank != 1)
                    throw new InvalidOperationException("SOLIDWORKS 返回了非一维实体数组。");

                double volume = 0;
                foreach (object body in bodies)
                {
                    VerifyTemporarySolid(body, null);
                    ValidateTopology(body);
                    volume += PartProbe.ReadBodyMeasurements(cad, body).Volume;
                }
                if (!(volume > 0) || double.IsInfinity(volume))
                    throw new InvalidOperationException("布尔结果实体的总体积无效。");

                result.VolumeM3 = volume;
                result.State = "BodiesMeasured";
                if (mesh)
                    ReadMeshes(result, bodies);
                if (deviation)
                    result.DeviationBounds = bodies.Cast<object>().Select(body => cad.Call(Body, body, "GetBodyBox") as double[]).ToArray();
            }
            catch (Exception exception)
            {
                result.Reason = CadAnalysis.DescribeError(exception);
            }
            finally
            {
                cad.ReleaseAfter(checkpoint);
                cad.Log("{0}: {1}; Error={2} {3}; Return={4}; Bodies={5}; Volume={6:G17} m³; {7}",
                    label, result.State, result.ErrorCode, result.ErrorName, result.ReturnedValue,
                    result.BodyCount, result.VolumeM3, result.Reason);
            }
            return result;
        }

        private void ReadMeshes(GeometryProbeOperation result, Array bodies)
        {
            try
            {
                var meshes = new List<float[]>(bodies.Length);
                long totalCoordinates = 0;
                foreach (object body in bodies)
                {
                    var coordinates = CadPartPreview.TessellateBody(cad, body);
                    totalCoordinates += coordinates.Length;
                    if (totalCoordinates > 6000000)
                        throw new InvalidOperationException("差集网格超出实验导出的大小限制。");
                    meshes.Add(coordinates);
                }
                result.TrianglesMetres = meshes.ToArray();
            }
            catch (Exception exception)
            {
                // Export of display triangles does not determine the B-Rep volume result.
                result.MeshIssue = CadAnalysis.DescribeError(exception);
            }
        }

        private void ValidateTopology(object body)
        {
            object faults = cad.Get(Body, body, "Check3");
            if (faults != null && (int)cad.Get("IFaultEntity", faults, "Count") != 0)
                throw new InvalidOperationException("SOLIDWORKS Check3 报告实体拓扑错误。");
        }

        private object CopySolid(object master)
        {
            object copy = cad.Call(Body, master, "Copy");
            VerifyTemporarySolid(copy, master);
            return copy;
        }

        private void VerifyTemporarySolid(object body, object master)
        {
            if (body == null || ReferenceEquals(body, master) ||
                !(bool)cad.Call(Body, body, "IsTemporaryBody") || (int)cad.Call(Body, body, "GetType") != solid)
                throw new InvalidOperationException("SOLIDWORKS 未返回所需的临时实体。");
        }
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using SWGrade.Domain;

namespace SWGrade.SolidWorks
{
    internal static class DatumCad
    {
        internal static IReadOnlyList<DatumPlane> ExteriorPlanes(CadInterop cad, object body)
        {
            var faces = cad.Call("IBody2", body, "GetFaces") as Array;
            var planes = new List<DatumPlane>();
            if (faces == null)
                return planes;
            foreach (object face in faces)
            {
                var surface = cad.Call("IFace2", face, "GetSurface");
                if (!(bool)cad.Call("ISurface", surface, "IsPlane") ||
                    !(cad.Get("IFace2", face, "Normal") is double[] normal) || normal.Length != 3 ||
                    !(cad.Get("ISurface", surface, "PlaneParams") is double[] data) || data.Length != 6)
                    continue;
                double length = Math.Sqrt(normal.Sum(value => value * value));
                if (!(length > 0) || double.IsInfinity(length))
                    continue;
                var plane = new DatumPlane
                {
                    X = normal[0] / length,
                    Y = normal[1] / length,
                    Z = normal[2] / length,
                    AreaSquareMetres = (double)cad.Call("IFace2", face, "GetArea")
                };
                if (!(plane.AreaSquareMetres > 0) || double.IsInfinity(plane.AreaSquareMetres))
                    continue;
                plane.OffsetMetres = plane.Project(data[3], data[4], data[5]);
                object[] extreme = { plane.X, plane.Y, plane.Z, double.NaN, double.NaN, double.NaN };
                cad.Call("IBody2", body, "GetExtremePoint", extreme);
                double boundary = plane.Project((double)extreme[3], (double)extreme[4], (double)extreme[5]);
                if (!(Math.Abs(boundary - plane.OffsetMetres) < 1e-7))
                    continue;
                var same = planes.Find(existing => existing.SameLocation(plane, 1e-7));
                if (same == null)
                    planes.Add(plane);
                else
                    same.AreaSquareMetres += plane.AreaSquareMetres;
            }
            return planes.OrderByDescending(plane => plane.AreaSquareMetres).ToArray();
        }
    }
}

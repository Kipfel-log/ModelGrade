namespace SWGrade.SolidWorks
{
    public sealed class SurfaceDeviation
    {
        internal SurfaceDeviation(DeviationSurface student, DeviationSurface reference)
        {
            Student = student;
            Reference = reference;
        }

        public DeviationSurface Student { get; }
        public DeviationSurface Reference { get; }
    }

    public sealed class DeviationSurface
    {
        internal DeviationSurface(float[] triangles, float[] millimetres)
        {
            TrianglesMetres = triangles;
            SignedMillimetres = millimetres;
        }

        public float[] TrianglesMetres { get; }
        public float[] SignedMillimetres { get; }
    }
}

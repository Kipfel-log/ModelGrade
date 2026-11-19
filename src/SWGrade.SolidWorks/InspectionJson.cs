using System;
using System.IO;
using System.Runtime.Serialization.Json;
using System.Text;

using SWGrade.Domain;

namespace SWGrade.SolidWorks
{
    public static class InspectionJson
    {
        public static string Serialize(ModelInspectionResult result) => SerializeReport(result);

        public static string SerializeComparison(ModelComparison result) => SerializeReport(result);

        public static string SerializeGeometryProbe(GeometryProbeReport result) => SerializeReport(result);

        internal static GeometryProbeReport ReadGeometryProbe(string path)
        {
            var serializer = CreateSerializer<GeometryProbeReport>();
            using (var stream = File.OpenRead(path))
                return (GeometryProbeReport)serializer.ReadObject(stream);
        }

        private static string SerializeReport<T>(T result) where T : class
        {
            if (result == null)
                throw new ArgumentNullException(nameof(result));

            var serializer = CreateSerializer<T>();
            using (var stream = new MemoryStream())
            {
                using (var writer = JsonReaderWriterFactory.CreateJsonWriter(stream, Encoding.UTF8, false, true))
                    serializer.WriteObject(writer, result);

                return Encoding.UTF8.GetString(stream.ToArray());
            }
        }

        private static DataContractJsonSerializer CreateSerializer<T>() =>
            new DataContractJsonSerializer(typeof(T), new DataContractJsonSerializerSettings { UseSimpleDictionaryFormat = true });

        public static void SaveNew(string path, string json)
        {
            if (!string.Equals(Path.GetExtension(path), ".json", StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("导出文件名必须以 .json 结尾。", nameof(path));

            // CreateNew also protects an existing link or file at the chosen path.
            using (var stream = File.Open(path, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            using (var writer = new StreamWriter(stream, new UTF8Encoding(false)))
                writer.Write(json);
        }
    }
}

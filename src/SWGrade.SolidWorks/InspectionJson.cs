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

        private static string SerializeReport<T>(T result) where T : class
        {
            if (result == null)
                throw new ArgumentNullException(nameof(result));

            var serializer = new DataContractJsonSerializer(typeof(T),
                new DataContractJsonSerializerSettings { UseSimpleDictionaryFormat = true });
            using (var stream = new MemoryStream())
            {
                using (var writer = JsonReaderWriterFactory.CreateJsonWriter(stream, Encoding.UTF8, false, true))
                    serializer.WriteObject(writer, result);

                return Encoding.UTF8.GetString(stream.ToArray());
            }
        }

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

using System;

namespace SWGrade.SolidWorks
{
    public sealed class OpenPartInfo
    {
        internal OpenPartInfo(int processId, string title, string path, string configuration, bool needsSave)
        {
            ProcessId = processId;
            Title = title;
            Path = path;
            Configuration = configuration;
            NeedsSave = needsSave;
        }

        internal int ProcessId { get; }
        public string Title { get; }
        public string Path { get; }
        public string Configuration { get; }
        public bool NeedsSave { get; }

        public bool CanRead => !string.IsNullOrWhiteSpace(Path) &&
            Path.EndsWith(".SLDPRT", StringComparison.OrdinalIgnoreCase);

        public string SavedState => string.IsNullOrWhiteSpace(Path) ? "尚未保存" :
            !CanRead ? "需要保存为 .SLDPRT" :
            NeedsSave ? "CAD 提示需保存" : "已保存";

        internal string ConfigurationForRead(string requested) =>
            !string.IsNullOrWhiteSpace(requested) ? requested :
            string.IsNullOrWhiteSpace(Configuration) ? null : Configuration;
    }
}

using System;
using System.Collections.Generic;

namespace SWGrade.SolidWorks
{
    public sealed class CadDetection
    {
        public string Windows { get; internal set; }
        public bool IsX64 { get; internal set; }
        public bool HasFramework48 { get; internal set; }
        public IReadOnlyList<int> InstalledYears { get; internal set; } = Array.Empty<int>();
        public bool InstallationChecked { get; internal set; }
        public bool? IsRunning { get; internal set; }
        public bool IsComConnected { get; internal set; }
        public string Revision { get; internal set; }
        public string ConnectedYear { get; internal set; }
        public bool CanAnalyze { get; internal set; }
        public string Message { get; internal set; }
    }
}

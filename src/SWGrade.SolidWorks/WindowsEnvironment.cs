using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;
using Microsoft.Win32;
using Microsoft.Win32.SafeHandles;

namespace SWGrade.SolidWorks
{
    internal static class WindowsEnvironment
    {
        internal const string CadProgId = "SldWorks.Application";
        private const int MinimumFrameworkRelease = 528040;
        private const int Windows10Build1903 = 18362;
        private const uint QueryLimitedInformation = 0x1000;

        internal static bool IsWindows11 => Environment.OSVersion.Version.Build >= 22000;

        private static bool IsLabTrial
        {
            get
            {
#if SWGRADE_CLASSROOM_LAB_1903
                return true;
#else
                return false;
#endif
            }
        }

        internal static bool IsSupportedClientVersion(bool client, Version version) =>
            client && version.Major == 10 && (version.Build >= 19045 || IsLabTrial && version.Build == Windows10Build1903);

        internal static void Report(Action<string> report, CadDetection detection = null)
        {
            var os = Environment.OSVersion.Version;
            void Log(string format, params object[] args) => report(string.Format(CultureInfo.InvariantCulture, format, args));
            if (IsLabTrial)
                Log("SWGrade 构建：Windows 10 1903 机房试用版（Windows 10 下使用 SOLIDWORKS 2025）。");
            using (var localMachine = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64))
            using (var currentVersion = localMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion"))
            using (var runtime = localMachine.OpenSubKey(@"SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full"))
            {
                bool client = string.Equals(currentVersion?.GetValue("InstallationType") as string,
                    "Client", StringComparison.OrdinalIgnoreCase);
                bool supportedVersion = IsSupportedClientVersion(client, os);
                string windows = os.Major == 10 && client ? (IsWindows11 ? "Windows 11" : "Windows 10") : "其他 Windows";
                Log("Windows：{0}，版本 {1}.{2}，build {3}.{4}，{5}，{6}",
                    windows,
                    os.Major, os.Minor, os.Build, currentVersion?.GetValue("UBR") ?? "未知",
                    currentVersion?.GetValue("DisplayVersion") ?? "版本标签未知",
                    currentVersion?.GetValue("EditionID") ?? "版本类型未知");
                int release = runtime?.GetValue("Release") as int? ?? 0;
                Log("系统 / Probe：{0} 位 / {1} 位；.NET Framework Release：{2}",
                    Environment.Is64BitOperatingSystem ? 64 : 32,
                    Environment.Is64BitProcess ? 64 : 32, release);

                if (detection != null)
                {
                    detection.Windows = windows;
                    detection.IsWindowsSupported = supportedVersion;
                    detection.IsX64 = Environment.Is64BitOperatingSystem && Environment.Is64BitProcess;
                    detection.HasFramework48 = release >= MinimumFrameworkRelease;
                }

                var installed = new List<int>();
                foreach (int year in new[] { 2025, 2026 })
                    if (ReportInstall(localMachine, year, report))
                        installed.Add(year);
                if (detection != null)
                {
                    detection.InstalledYears = installed.AsReadOnly();
                    detection.InstallationChecked = true;
                }

                var issues = new List<string>();
                if (!supportedVersion)
                {
                    string supported = IsLabTrial ? "Windows 10 1903、Windows 10 22H2 或 Windows 11" : "Windows 10 22H2 或 Windows 11";
                    issues.Add($"Windows 版本不满足要求（检测到 {windows}，内部版本 {os.Build}）。需要 {supported}。");
                }
                if (!Environment.Is64BitOperatingSystem || !Environment.Is64BitProcess)
                    issues.Add("需要在 64 位 Windows 上运行 x64 版 SWGrade。");
                if (release < MinimumFrameworkRelease)
                    issues.Add("未检测到 .NET Framework 4.8 或更新版本。可运行学生端离线包中的微软安装程序。");
                if (issues.Count != 0)
                    throw new InvalidOperationException(string.Join(" ", issues));
            }

            using (var classes = RegistryKey.OpenBaseKey(RegistryHive.ClassesRoot, RegistryView.Registry64))
            {
                foreach (string progId in new[] { CadProgId, CadProgId + ".33", CadProgId + ".34" })
                {
                    using (var registration = classes.OpenSubKey(progId + @"\CLSID"))
                        Log("COM 注册 {0}：{1}", progId, registration?.GetValue(null) ?? "未找到");
                }
            }
        }

        private static bool ReportInstall(RegistryKey localMachine, int year, Action<string> report)
        {
            using (var setup = localMachine.OpenSubKey(@"SOFTWARE\SolidWorks\SOLIDWORKS " + year + @"\Setup"))
            {
                var folder = setup?.GetValue("SolidWorks Folder") as string;
                string state = "未从安装注册表确认";
                bool installed = !string.IsNullOrWhiteSpace(folder) && File.Exists(Path.Combine(folder, "SLDWORKS.exe"));
                if (!string.IsNullOrWhiteSpace(folder))
                    state = installed ? "发现安装" : "有安装记录，未找到程序";

                report("SOLIDWORKS " + year + "：" + state);
                return installed;
            }
        }

        internal static bool IsCurrentUser(Process process)
        {
            using (var current = WindowsIdentity.GetCurrent())
            {
                // PROCESS_QUERY_LIMITED_INFORMATION is sufficient for OpenProcessToken.
                using (var processHandle = OpenProcess(QueryLimitedInformation, false, process.Id))
                {
                    if (processHandle.IsInvalid || !OpenProcessToken(processHandle, 0x0008, out var token))
                        throw new InvalidOperationException("无法确认 SOLIDWORKS 进程所属用户。",
                            new Win32Exception(Marshal.GetLastWin32Error()));

                    using (token)
                    using (var owner = new WindowsIdentity(token.DangerousGetHandle()))
                        return Equals(current.User, owner.User);
                }
            }
        }

        internal static string GetProcessImagePath(Process process)
        {
            using (var handle = OpenProcess(QueryLimitedInformation, false, process.Id))
            {
                var path = new StringBuilder(32768);
                int length = path.Capacity;
                if (handle.IsInvalid || !QueryFullProcessImageName(handle, 0, path, ref length))
                {
                    var error = new Win32Exception(Marshal.GetLastWin32Error());
                    throw new InvalidOperationException(
                        $"无法读取 SOLIDWORKS 进程路径（Windows 错误 {error.NativeErrorCode}）。{error.Message}", error);
                }
                return path.ToString();
            }
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern SafeFileHandle OpenProcess(uint access, bool inherit, int processId);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool QueryFullProcessImageName(SafeFileHandle process, uint flags, StringBuilder fileName, ref int size);

        [DllImport("advapi32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool OpenProcessToken(SafeFileHandle process, uint access, out SafeFileHandle token);
    }
}

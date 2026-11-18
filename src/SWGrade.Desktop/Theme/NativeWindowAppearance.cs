using System;
using System.Runtime.InteropServices;

namespace SWGrade.Desktop
{
    internal static class NativeWindowAppearance
    {
        // https://learn.microsoft.com/windows/win32/api/dwmapi/ne-dwmapi-dwmwindowattribute
        private const int UseImmersiveDarkMode = 20;
        private const int SystemBackdrop = 38;
        private const int NoBackdrop = 1;
        private const int MainWindowBackdrop = 2;
        private const int FirstSystemBackdropBuild = 22621;

        internal static bool Apply(IntPtr window, bool dark, bool preferMica)
        {
            int build = Environment.OSVersion.Version.Build;
            if (build < 22000)
                return false;

            int darkMode = dark ? 1 : 0;
            DwmSetWindowAttribute(window, UseImmersiveDarkMode, ref darkMode, sizeof(int));
            if (build < FirstSystemBackdropBuild)
                return false;

            int backdrop = preferMica ? MainWindowBackdrop : NoBackdrop;
            if (preferMica && DwmSetWindowAttribute(window, SystemBackdrop, ref backdrop, sizeof(int)) == 0)
            {
                var extended = new Margins { Left = -1, Right = -1, Top = -1, Bottom = -1 };
                if (DwmExtendFrameIntoClientArea(window, ref extended) == 0)
                    return true;
            }

            backdrop = NoBackdrop;
            DwmSetWindowAttribute(window, SystemBackdrop, ref backdrop, sizeof(int));
            var normal = new Margins();
            DwmExtendFrameIntoClientArea(window, ref normal);
            return false;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct Margins
        {
            public int Left;
            public int Right;
            public int Top;
            public int Bottom;
        }

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);

        [DllImport("dwmapi.dll")]
        private static extern int DwmExtendFrameIntoClientArea(IntPtr window, ref Margins margins);
    }
}

using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Media;
using Microsoft.Win32;
using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;
using Wpf.Ui.Markup;

namespace SWGrade.Desktop
{
    internal sealed class AppearanceSettings : INotifyPropertyChanged
    {
        private const string PreferenceKey = @"Software\SWGrade\Desktop";
        private bool dark;
        private bool preferMica = true;
        private string materialState = "不透明背景";

        internal AppearanceSettings()
        {
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(PreferenceKey))
                {
                    dark = (key?.GetValue("Dark") as int? ?? 0) != 0;
                    preferMica = (key?.GetValue("PreferMica") as int? ?? 1) != 0;
                }
            }
            catch (Exception exception) when (IsPreferenceError(exception))
            {
                // Theme settings are optional when the machine blocks per-user storage.
            }
        }

        public bool IsDark
        {
            get => dark;
            set
            {
                if (dark == value)
                    return;
                dark = value;
                Notify();
                Save();
                Changed?.Invoke(this, EventArgs.Empty);
            }
        }

        public bool PreferMica
        {
            get => preferMica;
            set
            {
                if (preferMica == value)
                    return;
                preferMica = value;
                Notify();
                Save();
                Changed?.Invoke(this, EventArgs.Empty);
            }
        }

        public string MaterialState
        {
            get => materialState;
            internal set { materialState = value; Notify(); }
        }

        internal bool CanUseMica()
        {
            if (!PreferMica || SystemParameters.HighContrast || !WindowBackdrop.IsSupported(WindowBackdropType.Mica))
                return false;

            try
            {
                using (var settings = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"))
                    return (settings?.GetValue("EnableTransparency") as int? ?? 1) != 0;
            }
            catch (Exception exception) when (IsPreferenceError(exception))
            {
                return false;
            }
        }

        internal void ApplyPalette()
        {
            var theme = SystemParameters.HighContrast ? ApplicationTheme.HighContrast :
                IsDark ? ApplicationTheme.Dark : ApplicationTheme.Light;

            // Apply control resources without WPF UI's window-background manager changing the native caption.
            foreach (var resources in System.Windows.Application.Current.Resources.MergedDictionaries)
                if (resources is ThemesDictionary themes)
                    themes.Theme = theme;
            if (theme == ApplicationTheme.HighContrast)
            {
                ApplyHighContrast();
                return;
            }

            ApplicationAccentColorManager.Apply(Color.FromRgb(42, 116, 103), theme, false, false);
            Set("Canvas", "#F2F5F6", "#161E23");
            Set("NavigationSurface", "#E9EFF1", "#1B2429");
            Set("Surface", "#FCFDFD", "#232E34");
            Set("SurfaceAlt", "#F1F5F6", "#2B363D");
            Set("ViewportSurface", "#E6EDF0", "#182328");
            Set("Ink", "#182C32", "#EDF3F4");
            Set("Muted", "#465C63", "#BECBD0");
            Set("Line", "#CCDADD", "#495B63");
            Set("Accent", "#176358", "#9BE1D2");
            Set("AccentSoft", "#DCEDE8", "#24443F");
            Set("HoverSurface", "#E7EFF1", "#314149");
            Set("PressedSurface", "#D8E7E9", "#3A4E56");
            Set("DisabledSurface", "#E7EBED", "#253137");
            Set("DisabledInk", "#596B71", "#9AAEB5");
            Set("BrandFill", "#14645A", "#7ECABB");
            Set("BrandHover", "#0F564D", "#95DBCE");
            Set("BrandPressed", "#104D45", "#68B4A6");
            Set("BrandInk", "#FFFFFF", "#092C26");
        }

        private void Save()
        {
            try
            {
                using (var key = Registry.CurrentUser.CreateSubKey(PreferenceKey))
                {
                    key?.SetValue("Dark", dark ? 1 : 0, RegistryValueKind.DWord);
                    key?.SetValue("PreferMica", preferMica ? 1 : 0, RegistryValueKind.DWord);
                }
            }
            catch (Exception exception) when (IsPreferenceError(exception))
            {
                // Changes still apply for this session.
            }
        }

        private static bool IsPreferenceError(Exception exception) =>
            exception is UnauthorizedAccessException || exception is System.Security.SecurityException ||
            exception is System.IO.IOException;

        private void Set(string key, string light, string darkValue) =>
            SetBrush(key, (Color)ColorConverter.ConvertFromString(IsDark ? darkValue : light));

        private static void ApplyHighContrast()
        {
            foreach (string key in new[] { "Canvas", "NavigationSurface", "Surface", "SurfaceAlt", "ViewportSurface", "HoverSurface", "PressedSurface", "AccentSoft", "DisabledSurface" })
                SetBrush(key, SystemColors.WindowColor);
            foreach (string key in new[] { "Ink", "Muted", "Line", "Accent" })
                SetBrush(key, SystemColors.WindowTextColor);
            foreach (string key in new[] { "BrandFill", "BrandHover", "BrandPressed" })
                SetBrush(key, SystemColors.HighlightColor);
            SetBrush("BrandInk", SystemColors.HighlightTextColor);
            SetBrush("DisabledInk", SystemColors.GrayTextColor);
        }

        private static void SetBrush(string key, Color color)
        {
            var brush = new SolidColorBrush(color);
            brush.Freeze();
            System.Windows.Application.Current.Resources[key] = brush;
        }

        internal event EventHandler Changed;
        public event PropertyChangedEventHandler PropertyChanged;
        private void Notify([CallerMemberName] string property = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(property));
    }
}

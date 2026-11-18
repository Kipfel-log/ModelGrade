param(
    [string] $ExePath = (Join-Path $PSScriptRoot '..\bin\SWGrade.Desktop.exe'),
    [string] $OutputDirectory = (Join-Path $PSScriptRoot '..\ui-evidence'),
    [ValidateSet(0, 100, 125, 150, 200)] [int] $ExpectedScale = 0,
    [int] $ExistingProcessId = 0,
    [ValidateSet('Current', 'Overview', 'Assignment', 'Settings')] [string] $Page = 'Current',
    [ValidateSet('Current', 'Expanded', 'Collapsed')] [string] $NavigationMode = 'Current',
    [switch] $RunDetection,
    [ValidatePattern('^[A-Za-z0-9_-]+$')] [string] $CaptureLabel = 'overview'
)

$ErrorActionPreference = 'Stop'
if ([Environment]::OSVersion.Platform -ne [PlatformID]::Win32NT) {
    throw 'Run this check in an interactive Windows desktop session.'
}

Add-Type -AssemblyName System.Drawing, UIAutomationClient, UIAutomationTypes
if (-not ('SWGradeCapture.Native' -as [type])) {
    Add-Type -TypeDefinition @'
using System;
using System.ComponentModel;
using System.Runtime.InteropServices;

namespace SWGradeCapture {
    [StructLayout(LayoutKind.Sequential)]
    public struct Rect {
        public int Left, Top, Right, Bottom;
    }

    public static class Native {
        [DllImport("user32.dll")]
        public static extern uint GetDpiForWindow(IntPtr window);

        [DllImport("user32.dll")]
        public static extern bool SetForegroundWindow(IntPtr window);

        [DllImport("user32.dll")]
        public static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        public static extern IntPtr SetThreadDpiAwarenessContext(IntPtr context);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool GetWindowRect(IntPtr window, out Rect bounds);

        [DllImport("dwmapi.dll")]
        private static extern int DwmGetWindowAttribute(IntPtr window, int attribute, out Rect bounds, int size);

        public static Rect GetPhysicalWindowBounds(IntPtr window) {
            Rect bounds;
            const int extendedFrameBounds = 9;
            if (DwmGetWindowAttribute(window, extendedFrameBounds, out bounds, Marshal.SizeOf(typeof(Rect))) == 0)
                return bounds;
            if (!GetWindowRect(window, out bounds))
                throw new Win32Exception(Marshal.GetLastWin32Error());
            return bounds;
        }
    }
}
'@
}

if ($ExistingProcessId -ne 0) {
    $desktop = Get-Process -Id $ExistingProcessId
    if ($desktop.ProcessName -ne 'SWGrade.Desktop') {
        throw 'The supplied process is not SWGrade.Desktop.'
    }
} else {
    $resolved = (Resolve-Path -LiteralPath $ExePath).Path
    $desktop = Start-Process -FilePath $resolved -WorkingDirectory (Split-Path -Parent $resolved) -PassThru
}

$window = [IntPtr]::Zero
for ($attempt = 0; $attempt -lt 80 -and $window -eq [IntPtr]::Zero; $attempt++) {
    Start-Sleep -Milliseconds 250
    $desktop.Refresh()
    if ($desktop.HasExited) {
        throw "SWGrade exited before showing a window. Exit code $($desktop.ExitCode)."
    }
    $window = $desktop.MainWindowHandle
}
if ($window -eq [IntPtr]::Zero) {
    throw 'SWGrade did not show a window within 20 seconds.'
}

$dpi = [SWGradeCapture.Native]::GetDpiForWindow($window)
if ($dpi -eq 0) {
    throw 'Could not determine effective window DPI.'
}
$scale = [int][Math]::Round(100.0 * $dpi / 96)
if ($ExpectedScale -ne 0 -and $scale -ne $ExpectedScale) {
    throw "The window is on a $scale% monitor. Expected $ExpectedScale%."
}

Start-Sleep -Milliseconds 900
$root = [System.Windows.Automation.AutomationElement]::FromHandle($window)
foreach ($name in '概览', '模型分析', '作业管理', '批量批改', '批改记录', '设置') {
    $condition = [System.Windows.Automation.PropertyCondition]::new(
        [System.Windows.Automation.AutomationElement]::NameProperty, $name)
    if ($null -eq $root.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $condition)) {
        throw "Could not find navigation item $name."
    }
}
$windowPattern = $root.GetCurrentPattern([System.Windows.Automation.WindowPattern]::Pattern)
if ($root.Current.Name -ne 'SWGrade' -or -not $windowPattern.Current.CanMinimize -or -not $windowPattern.Current.CanMaximize) {
    throw 'The window caption or its minimize/maximize capabilities are incorrect.'
}

function Find-ById([string] $Id) {
    $condition = [System.Windows.Automation.PropertyCondition]::new(
        [System.Windows.Automation.AutomationElement]::AutomationIdProperty, $Id)
    $root.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $condition)
}

function Invoke-Button($Element) {
    if ($null -eq $Element) {
        throw 'The expected button is missing.'
    }
    $Element.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
}

$toggle = Find-ById 'SidebarToggle'
if ($null -eq $toggle) {
    throw 'The sidebar toggle is missing.'
}
if ($NavigationMode -ne 'Current') {
    $expectToggle = if ($NavigationMode -eq 'Expanded') { '折叠导航' } else { '展开导航' }
    if ($toggle.Current.Name -ne $expectToggle) {
        Invoke-Button $toggle
        Start-Sleep -Milliseconds 350
    }
    if ($toggle.Current.Name -ne $expectToggle) {
        throw "Could not switch the sidebar to $NavigationMode."
    }
}
$settingsNav = Find-ById 'SettingsNavigation'
if ($null -eq $settingsNav -or $settingsNav.Current.IsOffscreen) {
    throw 'The fixed Settings navigation entry is missing or off-screen.'
}
$settingsBounds = $settingsNav.Current.BoundingRectangle
if ([Math]::Abs($root.Current.BoundingRectangle.Bottom - $settingsBounds.Bottom) -gt (48 * $dpi / 96)) {
    throw 'Settings navigation is not anchored at the bottom.'
}

if ($Page -eq 'Settings') {
    Invoke-Button $settingsNav
} elseif ($Page -ne 'Current') {
    $pageName = if ($Page -eq 'Assignment') { '作业管理' } else { '概览' }
    $nameCondition = [System.Windows.Automation.PropertyCondition]::new(
        [System.Windows.Automation.AutomationElement]::NameProperty, $pageName)
    $itemCondition = [System.Windows.Automation.PropertyCondition]::new(
        [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
        [System.Windows.Automation.ControlType]::ListItem)
    $selection = [System.Windows.Automation.AndCondition]::new(
        [System.Windows.Automation.Condition[]]@($nameCondition, $itemCondition))
    $navigationItem = $root.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $selection)
    if ($null -eq $navigationItem) {
        throw "Could not select navigation item $pageName."
    }
    $navigationItem.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
}
Start-Sleep -Milliseconds 350

if ($Page -eq 'Assignment' -and $null -eq (Find-ById 'StandardConfigurationChoice')) {
    throw 'The assignment editor or configuration selector is missing.'
}
if ($Page -eq 'Overview' -or $RunDetection) {
    $detect = Find-ById 'DetectEnvironment'
    if ($null -eq $detect) {
        throw 'The Overview environment detection button is missing.'
    }
    if ($RunDetection) {
        $deadline = (Get-Date).AddSeconds(30)
        while (-not $detect.Current.IsEnabled -and (Get-Date) -lt $deadline) {
            Start-Sleep -Milliseconds 200
        }
        Invoke-Button $detect
        Start-Sleep -Milliseconds 250
        $deadline = (Get-Date).AddSeconds(30)
        while (-not $detect.Current.IsEnabled -and (Get-Date) -lt $deadline) {
            Start-Sleep -Milliseconds 200
        }
        if (-not $detect.Current.IsEnabled) {
            throw 'The environment check did not finish within 30 seconds. Inspect SWGrade and SOLIDWORKS on the desktop.'
        }
    }
}

[void][SWGradeCapture.Native]::SetForegroundWindow($window)
Start-Sleep -Milliseconds 300
if ([SWGradeCapture.Native]::GetForegroundWindow() -ne $window) {
    throw 'Bring SWGrade to the foreground before capturing the window.'
}

$oldDpi = [SWGradeCapture.Native]::SetThreadDpiAwarenessContext([IntPtr](-4))
try {
    $bounds = [SWGradeCapture.Native]::GetPhysicalWindowBounds($window)
    $width = $bounds.Right - $bounds.Left
    $height = $bounds.Bottom - $bounds.Top
    if ($width -lt 1 -or $height -lt 1) {
        throw 'The SWGrade window has empty capture bounds.'
    }

    [void](New-Item -Path $OutputDirectory -ItemType Directory -Force)
    $stamp = Get-Date -Format 'yyyyMMdd-HHmmss-fff'
    $output = Join-Path $OutputDirectory "$CaptureLabel-$scale-pct-$stamp.png"
    $bitmap = [System.Drawing.Bitmap]::new($width, $height)
    try {
        $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
        try {
            $graphics.CopyFromScreen($bounds.Left, $bounds.Top, 0, 0, [System.Drawing.Size]::new($width, $height))
            $stream = [System.IO.File]::Open($output, [System.IO.FileMode]::CreateNew)
            try {
                $bitmap.Save($stream, [System.Drawing.Imaging.ImageFormat]::Png)
            } finally {
                $stream.Dispose()
            }
        } finally {
            $graphics.Dispose()
        }
    } finally {
        $bitmap.Dispose()
    }
    Write-Output "PASS navigation, caption capabilities and requested page. Effective scaling $scale%. PNG: $output"
    Write-Output "The app remains open. PID: $($desktop.Id)"
} finally {
    if ($oldDpi -ne [IntPtr]::Zero) {
        [void][SWGradeCapture.Native]::SetThreadDpiAwarenessContext($oldDpi)
    }
}

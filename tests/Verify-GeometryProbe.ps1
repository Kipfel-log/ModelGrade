# Uses actual native CAD. Provide the three saved fixtures described in docs/geometry-stage-a.md.
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)] [ValidateSet('2025', '2026')] [string] $ExpectedYear,
    [Parameter(Mandatory = $true)] [string] $Reference,
    [Parameter(Mandatory = $true)] [string] $Equivalent,
    [Parameter(Mandatory = $true)] [string] $MovedHole,
    [string] $Translated,
    [string] $Rotated,
    [string] $ProbePath = (Join-Path $PSScriptRoot '..\src\SWGrade.Probe\bin\x64\Release\net48\SWGrade.Probe.exe')
)

Set-StrictMode -Version 2.0
$ErrorActionPreference = 'Stop'
if ([Environment]::OSVersion.Platform -ne [PlatformID]::Win32NT) {
    throw 'Stage A requires Windows x64 and an interactive SOLIDWORKS session.'
}

$exe = (Get-Item -LiteralPath $ProbePath).FullName
$referencePath = (Get-Item -LiteralPath $Reference).FullName
$equivalentPath = (Get-Item -LiteralPath $Equivalent).FullName
$holePath = (Get-Item -LiteralPath $MovedHole).FullName
$work = Join-Path ([System.IO.Path]::GetTempPath()) ('SWGrade-Geometry-' + [guid]::NewGuid())
[void][System.IO.Directory]::CreateDirectory($work)
Write-Host "Stage A logs and real CAD evidence: $work"

function Get-SharedFileHash([string] $path) {
    # SOLIDWORKS can hold an open source part with write access.
    $stream = [System.IO.File]::Open($path, 'Open', 'Read', 'ReadWrite')
    try {
        (Get-FileHash -InputStream $stream -Algorithm SHA256).Hash
    }
    finally {
        $stream.Dispose()
    }
}

$originals = @{}
foreach ($path in @($referencePath, $equivalentPath, $holePath, $Translated, $Rotated)) {
    if ($path) {
        $file = Get-Item -LiteralPath $path
        $originals[$file.FullName] = @{
            Hash = Get-SharedFileHash $file.FullName
            WriteTime = $file.LastWriteTimeUtc
        }
    }
}
if ($originals.Count -lt 3 -or
    $originals[$referencePath].Hash -eq $originals[$equivalentPath].Hash -or
    $originals[$referencePath].Hash -eq $originals[$holePath].Hash) {
    throw 'Use three separate files with a different SHA-256 for each comparison fixture relative to the reference.'
}

function Get-OpenParts([string] $logname) {
    $lines = @(& $exe --list-open --expect $ExpectedYear)
    $exit = $LASTEXITCODE
    $logPath = Join-Path $work $logname
    $lines | Set-Content -LiteralPath $logPath -Encoding UTF8
    if ($exit -ne 0) {
        throw "The CAD document list could not be checked. See $logPath."
    }

    $lines | Where-Object { $_ -match '^.* \| .* \| .* \|(?: |$)' } | Sort-Object
}

function Invoke-GeometryCase([string] $name, [string] $student, [string] $alignment, [bool] $expectZero) {
    $studentPath = (Get-Item -LiteralPath $student).FullName
    $reportPath = Join-Path $work "$name.json"
    $logPath = Join-Path $work "$name.txt"
    & $exe --geometry --expect $ExpectedYear --reference $referencePath --student $studentPath --alignment $alignment --json $reportPath |
        Tee-Object -FilePath $logPath | Out-Host
    $exit = $LASTEXITCODE
    if ($exit -ne 0) {
        throw "Case $name needs review or could not run (exit $exit). See $logPath and $reportPath."
    }

    $data = Get-Content -LiteralPath $reportPath -Raw -Encoding UTF8 | ConvertFrom-Json
    if (-not $data.Measured -or [string]::IsNullOrWhiteSpace($data.CadRevision) -or
        -not $data.Reference.sourceIntegrityVerified -or -not $data.Student.sourceIntegrityVerified -or
        $data.Reference.documentHash -ine $originals[$referencePath].Hash -or
        $data.Student.documentHash -ine $originals[$studentPath].Hash -or
        $data.Reference.solidWorksVersion -ne $ExpectedYear -or $data.Student.solidWorksVersion -ne $ExpectedYear -or
        $null -eq $data.Missing.VolumeM3 -or $null -eq $data.Extra.VolumeM3 -or $null -eq $data.Overlap.VolumeM3 -or
        $null -eq $data.DifferenceRatio -or $data.ReferenceVolumeM3 -le 0 -or $data.StudentVolumeM3 -le 0) {
        throw "Case $name did not return verified real measurements. See $reportPath."
    }

    if ($expectZero -and $data.DifferenceRatio -gt 1e-8) {
        throw "Case $name should have no measurable difference. See $reportPath."
    }
    if (-not $expectZero -and ($data.Missing.VolumeM3 -le 0 -or $data.Extra.VolumeM3 -le 0)) {
        throw "Case $name should show both missing and extra volume. See $reportPath."
    }
    if ($alignment -eq 'coincident' -and
        ($data.AlignmentState -ne 'CoincidenceVerified' -or @($data.StudentToReferenceTransform).Count -ne 16)) {
        throw "Case $name did not validate a rigid alignment. See $reportPath."
    }
    Write-Host "PASS $name (real SOLIDWORKS $($data.CadRevision))"
}

$before = @(Get-OpenParts 'cad-before.txt')
if ($before.Count -eq 0) {
    throw 'Leave at least one visible part open in SOLIDWORKS to verify that the probe preserves existing documents.'
}
$cases = @(
    @{ Name = 'same-file'; Student = $referencePath; Alignment = 'original'; ExpectZero = $true }
    @{ Name = 'different-file-equivalent'; Student = $equivalentPath; Alignment = 'original'; ExpectZero = $true }
    @{ Name = 'moved-hole'; Student = $holePath; Alignment = 'original'; ExpectZero = $false }
)
if ($Translated) {
    $cases += @{ Name = 'translated-original'; Student = $Translated; Alignment = 'original'; ExpectZero = $false }
    $cases += @{ Name = 'translated-aligned'; Student = $Translated; Alignment = 'coincident'; ExpectZero = $true }
}
if ($Rotated) {
    $cases += @{ Name = 'rotated-aligned'; Student = $Rotated; Alignment = 'coincident'; ExpectZero = $true }
}

$failed = [System.Collections.Generic.List[string]]::new()
try {
    foreach ($case in $cases) {
        try {
            Invoke-GeometryCase $case.Name $case.Student $case.Alignment $case.ExpectZero
        }
        catch {
            $failed.Add($case.Name)
            Write-Warning $_.Exception.Message
        }
    }
}
finally {
    foreach ($path in $originals.Keys) {
        $file = Get-Item -LiteralPath $path
        if ((Get-SharedFileHash $path) -ine $originals[$path].Hash -or
            $file.LastWriteTimeUtc -ne $originals[$path].WriteTime) {
            throw "The source file changed during testing: $path"
        }
    }
    $after = @(Get-OpenParts 'cad-after.txt')
    if (($before -join "`n") -cne ($after -join "`n")) {
        throw "Visible open parts changed during testing. Compare the preserved logs in $work."
    }
    Write-Host 'PASS Source files and visible open CAD parts were preserved.'
}
if ($failed.Count -gt 0) {
    throw ('Real CAD verification failed for: ' + ($failed -join ', ') + ". Logs: $work")
}

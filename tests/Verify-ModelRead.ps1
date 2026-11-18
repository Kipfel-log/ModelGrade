# Runs the real Probe twice on an independent part. Requires the selected CAD to be running.
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)] [string] $Model,
    [Parameter(Mandatory = $true)] [ValidateSet('2025', '2026')] [string] $ExpectedYear,
    [string] $ProbePath = (Join-Path $PSScriptRoot '..\bin\SWGrade.Probe.exe'),
    [string] $Configuration,
    [switch] $WithoutMaterial
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version 2.0

$source = Get-Item -LiteralPath $Model
$hash = (Get-FileHash -LiteralPath $source.FullName -Algorithm SHA256).Hash
$writeTime = $source.LastWriteTimeUtc
$exe = (Get-Item -LiteralPath $ProbePath).FullName
$work = Join-Path ([System.IO.Path]::GetTempPath()) ('SWGrade-Verify-' + [guid]::NewGuid())
[void][System.IO.Directory]::CreateDirectory($work)

try {
    $results = @()
    foreach ($run in 1..2) {
        $report = Join-Path $work ("run-$run.json")
        $probeArgs = @('--expect', $ExpectedYear, '--model', $source.FullName, '--json', $report)
        if ($Configuration) {
            $probeArgs += @('--config', $Configuration)
        }

        & $exe @probeArgs
        if ($LASTEXITCODE -ne 0) {
            throw "Probe read $run failed with exit code $LASTEXITCODE."
        }

        $data = Get-Content -LiteralPath $report -Raw -Encoding UTF8 | ConvertFrom-Json
        if (-not $data.openedSuccessfully -or -not $data.sourceIntegrityVerified -or
            $data.documentHash -ine $hash -or $data.solidWorksVersion -ne $ExpectedYear -or
            $data.solidBodyCount -lt 1 -or $data.volumeM3 -le 0 -or $data.surfaceAreaM2 -le 0) {
            throw "Probe read $run lacks verified part measurements or source integrity evidence."
        }
        if ($Configuration -and $data.configuration -cne $Configuration) {
            throw "Unexpected active configuration in read $run."
        }
        if ($WithoutMaterial -and ($null -ne $data.massKg -or $null -ne $data.densityKgPerM3)) {
            throw "Read $run returned mass or density for the no-material check."
        }

        $results += ,$data
    }

    $first = $results[0] | ConvertTo-Json -Depth 8 -Compress
    $second = $results[1] | ConvertTo-Json -Depth 8 -Compress
    if ($first -cne $second) {
        throw 'The two JSON results differ. Review the console output and CAD state.'
    }

    if ((Get-FileHash -LiteralPath $source.FullName -Algorithm SHA256).Hash -ine $hash -or
        (Get-Item -LiteralPath $source.FullName).LastWriteTimeUtc -ne $writeTime) {
        throw 'The source file changed during the test.'
    }

    Write-Output 'PASS Two real reads matched. Source SHA-256 and LastWriteTimeUtc were preserved.'
}
finally {
    foreach ($run in 1..2) {
        $report = Join-Path $work ("run-$run.json")
        if (Test-Path -LiteralPath $report) {
            Remove-Item -LiteralPath $report
        }
    }

    Remove-Item -LiteralPath $work
}

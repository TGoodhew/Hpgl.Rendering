<#
.SYNOPSIS
    Fails the build when merged code coverage falls below the agreed thresholds.

.DESCRIPTION
    Reads the Cobertura report that ReportGenerator produces after merging the per-TFM
    runs, and compares its line and branch rates against the thresholds below.

    The check runs against the MERGED report on purpose. net472 and net8.0-windows
    exercise the same source through different runtimes, so a line covered on one and
    not the other is still covered by the test suite; gating each TFM separately would
    demand redundant tests to satisfy an artefact of multi-targeting.

    Lowering a threshold requires editing this file, which shows up in review as a
    deliberate act rather than a number quietly drifting down.

.PARAMETER ReportPath
    Path to the merged Cobertura XML. Defaults to the CI output location.

.PARAMETER LineThreshold
    Minimum line coverage percentage.

.PARAMETER BranchThreshold
    Minimum branch coverage percentage.

.EXAMPLE
    pwsh eng/Check-Coverage.ps1
#>
[CmdletBinding()]
param(
    [string] $ReportPath = 'artifacts/coverage/Cobertura.xml',

    # Baseline measured 2026-08-01 at 90.6% line / 80.0% branch, with StrokeFont.cs
    # (generated glyph data) excluded. Line is the measured value rounded down.
    [double] $LineThreshold = 90,

    # Branch gets one point of slack rather than the measured 80.0%: a threshold set at
    # the exact measured value has zero tolerance, so the first change that adds a single
    # uncovered branch fails CI even when overall coverage is healthy. Ratchet it up as
    # #8 and #9 add cases.
    [double] $BranchThreshold = 79
)

$ErrorActionPreference = 'Stop'

# Note on failure style: this script signals failure with an explicit `exit 1`, never with
# Write-Error alone. Under `pwsh -File`, Write-Error prints the message but leaves $LASTEXITCODE
# at 0, so a gate written that way reports a failure and lets the build go green - which is
# worse than having no gate at all.
if (-not (Test-Path $ReportPath)) {
    Write-Host "::error::Coverage report not found at '$ReportPath'. Did the test step run with the XPlat Code Coverage collector?"
    exit 1
}

[xml] $report = Get-Content -Path $ReportPath -Raw

# Cobertura expresses these as 0..1 rates on the root <coverage> element.
$line   = [math]::Round([double] $report.coverage.'line-rate'   * 100, 1)
$branch = [math]::Round([double] $report.coverage.'branch-rate' * 100, 1)

Write-Host "Line coverage:   $line% (threshold $LineThreshold%)"
Write-Host "Branch coverage: $branch% (threshold $BranchThreshold%)"

$failures = @()
if ($line   -lt $LineThreshold)   { $failures += "line coverage $line% is below the $LineThreshold% threshold" }
if ($branch -lt $BranchThreshold) { $failures += "branch coverage $branch% is below the $BranchThreshold% threshold" }

if ($failures.Count -gt 0) {
    foreach ($f in $failures) { Write-Host "::error::$f" }
    Write-Host ("Coverage gate FAILED: " + ($failures -join '; '))
    exit 1
}

Write-Host "Coverage gate passed."
exit 0

param(
    # Arguments for StatisticsChecks, such as '--assistant-checks'; empty runs the whole suite.
    [string[]]$Checks = @(),
    [switch]$NoBuild,
    [switch]$Open
)

$ErrorActionPreference = 'Stop'
$repository = Split-Path $PSScriptRoot -Parent
. (Join-Path $PSScriptRoot 'CodeQuality.Common.ps1')
$project = Join-Path $repository 'Tests/StatisticsChecks/StatisticsChecks.csproj'
$testProject = Join-Path $repository 'Tests/League.Tests/League.Tests.csproj'
# Debug output keeps a running bin/Release League untouched.
$checksDll = Join-Path $repository 'Tests/StatisticsChecks/bin/Debug/net8.0-windows/StatisticsChecks.dll'
$output = Join-Path $repository 'bin/coverage'
$checksCoverage = Join-Path $output 'checks.cobertura.xml'
$xunitCoverage = Join-Path $output 'xunit.cobertura.xml'
$report = Join-Path $output 'report'

$env:DOTNET_COVERAGE_TELEMETRY_OPTOUT = '1'
$env:DOTNET_COVERAGE_NOLOGO = '1'
Push-Location $repository
try {
    Invoke-QualityCommand 'dotnet' @('tool', 'restore')
    if (!$NoBuild) {
        Invoke-QualityCommand 'dotnet' @('build', $project, '-c', 'Debug', '-v', 'quiet', '-nologo', '-clp:ErrorsOnly')
    }
    if (!(Test-Path -LiteralPath $checksDll)) { throw "Checks were not built: $checksDll" }

    if (Test-Path -LiteralPath $output) { Remove-Item -LiteralPath $output -Recurse -Force }
    New-Item -ItemType Directory -Force -Path $output | Out-Null
    # The checks isolate their own data root; coverage only wraps the process and records League.dll.
    $command = (@('dotnet', "`"$checksDll`"") + $Checks) -join ' '
    Invoke-QualityCommand 'dotnet' @('tool', 'run', 'dotnet-coverage', 'collect', $command,
        '--settings', (Join-Path $PSScriptRoot 'coverage.runsettings'), '-f', 'cobertura', '-o', $checksCoverage)

    # The xUnit project is measured in its own pass over the test host, then merged: a check can only move from the
    # old suite to the new one without losing coverage once both runs feed the report.
    $testCommand = "dotnet test `"$testProject`" -v quiet --nologo"
    Invoke-QualityCommand 'dotnet' @('tool', 'run', 'dotnet-coverage', 'collect', $testCommand,
        '--settings', (Join-Path $PSScriptRoot 'coverage.runsettings'), '-f', 'cobertura', '-o', $xunitCoverage)

    Invoke-QualityCommand 'dotnet' @('tool', 'run', 'reportgenerator', "-reports:$checksCoverage;$xunitCoverage",
        "-targetdir:$report", '-reporttypes:Html;TextSummary', '-filefilters:-*.Designer.cs;-*.g.cs',
        '-verbosity:Warning')
    Get-Content -LiteralPath (Join-Path $report 'Summary.txt') -TotalCount 12
    Write-Output "Report: $(Join-Path $report 'index.html')"
    if ($Open) { Start-Process (Join-Path $report 'index.html') }
} finally {
    Pop-Location
}

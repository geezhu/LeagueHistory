param(
    [string]$BaseRef = '',
    [string[]]$Files = @(),
    [switch]$Fix,
    [switch]$SkipAnalysis
)

$ErrorActionPreference = 'Stop'
$repository = Split-Path $PSScriptRoot -Parent
. (Join-Path $PSScriptRoot 'CodeQuality.Common.ps1')
$configuration = Get-Content -LiteralPath (Join-Path $repository 'config/code-quality.json') -Raw | ConvertFrom-Json
$selected = @(Get-CodeQualitySelection $repository $configuration $BaseRef $Files)
# Validate the generated configuration before restoring tools or formatting anything.
& (Join-Path $PSScriptRoot 'Update-CodeQualityConfig.ps1') -Check
if (!$selected.Count) { Write-Output 'No source files selected.'; return }

Push-Location $repository
try {
    $csharp = @($selected | Where-Object { $_ -like '*.cs' })
    $cpp = @($selected | Where-Object { $_ -match '\.(cpp|h|hpp|inl)$' })
    if ($csharp.Count) {
        Invoke-QualityCommand 'dotnet' @('tool', 'restore')
        $action = if ($Fix) { 'format' } else { 'check' }
        Invoke-QualityCommand 'dotnet' (@('tool', 'run', 'csharpier', $action) + $csharp)
    }
    if ($cpp.Count) {
        $formatter = Get-CodeQualityClangFormat
        foreach ($path in $cpp) {
            $arguments = if ($Fix) { @('--style=file', '-i', $path) } else { @('--style=file', '--dry-run', '--Werror', $path) }
            Invoke-QualityCommand $formatter $arguments
        }
    }

    if ($csharp.Count -and !$SkipAnalysis) {
        $projects = @('League.csproj')
        if ($csharp | Where-Object { $_ -like 'Tests/StatisticsChecks/*' }) {
            $projects += 'Tests/StatisticsChecks/StatisticsChecks.csproj'
        }
        $violations = [Collections.Generic.List[string]]::new()
        foreach ($report in (Invoke-CodeQualityAnalysis $repository $projects)) {
            foreach ($violation in (Get-CodeQualityViolations $repository $report $csharp $configuration.analysisRules)) {
                $violations.Add($violation)
            }
        }
        if ($violations.Count) {
            $violations | Sort-Object -Unique | Write-Output
            throw 'Selected source files have code quality violations.'
        }
    }
    Write-Output ('PASS: source files checked: ' + $selected.Count)
} finally {
    Pop-Location
}

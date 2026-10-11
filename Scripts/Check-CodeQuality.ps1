param(
    [string]$BaseRef = '',
    [string[]]$Files = @(),
    [switch]$All,
    [switch]$Fix,
    [switch]$SkipAnalysis
)

$ErrorActionPreference = 'Stop'
$repository = Split-Path $PSScriptRoot -Parent
. (Join-Path $PSScriptRoot 'CodeQuality.Common.ps1')
$configuration = Get-Content -LiteralPath (Join-Path $repository 'config/code-quality.json') -Raw | ConvertFrom-Json
$selected = @(Get-CodeQualitySelection $repository $configuration $BaseRef $Files -All:$All)
# Validate ownership before restoring tools or formatting anything.
& (Join-Path $PSScriptRoot 'Update-CodeQualityScope.ps1') -Check
if (!$selected.Count) { Write-Output 'No fork-added source files selected.'; return }

function Invoke-QualityCommand {
    param([string]$Executable, [string[]]$Arguments)
    & $Executable @Arguments
    if ($LASTEXITCODE -ne 0) { throw "$Executable failed with exit code $LASTEXITCODE." }
}

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
        Invoke-QualityCommand 'dotnet' @('restore', 'Tools/CodeQuality/CodeQuality.proj', '--verbosity', 'quiet')
        $assetsPath = Join-Path $repository 'Tools/CodeQuality/obj/project.assets.json'
        $assets = Get-Content -LiteralPath $assetsPath -Raw | ConvertFrom-Json
        $analyzer = @($assets.packageFolders.PSObject.Properties.Name | ForEach-Object {
            Join-Path $_ 'meziantou.analyzer/3.0.294/analyzers/dotnet/roslyn4.8/cs/Meziantou.Analyzer.dll'
        } | Where-Object { Test-Path -LiteralPath $_ }) | Select-Object -First 1
        if (!$analyzer) { throw 'The pinned Meziantou analyzer was not restored.' }
        $defaults = [IO.Path]::GetFullPath((Join-Path (Split-Path $analyzer -Parent) '../../../../configuration/none.editorconfig'))
        if (!(Test-Path -LiteralPath $defaults)) { throw 'The Meziantou disabled-rule preset was not restored.' }
        Invoke-QualityCommand 'dotnet' @('restore', 'Tests/StatisticsChecks/StatisticsChecks.csproj', '--verbosity', 'quiet')
        $output = Join-Path $repository 'bin/code-quality'
        New-Item -ItemType Directory -Force -Path $output | Out-Null
        $targets = Join-Path $repository 'Tools/CodeQuality/Analyzers.targets'
        $projects = @('League.csproj')
        if ($csharp | Where-Object { $_ -like 'Tests/StatisticsChecks/*' }) {
            $projects += 'Tests/StatisticsChecks/StatisticsChecks.csproj'
        }
        $violations = [Collections.Generic.List[string]]::new()
        foreach ($project in $projects) {
            $name = [IO.Path]::GetFileNameWithoutExtension($project)
            $report = Join-Path $output "$name.sarif"
            $log = Join-Path $output "$name.log"
            # Rebuild avoids a cached compilation silently omitting the analysis report.
            $arguments = @('build', $project, '-c', 'Release', '--no-restore', '-t:Rebuild', '-v:quiet',
                "-p:CustomBeforeMicrosoftCommonTargets=$targets", "-p:CodeQualityAnalyzer=$analyzer",
                "-p:CodeQualityDefaults=$defaults", '-p:EnforceCodeStyleInBuild=true', "-p:ErrorLog=$report%2cversion=2.1")
            if ($project -ne 'League.csproj') { $arguments += '-p:BuildProjectReferences=false' }
            & dotnet @arguments *> $log
            if ($LASTEXITCODE -ne 0) {
                Get-Content -LiteralPath $log -Tail 20
                throw "Code quality compilation failed: $project"
            }
            if (!(Test-Path -LiteralPath $report)) { throw "Analysis report was not generated: $project" }
            $diagnostics = Get-Content -LiteralPath $report -Raw -Encoding UTF8 | ConvertFrom-Json
            foreach ($violation in (Get-CodeQualityViolations $repository $diagnostics $csharp $configuration.analysisRules)) {
                $violations.Add($violation)
            }
        }
        if ($violations.Count) {
            $violations | Sort-Object -Unique | Write-Output
            throw 'Selected fork-added files have code quality violations.'
        }
    }
    Write-Output ('PASS: fork-added source files checked: ' + $selected.Count)
} finally {
    Pop-Location
}

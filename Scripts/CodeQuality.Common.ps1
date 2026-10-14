function Get-CodeQualityRelativePath {
    param([string]$Repository, [string]$Path)

    $root = [IO.Path]::GetFullPath($Repository).TrimEnd('\', '/') + [IO.Path]::DirectorySeparatorChar
    $inputPath = if ([IO.Path]::IsPathRooted($Path)) { $Path } else { Join-Path $Repository $Path }
    $fullPath = [IO.Path]::GetFullPath($inputPath)
    if (!$fullPath.StartsWith($root, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Source path is outside the repository: $Path"
    }
    return $fullPath.Substring($root.Length).Replace('\', '/')
}

function Get-CodeQualitySelection {
    param(
        [string]$Repository,
        [object]$Configuration,
        [string]$BaseRef,
        [string[]]$Files
    )

    $protectedPaths = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    foreach ($path in $Configuration.upstreamFiles) { [void]$protectedPaths.Add($path) }

    if ($Files) {
        $candidates = @($Files | ForEach-Object { Get-CodeQualityRelativePath $Repository $_ })
        foreach ($path in $candidates) {
            if ($protectedPaths.Contains($path)) { throw "Upstream file is excluded from code quality changes: $path" }
        }
    } else {
        # Every source outside the baseline list is checked by default, whoever wrote it; a base reference
        # narrows the run to its changes.
        [string[]]$arguments = if ($BaseRef) {
            @('diff', '--name-only', '--diff-filter=ACMR', "$BaseRef...HEAD")
        } else {
            @('ls-files')
        }
        $candidates = @(& git -C $Repository @arguments)
        if ($LASTEXITCODE -ne 0) { throw 'Unable to enumerate source changes.' }
        $untracked = @(& git -C $Repository ls-files --others --exclude-standard)
        if ($LASTEXITCODE -ne 0) { throw 'Unable to enumerate new source files.' }
        $candidates += $untracked
    }

    return @($candidates | Where-Object {
        $_ -match '\.(cs|cpp|h|hpp|inl)$' -and
        $_ -notmatch '(^|/)(bin|obj)/|\.(Designer|g|g\.i)\.cs$' -and
        !$protectedPaths.Contains($_) -and
        (Test-Path -LiteralPath (Join-Path $Repository $_) -PathType Leaf)
    } | Sort-Object -Unique)
}

function Get-CodeQualityClangFormat {
    $command = Get-Command clang-format -ErrorAction SilentlyContinue
    if ($command) { return $command.Source }

    $locator = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio/Installer/vswhere.exe'
    if (Test-Path -LiteralPath $locator) {
        $studio = & $locator -latest -products '*' -property installationPath
        if ($studio) {
            $formatter = Join-Path $studio 'VC/Tools/Llvm/bin/clang-format.exe'
            if (Test-Path -LiteralPath $formatter) { return $formatter }
        }
    }
    throw 'clang-format is required for selected C++ files. Install the Visual Studio C++ LLVM tools.'
}

function Get-CodeQualityDiagnosticPath {
    param([string]$Repository, [string]$Uri)

    $location = [Uri]::new($Uri, [UriKind]::RelativeOrAbsolute)
    $path = if ($location.IsAbsoluteUri -and $location.IsFile) { $location.LocalPath } else { [Uri]::UnescapeDataString($Uri) }
    return Get-CodeQualityRelativePath $Repository $path
}

function Get-CodeQualityViolations {
    param([string]$Repository, [object]$Report, [string[]]$Selected, [string[]]$Rules)

    if ($Report.version -notin @('1.0.0', '2.1.0')) { throw 'Unsupported SARIF version; refusing to skip analysis.' }
    foreach ($run in $Report.runs) {
        foreach ($result in $run.results) {
            if ($result.ruleId -notin $Rules) { continue }
            foreach ($location in $result.locations) {
                if ($Report.version -eq '1.0.0') {
                    $uri = $location.resultFile.uri
                    $line = $location.resultFile.region.startLine
                    $message = $result.message
                } else {
                    $uri = $location.physicalLocation.artifactLocation.uri
                    $line = $location.physicalLocation.region.startLine
                    $message = $result.message.text
                }
                if (!$uri) { throw 'A selected rule has no source location; analysis cannot be verified.' }
                $path = Get-CodeQualityDiagnosticPath $Repository $uri
                if ($path -in $Selected) { Write-Output "${path}:${line} $($result.ruleId): $message" }
            }
        }
    }
}

function Invoke-QualityCommand {
    param([string]$Executable, [string[]]$Arguments)
    & $Executable @Arguments
    if ($LASTEXITCODE -ne 0) { throw "$Executable failed with exit code $LASTEXITCODE." }
}

# Rebuilds each project with the pinned Meziantou analyzer and returns the parsed SARIF reports.
# $GlobalConfig, when given, replaces the repository .editorconfig files for the whole build.
function Invoke-CodeQualityAnalysis {
    param([string]$Repository, [string[]]$Projects, [string]$GlobalConfig = '')

    Invoke-QualityCommand 'dotnet' @('restore', (Join-Path $Repository 'Tools/CodeQuality/CodeQuality.proj'), '--verbosity', 'quiet')
    $assetsPath = Join-Path $Repository 'Tools/CodeQuality/obj/project.assets.json'
    $assets = Get-Content -LiteralPath $assetsPath -Raw | ConvertFrom-Json
    $analyzer = @($assets.packageFolders.PSObject.Properties.Name | ForEach-Object {
        Join-Path $_ 'meziantou.analyzer/3.0.294/analyzers/dotnet/roslyn4.8/cs/Meziantou.Analyzer.dll'
    } | Where-Object { Test-Path -LiteralPath $_ }) | Select-Object -First 1
    if (!$analyzer) { throw 'The pinned Meziantou analyzer was not restored.' }
    $defaults = [IO.Path]::GetFullPath((Join-Path (Split-Path $analyzer -Parent) '../../../../configuration/none.editorconfig'))
    if (!(Test-Path -LiteralPath $defaults)) { throw 'The Meziantou disabled-rule preset was not restored.' }
    if ($Projects -contains 'Tests/StatisticsChecks/StatisticsChecks.csproj') {
        Invoke-QualityCommand 'dotnet' @('restore', (Join-Path $Repository 'Tests/StatisticsChecks/StatisticsChecks.csproj'), '--verbosity', 'quiet')
    }
    $output = Join-Path $Repository 'bin/code-quality'
    New-Item -ItemType Directory -Force -Path $output | Out-Null
    $targets = Join-Path $Repository 'Tools/CodeQuality/Analyzers.targets'
    # Forward slashes: a trailing backslash would escape the closing quote of a path with spaces.
    $build = ($output -replace '\\', '/') + '/build/'
    foreach ($project in $Projects) {
        $name = [IO.Path]::GetFileNameWithoutExtension($project)
        $report = Join-Path $output "$name.sarif"
        $log = Join-Path $output "$name.log"
        # Rebuild avoids a cached compilation silently omitting the analysis report.
        $arguments = @('build', (Join-Path $Repository $project), '-c', 'Release', '--no-restore', '-t:Rebuild', '-v:quiet',
            "-p:CustomBeforeMicrosoftCommonTargets=$targets", "-p:CodeQualityAnalyzer=$analyzer",
            "-p:CodeQualityDefaults=$defaults", '-p:EnforceCodeStyleInBuild=true', "-p:ErrorLog=$report%2cversion=2.1",
            "-p:CodeQualityOutput=$build")
        if ($GlobalConfig) { $arguments += @('-p:DiscoverEditorConfigFiles=false', "-p:CodeQualityGlobalConfig=$GlobalConfig") }
        if ($project -ne 'League.csproj') { $arguments += '-p:BuildProjectReferences=false' }
        & dotnet @arguments *> $log
        if ($LASTEXITCODE -ne 0) {
            Get-Content -LiteralPath $log -Tail 20
            throw "Code quality compilation failed: $project"
        }
        if (!(Test-Path -LiteralPath $report)) { throw "Analysis report was not generated: $project" }
        Get-Content -LiteralPath $report -Raw -Encoding UTF8 | ConvertFrom-Json
    }
}

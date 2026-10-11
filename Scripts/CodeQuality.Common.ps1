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
        [string[]]$Files,
        [switch]$All
    )

    $protectedPaths = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    foreach ($path in $Configuration.upstreamFiles) { [void]$protectedPaths.Add($path) }

    if ($Files) {
        $candidates = @($Files | ForEach-Object { Get-CodeQualityRelativePath $Repository $_ })
        foreach ($path in $candidates) {
            if ($protectedPaths.Contains($path)) { throw "Upstream file is excluded from code quality changes: $path" }
        }
    } else {
        [string[]]$arguments = if ($All) {
            @('ls-files')
        } elseif ($BaseRef) {
            @('diff', '--name-only', '--diff-filter=ACMR', "$BaseRef...HEAD")
        } else {
            @('diff', '--name-only', '--diff-filter=ACMR', 'HEAD')
        }
        $candidates = @(& git -C $Repository @arguments)
        if ($LASTEXITCODE -ne 0) { throw 'Unable to enumerate source changes.' }
        $untracked = @(& git -C $Repository ls-files --others --exclude-standard)
        if ($LASTEXITCODE -ne 0) { throw 'Unable to enumerate new source files.' }
        $candidates += $untracked
        $candidates += $Configuration.adoptedFiles
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

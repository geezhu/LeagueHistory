param(
    [switch]$IncludeStyle,
    [switch]$SkipAnalysis
)

# Checks only the lines added to baseline C# files since the recorded upstream commit, including
# uncommitted edits, whoever added them. Upstream lines are never reported, so fixing a finding stays a
# local edit, as AGENTS.md requires for upstream files.
# AGENTS.md keeps upstream files out of the format and structure rules, so braces and method length
# are listed only with -IncludeStyle and never fail the check; exception, async and resource rules do.
$ErrorActionPreference = 'Stop'
$repository = Split-Path $PSScriptRoot -Parent
. (Join-Path $PSScriptRoot 'CodeQuality.Common.ps1')
$configuration = Get-Content -LiteralPath (Join-Path $repository 'config/code-quality.json') -Raw | ConvertFrom-Json
$base = $configuration.upstreamCommit
$files = @($configuration.upstreamFiles | Where-Object {
    $_ -like '*.cs' -and $_ -notlike '*.Designer.cs' -and (Test-Path -LiteralPath (Join-Path $repository $_) -PathType Leaf)
})

function Get-AddedLines {
    param([string]$Path)

    $lines = [Collections.Generic.HashSet[int]]::new()
    # -w: re-indented baseline lines are not new code.
    foreach ($row in @(& git -C $repository diff -w -U0 --no-color $base -- $Path)) {
        if ($row -match '^@@ -\d+(?:,\d+)? \+(\d+)(?:,(\d+))? @@') {
            $count = if ($Matches[2]) { [int]$Matches[2] } else { 1 }
            for ($line = [int]$Matches[1]; $line -lt [int]$Matches[1] + $count; $line++) { [void]$lines.Add($line) }
        }
    }
    if ($LASTEXITCODE -ne 0) { throw "Unable to diff $Path against the upstream commit." }
    return , $lines
}

function Get-LineNumber {
    param([string]$Text, [int]$Index)
    return ([regex]::Matches($Text.Substring(0, $Index), "`n")).Count + 1
}

# A comment on the line or the line above explains an intentional exception (AGENTS.md asks for one).
function Test-Explained {
    param([string[]]$Lines, [int]$Line)
    return $Lines[$Line - 1] -match '(^|\s)//|/\*' -or ($Line -gt 1 -and $Lines[$Line - 2].Trim().StartsWith('//'))
}

# Upstream code keeps one statement per line with spaced operators; packed lines added later break
# that existing layout, so they fail even though other format rules do not apply to upstream files.
function Get-StatementCount {
    param([string]$Code)
    $depth = 0
    $count = 0
    foreach ($character in $Code.ToCharArray()) {
        if ($character -eq '(') { $depth++ }
        elseif ($character -eq ')') { $depth-- }
        elseif ($character -eq ';' -and $depth -eq 0) { $count++ }
    }
    return $count
}

function Get-CodeOnly {
    param([string]$Line)
    $code = $Line -replace '@?\$?"(?:[^"\\]|\\.)*"', '""' -replace "'(?:[^'\\]|\\.)'", "''"
    # Auto-property accessors are one declaration, not separate statements.
    return ($code -replace '//.*$', '') -replace '\b(get|set|init);', '$1'
}

$packedSpacing = '\b(if|while|foreach|for|lock|switch|using|catch)\(|\)(return|throw|continue|break)\b|[^\s=!<>]==[^\s=]|[^\s=!<>]!=[^\s]'
$styleRules = @('IDE0011', 'MA0051')
$added = @{}
foreach ($path in $files) {
    $lines = Get-AddedLines $path
    if ($lines.Count) { $added[$path] = $lines }
}

$findings = [Collections.Generic.List[string]]::new()
$style = [Collections.Generic.List[string]]::new()
$emptyCatch = 'catch\b\s*(?:\([^)]*\))?\s*(?:when\s*\((?:[^()]|\([^()]*\))*\))?\s*\{\s*\}'
$syncWait = '\.Wait\(\s*\)|\.GetAwaiter\(\)\.GetResult\(\)|\.Result\b(?!\s*=[^=>])'
$asyncVoid = '\basync\s+void\s+\w+\s*\((?!\s*object\??\s+\w+\s*,)'
foreach ($path in $added.Keys) {
    $text = [IO.File]::ReadAllText((Join-Path $repository $path))
    $lines = $text -split "`n"
    foreach ($match in [regex]::Matches($text, $emptyCatch)) {
        $line = Get-LineNumber $text $match.Index
        if ($added[$path].Contains($line) -and !(Test-Explained $lines $line)) { $findings.Add("${path}:${line} EMPTY-CATCH: 忽略异常时需要在附近说明原因") }
    }
    foreach ($line in $added[$path]) {
        $code = ($lines[$line - 1] -replace '//.*$', '')
        if ($code -match $syncWait -and !(Test-Explained $lines $line)) { $findings.Add("${path}:${line} SYNC-WAIT: 同步等待异步结果，确认不在 UI 线程") }
        $statements = Get-CodeOnly $lines[$line - 1]
        if ((Get-StatementCount $statements) -ge 2) { $findings.Add("${path}:${line} PACKED: 一行只写一条语句") }
        elseif ($statements -match $packedSpacing) { $findings.Add("${path}:${line} PACKED: 按上游写法保留关键字和运算符两侧的空格") }
        if ($code -match $asyncVoid) { $findings.Add("${path}:${line} ASYNC-VOID: 非事件处理的 async void 会丢失异常") }
    }
}

if (!$SkipAnalysis -and $added.Count) {
    # The repository .editorconfig turns these rules off for upstream files; a global config turns
    # them back on for this build only, and the added-line filter below keeps the scope.
    $globalConfig = Join-Path $repository 'bin/code-quality/upstream-changes.globalconfig'
    New-Item -ItemType Directory -Force -Path (Split-Path $globalConfig) | Out-Null
    $settings = @('is_global = true', 'global_level = 200', 'csharp_prefer_braces = true:warning',
        'MA0051.maximum_lines_per_method = 80', 'MA0051.maximum_statements_per_method = 40')
    $settings += @($configuration.analysisRules | ForEach-Object { "dotnet_diagnostic.$_.severity = warning" })
    [IO.File]::WriteAllLines($globalConfig, $settings)
    foreach ($report in (Invoke-CodeQualityAnalysis $repository @('League.csproj') $globalConfig)) {
        foreach ($violation in (Get-CodeQualityViolations $repository $report @($added.Keys) $configuration.analysisRules)) {
            if ($violation -match '^(.+?):(\d+) (\w+):' -and $added[$Matches[1]].Contains([int]$Matches[2])) {
                if ($Matches[3] -in $styleRules) { $style.Add($violation) } else { $findings.Add($violation) }
            }
        }
    }
}

$total = ($added.Values | ForEach-Object { $_.Count } | Measure-Object -Sum).Sum
if ($IncludeStyle -and $style.Count) {
    Write-Output "格式与结构（AGENTS.md 不要求上游文件遵守，仅供参考）：$($style.Count) 处"
    $style | Sort-Object -Unique | Write-Output
}
if ($findings.Count) {
    $findings | Sort-Object -Unique | Write-Output
    throw "原有源码中相对基线新增的代码有 $($findings.Count) 处不符合规范（检查 $($added.Count) 个文件、$total 行）。"
}
Write-Output "PASS: $($added.Count) 个原有源码中相对基线新增的 $total 行已检查（另有 $($style.Count) 处格式与结构参考项，用 -IncludeStyle 查看）。"

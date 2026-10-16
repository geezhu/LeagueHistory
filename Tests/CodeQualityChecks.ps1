$ErrorActionPreference = 'Stop'
$repository = Split-Path $PSScriptRoot -Parent
. (Join-Path $repository 'Scripts/CodeQuality.Common.ps1')
$configuration = Get-Content -LiteralPath (Join-Path $repository 'config/code-quality.json') -Raw | ConvertFrom-Json

function Assert-QualityCheck {
    param([bool]$Condition, [string]$Message)
    if (!$Condition) { throw $Message }
}

& (Join-Path $repository 'Scripts/Update-CodeQualityConfig.ps1') -Check
$editor = Get-Content -LiteralPath (Join-Path $repository '.editorconfig') -Raw
$sections = @([regex]::Matches($editor, '(?m)^\[([^\]\r\n]+)\]') | ForEach-Object { $_.Groups[1].Value })
Assert-QualityCheck ($sections.Count -eq 1 -and $sections[0] -eq '*.cs') 'Every C# source must share one rule section.'
$ignored = @(Get-Content -LiteralPath (Join-Path $repository '.csharpierignore') | Where-Object { $_ -match '\.cs$' })
Assert-QualityCheck (!($ignored | Where-Object { $_ -notmatch '\*\.(Designer|g|g\.i)\.cs$' })) 'Only generated C# may skip formatting.'

$voicePath = 'Services/SenseVoiceSession.cs'
$selected = @(Get-CodeQualitySelection $repository $configuration '' @($voicePath))
Assert-QualityCheck ($selected.Count -eq 1 -and $selected[0] -eq $voicePath) 'Explicit new-source selection failed.'
$expected = @(Get-CodeQualitySelection $repository $configuration '' @((Join-Path $repository $voicePath)))
Assert-QualityCheck ($expected.Count -eq 1 -and $expected[0] -eq $voicePath) 'Absolute source path did not normalize.'

$escaped = $false
try {
    Get-CodeQualitySelection $repository $configuration '' @('../outside.cs') | Out-Null
} catch {
    $escaped = $_.Exception.Message -like 'Source path is outside*'
}
Assert-QualityCheck $escaped 'Paths outside the repository were accepted.'
$voiceUri = ([Uri](Join-Path $repository $voicePath)).AbsoluteUri
Assert-QualityCheck ((Get-CodeQualityDiagnosticPath $repository $voiceUri) -eq $voicePath) 'SARIF file URI failed to normalize.'

# Compiler versions emit different SARIF shapes; neither may silently lose a real diagnostic.
$locationV1 = @{ resultFile = @{ uri = $voiceUri; region = @{ startLine = 7 } } }
$locationV2 = @{ physicalLocation = @{ artifactLocation = @{ uri = $voiceUri }; region = @{ startLine = 7 } } }
foreach ($version in @('1.0.0', '2.1.0')) {
    $location = if ($version -eq '1.0.0') { $locationV1 } else { $locationV2 }
    $message = if ($version -eq '1.0.0') { 'missing token' } else { @{ text = 'missing token' } }
    $fixture = @{ version = $version; runs = @(@{ results = @(@{ ruleId = 'MA0040'; message = $message; locations = @($location) }) }) }
    $violations = @(Get-CodeQualityViolations $repository $fixture @($voicePath) @('MA0040'))
    Assert-QualityCheck ($violations.Count -eq 1 -and $violations[0] -match 'MA0040: missing token') "SARIF $version missed a selected source diagnostic."
    $otherSources = @(Get-CodeQualityViolations $repository $fixture @('Services/Other.cs') @('MA0040'))
    Assert-QualityCheck ($otherSources.Count -eq 0) "SARIF $version included an unselected source."
}

$all = @(Get-CodeQualitySelection $repository $configuration '' @())
# The default run covers every tracked source, upstream or not, with no list to fall behind.
$sources = @(git -C $repository ls-files | Where-Object {
    $_ -match '\.(cs|cpp|h|hpp|inl)$' -and $_ -notmatch '(^|/)(bin|obj)/|\.(Designer|g|g\.i)\.cs$'
})
$skipped = @($sources | Where-Object { $_ -notin $all })
Assert-QualityCheck ($sources.Count -gt 0 -and $skipped.Count -eq 0) ('The default run skipped: ' + ($skipped -join ', '))
Assert-QualityCheck ('Forms/FormMain.cs' -in $all) 'Files that came from upstream must be checked too.'

Write-Output ('PASS: all ' + $sources.Count + ' sources selected; path boundaries and SARIF parsing verified.')

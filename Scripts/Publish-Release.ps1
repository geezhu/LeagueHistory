param(
    [Parameter(Mandatory = $true)][string]$Version,
    [string]$OutputDirectory = ''
)
$ErrorActionPreference = 'Stop'
# Builds the package for other users: a self-contained League.exe (no .NET install needed), the unsigned
# Game Bar MSIX under GameBar/ (each computer signs it with its own certificate when installing from
# Settings), and no voice model (downloaded in Settings). No signing key is involved.
# The package follows upstream's: files at the zip root, with version.txt for the auto-updater; the title bar
# keeps upstream's text.
$projectRoot = Split-Path $PSScriptRoot -Parent
if (!$OutputDirectory) { $OutputDirectory = Join-Path $projectRoot "bin/Publish/$Version" }
$output = [IO.Path]::GetFullPath($OutputDirectory)
if ((Test-Path $output) -and (Get-ChildItem $output -Force | Select-Object -First 1)) {
    throw "输出目录已存在且不为空：$output。请换一个版本号或手动清理后重试。"
}

$app = Join-Path $output 'League'
$gameBarBuild = Join-Path $output 'GameBarBuild'
& dotnet publish (Join-Path $projectRoot 'League.csproj') -c Release -r win-x64 --self-contained true `
    -p:BundleVoiceModel=false -p:DebugType=none -o $app -nologo -v q
if ($LASTEXITCODE -ne 0) { throw "dotnet publish 失败：$LASTEXITCODE" }
[IO.File]::WriteAllText((Join-Path $app 'version.txt'), $Version.TrimStart('v', 'V'))

& (Join-Path $projectRoot 'GameBar/Build.ps1') -OutputDirectory $gameBarBuild
$components = New-Item -ItemType Directory -Force (Join-Path $app 'GameBar')
Copy-Item (Join-Path $gameBarBuild 'LeagueHistory.GameBar.msix') $components
Remove-Item -Recurse -Force $gameBarBuild

$zip = Join-Path $output "League-$Version-win-x64.zip"
# Antivirus may briefly lock freshly written files; retry the archive instead of failing the release.
for ($attempt = 1; ; $attempt++) {
    try { Compress-Archive -Path (Join-Path $app '*') -DestinationPath $zip -Force; break }
    catch {
        if ($attempt -ge 3) { throw }
        Start-Sleep -Seconds 3
    }
}
$hash = (Get-FileHash $zip -Algorithm SHA256).Hash
$size = [Math]::Round((Get-Item $zip).Length / 1MB, 1)
Write-Host "发布包：$zip（$size MB）"
Write-Host "SHA256：$hash"

# The one-click installer carries the same files; the zip stays for the auto-updater and manual installs.
$iscc = @(
    (Join-Path $env:LOCALAPPDATA 'Programs/Inno Setup 6/ISCC.exe'),
    (Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6/ISCC.exe'),
    (Join-Path $env:ProgramFiles 'Inno Setup 6/ISCC.exe')
) | Where-Object { Test-Path $_ } | Select-Object -First 1
if (!$iscc) { throw '需要 Inno Setup 6 生成安装程序：winget install JRSoftware.InnoSetup' }
& $iscc /Q "/DAppVersion=$($Version.TrimStart('v', 'V'))" "/DSourceDir=$app" "/DOutputDir=$output" `
    (Join-Path $projectRoot 'Scripts/Installer/LeagueHistory.iss')
if ($LASTEXITCODE -ne 0) { throw "ISCC 失败：$LASTEXITCODE" }
$setup = Join-Path $output "League-$($Version.TrimStart('v', 'V'))-setup.exe"
$setupSize = [Math]::Round((Get-Item $setup).Length / 1MB, 1)
Write-Host "安装程序：$setup（$setupSize MB）"
Write-Host "SHA256：$((Get-FileHash $setup -Algorithm SHA256).Hash)"

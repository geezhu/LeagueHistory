param([string]$PackageDirectory = '', [switch]$KeepGameBarRunning)
$ErrorActionPreference = 'Stop'
if (!$PackageDirectory) { $PackageDirectory = Join-Path (Split-Path $PSScriptRoot -Parent) 'bin/GameBar/Package' }
$manifestPath = Join-Path ([IO.Path]::GetFullPath($PackageDirectory)) 'AppxManifest.xml'
if (!(Test-Path -LiteralPath $manifestPath)) { throw '请先运行 GameBar/Build.ps1 生成组件。' }
[xml]$manifest = Get-Content -Raw -Encoding UTF8 -LiteralPath $manifestPath
if ($manifest.Package.Identity.Name -ne 'LeagueHistory.GameBar' -or $manifest.Package.Identity.Publisher -ne 'CN=LeagueHistory') {
    throw '这不是 LeagueHistory 的 Game Bar 组件。'
}
$developerMode = Get-ItemPropertyValue 'HKLM:/SOFTWARE/Microsoft/Windows/CurrentVersion/AppModelUnlock' AllowDevelopmentWithoutDevLicense -ErrorAction SilentlyContinue
if ($developerMode -ne 1) { throw '本脚本仅用于已经启用开发模式的开发电脑，不会修改系统设置。普通发行版需要正式签名的 MSIX。' }
$previous = Get-AppxPackage -Name LeagueHistory.GameBar
Add-AppxPackage -Register $manifestPath -ForceTargetApplicationShutdown
$installed = Get-AppxPackage -Name LeagueHistory.GameBar
$expectedDirectory = [IO.Path]::GetDirectoryName($manifestPath).TrimEnd('\','/')
if (!$installed -or $installed.InstallLocation.TrimEnd('\','/') -ne $expectedDirectory) {
    throw 'Windows 仍指向旧构建目录。请提高 Package.appxmanifest 的版本号，重新构建后再安装。'
}
# Updating the package can leave Game Bar holding a dead widget window. Reload its UI host,
# not the game or the League desktop process. Leave recording sessions alone.
if ($previous -and !$KeepGameBarRunning) {
    $gameBarPackage = Get-AppxPackage -Name Microsoft.XboxGamingOverlay
    if (Get-Process -Name BroadcastDVRServer -ErrorAction SilentlyContinue) {
        Write-Warning '录制服务正在运行，未自动重启 Game Bar。录制结束后请退出并重新打开 Game Bar，避免保留旧组件窗口。'
    } elseif ($gameBarPackage) {
        foreach ($hostProcess in (Get-Process -Name GameBar -ErrorAction SilentlyContinue)) {
            if ($hostProcess.Path -eq (Join-Path $gameBarPackage.InstallLocation 'GameBar.exe')) {
                Stop-Process -Id $hostProcess.Id
            }
        }
    }
}
$installed | Select-Object Name,PackageFamilyName,InstallLocation
Write-Host '已注册。请按 Win＋G，在小组件列表打开并固定「League 对局速览」。保留 Package 目录，勿移动或删除。'

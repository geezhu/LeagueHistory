param(
    [Parameter(Mandatory = $true)][string]$Version,
    [string]$OutputDirectory = '',
    [string]$CertificateThumbprint = ''
)
$ErrorActionPreference = 'Stop'
# Builds the package for other users: a self-contained League.exe (no .NET install needed), the signed
# Game Bar MSIX with its public certificate under GameBar/, and no voice model (downloaded in Settings).
$projectRoot = Split-Path $PSScriptRoot -Parent
if (!$OutputDirectory) { $OutputDirectory = Join-Path $projectRoot "bin/Publish/$Version" }
$output = [IO.Path]::GetFullPath($OutputDirectory)
if ((Test-Path $output) -and (Get-ChildItem $output -Force | Select-Object -First 1)) {
    throw "输出目录已存在且不为空：$output。请换一个版本号或手动清理后重试。"
}

$public = Join-Path $projectRoot 'GameBar/LeagueHistory.GameBar.cer'
if (!$CertificateThumbprint) {
    if (!(Test-Path $public)) { throw '缺少签名证书，请先运行 Scripts/New-GameBarCertificate.ps1。' }
    $CertificateThumbprint = ([Security.Cryptography.X509Certificates.X509Certificate2]::new($public)).Thumbprint
}
$signing = Get-Item "Cert:\CurrentUser\My\$CertificateThumbprint" -ErrorAction SilentlyContinue
if (!$signing -or !$signing.HasPrivateKey) {
    throw "当前账户没有证书 $CertificateThumbprint 的私钥。请导入备份的 .pfx，或运行 Scripts/New-GameBarCertificate.ps1。"
}

$app = Join-Path $output 'League'
$gameBarBuild = Join-Path $output 'GameBarBuild'
& dotnet publish (Join-Path $projectRoot 'League.csproj') -c Release -r win-x64 --self-contained true `
    -p:BundleVoiceModel=false -p:DebugType=none -p:Version=$Version -o $app -nologo -v q
if ($LASTEXITCODE -ne 0) { throw "dotnet publish 失败：$LASTEXITCODE" }

& (Join-Path $projectRoot 'GameBar/Build.ps1') -OutputDirectory $gameBarBuild -CertificateThumbprint $CertificateThumbprint
$components = New-Item -ItemType Directory -Force (Join-Path $app 'GameBar')
Copy-Item (Join-Path $gameBarBuild 'LeagueHistory.GameBar.msix') $components
Copy-Item $public $components
Remove-Item -Recurse -Force $gameBarBuild

$zip = Join-Path $output "League-$Version-win-x64.zip"
# Antivirus may briefly lock freshly written files; retry the archive instead of failing the release.
for ($attempt = 1; ; $attempt++) {
    try { Compress-Archive -Path $app -DestinationPath $zip -Force; break }
    catch {
        if ($attempt -ge 3) { throw }
        Start-Sleep -Seconds 3
    }
}
$hash = (Get-FileHash $zip -Algorithm SHA256).Hash
$size = [Math]::Round((Get-Item $zip).Length / 1MB, 1)
Write-Host "发布包：$zip（$size MB）"
Write-Host "SHA256：$hash"

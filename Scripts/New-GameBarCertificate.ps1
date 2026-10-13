param([string]$BackupPath = '', [SecureString]$BackupPassword)
$ErrorActionPreference = 'Stop'
# Creates (or reuses) the self-signed certificate that signs the Game Bar MSIX. The private key stays in
# this Windows account's certificate store; only the public certificate is written into the repository.
# Its subject must equal the Publisher in GameBar/Package.appxmanifest.
$projectRoot = Split-Path $PSScriptRoot -Parent
$subject = 'CN=LeagueHistory'
$codeSigning = '1.3.6.1.5.5.7.3.3'
$certificate = Get-ChildItem Cert:\CurrentUser\My |
    Where-Object { $_.Subject -eq $subject -and $_.HasPrivateKey -and $_.NotAfter -gt (Get-Date) -and ($_.EnhancedKeyUsageList.ObjectId -contains $codeSigning) } |
    Sort-Object NotAfter -Descending | Select-Object -First 1
if ($certificate) {
    Write-Host "沿用已有签名证书：$($certificate.Thumbprint)"
} else {
    $certificate = New-SelfSignedCertificate -Type Custom -Subject $subject -KeyUsage DigitalSignature `
        -FriendlyName 'LeagueHistory Game Bar 组件签名' -CertStoreLocation Cert:\CurrentUser\My `
        -NotAfter (Get-Date).AddYears(10) -TextExtension @("2.5.29.37={text}$codeSigning", '2.5.29.19={text}')
    Write-Host "已创建签名证书：$($certificate.Thumbprint)"
}
$public = Join-Path $projectRoot 'GameBar/LeagueHistory.GameBar.cer'
Export-Certificate -Cert $certificate -FilePath $public | Out-Null
Write-Host "公钥证书：$public（随发布包分发，用户安装组件时信任它）"
if ($BackupPath -and $BackupPassword) {
    Export-PfxCertificate -Cert $certificate -FilePath $BackupPath -Password $BackupPassword | Out-Null
    Write-Host "私钥备份：$BackupPath（不要提交到仓库）"
} else {
    Write-Warning '尚未备份私钥。换电脑或重装系统后需要同一证书才能让用户直接更新组件；请用 -BackupPath 和 -BackupPassword 导出 .pfx 并妥善保存。'
}

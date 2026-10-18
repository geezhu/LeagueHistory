param([string]$PackageDirectory = '', [ValidateSet('skills','builds','runes','augments','live','record','roster','voice','waves','marks','controls','stats','augmentpage','')][string]$DetailTab = '', [ValidateSet('expanded','collapsed','')][string]$WaveMini = '')
$ErrorActionPreference = 'Stop'
if ($PackageDirectory) { & "$PSScriptRoot/Install-Development.ps1" -PackageDirectory $PackageDirectory }
$package = Get-AppxPackage -Name LeagueHistory.GameBar
if (!$package) { throw '请先安装 Game Bar 开发组件。' }
# Activate our own app with an explicit render-verification argument. No game input is sent.
Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
[ComImport, Guid("2E941141-7F97-4756-BA1D-9DECDE894A3D"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface IPreviewActivationManager {
    void ActivateApplication([MarshalAs(UnmanagedType.LPWStr)] string appId,
        [MarshalAs(UnmanagedType.LPWStr)] string arguments, uint options, out uint processId);
}
public static class PreviewActivation {
    public static uint Run(string appId, string arguments) {
        var manager=(IPreviewActivationManager)Activator.CreateInstance(Type.GetTypeFromCLSID(new Guid("45BA127D-10A8-46EA-8AB7-56EA9078943C")));
        try { uint pid; manager.ActivateApplication(appId,arguments,0,out pid); return pid; }
        finally { Marshal.ReleaseComObject(manager); }
    }
}
'@
$arguments = if ($WaveMini) { "--capture-wave-mini:$WaveMini" } elseif ($DetailTab) { "--capture-detail-fixture:$DetailTab" } else { '--capture-preview' }
[PreviewActivation]::Run($package.PackageFamilyName + '!App', $arguments)
$imageName = if ($WaveMini) { "wave-mini-preview-$WaveMini.png" } elseif ($DetailTab) { "detail-preview-$DetailTab.png" } else { 'preview-verification.png' }
Write-Host "检查输出：$env:LOCALAPPDATA/Packages/$($package.PackageFamilyName)/LocalState/$imageName"

param([string]$OutputDirectory = '', [string]$SdkVersion = '7.3.2607010')
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
if (!$OutputDirectory) { $OutputDirectory = Join-Path $projectRoot 'bin/GameBar' }
$output = [IO.Path]::GetFullPath($OutputDirectory)
$stage = Join-Path $output 'Package'
$generated = Join-Path $output 'Generated'
$sdkPackage = Join-Path $projectRoot 'bin/gamebar-sdk/package'
$nugetSpec = Join-Path $sdkPackage 'Microsoft.Gaming.XboxGameBar.nuspec'
New-Item -ItemType Directory -Force $output,$stage,$generated | Out-Null
if (!(Test-Path $nugetSpec) -or ([xml](Get-Content -Raw $nugetSpec)).package.metadata.version -ne $SdkVersion) {
    $sdkPackage = Join-Path $output "SDK-$SdkVersion"
    if (!(Test-Path (Join-Path $sdkPackage 'Microsoft.Gaming.XboxGameBar.nuspec'))) {
        $zip = Join-Path $output "sdk-$SdkVersion.zip"
        Invoke-WebRequest "https://api.nuget.org/v3-flatcontainer/microsoft.gaming.xboxgamebar/$SdkVersion/microsoft.gaming.xboxgamebar.$SdkVersion.nupkg" -OutFile $zip
        Expand-Archive -LiteralPath $zip -DestinationPath $sdkPackage -Force
    }
}
$kits = 'C:/Program Files (x86)/Windows Kits/10'
$kitVersion = Get-ChildItem (Join-Path $kits 'Include') -Directory | Where-Object { Test-Path (Join-Path $_.FullName 'cppwinrt/winrt/Windows.UI.Xaml.h') } | Sort-Object Name -Descending | Select-Object -First 1 -ExpandProperty Name
if (!$kitVersion) { throw '需要 Windows 10/11 SDK（含 C++/WinRT）。' }
$vswhere = 'C:/Program Files (x86)/Microsoft Visual Studio/Installer/vswhere.exe'
$vs = & $vswhere -latest -products '*' -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
$vc = Get-ChildItem (Join-Path $vs 'VC/Tools/MSVC') -Directory | Sort-Object Name -Descending | Select-Object -First 1 -ExpandProperty FullName
$sdkBin = Join-Path $kits "bin/$kitVersion/x64"
function Invoke-Checked([string]$File, [string[]]$Arguments) {
    & $File @Arguments
    if ($LASTEXITCODE -ne 0) { throw "$File failed: $LASTEXITCODE" }
}
$metadata = Join-Path $kits "UnionMetadata/$kitVersion/Windows.winmd"
Invoke-Checked (Join-Path $sdkBin 'cppwinrt.exe') @('-input',"$sdkPackage/lib/uap10.0/Microsoft.Gaming.XboxGameBar.winmd",'-input',"$sdkPackage/private/Microsoft.Gaming.XboxGameBar.Private.winmd",'-reference',$metadata,'-output',$generated)
$include = @("$vc/include", "$kits/Include/$kitVersion/ucrt", "$kits/Include/$kitVersion/shared", "$kits/Include/$kitVersion/um", "$kits/Include/$kitVersion/winrt", "$kits/Include/$kitVersion/cppwinrt", $generated)
$compile = @('/nologo','/std:c++20','/EHsc','/O2','/MD','/utf-8','/DUNICODE','/D_UNICODE','/DWINAPI_FAMILY=WINAPI_FAMILY_APP','/bigobj')
$compile += $include | ForEach-Object { "/I$_" }
$compile += @("$PSScriptRoot/Widget.cpp", "/Fo$output/Widget.obj", "/Fe$stage/LeagueHistory.GameBar.exe", '/link', '/SUBSYSTEM:WINDOWS', '/APPCONTAINER', '/DYNAMICBASE', '/NXCOMPAT', "/LIBPATH:$vc/lib/x64/uwp", "/LIBPATH:$vc/lib/x64", "/LIBPATH:$kits/Lib/$kitVersion/ucrt/x64", "/LIBPATH:$kits/Lib/$kitVersion/um/x64", 'WindowsApp.lib')
Invoke-Checked "$vc/bin/Hostx64/x64/cl.exe" $compile
Copy-Item -LiteralPath "$sdkPackage/runtimes/win10-x64/native/Microsoft.Gaming.XboxGameBar.dll" -Destination $stage
Copy-Item -LiteralPath "$sdkPackage/runtimes/win10-x64/native/Microsoft.Gaming.XboxGameBar.pri" -Destination $stage
Copy-Item -LiteralPath "$sdkPackage/license.txt" -Destination "$stage/GameBar-SDK-license.txt"
Copy-Item -LiteralPath "$sdkPackage/lib/uap10.0/Microsoft.Gaming.XboxGameBar.winmd","$sdkPackage/private/Microsoft.Gaming.XboxGameBar.Private.winmd" -Destination $stage

# Generate registrations from this SDK version, including new private interfaces.
# Hard-coding an older sample's GUID list silently breaks the Game Bar connection.
$classes = Get-ChildItem "$generated/winrt/impl" -Filter 'Microsoft.Gaming.XboxGameBar*.0.h' | Where-Object Name -NotLike '*Private*' | Get-Content | Select-String 'category<winrt::(.+)>\{ using type = class_category;'
$activation = ($classes | ForEach-Object { '<ActivatableClass ActivatableClassId="' + $_.Matches[0].Groups[1].Value.Replace('::','.') + '" ThreadingModel="both" />' }) -join "`n"
$interfaces = Get-Content "$generated/winrt/impl/Microsoft.Gaming.XboxGameBar.Private.0.h" | Select-String 'guid_v<winrt::([^>]+)>.+// ([0-9A-F-]{36})'
$proxy = ($interfaces | ForEach-Object { '<Interface Name="' + $_.Matches[0].Groups[1].Value.Replace('::','.') + '" InterfaceId="' + $_.Matches[0].Groups[2].Value + '" />' }) -join "`n"
$manifest = Get-Content -Raw -Encoding UTF8 "$PSScriptRoot/Package.appxmanifest"
$manifest.Replace('<!-- SDK_ACTIVATION -->',$activation).Replace('<!-- SDK_INTERFACES -->',$proxy) | Set-Content -Encoding UTF8 "$stage/AppxManifest.xml"
New-Item -ItemType Directory -Force "$stage/Assets","$stage/GameBar" | Out-Null
Add-Type -AssemblyName System.Drawing
$icon = [Drawing.Icon]::new("$projectRoot/favicon.ico")
$sourceImage = $icon.ToBitmap()
foreach ($size in 44,50,150) {
    $bitmap = [Drawing.Bitmap]::new($size,$size)
    $graphics = [Drawing.Graphics]::FromImage($bitmap)
    $graphics.Clear([Drawing.Color]::FromArgb(7,30,41))
    $graphics.DrawImage($sourceImage,0,0,$size,$size)
    $bitmap.Save("$stage/Assets/Logo$size.png",[Drawing.Imaging.ImageFormat]::Png)
    $graphics.Dispose(); $bitmap.Dispose()
}
$sourceImage.Dispose(); $icon.Dispose()
Invoke-Checked "$sdkBin/makeappx.exe" @('pack','/d',$stage,'/p',"$output/LeagueHistory.GameBar.msix",'/o')
Write-Host "组件已生成：$output/LeagueHistory.GameBar.msix（尚未签名 / 安装）"

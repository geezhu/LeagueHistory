; Installer for the self-contained publish output. Publish-Release.ps1 passes the version and folders:
;   ISCC.exe /DAppVersion=1.2.3 /DSourceDir=<publish folder> /DOutputDir=<release folder> LeagueHistory.iss
; The default folder is Program Files on the first fixed drive other than C:, else C:\Program Files. The folder is
; made writable for users because League.exe runs without administrator rights and its auto-update replaces files.

#ifndef AppVersion
  #error AppVersion is required
#endif
#ifndef SourceDir
  #error SourceDir is required
#endif
#ifndef OutputDir
  #define OutputDir "."
#endif

[Setup]
AppId={{6F1E4C2A-8B3D-4E7A-9C51-2D8F0A6B3E91}
AppName=LeagueHistory
AppVersion={#AppVersion}
AppVerName=LeagueHistory {#AppVersion}
AppPublisher=LeagueHistory
DefaultDirName={code:DefaultInstallDir}
DefaultGroupName=LeagueHistory
DisableProgramGroupPage=yes
PrivilegesRequired=admin
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.19041
OutputDir={#OutputDir}
OutputBaseFilename=League-{#AppVersion}-setup
SetupIconFile=..\..\favicon.ico
UninstallDisplayIcon={app}\League.exe
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
CloseApplications=yes

[Languages]
Name: "chs"; MessagesFile: "ChineseSimplified.isl"

[Tasks]
Name: "gamebar"; Description: "安装 Game Bar 组件（对局速览，首次安装需在管理员授权中选择“是”以信任组件证书）"
Name: "desktopicon"; Description: "创建桌面快捷方式"

[Dirs]
Name: "{app}"; Permissions: users-modify

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\LeagueHistory"; Filename: "{app}\League.exe"
Name: "{group}\卸载 LeagueHistory"; Filename: "{uninstallexe}"
Name: "{autodesktop}\LeagueHistory"; Filename: "{app}\League.exe"; Tasks: desktopicon

[Run]
; The component installs per user, so this runs as the signed-in user rather than the elevated setup account.
Filename: "{app}\League.exe"; Parameters: "--install-gamebar"; StatusMsg: "正在安装 Game Bar 组件…"; Tasks: gamebar; Flags: runasoriginaluser waituntilterminated
Filename: "{app}\League.exe"; Description: "启动 LeagueHistory"; Flags: postinstall nowait skipifsilent runasoriginaluser

[UninstallRun]
Filename: "powershell.exe"; Parameters: "-NoProfile -NonInteractive -Command ""Get-AppxPackage -AllUsers -Name LeagueHistory.GameBar | Remove-AppxPackage -AllUsers"""; RunOnceId: "RemoveGameBar"; Flags: runhidden

[Code]
function GetDriveType(Root: String): Cardinal;
  external 'GetDriveTypeW@kernel32.dll stdcall';

const
  DRIVE_FIXED = 3;

function DefaultInstallDir(Param: String): String;
var
  Code: Integer;
  Drive: String;
begin
  for Code := Ord('D') to Ord('Z') do
  begin
    Drive := Chr(Code) + ':\';
    if GetDriveType(Drive) = DRIVE_FIXED then
    begin
      Result := Drive + 'Program Files\LeagueHistory';
      exit;
    end;
  end;
  Result := ExpandConstant('{autopf}\LeagueHistory');
end;

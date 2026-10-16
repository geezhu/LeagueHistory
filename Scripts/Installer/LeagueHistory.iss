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

var
  RemoveData: Boolean;

// Asks once before uninstalling whether to remove everything the program wrote. Off by default: settings and the
// downloaded voice model survive a reinstall. A silent uninstall keeps the data.
function InitializeUninstall(): Boolean;
var
  Form: TSetupForm;
  Intro, Detail: TNewStaticText;
  Check: TNewCheckBox;
  Uninstall, Cancel: TNewButton;
begin
  Result := True;
  RemoveData := False;
  if UninstallSilent then
    exit;

  Form := CreateCustomForm(ScaleX(440), ScaleY(196), False, False);
  try
    Form.Caption := '卸载 LeagueHistory';
    Intro := TNewStaticText.Create(Form);
    Intro.Parent := Form;
    Intro.Left := ScaleX(16);
    Intro.Top := ScaleY(16);
    Intro.Caption := '将卸载 LeagueHistory 和 Game Bar 组件。';
    Check := TNewCheckBox.Create(Form);
    Check.Parent := Form;
    Check.Left := ScaleX(16);
    Check.Top := ScaleY(44);
    Check.Width := Form.ClientWidth - ScaleX(32);
    Check.Height := ScaleY(20);
    Check.Caption := '同时删除本程序产生的所有数据（不可恢复）';
    Detail := TNewStaticText.Create(Form);
    Detail.Parent := Form;
    Detail.Left := ScaleX(34);
    Detail.Top := ScaleY(68);
    Detail.Width := Form.ClientWidth - ScaleX(50);
    Detail.AutoSize := False;
    Detail.WordWrap := True;
    Detail.Height := ScaleY(76);
    Detail.Caption := '设置、搜索记录、日志、缓存、已下载的语音模型、安装目录里的剩余文件、' +
      '本程序创建的定时关机任务，以及 Game Bar 组件的本机签名证书。';
    Uninstall := TNewButton.Create(Form);
    Uninstall.Parent := Form;
    Uninstall.Caption := '卸载';
    Uninstall.ModalResult := mrOk;
    Uninstall.Default := True;
    Uninstall.Width := ScaleX(80);
    Uninstall.Height := ScaleY(26);
    Uninstall.Left := Form.ClientWidth - ScaleX(184);
    Uninstall.Top := Form.ClientHeight - ScaleY(40);
    Cancel := TNewButton.Create(Form);
    Cancel.Parent := Form;
    Cancel.Caption := '取消';
    Cancel.ModalResult := mrCancel;
    Cancel.Cancel := True;
    Cancel.Width := ScaleX(80);
    Cancel.Height := ScaleY(26);
    Cancel.Left := Form.ClientWidth - ScaleX(96);
    Cancel.Top := Uninstall.Top;
    Result := Form.ShowModal() = mrOk;
    RemoveData := Check.Checked;
  finally
    Form.Free();
  end;
end;

// Only folders and items this program names as its own: the shared %LOCALAPPDATA%\League folder loses just the
// program's subfolders, the install folder is removed only when it is the LeagueHistory folder, and the scheduled
// task only when it carries the program's marker. The uninstaller runs elevated, so the per-user folders are those of
// the account that approved the prompt, normally the signed-in user.
procedure RemoveUserData();
var
  Shared, App: String;
  ResultCode: Integer;
begin
  DelTree(ExpandConstant('{localappdata}\LeagueHistory'), True, True, True);
  Shared := ExpandConstant('{localappdata}\League');
  DelTree(Shared + '\Logs', True, True, True);
  DelTree(Shared + '\Cache', True, True, True);
  DelTree(Shared + '\VoiceModels', True, True, True);
  RemoveDir(Shared);
  App := RemoveBackslash(ExpandConstant('{app}'));
  if CompareText(ExtractFileName(App), 'LeagueHistory') = 0 then
    DelTree(App, True, True, True);
  Exec('powershell.exe',
    '-NoProfile -NonInteractive -Command "' +
    'Get-ScheduledTask -TaskName ''ShutdownAfterLoL_2300'' -ErrorAction SilentlyContinue | ' +
    'Where-Object { $_.Description -eq ''LeagueHistory.ScheduledShutdown.v1'' } | ' +
    'Unregister-ScheduledTask -Confirm:$false; ' +
    'Get-ChildItem Cert:\CurrentUser\My, Cert:\LocalMachine\TrustedPeople | ' +
    'Where-Object { $_.Subject -eq ''CN=LeagueHistory'' } | Remove-Item"',
    '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if (CurUninstallStep = usPostUninstall) and RemoveData then
    RemoveUserData();
end;

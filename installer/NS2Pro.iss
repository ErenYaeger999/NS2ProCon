#ifndef PublishDir
  #error PublishDir must point at the verified publish folder
#endif
#ifndef AppVersion
  #error AppVersion is required
#endif
#ifndef AppDisplayName
  #error AppDisplayName is required
#endif
#ifndef AppGuid
  #define AppGuid "C9638C50-B604-4185-A798-2B034C74847D"
#endif

[Setup]
AppId={{{#AppGuid}}
AppName={#AppDisplayName}
AppVersion={#AppVersion}
AppPublisher={#AppDisplayName}
AppSupportURL=mailto:945558278@qq.com
AppContact=945558278@qq.com
DefaultDirName={autopf}\{#AppDisplayName}
DefaultGroupName={#AppDisplayName}
DisableProgramGroupPage=yes
; Stable AppId + these options update an existing installation in place.
UsePreviousAppDir=yes
DisableDirPage=auto
AlwaysShowDirOnReadyPage=yes
UsePreviousGroup=yes
ArchitecturesAllowed=x64os
ArchitecturesInstallIn64BitMode=x64os
MinVersion=10.0.19041
PrivilegesRequired=admin
PrivilegesRequiredOverridesAllowed=dialog
UsePreviousPrivileges=yes
OutputDir={#SourcePath}..\artifacts\release
OutputBaseFilename=NS2Pro-{#AppVersion}-win-x64-setup
SetupIconFile={#PublishDir}\Assets\App.ico
UninstallDisplayIcon={app}\NS2Pro.exe
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
AppMutex=Local\NS2Pro.NativeApp
CloseApplications=no
RestartApplications=no
UsePreviousTasks=yes
UninstallDisplayName={#AppDisplayName}
InfoBeforeFile=install-info.txt

[Languages]
Name: "chinesesimplified"; MessagesFile: "ChineseSimplified.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "创建桌面快捷方式"; GroupDescription: "快捷方式："

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "{#PublishDir}\Prerequisites\ViGEmBus_1.22.0_x64_x86_arm64.exe"; Flags: dontcopy

[Icons]
Name: "{autoprograms}\{#AppDisplayName}"; Filename: "{app}\NS2Pro.exe"; WorkingDir: "{app}"
Name: "{autodesktop}\{#AppDisplayName}"; Filename: "{app}\NS2Pro.exe"; WorkingDir: "{app}"; Tasks: desktopicon

[InstallDelete]
#include "obsolete-files.iss"
Type: files; Name: "{autoprograms}\NS2 Pro.lnk"
Type: files; Name: "{autodesktop}\NS2 Pro 无线连接 PC.lnk"

[Run]
Filename: "{app}\NS2Pro.exe"; Description: "打开{#AppDisplayName}，开始连接手柄"; Flags: nowait postinstall skipifsilent runasoriginaluser; Check: CanLaunch

[Code]
var
  DriverNeedsRestart: Boolean;
  UpdatingExistingInstall: Boolean;

procedure InitializeWizard;
var
  UninstallKey: String;
  RootKey: Integer;
begin
  UninstallKey := 'Software\Microsoft\Windows\CurrentVersion\Uninstall\{{#AppGuid}}_is1';
  if IsAdminInstallMode then RootKey := HKLM64 else RootKey := HKCU64;
  UpdatingExistingInstall := RegKeyExists(RootKey, UninstallKey);
end;

function ShouldSkipPage(PageID: Integer): Boolean;
begin
  Result := UpdatingExistingInstall and ((PageID = wpInfoBefore) or (PageID = wpSelectDir) or (PageID = wpSelectTasks));
end;

procedure CurPageChanged(CurPageID: Integer);
begin
  if UpdatingExistingInstall and (CurPageID = wpReady) then begin
    WizardForm.ReadyLabel.Caption := '检测到已安装的{#AppDisplayName}，将更新到 v{#AppVersion}。沿用原安装位置、快捷方式和用户设置。';
    WizardForm.NextButton.Caption := '更新(&U)';
  end;
end;

function CanLaunch: Boolean;
begin
  Result := not DriverNeedsRestart;
end;

function HasNewerVersion(RootKey: Integer; var PreviousVersion: String): Boolean;
var
  VersionKey: String;
  InstalledCode, IncomingCode: Int64;
begin
  VersionKey := 'Software\Microsoft\Windows\CurrentVersion\Uninstall\{{#AppGuid}}_is1';
  Result := RegQueryStringValue(RootKey, VersionKey, 'DisplayVersion', PreviousVersion) and
    StrToVersion(PreviousVersion, InstalledCode) and StrToVersion('{#AppVersion}', IncomingCode) and
    (InstalledCode > IncomingCode);
end;

function InitializeSetup: Boolean;
var
  PreviousVersion: String;
begin
  if HasNewerVersion(HKLM64, PreviousVersion) or HasNewerVersion(HKCU64, PreviousVersion) then begin
    Result := False;
    SuppressibleMsgBox('已安装更高版本 v' + PreviousVersion + '，本安装包不会降级覆盖。若确需回退，请先卸载当前版本；个人设置会保留。', mbInformation, MB_OK, IDOK);
    exit;
  end;
  Result := not CheckForMutexes('Local\NS2Pro.NativeApp');
  if not Result then
    SuppressibleMsgBox('请先退出{#AppDisplayName}，再运行安装包。如果关闭窗口后仍在后台运行，请从右下角托盘菜单选择「断开并退出」。', mbInformation, MB_OK, IDOK);
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  ExitCode: Integer;
  DriverPath: String;
  Started: Boolean;
begin
  Result := '';
  if RegKeyExists(HKLM64, 'SYSTEM\CurrentControlSet\Services\ViGEmBus') then exit;
  WizardForm.PreparingLabel.Caption := '正在安装游戏手柄驱动，请稍候…';
  ExtractTemporaryFile('ViGEmBus_1.22.0_x64_x86_arm64.exe');
  DriverPath := ExpandConstant('{tmp}\ViGEmBus_1.22.0_x64_x86_arm64.exe');
  if IsAdminInstallMode then
    Started := Exec(DriverPath, '/exenoui /qn /norestart', '', SW_HIDE, ewWaitUntilTerminated, ExitCode)
  else
    Started := ShellExec('runas', DriverPath, '/exenoui /qn /norestart', '', SW_HIDE, ewWaitUntilTerminated, ExitCode);
  if not Started then begin
    Result := '无法启动官方驱动安装程序。安装驱动需要管理员权限，请允许权限提示后重试。';
    exit;
  end;
  case ExitCode of
    0: Log('ViGEmBus installed successfully.');
    3010, 1641: begin DriverNeedsRestart := True; NeedsRestart := True; end;
  else
    Result := '游戏手柄驱动安装未完成，错误码 ' + IntToStr(ExitCode) + '。安装日志可帮助排查，也可联系 945558278@qq.com。';
  end;
end;

function NeedRestart: Boolean;
begin
  Result := DriverNeedsRestart;
end;

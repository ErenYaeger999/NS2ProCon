#ifndef PublishDir
  #error PublishDir must point at the verified publish folder
#endif
#ifndef AppVersion
  #error AppVersion is required
#endif
#ifndef AppDisplayName
  #error AppDisplayName is required
#endif

[Setup]
AppId={{C9638C50-B604-4185-A798-2B034C74847D}
AppName={#AppDisplayName}
AppVersion={#AppVersion}
AppPublisher={#AppDisplayName}
AppSupportURL=mailto:945558278@qq.com
AppContact=945558278@qq.com
DefaultDirName={autopf}\{#AppDisplayName}
DefaultGroupName={#AppDisplayName}
DisableProgramGroupPage=yes
ArchitecturesAllowed=x64os
ArchitecturesInstallIn64BitMode=x64os
MinVersion=10.0.19041
PrivilegesRequired=admin
PrivilegesRequiredOverridesAllowed=commandline
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
Type: files; Name: "{autoprograms}\NS2 Pro.lnk"
Type: files; Name: "{autodesktop}\NS2 Pro 无线连接 PC.lnk"

[Run]
Filename: "{app}\NS2Pro.exe"; Description: "打开{#AppDisplayName}，开始连接手柄"; Flags: nowait postinstall skipifsilent runasoriginaluser; Check: CanLaunch

[Code]
var
  DriverNeedsRestart: Boolean;

function CanLaunch: Boolean;
begin
  Result := not DriverNeedsRestart;
end;

function InitializeSetup: Boolean;
begin
  Result := not CheckForMutexes('Local\NS2Pro.NativeApp');
  if not Result then
    SuppressibleMsgBox('请先退出{#AppDisplayName}，再运行安装包。如果关闭窗口后仍在后台运行，请从右下角托盘菜单选择「断开并退出」。', mbInformation, MB_OK, IDOK);
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  ExitCode: Integer;
  DriverPath: String;
begin
  Result := '';
  if RegKeyExists(HKLM64, 'SYSTEM\CurrentControlSet\Services\ViGEmBus') then exit;
  if not IsAdminInstallMode then begin
    Result := '安装游戏手柄驱动需要管理员权限。请重新运行安装包并允许管理员权限。';
    exit;
  end;
  WizardForm.PreparingLabel.Caption := '正在安装游戏手柄驱动，请稍候…';
  ExtractTemporaryFile('ViGEmBus_1.22.0_x64_x86_arm64.exe');
  DriverPath := ExpandConstant('{tmp}\ViGEmBus_1.22.0_x64_x86_arm64.exe');
  if not Exec(DriverPath, '/exenoui /qn /norestart', '', SW_HIDE, ewWaitUntilTerminated, ExitCode) then begin
    Result := '无法启动官方驱动安装程序。请检查管理员权限后重试。';
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

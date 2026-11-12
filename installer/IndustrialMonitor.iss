; 工业监控 — Windows 安装包（Inno Setup 6）
; 开发机编译：双击 build-installer.bat 或运行 scripts\build-installer.ps1
; 现场安装：双击生成的 IndustrialMonitor-{版本}-Setup.exe，无需命令行

#ifndef PublishDir
  #define PublishDir "..\src\HuaGuang.Monitor\bin\Release\net10.0-windows10.0.19041.0\win-x64\publish"
#endif

#ifndef MyAppVersion
  #define MyAppVersion "1.0.0"
#endif

#ifndef MyAppRevision
  #define MyAppRevision "1"
#endif

#define MyAppName "工业监控"
#define MyAppPublisher "Industrial Monitor"
#define MyAppExeName "HuaGuang.Monitor.exe"
#define MyAppPackageId "com.industrial.monitor"
#define MyAppId "{{A7C3E9F1-2B4D-4F8A-9E6C-1D5A0B3C7E2F}"
#define ServiceName "HuaGuangMonitor"
#define ServiceExeName "HuaGuang.Monitor.Service.exe"
#define ServiceDisplayName "工业监控采集服务"
#define ServiceDescription "工业监控 PLC 采集与 MQTT 推送后台服务"
#define WatchdogServiceName "HuaGuangMonitorWatchdog"
#define WatchdogExeName "HuaGuang.Monitor.Watchdog.Service.exe"
#define WatchdogDisplayName "工业监控守护服务"
#define WatchdogDescription "监控主采集服务、独立自启动实例与界面进程，异常退出时自动重启"

[Setup]
AppId={#MyAppId}
AppName={#MyAppName}
AppVersion={#MyAppVersion}.{#MyAppRevision}
AppVerName={#MyAppName} {#MyAppVersion}（修订 {#MyAppRevision}）
AppPublisher={#MyAppPublisher}
DefaultDirName={autopf}\IndustrialMonitor
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
OutputDir=output
OutputBaseFilename=IndustrialMonitor-{#MyAppVersion}-r{#MyAppRevision}-Setup
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=admin
; WinUI 3 / Windows App Runtime 需要 kernel32!IsWow64Process2（Win10 1709 起才有）。
; MAUI 官方最低是 1809（17763）。1607 LTSB / Server 2016 会弹「无法定位 IsWow64Process2」。
MinVersion=10.0.17763
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
SetupIconFile=appicon.ico
UninstallDisplayIcon={app}\appicon.ico
SetupLogging=yes
; 界面进程拦截关闭仅隐藏窗口，Restart Manager 无法结束进程会导致安装卡住。
CloseApplications=no
; 仅在编译时传入代码签名证书时定义。signtool 由 scripts\sign-with-signtool.ps1 传给 ISCC。
#ifdef SignToolConfigured
SignTool=huaguang
SignedUninstaller=yes
#endif

[Languages]
Name: "chinesesimplified"; MessagesFile: "languages\ChineseSimplified.isl"

[Messages]
WindowsVersionNotSupported=工业监控需要 64 位 Windows 10 版本 1809（内部版本 17763）或更高。%n%n当前系统过旧，无法加载 Windows App Runtime（常见报错：找不到入口点 IsWow64Process2）。%n%n可用：Win10 LTSC 2019/2021、Win10 21H2/22H2、Windows 11、Server 2019/2022。%n不可用：Win7/Win8、Win10 1507/1607（LTSB 2016）、Win10 1703、32 位 Windows。

[Tasks]
Name: "installservice"; Description: "安装并启动后台采集服务（推荐）"; GroupDescription: "附加选项:"; Flags: checkedonce
Name: "installwatchdog"; Description: "安装守护服务（监控采集/界面异常退出并重启，推荐）"; GroupDescription: "附加选项:"; Flags: checkedonce
Name: "desktopicon"; Description: "创建桌面快捷方式"; GroupDescription: "附加选项:"; Flags: checkedonce
Name: "startup"; Description: "登录 Windows 时自动打开监控界面（可选）"; GroupDescription: "附加选项:"; Flags: checkedonce

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; IconFilename: "{app}\appicon.ico"; WorkingDir: "{app}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; IconFilename: "{app}\appicon.ico"; Tasks: desktopicon; WorkingDir: "{app}"

[InstallDelete]
Type: files; Name: "{group}\先河订阅.lnk"
Type: files; Name: "{group}\华迪订阅.lnk"
Type: files; Name: "{group}\撒粉订阅.lnk"
Type: files; Name: "{group}\平板订阅.lnk"
Type: files; Name: "{group}\C型火焰订阅.lnk"
Type: files; Name: "{group}\S7订阅.lnk"
Type: files; Name: "{autodesktop}\先河订阅.lnk"
Type: files; Name: "{autodesktop}\华迪订阅.lnk"
Type: files; Name: "{autodesktop}\撒粉订阅.lnk"
Type: files; Name: "{autodesktop}\平板订阅.lnk"
Type: files; Name: "{autodesktop}\C型火焰订阅.lnk"
Type: files; Name: "{autodesktop}\S7订阅.lnk"

[Registry]
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "IndustrialMonitor"; ValueData: """{app}\{#MyAppExeName}"""; Tasks: startup; Flags: uninsdeletevalue

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "立即运行 {#MyAppName}"; Flags: nowait postinstall skipifsilent unchecked

[UninstallDelete]
Type: filesandordirs; Name: "{app}"

[Code]
var
  DeleteUserData: Boolean;
  ReplaceLinesPage: TWizardPage;
  KeepLinesRadio: TNewRadioButton;
  ReplaceLinesRadio: TNewRadioButton;

const
  ForceUiExitFlagRel = 'com.industrial.monitor\Data\installer-force-ui-exit.flag';

function WindowsBuildTooOldMessage(): String;
var
  Version: TWindowsVersion;
begin
  GetWindowsVersionEx(Version);
  Result :=
    '工业监控需要 64 位 Windows 10 版本 1809（内部版本 17763）或更高。' + #13#10 + #13#10 +
    '当前系统：Windows ' + IntToStr(Version.Major) + '.' + IntToStr(Version.Minor) +
    '，内部版本 ' + IntToStr(Version.Build) + '。' + #13#10 + #13#10 +
    '过旧系统无法加载 Windows App Runtime（常见报错：找不到入口点 IsWow64Process2）。' + #13#10 +
    '请升级到 Win10 LTSC 2019/2021、Win10 21H2/22H2 或 Windows 11 后再安装。';
end;

procedure InitializeWizard();
var
  Hint: TNewStaticText;
begin
  ReplaceLinesPage := CreateCustomPage(
    wpSelectTasks,
    '产线文件',
    '本机已有的产线 Excel 默认保留。需要时可以改用安装包里的那一份。');

  KeepLinesRadio := TNewRadioButton.Create(ReplaceLinesPage);
  KeepLinesRadio.Parent := ReplaceLinesPage.Surface;
  KeepLinesRadio.Caption := '保留本机产线文件（推荐）';
  KeepLinesRadio.Checked := True;
  KeepLinesRadio.Left := 0;
  KeepLinesRadio.Top := 0;
  KeepLinesRadio.Width := ReplaceLinesPage.SurfaceWidth;
  KeepLinesRadio.Height := ScaleY(22);

  ReplaceLinesRadio := TNewRadioButton.Create(ReplaceLinesPage);
  ReplaceLinesRadio.Parent := ReplaceLinesPage.Surface;
  ReplaceLinesRadio.Caption := '强制用安装包中的产线文件替换本机已有产线';
  ReplaceLinesRadio.Left := 0;
  ReplaceLinesRadio.Top := KeepLinesRadio.Top + KeepLinesRadio.Height + ScaleY(8);
  ReplaceLinesRadio.Width := ReplaceLinesPage.SurfaceWidth;
  ReplaceLinesRadio.Height := ScaleY(22);

  Hint := TNewStaticText.Create(ReplaceLinesPage);
  Hint.Parent := ReplaceLinesPage.Surface;
  Hint.Left := 0;
  Hint.Top := ReplaceLinesRadio.Top + ReplaceLinesRadio.Height + ScaleY(16);
  Hint.Width := ReplaceLinesPage.SurfaceWidth;
  Hint.Height := ScaleY(140);
  Hint.AutoSize := False;
  Hint.WordWrap := True;
  Hint.Caption :=
    '替换的是本机数据目录里与安装包同名的产线 Excel（通常在' + #13#10 +
    'C:\ProgramData\com.industrial.monitor\Data\lines）。' + #13#10 +
    '现场改过的点表、PLC 地址、MQTT 会变成安装包里的版本。' + #13#10 +
    '历史数据不删除，当前选中的产线也不会改。' + #13#10 +
    '被覆盖的原文件会先备份到该目录下的 _installer-backup-日期时间 文件夹。' + #13#10 + #13#10 +
    '不替换时，安装不会改动已有产线文件；本机还没有的产线仍由程序首次启动时补上。';
end;

function NextButtonClick(CurPageID: Integer): Boolean;
begin
  Result := True;
  if (ReplaceLinesPage <> nil) and (CurPageID = ReplaceLinesPage.ID) and ReplaceLinesRadio.Checked then
  begin
    Result := MsgBox(
      '将用安装包里的产线 Excel 覆盖本机已有的同名产线。' + #13#10 +
      '现场改过的点表、PLC 地址、MQTT 会被替换，此操作不能在安装程序里撤销。' + #13#10 +
      '原文件会先备份。' + #13#10 + #13#10 +
      '确定要强制替换吗？',
      mbConfirmation,
      MB_YESNO or MB_DEFBUTTON2) = IDYES;
  end;
end;

function InitializeSetup(): Boolean;
var
  Version: TWindowsVersion;
begin
  GetWindowsVersionEx(Version);
  if (Version.Major < 10) or ((Version.Major = 10) and (Version.Build < 17763)) then
  begin
    MsgBox(WindowsBuildTooOldMessage(), mbError, MB_OK);
    Result := False;
    Exit;
  end;
  Result := True;
end;

function ForceUiExitFlagPath(): String;
begin
  Result := ExpandConstant('{commonappdata}') + '\' + ForceUiExitFlagRel;
end;

procedure EnsureForceUiExitFlag();
begin
  ForceDirectories(ExtractFileDir(ForceUiExitFlagPath()));
  SaveStringToFile(ForceUiExitFlagPath(), '1', False);
end;

procedure RemoveForceUiExitFlag();
begin
  if FileExists(ForceUiExitFlagPath()) then
    DeleteFile(ForceUiExitFlagPath());
end;

procedure KillImage(const ImageName: String);
var
  ResultCode: Integer;
begin
  Exec(ExpandConstant('{sys}\taskkill.exe'), '/IM "' + ImageName + '" /T /F', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
end;

procedure StopWindowsService(const ServiceName: String);
var
  ResultCode: Integer;
begin
  { net stop 会等到服务真正退出；服务不存在时忽略错误。 }
  Exec(ExpandConstant('{sys}\net.exe'), 'stop ' + ServiceName, '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
end;

{ 升级/覆盖安装前必须先停守护、采集服务并结束界面，否则 exe 被占用会复制失败。 }
procedure StopRunningAppAndServices();
begin
  EnsureForceUiExitFlag();
  { 先停守护，避免它把采集服务或界面再拉起来。 }
  StopWindowsService('{#WatchdogServiceName}');
  StopWindowsService('{#ServiceName}');
  StopWindowsService('{#ServiceName}-acq-xianhe');
  StopWindowsService('{#ServiceName}-acq-huadi');
  StopWindowsService('{#ServiceName}-acq-safen');
  StopWindowsService('{#ServiceName}-acq-pingban');
  StopWindowsService('{#ServiceName}-acq-cyhy');
  StopWindowsService('{#ServiceName}-acq-zhidahy');
  StopWindowsService('{#ServiceName}-acq-shanghaihy');
  StopWindowsService('{#ServiceName}-acq-meshy');
  StopWindowsService('{#ServiceName}-acq-s7');
  StopWindowsService('{#ServiceName}-sub');
  KillImage('{#MyAppExeName}');
  KillImage('{#ServiceExeName}');
  KillImage('{#WatchdogExeName}');
  Sleep(1000);
end;

procedure KillMonitorUiProcess();
begin
  StopRunningAppAndServices();
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  Result := '';
  StopRunningAppAndServices();
end;

function ServiceExePath(): String;
begin
  Result := ExpandConstant('{app}\service\{#ServiceExeName}');
end;

function WatchdogExePath(): String;
begin
  Result := ExpandConstant('{app}\service\{#WatchdogExeName}');
end;

procedure StopAndDeleteMonitorService();
var
  ResultCode: Integer;
begin
  Exec(ExpandConstant('{sys}\sc.exe'), 'stop {#ServiceName}', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  Exec(ExpandConstant('{sys}\sc.exe'), 'delete {#ServiceName}', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
end;

procedure StopAndDeleteWatchdogService();
var
  ResultCode: Integer;
begin
  Exec(ExpandConstant('{sys}\sc.exe'), 'stop {#WatchdogServiceName}', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  Exec(ExpandConstant('{sys}\sc.exe'), 'delete {#WatchdogServiceName}', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
end;

procedure DisableShadowStacksForExe(const ExePath: String);
var
  ResultCode: Integer;
  Args: String;
begin
  if not FileExists(ExePath) then
    Exit;

  Args := '-NoProfile -ExecutionPolicy Bypass -Command "Set-ProcessMitigation -Name ''' +
    ExePath + ''' -Disable ShadowStacks"';
  Exec(ExpandConstant('{sys}\WindowsPowerShell\v1.0\powershell.exe'), Args, '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
end;

procedure ConfigureOldWindowsCompat();
begin
  DisableShadowStacksForExe(ExpandConstant('{app}\{#MyAppExeName}'));
  DisableShadowStacksForExe(ServiceExePath());
  if FileExists(WatchdogExePath()) then
    DisableShadowStacksForExe(WatchdogExePath());
end;

function InstallMonitorService(): Boolean;
var
  ResultCode: Integer;
  BinPath, CreateArgs: String;
begin
  Result := False;
  if not FileExists(ServiceExePath()) then
  begin
    MsgBox('未找到后台服务程序，跳过服务注册。', mbError, MB_OK);
    Exit;
  end;

  StopAndDeleteMonitorService();

  BinPath := ServiceExePath();
  CreateArgs := 'create {#ServiceName} binPath= "' + BinPath + '" start= auto DisplayName= "{#ServiceDisplayName}"';
  if not Exec(ExpandConstant('{sys}\sc.exe'), CreateArgs, '', SW_HIDE, ewWaitUntilTerminated, ResultCode) then
  begin
    MsgBox('注册 Windows 服务失败。', mbError, MB_OK);
    Exit;
  end;

  Exec(ExpandConstant('{sys}\sc.exe'), 'description {#ServiceName} "{#ServiceDescription}"', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  Exec(ExpandConstant('{sys}\sc.exe'), 'start {#ServiceName}', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  Result := True;
end;

function InstallWatchdogService(): Boolean;
var
  ResultCode: Integer;
  BinPath, CreateArgs: String;
begin
  Result := False;
  if not FileExists(WatchdogExePath()) then
  begin
    MsgBox('未找到守护服务程序，跳过守护服务注册。', mbInformation, MB_OK);
    Exit;
  end;

  StopAndDeleteWatchdogService();

  BinPath := WatchdogExePath();
  CreateArgs := 'create {#WatchdogServiceName} binPath= "' + BinPath + '" start= auto DisplayName= "{#WatchdogDisplayName}"';
  if not Exec(ExpandConstant('{sys}\sc.exe'), CreateArgs, '', SW_HIDE, ewWaitUntilTerminated, ResultCode) then
  begin
    MsgBox('注册守护 Windows 服务失败。', mbError, MB_OK);
    Exit;
  end;

  Exec(ExpandConstant('{sys}\sc.exe'), 'description {#WatchdogServiceName} "{#WatchdogDescription}"', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  Exec(ExpandConstant('{sys}\sc.exe'), 'start {#WatchdogServiceName}', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  Result := True;
end;

function SharedLinesDirectory(): String;
var
  OriginPath: String;
  Lines: TArrayOfString;
  Recorded: String;
begin
  Result := ExpandConstant('{commonappdata}\com.industrial.monitor\Data');
  OriginPath := Result + '\path-origin.txt';
  if LoadStringsFromFile(OriginPath, Lines) and (GetArrayLength(Lines) >= 2) then
  begin
    Recorded := Trim(Lines[1]);
    if (Recorded <> '') and DirExists(Recorded) then
      Result := Recorded;
  end;
  Result := Result + '\lines';
end;

procedure GrantUsersModify(const Directory: String);
var
  ResultCode: Integer;
begin
  { 与程序启动时一致：Users 可改产线 Excel，避免管理员复制后现场无法保存。 }
  Exec(
    ExpandConstant('{sys}\icacls.exe'),
    '"' + Directory + '" /grant *S-1-5-32-545:(OI)(CI)M /T /C',
    '',
    SW_HIDE,
    ewWaitUntilTerminated,
    ResultCode);
end;

procedure ReplaceLocalLineFiles();
var
  SourceDir, DestDir, BackupDir, SourceFile, DestFile: String;
  FindRec: TFindRec;
  Copied, BackedUp, Failed: Integer;
  FailedNames: String;
  SkipReplace: Boolean;
begin
  SourceDir := ExpandConstant('{app}\lines');
  DestDir := SharedLinesDirectory();
  if not DirExists(SourceDir) then
  begin
    MsgBox('安装目录中没有产线文件，无法替换。', mbError, MB_OK);
    Exit;
  end;

  ForceDirectories(DestDir);
  BackupDir := DestDir + '\_installer-backup-' + GetDateTimeString('yyyymmdd-hhnnss', #0, #0);
  Copied := 0;
  BackedUp := 0;
  Failed := 0;
  FailedNames := '';

  if FindFirst(SourceDir + '\*.xlsx', FindRec) then
  try
    repeat
      if ((FindRec.Attributes and FILE_ATTRIBUTE_DIRECTORY) = 0) and
         (CompareText(Copy(FindRec.Name, 1, 2), '~$') <> 0) then
      begin
        SourceFile := SourceDir + '\' + FindRec.Name;
        DestFile := DestDir + '\' + FindRec.Name;
        SkipReplace := False;
        if FileExists(DestFile) then
        begin
          if (not ForceDirectories(BackupDir)) or (not FileCopy(DestFile, BackupDir + '\' + FindRec.Name, False)) then
          begin
            Failed := Failed + 1;
            FailedNames := FailedNames + #13#10 + FindRec.Name + '（备份失败，未覆盖）';
            Log('备份产线文件失败，已跳过替换: ' + DestFile);
            SkipReplace := True;
          end
          else
            BackedUp := BackedUp + 1;
        end;
        if not SkipReplace then
        begin
          if FileCopy(SourceFile, DestFile, False) then
          begin
            Copied := Copied + 1;
            Log('已用安装包替换产线文件: ' + FindRec.Name);
          end
          else
          begin
            Failed := Failed + 1;
            FailedNames := FailedNames + #13#10 + FindRec.Name;
            Log('替换产线文件失败: ' + DestFile);
          end;
        end;
      end;
    until not FindNext(FindRec);
  finally
    FindClose(FindRec);
  end;

  if Copied > 0 then
    GrantUsersModify(DestDir);

  if not WizardSilent then
  begin
    if Failed > 0 then
      MsgBox(
        '已替换 ' + IntToStr(Copied) + ' 个产线文件，' + IntToStr(Failed) + ' 个失败。' +
        '请关闭正在打开这些 Excel 的程序后重新安装并再次选择替换。' + FailedNames,
        mbError, MB_OK)
    else if Copied = 0 then
      MsgBox('安装包中没有可替换的产线 Excel。', mbInformation, MB_OK)
    else if BackedUp = 0 then
      MsgBox('已写入 ' + IntToStr(Copied) + ' 个产线文件。本机之前没有同名文件。', mbInformation, MB_OK)
    else
      MsgBox(
        '已用安装包中的产线文件替换本机 ' + IntToStr(Copied) + ' 个文件。' + #13#10 +
        '原文件备份在：' + #13#10 + BackupDir,
        mbInformation, MB_OK);
  end;
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep = ssInstall then
    KillMonitorUiProcess();

  if CurStep = ssPostInstall then
  begin
    RemoveForceUiExitFlag();
    if (ReplaceLinesRadio <> nil) and ReplaceLinesRadio.Checked then
      ReplaceLocalLineFiles();
    ConfigureOldWindowsCompat();
    if WizardIsTaskSelected('installservice') then
      InstallMonitorService();
    if WizardIsTaskSelected('installwatchdog') then
      InstallWatchdogService();
  end;
end;

procedure DeletePackageUserDataUnder(const Root: String);
var
  FindRec: TFindRec;
  PublisherDir, PackageDir: String;
begin
  if not DirExists(Root) then
    Exit;

  if FindFirst(Root + '\*', FindRec) then
  try
    repeat
      if (FindRec.Name <> '.') and (FindRec.Name <> '..') and
         ((FindRec.Attributes and FILE_ATTRIBUTE_DIRECTORY) <> 0) then
      begin
        PublisherDir := Root + '\' + FindRec.Name;
        PackageDir := PublisherDir + '\{#MyAppPackageId}';
        if DirExists(PackageDir) then
          DelTree(PackageDir, True, True, True);
      end;
    until not FindNext(FindRec);
  finally
    FindClose(FindRec);
  end;
end;

function InitializeUninstall(): Boolean;
var
  Answer: Integer;
begin
  DeleteUserData := False;
  Answer := MsgBox(
    '是否同时删除用户数据？' + #13#10 + #13#10 +
    '包括：历史数据库 (history.db)、' + #13#10 +
    '以及 AppData 中保存的产线 Excel 配置。' + #13#10 + #13#10 +
    '选「是」将永久删除，无法恢复。',
    mbConfirmation,
    MB_YESNO or MB_DEFBUTTON2);
  DeleteUserData := (Answer = IDYES);
  Result := True;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if CurUninstallStep = usUninstall then
  begin
    StopRunningAppAndServices();
    StopAndDeleteWatchdogService();
    StopAndDeleteMonitorService();
  end;

  if (CurUninstallStep = usPostUninstall) and DeleteUserData then
  begin
    DeletePackageUserDataUnder(ExpandConstant('{localappdata}'));
    DeletePackageUserDataUnder(ExpandConstant('{userappdata}'));
  end;
end;

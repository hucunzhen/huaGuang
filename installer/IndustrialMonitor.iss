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
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
SetupIconFile=appicon.ico
UninstallDisplayIcon={app}\appicon.ico
SetupLogging=yes

[Languages]
Name: "chinesesimplified"; MessagesFile: "languages\ChineseSimplified.isl"

[Tasks]
Name: "desktopicon"; Description: "创建桌面快捷方式"; GroupDescription: "附加选项:"; Flags: checkedonce
Name: "startup"; Description: "开机自动启动（Windows 登录后运行）"; GroupDescription: "附加选项:"; Flags: checkedonce

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; IconFilename: "{app}\appicon.ico"; WorkingDir: "{app}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; IconFilename: "{app}\appicon.ico"; Tasks: desktopicon; WorkingDir: "{app}"

[Registry]
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "IndustrialMonitor"; ValueData: """{app}\{#MyAppExeName}"""; Tasks: startup; Flags: uninsdeletevalue

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "立即运行 {#MyAppName}"; Flags: nowait postinstall skipifsilent unchecked

[UninstallDelete]
Type: filesandordirs; Name: "{app}"

[Code]
var
  DeleteUserData: Boolean;

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
    '包括：本地设置 (settings.json)、历史数据库 (history.db)、' + #13#10 +
    '以及 AppData 中保存的产线 Excel 配置。' + #13#10 + #13#10 +
    '选「是」将永久删除，无法恢复。',
    mbConfirmation,
    MB_YESNO or MB_DEFBUTTON2);
  DeleteUserData := (Answer = IDYES);
  Result := True;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if (CurUninstallStep = usPostUninstall) and DeleteUserData then
  begin
    DeletePackageUserDataUnder(ExpandConstant('{localappdata}'));
    DeletePackageUserDataUnder(ExpandConstant('{userappdata}'));
  end;
end;

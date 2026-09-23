#ifndef AppVersion
  #define AppVersion "0.1.0"
#endif

#ifndef Runtime
  #define Runtime "win-x64"
#endif

#define AppName "桌面收纳"
#define AppPublisher "Mua-xiu"
#define AppExeName "DesktopOrganizer.exe"
#define RepoRoot "..\.."
#define PublishDir RepoRoot + "\artifacts\publish\" + Runtime

[Setup]
AppId={{B6D64D5B-E695-4C8A-9348-E823B74A766E}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher={#AppPublisher}
AppPublisherURL=https://github.com/Mua-xiu/desktop-icons-storage
AppSupportURL=https://github.com/Mua-xiu/desktop-icons-storage/issues
DefaultDirName={localappdata}\Programs\DesktopOrganizer
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
DisableDirPage=no
UsePreviousAppDir=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0
OutputDir={#RepoRoot}\artifacts\installer
OutputBaseFilename=DesktopOrganizer-Setup-{#AppVersion}-{#Runtime}
SetupIconFile={#RepoRoot}\src\App\Assets\app.ico
UninstallDisplayIcon={app}\{#AppExeName}
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
CloseApplications=yes
CloseApplicationsFilter={#AppExeName}
RestartApplications=no
AppMutex=Local\DesktopOrganizer.SingleInstance
ChangesEnvironment=no
ChangesAssociations=no
VersionInfoVersion={#AppVersion}
VersionInfoDescription=桌面收纳安装程序
VersionInfoProductName=桌面收纳 DesktopOrganizer
VersionInfoCompany={#AppPublisher}

[Languages]
Name: "chinesesimplified"; MessagesFile: "compiler:Languages\ChineseSimplified.isl"

[Tasks]
Name: "desktopicon"; Description: "创建桌面快捷方式"; GroupDescription: "附加快捷方式："; Flags: unchecked

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\{#AppExeName}"; WorkingDir: "{app}"; IconFilename: "{app}\{#AppExeName}"
Name: "{group}\卸载{#AppName}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExeName}"; WorkingDir: "{app}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#AppExeName}"; Description: "启动{#AppName}"; Flags: nowait postinstall skipifsilent

[Code]
procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if CurUninstallStep = usUninstall then
  begin
    { 只清理应用创建的自启值；用户配置与 DesktopBlocks 收纳文件始终保留。 }
    RegDeleteValue(HKEY_CURRENT_USER,
      'Software\Microsoft\Windows\CurrentVersion\Run', 'DesktopOrganizer');
  end;
end;

#ifndef AppVersion
  #define AppVersion "0.2.0"
#endif

#ifndef Runtime
  #define Runtime "win-x64"
#endif

#define AppName "DesktopIconsStorage"
#define AppPublisher "Mua-xiu"
#define AppExeName "DesktopIconsStorage.exe"
#define RepoRoot "..\.."
#define PublishDir RepoRoot + "\artifacts\publish\" + Runtime

[Setup]
AppId={{B6D64D5B-E695-4C8A-9348-E823B74A766E}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher={#AppPublisher}
AppPublisherURL=https://github.com/Mua-xiu/desktop-icons-storage
AppSupportURL=https://github.com/Mua-xiu/desktop-icons-storage/issues
DefaultDirName={localappdata}\Programs\DesktopIconsStorage
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
DisableDirPage=no
UsePreviousAppDir=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0
OutputDir={#RepoRoot}\artifacts\installer
OutputBaseFilename=DesktopIconsStorage-Setup-{#AppVersion}-{#Runtime}
SetupIconFile={#RepoRoot}\src\App\Assets\app.ico
UninstallDisplayIcon={app}\{#AppExeName}
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
CloseApplications=yes
CloseApplicationsFilter={#AppExeName}
RestartApplications=no
AppMutex=Local\DesktopIconsStorage.SingleInstance
ChangesEnvironment=no
ChangesAssociations=no
VersionInfoVersion={#AppVersion}
VersionInfoDescription=DesktopIconsStorage Installer
VersionInfoProductName=DesktopIconsStorage
VersionInfoCompany={#AppPublisher}

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Additional shortcuts:"; Flags: unchecked

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\{#AppExeName}"; WorkingDir: "{app}"; IconFilename: "{app}\{#AppExeName}"
Name: "{group}\Uninstall {#AppName}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExeName}"; WorkingDir: "{app}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#AppExeName}"; Description: "Launch {#AppName}"; Flags: nowait postinstall skipifsilent

[Code]
function ReadStorageRoot(): String;
begin
  if not RegQueryStringValue(HKEY_CURRENT_USER,
    'Software\DesktopIconsStorage', 'StorageRoot', Result) then
    Result := ExpandConstant('{userprofile}\DesktopIconsStorage');
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  StorageRoot: String;
  ResultCode: Integer;
  CleanupSucceeded: Boolean;
begin
  if CurUninstallStep = usUninstall then
  begin
    StorageRoot := ReadStorageRoot();
    if MsgBox(
      'Do you want to move all stored icons back to the Desktop and remove ' +
      'DesktopIconsStorage data?' + #13#10 + #13#10 +
      'Choose No to keep all stored files and settings.',
      mbConfirmation, MB_YESNO or MB_DEFBUTTON2) = IDYES then
    begin
      CleanupSucceeded := Exec(
        ExpandConstant('{app}\{#AppExeName}'), '--uninstall-cleanup', '',
        SW_HIDE, ewWaitUntilTerminated, ResultCode) and (ResultCode = 0);
      if not CleanupSucceeded then
        MsgBox(
          'Some data could not be restored automatically. The remaining files ' +
          'have been kept at:' + #13#10 + StorageRoot + #13#10 + #13#10 +
          'Settings may remain at:' + #13#10 +
          ExpandConstant('{userappdata}\DesktopIconsStorage'),
          mbError, MB_OK);
    end
    else
      MsgBox(
        'Your stored icons and settings have been preserved.' + #13#10 + #13#10 +
        'Stored icons:' + #13#10 + StorageRoot + #13#10 + #13#10 +
        'Settings and layout:' + #13#10 +
        ExpandConstant('{userappdata}\DesktopIconsStorage'),
        mbInformation, MB_OK);

    RegDeleteValue(HKEY_CURRENT_USER,
      'Software\Microsoft\Windows\CurrentVersion\Run', 'DesktopIconsStorage');
    RegDeleteValue(HKEY_CURRENT_USER,
      'Software\Microsoft\Windows\CurrentVersion\Run', 'DesktopOrganizer');
    RegDeleteKeyIncludingSubkeys(HKEY_CURRENT_USER, 'Software\DesktopIconsStorage');
  end;
end;

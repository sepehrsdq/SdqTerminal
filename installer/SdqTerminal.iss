#define AppName "SdqTerminal"
#ifndef AppVersion
  #error AppVersion must be supplied by build-release.ps1 or the release workflow
#endif
#define AppPublisher "Sepehrsdq"
#define AppExeName "SdqTerminal.exe"
#define PublishDirectory "..\artifacts\publish\win-x64"

[Setup]
AppId={{A5BC0F01-F4CD-49C4-B85F-8B88ACDC4416}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher={#AppPublisher}
DefaultDirName={autopf}\Sepehrsdq\SdqTerminal
DefaultGroupName=SdqTerminal
; The product was renamed from Rtl Terminal; never reuse that install folder or group.
UsePreviousAppDir=no
UsePreviousGroup=no
UninstallDisplayName=SdqTerminal
UninstallDisplayIcon={app}\{#AppExeName}
OutputDir=..\artifacts\release
OutputBaseFilename=SdqTerminal-Setup-{#AppVersion}-x64
SetupIconFile=..\src\SdqTerminal\Assets\SdqTerminal.ico
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
VersionInfoVersion={#AppVersion}.0
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
PrivilegesRequired=admin
MinVersion=10.0.19041
CloseApplications=yes
RestartApplications=no

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Additional shortcuts:"

[Files]
Source: "{#PublishDirectory}\{#AppExeName}"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#PublishDirectory}\ConPty\*"; DestDir: "{app}\ConPty"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\SdqTerminal"; Filename: "{app}\{#AppExeName}"
Name: "{group}\Uninstall SdqTerminal"; Filename: "{uninstallexe}"
Name: "{autodesktop}\SdqTerminal"; Filename: "{app}\{#AppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#AppExeName}"; Description: "Launch SdqTerminal"; Flags: nowait postinstall skipifsilent

[InstallDelete]
Type: filesandordirs; Name: "{commonprograms}\Rtl Terminal"
Type: files; Name: "{autodesktop}\Rtl Terminal.lnk"

[UninstallDelete]
Type: filesandordirs; Name: "{app}"

[UninstallRun]
Filename: "{sys}\reg.exe"; Parameters: "delete ""HKCU\Software\Classes\Directory\shell\SdqTerminal"" /f"; Flags: runhidden; RunOnceId: "RemoveDirectoryShellMenu"
Filename: "{sys}\reg.exe"; Parameters: "delete ""HKCU\Software\Classes\Directory\Background\shell\SdqTerminal"" /f"; Flags: runhidden; RunOnceId: "RemoveBackgroundShellMenu"

[Code]
procedure RemoveLegacyInstall;
var
  LegacyDir: String;
  ResultCode: Integer;
  Waited: Integer;
begin
  { Releases built before the rename used the same AppId, so an in-place
    upgrade would leave the old folder and executable behind. Remove that
    installation first, then continue into the new folder. }
  LegacyDir := ExpandConstant('{autopf}\behnamapps\Rtl Terminal');
  if CompareText(ExpandConstant('{app}'), LegacyDir) = 0 then
    Exit;
  if not FileExists(LegacyDir + '\unins000.exe') then
    Exit;
  if not Exec(LegacyDir + '\unins000.exe',
      '/VERYSILENT /SUPPRESSMSGBOXES /NORESTART', '', SW_HIDE,
      ewNoWait, ResultCode) then
    Exit;
  Waited := 0;
  while (Waited < 90000) and FileExists(LegacyDir + '\unins000.exe') do
  begin
    Sleep(500);
    Waited := Waited + 500;
  end;
  { Let the old uninstaller finish deleting its registry entry before this
    installation writes the shared uninstall key. }
  Sleep(3000);
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep = ssInstall then
    RemoveLegacyInstall;
end;

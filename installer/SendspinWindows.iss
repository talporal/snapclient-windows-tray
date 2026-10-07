#ifndef AppVersion
#define AppVersion "0.2.0"
#endif
[Setup]
AppId={{45987F72-3694-4930-A68D-54B49E2F50D1}
AppName=Sendspin Windows
AppVersion={#AppVersion}
DefaultDirName={autopf}\Sendspin Windows
DefaultGroupName=Sendspin Windows
PrivilegesRequired=admin
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.22000
OutputDir={#OutputDir}
OutputBaseFilename=SendspinWindows-{#AppVersion}-x64
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
SetupIconFile={#SourceRoot}\Tray\Assets\App.ico
UninstallDisplayIcon={app}\Tray\Sendspin.Tray.exe
CloseApplications=yes
[Files]
Source: "{#SourceRoot}\Tray\*"; DestDir: "{app}\Tray"; Flags: recursesubdirs createallsubdirs ignoreversion
Source: "{#SourceRoot}\Service\*"; DestDir: "{app}\Service"; Flags: recursesubdirs createallsubdirs ignoreversion
Source: "{#SourceRoot}\Scripts\*"; DestDir: "{app}\Scripts"; Flags: ignoreversion
Source: "{#SourceRoot}\Notices\*"; DestDir: "{app}\Notices"; Flags: ignoreversion
Source: "{#SourceRoot}\Scripts\prepare-upgrade.ps1"; DestDir: "{tmp}"; Flags: dontcopy
[InstallDelete]
Type: filesandordirs; Name: "{app}\Tray"
Type: filesandordirs; Name: "{app}\Service"
Type: filesandordirs; Name: "{app}\Scripts"
Type: filesandordirs; Name: "{app}\Notices"
[Icons]
Name: "{group}\Sendspin settings"; Filename: "{app}\Tray\Sendspin.Tray.exe"
Name: "{group}\Uninstall Sendspin Windows"; Filename: "{uninstallexe}"
[Registry]
Root: HKLM; Subkey: "SOFTWARE\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "SendspinWindowsTray"; ValueData: """{app}\Tray\Sendspin.Tray.exe"" --background"; Flags: uninsdeletevalue
[Run]
Filename: "{app}\Tray\Sendspin.Tray.exe"; Description: "Open Sendspin settings"; Flags: postinstall nowait skipifsilent runasoriginaluser
[UninstallRun]
Filename: "{sys}\WindowsPowerShell\v1.0\powershell.exe"; Parameters: "-NoProfile -ExecutionPolicy Bypass -File ""{app}\Scripts\uninstall-service.ps1"""; Flags: runhidden waituntilterminated; RunOnceId: "RemoveSendspinService"
[Code]
function PrepareToInstall(var NeedsRestart: Boolean): String;
var ExitCode: Integer;
begin
 Result := '';
 ExtractTemporaryFile('prepare-upgrade.ps1');
 if not Exec(ExpandConstant('{sys}\WindowsPowerShell\v1.0\powershell.exe'), '-NoProfile -ExecutionPolicy Bypass -File "'+ExpandConstant('{tmp}\prepare-upgrade.ps1')+'"', '', SW_HIDE, ewWaitUntilTerminated, ExitCode) then Result := 'Could not prepare the previous installation.'
 else if ExitCode <> 0 then Result := 'Could not stop the previous audio service. Close controls and stop it before retrying.';
end;
procedure CurStepChanged(CurStep: TSetupStep);
var ExitCode: Integer;
begin
 if CurStep = ssPostInstall then begin
  if not Exec(ExpandConstant('{sys}\WindowsPowerShell\v1.0\powershell.exe'), '-NoProfile -ExecutionPolicy Bypass -File "'+ExpandConstant('{app}\Scripts\install-service.ps1')+'" -InstallRoot "'+ExpandConstant('{app}')+'"', '', SW_HIDE, ewWaitUntilTerminated, ExitCode) then RaiseException('Cannot start service installation.')
  else if ExitCode <> 0 then RaiseException('Service installation failed. Check Windows audio services and administrator access.');
 end;
end;

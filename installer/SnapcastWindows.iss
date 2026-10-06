#ifndef AppVersion
#define AppVersion "0.1.0"
#endif
[Setup]
AppId={{45987F72-3694-4930-A68D-54B49E2F50D1}
AppName=Snapcast Windows
AppVersion={#AppVersion}
DefaultDirName={autopf}\Snapcast Windows
DefaultGroupName=Snapcast Windows
PrivilegesRequired=admin
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.22000
OutputDir={#OutputDir}
OutputBaseFilename=SnapcastWindows-{#AppVersion}-x64
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
CloseApplications=yes
[Files]
Source: "{#SourceRoot}\Tray\*"; DestDir: "{app}\Tray"; Flags: recursesubdirs createallsubdirs ignoreversion
Source: "{#SourceRoot}\Service\*"; DestDir: "{app}\Service"; Flags: recursesubdirs createallsubdirs ignoreversion
Source: "{#SourceRoot}\Scripts\*"; DestDir: "{app}\Scripts"; Flags: ignoreversion
Source: "{#SourceRoot}\vc_redist.exe"; DestDir: "{tmp}"; Flags: deleteafterinstall
Source: "{#SourceRoot}\Notices\*"; DestDir: "{app}\Notices"; Flags: recursesubdirs createallsubdirs ignoreversion
[Icons]
Name: "{group}\Snapcast controls"; Filename: "{app}\Tray\Snapcast.Tray.exe"
Name: "{group}\Uninstall Snapcast Windows"; Filename: "{uninstallexe}"
[Registry]
Root: HKLM; Subkey: "SOFTWARE\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "SnapcastWindowsTray"; ValueData: """{app}\Tray\Snapcast.Tray.exe"" --background"; Flags: uninsdeletevalue
[Run]
Filename: "{tmp}\vc_redist.exe"; Parameters: "/install /quiet /norestart"; Flags: waituntilterminated; StatusMsg: "Installing audio runtime…"
Filename: "{app}\Tray\Snapcast.Tray.exe"; Description: "Open Snapcast controls"; Flags: postinstall nowait skipifsilent runasoriginaluser
[UninstallRun]
Filename: "{sys}\WindowsPowerShell\v1.0\powershell.exe"; Parameters: "-NoProfile -ExecutionPolicy Bypass -File ""{app}\Scripts\uninstall-service.ps1"""; Flags: runhidden waituntilterminated
[Code]
function PrepareToInstall(var NeedsRestart: Boolean): String;
var Code: Integer;
begin
  Result := '';
  if FileExists(ExpandConstant('{app}\Scripts\prepare-upgrade.ps1')) then begin
    if not Exec(ExpandConstant('{sys}\WindowsPowerShell\v1.0\powershell.exe'), '-NoProfile -ExecutionPolicy Bypass -File "'+ExpandConstant('{app}\Scripts\prepare-upgrade.ps1')+'"', '', SW_HIDE, ewWaitUntilTerminated, Code) then Result := 'Could not stop the previous service.'
    else if Code <> 0 then Result := 'Could not stop the previous service. Close controls and stop SnapcastWindows before retrying.';
  end;
end;


procedure CurStepChanged(CurStep: TSetupStep);
var ExitCode: Integer;
begin
  if CurStep = ssPostInstall then begin
    if not Exec(ExpandConstant('{sys}\WindowsPowerShell\v1.0\powershell.exe'), '-NoProfile -ExecutionPolicy Bypass -File "'+ExpandConstant('{app}\Scripts\install-service.ps1')+'" -InstallRoot "'+ExpandConstant('{app}')+'"', '', SW_HIDE, ewWaitUntilTerminated, ExitCode) then
      RaiseException('Unable to run service installation.')
    else if ExitCode <> 0 then
      RaiseException('Background service installation failed. Check Windows Audio services and administrator permissions.');
  end;
end;

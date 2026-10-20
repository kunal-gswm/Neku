; ─────────────────────────────────────────────────────────────────────────────
; Neku - Inno Setup 6 Script
; Generates artifacts\Neku-Setup-<version>.exe (per-user, non-admin Windows installer)
; Installs desktop companion to {app}\Neku.exe and CLI to {app}\bin\neku.exe
; Automatically registers {app}\bin in User HKCU\Environment\Path
; ─────────────────────────────────────────────────────────────────────────────

#ifndef MyAppVersion
  #define MyAppVersion "1.0.2"
#endif

#define MyAppName "Neku"
#define MyAppPublisher "Neku"
#define MyAppURL "https://github.com/kunal-gswm/Neku"
#define MyAppExeName "Neku.exe"
#define MyCliExeName "neku.exe"

[Setup]
AppId={{5D9D49C7-9D19-4C79-99E7-3E5A47C7B1F2}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}
AppUpdatesURL={#MyAppURL}
DefaultDirName={localappdata}\Programs\{#MyAppName}
DisableProgramGroupPage=yes
LicenseFile=..\LICENSE
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=commandline
OutputDir=..\artifacts
OutputBaseFilename=Neku-Setup-{#MyAppVersion}
SetupIconFile=..\src\DesktopPet\Assets\app.ico
UninstallDisplayIcon={app}\{#MyAppExeName}
UninstallDisplayName={#MyAppName}
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
CloseApplications=yes
AppMutex=Neku.SingleInstance
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
; Desktop icon: selected by default
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"
; Autostart: opt-in / unchecked for fresh install, preserved on upgrade via [Code]
Name: "autostart"; Description: "Start Neku when I sign in to Windows"; GroupDescription: "Windows Integration:"; Flags: unchecked

[Files]
Source: "..\dist\{#MyAppExeName}"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\dist\bin\{#MyCliExeName}"; DestDir: "{app}\bin"; Flags: ignoreversion
Source: "..\dist\neku.cmd"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\src\DesktopPet\Assets\app.ico"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\LICENSE"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\THIRD_PARTY_NOTICES.md"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
; Start Menu shortcut: has Ctrl+Alt+N hotkey and --launcher-start argument
Name: "{autoprograms}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Parameters: "--launcher-start"; IconFilename: "{app}\app.ico"; HotKey: "Ctrl+Alt+N"; Comment: "Neku Desktop Companion"
; Desktop shortcut (optional, no hotkey)
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; IconFilename: "{app}\app.ico"; Tasks: desktopicon; Comment: "Neku Desktop Companion"

[Registry]
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "Neku"; ValueData: """{app}\{#MyAppExeName}"""; Flags: uninsdeletevalue; Tasks: autostart

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(MyAppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent

[Code]
const
  WM_SETTINGCHANGE = $001A;
  SMTO_ABORTIFHUNG = $0002;

function SendMessageTimeout(hWnd: HWND; Msg: UINT; wParam: Longint; lParam: String; fuFlags: UINT; uTimeout: UINT; out lpdwResult: Longint): Longint;
external 'SendMessageTimeoutW@user32.dll stdcall';

procedure BroadcastEnvironmentChange;
var
  Dummy: Longint;
begin
  SendMessageTimeout(HWND_BROADCAST, WM_SETTINGCHANGE, 0, 'Environment', SMTO_ABORTIFHUNG, 2000, Dummy);
end;

procedure AddDirToUserPath(DirToAdd: String);
var
  CurrentPath: String;
  NewPath: String;
  Target: String;
  UpperCurrent: String;
  UpperTarget: String;
begin
  Target := RemoveQuotes(DirToAdd);
  if not RegQueryStringValue(HKCU, 'Environment', 'Path', CurrentPath) then
    CurrentPath := '';

  UpperCurrent := ';' + Uppercase(CurrentPath) + ';';
  UpperTarget := ';' + Uppercase(Target) + ';';

  if Pos(UpperTarget, UpperCurrent) = 0 then
  begin
    if CurrentPath = '' then
      NewPath := Target
    else if Copy(CurrentPath, Length(CurrentPath), 1) = ';' then
      NewPath := CurrentPath + Target
    else
      NewPath := CurrentPath + ';' + Target;

    if RegWriteStringValue(HKCU, 'Environment', 'Path', NewPath) then
      BroadcastEnvironmentChange;
  end;
end;

procedure RemoveDirFromUserPath(DirToRemove: String);
var
  CurrentPath: String;
  NewPath: String;
  Target: String;
  UpperCurrent: String;
  UpperTarget: String;
  P: Integer;
begin
  Target := RemoveQuotes(DirToRemove);
  if RegQueryStringValue(HKCU, 'Environment', 'Path', CurrentPath) then
  begin
    UpperTarget := Uppercase(Target);
    UpperCurrent := Uppercase(CurrentPath);

    if UpperCurrent = UpperTarget then
      NewPath := ''
    else if Pos(UpperTarget + ';', UpperCurrent) = 1 then
    begin
      NewPath := CurrentPath;
      Delete(NewPath, 1, Length(Target) + 1);
    end
    else if Pos(';' + UpperTarget + ';', UpperCurrent) > 0 then
    begin
      P := Pos(';' + UpperTarget + ';', UpperCurrent);
      NewPath := CurrentPath;
      Delete(NewPath, P, Length(Target) + 1);
    end
    else if Pos(';' + UpperTarget, UpperCurrent) > 0 then
    begin
      P := Pos(';' + UpperTarget, UpperCurrent);
      NewPath := CurrentPath;
      Delete(NewPath, P, Length(Target) + 1);
    end
    else
      NewPath := CurrentPath;

    if NewPath <> CurrentPath then
    begin
      RegWriteStringValue(HKCU, 'Environment', 'Path', NewPath);
      BroadcastEnvironmentChange;
    end;
  end;
end;

procedure InitializeWizard;
var
  ExistingVal: String;
begin
  if RegQueryStringValue(HKCU, 'Software\Microsoft\Windows\CurrentVersion\Run', 'Neku', ExistingVal) then
  begin
    WizardSelectTasks('autostart');
  end;
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep = ssPostInstall then
  begin
    // 1. Manage autostart registry entry
    if not WizardIsTaskSelected('autostart') then
    begin
      RegDeleteValue(HKCU, 'Software\Microsoft\Windows\CurrentVersion\Run', 'Neku');
    end;

    // 2. Register {app}\bin in User PATH
    AddDirToUserPath(ExpandConstant('{app}\bin'));
  end;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if CurUninstallStep = usPostUninstall then
  begin
    // Cleanly remove {app}\bin from User PATH on uninstall
    RemoveDirFromUserPath(ExpandConstant('{app}\bin'));
  end;
end;

procedure CurPageChanged(CurPageID: Integer);
begin
  if CurPageID = wpFinished then
  begin
    WizardForm.FinishedLabel.Caption :=
      WizardForm.FinishedLabel.Caption + #13#10#13#10 +
      'Terminal CLI:' + #13#10 +
      '• Run "neku --help" from PowerShell or Windows Terminal' + #13#10 +
      '  (Restart open terminal windows to refresh PATH)' + #13#10#13#10 +
      'Keyboard Shortcuts:' + #13#10 +
      '• Ctrl + Alt + N : Launch or unhide Neku' + #13#10 +
      '• Ctrl + Alt + Q : Gracefully exit Neku';
  end;
end;

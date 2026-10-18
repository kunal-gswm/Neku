; ─────────────────────────────────────────────────────────────────────────────
; Neku - Inno Setup 6 Script
; Generates artifacts\Neku-Setup-<version>.exe (per-user, non-admin Windows installer)
; ─────────────────────────────────────────────────────────────────────────────

#ifndef MyAppVersion
  #define MyAppVersion "0.5.0"
#endif

#define MyAppName "Neku"
#define MyAppPublisher "Neku"
#define MyAppURL "https://github.com/kunal-gswm/Neku"
#define MyAppExeName "Neku.exe"

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
// Pre-select autostart task if user already had Neku autostart configured in Windows registry
procedure InitializeWizard;
var
  ExistingVal: String;
begin
  if RegQueryStringValue(HKCU, 'Software\Microsoft\Windows\CurrentVersion\Run', 'Neku', ExistingVal) then
  begin
    WizardSelectTasks('autostart');
  end;
end;

// If user unchecked autostart task, ensure any previous autostart registry entry is removed
procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep = ssPostInstall then
  begin
    if not WizardIsTaskSelected('autostart') then
    begin
      RegDeleteValue(HKCU, 'Software\Microsoft\Windows\CurrentVersion\Run', 'Neku');
    end;
  end;
end;

// Show keyboard shortcuts info on completion page
procedure CurPageChanged(CurPageID: Integer);
begin
  if CurPageID = wpFinished then
  begin
    WizardForm.FinishedLabel.Caption :=
      WizardForm.FinishedLabel.Caption + #13#10#13#10 +
      'Keyboard Shortcuts:' + #13#10 +
      '• Ctrl + Alt + N : Launch or unhide Neku' + #13#10 +
      '• Ctrl + Alt + Q : Gracefully exit Neku';
  end;
end;

; Fenstr Installer (Inno Setup 6)
;
; Usage:  iscc /DVersion=x.y.z /DPublishDir=<path> installer\fenstr.iss
; The release script (scripts/release.ps1) invokes this automatically.

#ifndef Version
  #define Version "0.0.0"
#endif
#ifndef PublishDir
  #error "Pass /DPublishDir=<absolute-path-to-publish-output> on the ISCC command line."
#endif

#define AppName      "Fenstr"
#define AppExeName   "Fenstr.exe"
#define AppPublisher "patrichiel"
#define AppURL       "https://github.com/patrickiel/Fenstr"

[Setup]
AppId={{B7E3F1A2-8C4D-4E5F-9A6B-1D2E3F4A5B6C}
AppName={#AppName}
AppVersion={#Version}
AppVerName={#AppName} {#Version}
AppPublisher={#AppPublisher}
AppPublisherURL={#AppURL}
AppSupportURL={#AppURL}/issues
DefaultDirName={autopf}\{#AppName}
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
PrivilegesRequired=admin
OutputBaseFilename=Fenstr-v{#Version}-setup
SetupIconFile=..\Assets\tray-dark.ico
UninstallDisplayIcon={app}\{#AppExeName}
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
AppMutex=Fenstr.SingleInstance.{{8F3B2A1C-5E4D-4C7B-9A6F-1D2E3F4A5B6C}
CloseApplications=yes
CloseApplicationsFilter={#AppExeName}
MinVersion=10.0.17763
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "autostart"; Description: "Start {#AppName} automatically on login"; \
  GroupDescription: "Additional options:"

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: recursesubdirs ignoreversion

[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\{#AppExeName}"

[Registry]
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; \
  ValueName: "Fenstr"; ValueType: string; \
  ValueData: """{app}\{#AppExeName}"""; Flags: uninsdeletevalue; Tasks: autostart

[Run]
Filename: "{app}\{#AppExeName}"; Description: "Launch {#AppName}"; \
  Flags: shellexec nowait postinstall skipifsilent

[UninstallRun]
Filename: "taskkill.exe"; Parameters: "/IM {#AppExeName} /F"; \
  Flags: runhidden; RunOnceId: "KillFenstr"

[UninstallDelete]
Type: filesandordirs; Name: "{localappdata}\Fenstr"

[Code]
procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if CurUninstallStep = usUninstall then
  begin
    RegWriteStringValue(HKEY_CURRENT_USER, 'Control Panel\Desktop',
      'WindowArrangementActive', '1');
    RegWriteDWordValue(HKEY_CURRENT_USER,
      'Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced',
      'EnableSnapAssistFlyout', 1);
    RegWriteDWordValue(HKEY_CURRENT_USER,
      'Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced',
      'EnableSnapBar', 1);
    RegWriteDWordValue(HKEY_CURRENT_USER,
      'Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced',
      'SnapAssist', 1);
  end;
end;

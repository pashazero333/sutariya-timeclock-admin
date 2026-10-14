#define MyAppName "Sutariya TimeClock"
#define MyAppVersion "1.1.0"
#define MyAppPublisher "Sutariya Law"
#define MyAppExeName "SutariyaTimeClock.exe"

[Setup]
AppId={{8F83F4B4-12C7-4AF2-A38F-C44751607E92}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={autopf}\Sutariya TimeClock
DefaultGroupName=Sutariya TimeClock
DisableProgramGroupPage=yes
OutputDir=Output
OutputBaseFilename=SutariyaTimeClockSetup
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=admin
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
UninstallDisplayIcon={app}\{#MyAppExeName}
CloseApplications=yes
RestartApplications=no

[Files]
Source: "..\publish\{#MyAppExeName}"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{autodesktop}\Sutariya TimeClock"; Filename: "{app}\{#MyAppExeName}"; WorkingDir: "{app}"
Name: "{group}\Sutariya TimeClock"; Filename: "{app}\{#MyAppExeName}"; WorkingDir: "{app}"

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "Launch Sutariya TimeClock"; Flags: nowait postinstall skipifsilent

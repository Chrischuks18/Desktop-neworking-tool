#define MyAppName "Choice Flame Communications Network"
#define MyAppVersion "1.0.0"
#define MyAppPublisher "Choice Flame Communications"
#define MyAppExeName "OfficeNetwork.Client.exe"

[Setup]
AppId={{7D6B792D-9D4D-4E33-94B7-CC1A32C49C55}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={autopf}\Choice Flame Communications Network
DefaultGroupName={#MyAppName}
OutputDir=output
OutputBaseFilename=Choice-Flame-Communications-Network-Setup
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=admin
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
UninstallDisplayName={#MyAppName}

[Files]
Source: "..\artifacts\client\*"; DestDir: "{app}\Client"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "..\artifacts\server\*"; DestDir: "{app}\Server"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\{#MyAppName}"; Filename: "{app}\Client\{#MyAppExeName}"; IconFilename: "{app}\Client\Assets\ChoiceFlame.ico"; IconIndex: 0
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\Client\{#MyAppExeName}"; IconFilename: "{app}\Client\Assets\ChoiceFlame.ico"; IconIndex: 0; Tasks: desktopicon

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Shortcuts:"

[Registry]
Root: HKLM; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "ChoiceFlameNetworkServer"; ValueData: """{app}\Server\OfficeNetwork.Server.exe"""; Flags: uninsdeletevalue

[InstallDelete]
Type: files; Name: "{autodesktop}\{#MyAppName}.lnk"
Type: files; Name: "{autoprograms}\{#MyAppName}.lnk"

[Run]
Filename: "{app}\Server\OfficeNetwork.Server.exe"; Description: "Start Choice Flame network server"; Flags: nowait runhidden
Filename: "{app}\Client\{#MyAppExeName}"; Description: "Launch {#MyAppName}"; Flags: nowait postinstall skipifsilent

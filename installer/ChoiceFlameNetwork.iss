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
Source: "..\artifacts\server\*"; DestDir: "{app}\Server"; Flags: ignoreversion recursesubdirs createallsubdirs; Tasks: servermode

[Icons]
Name: "{autoprograms}\{#MyAppName}"; Filename: "{app}\Client\{#MyAppExeName}"; IconFilename: "{app}\Client\Assets\ChoiceFlame.ico"; IconIndex: 0
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\Client\{#MyAppExeName}"; IconFilename: "{app}\Client\Assets\ChoiceFlame.ico"; IconIndex: 0; Tasks: desktopicon

[Tasks]
Name: "servermode"; Description: "Office Server computer (hosts files, accounts and chat)"; GroupDescription: "Computer role:"; Flags: exclusive
Name: "clientmode"; Description: "Client computer (Director, Editor or News Sourcing)"; GroupDescription: "Computer role:"; Flags: exclusive unchecked
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Shortcuts:"

[Registry]
Root: HKLM; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "ChoiceFlameNetworkServer"; ValueData: """{app}\Server\OfficeNetwork.Server.exe"""; Flags: uninsdeletevalue; Tasks: servermode

[Run]
Filename: "{sys}\netsh.exe"; Parameters: "advfirewall firewall delete rule name=""Choice Flame Network Server"""; Flags: runhidden waituntilterminated; Tasks: servermode
Filename: "{sys}\netsh.exe"; Parameters: "advfirewall firewall add rule name=""Choice Flame Network Server"" dir=in action=allow program=""{app}\Server\OfficeNetwork.Server.exe"" protocol=TCP localport=5077 remoteip=LocalSubnet profile=private"; Flags: runhidden waituntilterminated; Tasks: servermode
Filename: "{app}\Server\OfficeNetwork.Server.exe"; Description: "Start Choice Flame network server"; Flags: nowait runhidden; Tasks: servermode
Filename: "{app}\Client\{#MyAppExeName}"; Description: "Launch {#MyAppName}"; Flags: nowait postinstall skipifsilent

[UninstallRun]
Filename: "{sys}\netsh.exe"; Parameters: "advfirewall firewall delete rule name=""Choice Flame Network Server"""; Flags: runhidden waituntilterminated; RunOnceId: "RemoveChoiceFlameFirewall"

[InstallDelete]
Type: files; Name: "{autodesktop}\{#MyAppName}.lnk"
Type: files; Name: "{autoprograms}\{#MyAppName}.lnk"


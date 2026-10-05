#define AppName "Panda Chatbox"
#define AppVersion "3.5.1"
#define AppPublisher "AstralShadows"
#define AppExeName "PandaChatbox.exe"

[Setup]
AppId={{A84C1BE6-808A-4E84-89EF-F0E6E7E2DA31}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher={#AppPublisher}
DefaultDirName={localappdata}\Programs\Panda Chatbox
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
OutputDir=installer-output
OutputBaseFilename=PandaChatbox-Setup
SetupIconFile=favicon.ico
UninstallDisplayIcon={app}\{#AppExeName}
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
VersionInfoVersion=3.5.1.0
VersionInfoProductVersion={#AppVersion}
VersionInfoProductName={#AppName}

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Additional shortcuts:"; Flags: unchecked

[Files]
Source: "publish\{#AppExeName}"; DestDir: "{app}"; Flags: ignoreversion
Source: "publish\PandaChatbox.Native.dll"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\{#AppName}"; Filename: "{app}\{#AppExeName}"; WorkingDir: "{app}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExeName}"; WorkingDir: "{app}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#AppExeName}"; Description: "Launch {#AppName}"; WorkingDir: "{app}"; Flags: postinstall nowait skipifsilent

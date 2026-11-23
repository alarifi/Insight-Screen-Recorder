#define AppName "Insight Screen Recorder"
#define AppVersion "1.6.4"
#define AppPublisher "INSIGHT Software"
#define AppExeName "InsightScreenRecorder.exe"

[Setup]
AppId={{D3482D53-37D8-4B78-ADCC-04B2EA51B63C}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher={#AppPublisher}
DefaultDirName={autopf}\Insight Tools
UsePreviousAppDir=no
DefaultGroupName={#AppName}
UninstallDisplayIcon={app}\{#AppExeName}
ArchitecturesAllowed=x64os
ArchitecturesInstallIn64BitMode=x64os
PrivilegesRequired=admin
OutputDir=output
OutputBaseFilename=InsightScreenRecorder_Setup_{#AppVersion}_x64
SetupIconFile=embedded\App.ico
WizardImageFile=embedded\WizardImage0.bmp
WizardSmallImageFile=embedded\WizardSmallImage0.bmp
LicenseFile=embedded\License.rtf
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
Uninstallable=yes

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Additional shortcuts:"; Flags: unchecked

[Files]
Source: "Release\*"; Excludes: "*.zip,*.pdb,SHA256SUMS.txt,FILES-SHA256.txt,THIRD_PARTY_NOTICES.md,TOOLCHAIN.json,*-LICENSE-INFO.txt,GPL-3.0.txt"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "THIRD_PARTY_NOTICES.md"; DestDir: "{app}"; Flags: ignoreversion
Source: "Build\tools\*-LICENSE-INFO.txt"; DestDir: "{app}\tools"; Flags: ignoreversion
Source: "Build\tools\TOOLCHAIN.json"; DestDir: "{app}\tools"; Flags: ignoreversion
Source: "Build\tools\GPL-3.0.txt"; DestDir: "{app}\tools"; Flags: ignoreversion
Source: "embedded\License.txt"; DestDir: "{app}\Documentation"; DestName: "INSIGHT-Screen-Recorder-EULA.txt"; Flags: ignoreversion
Source: "embedded\License.rtf"; DestDir: "{app}\Documentation"; DestName: "INSIGHT-Screen-Recorder-EULA.rtf"; Flags: ignoreversion

[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\{#AppExeName}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#AppExeName}"; Description: "Launch {#AppName}"; Flags: postinstall nowait skipifsilent

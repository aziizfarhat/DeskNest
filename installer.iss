#ifndef AppVersion
  #define AppVersion "0.7.0.2"
#endif

[Setup]
AppId={{A7C4E1F2-5B3D-4A8E-9C61-2D7F8B4A1E90}
AppName=DeskNest
AppVersion={#AppVersion}
AppPublisher=DeskNest
DefaultDirName={autopf}\DeskNest
DefaultGroupName=DeskNest
PrivilegesRequired=lowest
OutputDir=installer
OutputBaseFilename=DeskNest-Setup
SetupIconFile=app.ico
Compression=lzma2
SolidCompression=yes
ArchitecturesInstallIn64BitMode=x64compatible
UninstallDisplayName=DeskNest
UninstallDisplayIcon={app}\DeskNest.exe
AppMutex=NestsLite.SingleInstance,DeskNest.SingleInstance
CloseApplications=yes
RestartApplications=no

[Languages]
Name: "french"; MessagesFile: "compiler:Languages\French.isl"

[Tasks]
Name: "startup"; Description: "Lancer DeskNest au demarrage de Windows"; GroupDescription: "Options :"

[InstallDelete]
; Remove the old executable after the previous version has been closed.
Type: files; Name: "{app}\NestsLite.exe"

[Files]
Source: "publish\DeskNest.exe"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\DeskNest"; Filename: "{app}\DeskNest.exe"

[Registry]
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "DeskNest"; ValueData: """{app}\DeskNest.exe"""; Tasks: startup; Flags: uninsdeletevalue

[Run]
Filename: "{app}\DeskNest.exe"; Description: "Lancer DeskNest"; Flags: nowait postinstall skipifsilent

#ifndef MyAppVersion
  #define MyAppVersion "0.1.0"
#endif

[Setup]
AppId={{C5A2C2D9-5B71-4D2E-8E6F-6F9A7C6D1201}
AppName=SpeedPhotos
AppVersion={#MyAppVersion}
AppPublisher=SpeedPhotos
DefaultDirName={autopf}\SpeedPhotos
DefaultGroupName=SpeedPhotos
UninstallDisplayIcon={app}\SpeedPhotos.exe
OutputDir=Output
OutputBaseFilename=SpeedPhotos-Setup
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=admin
MinVersion=6.1sp1
CloseApplications=yes
RestartApplications=no
ArchitecturesInstallIn64BitMode=x64

[Languages]
Name: "spanish"; MessagesFile: "compiler:Languages\Spanish.isl"

[Tasks]
Name: "desktopicon"; Description: "Crear un acceso directo en el escritorio"; GroupDescription: "Accesos directos:"; Flags: unchecked

[Files]
Source: "publish\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\SpeedPhotos"; Filename: "{app}\SpeedPhotos.exe"; WorkingDir: "{app}"
Name: "{autodesktop}\SpeedPhotos"; Filename: "{app}\SpeedPhotos.exe"; WorkingDir: "{app}"; Tasks: desktopicon

[Registry]
Root: HKCU; Subkey: "Software\Classes\SystemFileAssociations\image\shell\OpenWithSpeedPhotos"; ValueType: string; ValueName: ""; ValueData: "Abrir con SpeedPhotos"; Flags: uninsdeletekey
Root: HKCU; Subkey: "Software\Classes\SystemFileAssociations\image\shell\OpenWithSpeedPhotos"; ValueType: string; ValueName: "Icon"; ValueData: "{app}\SpeedPhotos.exe"
Root: HKCU; Subkey: "Software\Classes\SystemFileAssociations\image\shell\OpenWithSpeedPhotos"; ValueType: string; ValueName: "MultiSelectModel"; ValueData: "Player"
Root: HKCU; Subkey: "Software\Classes\SystemFileAssociations\image\shell\OpenWithSpeedPhotos\command"; ValueType: string; ValueName: ""; ValueData: """{app}\SpeedPhotos.exe"" %*"; Flags: uninsdeletekey

[Run]
Filename: "{app}\SpeedPhotos.exe"; Description: "Iniciar SpeedPhotos"; Flags: postinstall nowait skipifsilent

; Inno Setup script — builds FLAFYO-GAME-TRANSLATOR-Setup.exe (Install / Uninstall; run it again over an install to repair)
[Setup]
AppName=FLAFYO GAME TRANSLATOR
AppVersion=0.3.1
AppPublisher=FLAFYO
DefaultDirName={autopf}\FLAFYO Game Translator
DefaultGroupName=FLAFYO
OutputDir=..\installer-out
OutputBaseFilename=FLAFYO-GAME-TRANSLATOR-Setup
CloseApplications=yes
RestartApplications=yes
Compression=lzma2
SolidCompression=yes
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
PrivilegesRequiredOverridesAllowed=dialog
UninstallDisplayIcon={app}\FLAFYO.GameTranslator.exe
LicenseFile=..\THIRD_PARTY_LICENSES.txt
SetupIconFile=..\src\Flafyo.App\Assets\flafyo.ico

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"

[Files]
Source: "..\publish\FLAFYO.GameTranslator.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\THIRD_PARTY_LICENSES.txt"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\FLAFYO GAME TRANSLATOR"; Filename: "{app}\FLAFYO.GameTranslator.exe"
Name: "{autodesktop}\FLAFYO GAME TRANSLATOR"; Filename: "{app}\FLAFYO.GameTranslator.exe"; Tasks: desktopicon

[Run]
Filename: "{app}\FLAFYO.GameTranslator.exe"; Description: "Launch FLAFYO GAME TRANSLATOR"; Flags: nowait postinstall skipifsilent

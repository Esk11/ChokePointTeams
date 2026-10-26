; Inno Setup script for ChokePoint Teams (free tool: jrsoftware.org/isinfo.php)
; Keep this file next to build.bat, in:  D:\Apps\ChokePoint for Teams\
; Create the installer by running:  build.bat installer

#ifndef BuildDir
  #error This script needs a build folder. Run "build.bat installer" instead of compiling it directly.
#endif
#ifndef Stamp
  #define Stamp "dev"
#endif

[Setup]
AppName=ChokePoint for Teams
AppVersion=1.0.0
AppPublisher=ChokePoint
DefaultDirName={autopf}\ChokePoint for Teams
DefaultGroupName=ChokePoint for Teams
OutputDir=Builds
OutputBaseFilename=ChokePointforTeams-Installer-{#Stamp}
SetupIconFile=ChokePointTeams\choke-point-glyph.ico
UninstallDisplayIcon={app}\ChokePointTeams.exe
Compression=lzma2
SolidCompression=yes
PrivilegesRequired=admin
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
WizardImageFile=ChokePointTeams\installer\wizard-large.bmp,ChokePointTeams\installer\wizard-large-150.bmp,ChokePointTeams\installer\wizard-large-200.bmp
WizardSmallImageFile=ChokePointTeams\installer\wizard-small.bmp,ChokePointTeams\installer\wizard-small-200.bmp

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; Flags: unchecked

[Files]
Source: "{#BuildDir}\*"; DestDir: "{app}"; Flags: recursesubdirs ignoreversion

[Icons]
Name: "{group}\ChokePoint for Teams"; Filename: "{app}\ChokePointTeams.exe"
Name: "{autodesktop}\ChokePoint for Teams"; Filename: "{app}\ChokePointTeams.exe"; Tasks: desktopicon

[UninstallDelete]
; Remove the WebView2 profile directory left by older versions using the default location.
Type: filesandordirs; Name: "{app}\ChokePointTeams.exe.WebView2"


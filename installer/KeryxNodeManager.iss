; Inno Setup script for Keryx Node Manager.
; Expects `dotnet publish ... -o artifacts\publish\win-x64` to have already produced the
; self-contained win-x64 build (see docs/BUILD.md). Run with:
;   "C:\Program Files (x86)\Inno Setup 6\ISCC.exe" installer\KeryxNodeManager.iss
;
; Not yet run in this session (no Windows/Inno Setup available in the sandbox this project was
; built in) - see PROJECT_STATUS.md. Written against real Inno Setup 6 syntax and reviewed
; carefully, but treat the first real run as the actual verification.

#define MyAppName "Keryx Node Manager"
#define MyAppVersion "0.2.7"
#define MyAppPublisher "Keryx Node Manager (community project, not an official Keryx Labs product)"
#define MyAppExeName "KeryxNodeManager.exe"
#define MyPublishDir "..\artifacts\publish\win-x64"

[Setup]
AppId={{6C1B1E6E-6C3B-4B7E-9C1A-2F1B7B6E9A11}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={autopf}\KeryxNodeManager
DefaultGroupName=Keryx Node Manager
DisableProgramGroupPage=yes
; asInvoker: install to a user-writable location by default (Program Files still needs admin for
; the initial install itself, which Windows will prompt for once - see docs/SECURITY.md for why
; the *app* itself never demands elevation at runtime).
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=commandline dialog
OutputDir=..\artifacts
OutputBaseFilename=KeryxNodeManager-Setup-{#MyAppVersion}
SetupIconFile=app.ico
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
UninstallDisplayIcon={app}\{#MyAppExeName}
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible

; 0.2.6 brief §3: installer must offer all seven app languages, not just ru/en. All six
; non-English .isl files below ship with a stock Inno Setup 6 install (compiler:Languages\*.isl) -
; no custom translation files needed for the built-in wizard chrome (welcome/license/directory/
; tasks/ready/progress/finish/error screens all come translated from Inno Setup itself). Only the
; one custom Task description below needs its own per-language text, via [CustomMessages].
[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"
Name: "russian"; MessagesFile: "compiler:Languages\Russian.isl"
Name: "german"; MessagesFile: "compiler:Languages\German.isl"
Name: "spanish"; MessagesFile: "compiler:Languages\Spanish.isl"
Name: "french"; MessagesFile: "compiler:Languages\French.isl"
Name: "italian"; MessagesFile: "compiler:Languages\Italian.isl"
Name: "ukrainian"; MessagesFile: "compiler:Languages\Ukrainian.isl"

[CustomMessages]
english.StartupIconDesc=Start Keryx Node Manager when Windows starts
russian.StartupIconDesc=Запускать Keryx Node Manager при входе в Windows
german.StartupIconDesc=Keryx Node Manager beim Windows-Start starten
spanish.StartupIconDesc=Iniciar Keryx Node Manager al arrancar Windows
french.StartupIconDesc=Démarrer Keryx Node Manager au démarrage de Windows
italian.StartupIconDesc=Avvia Keryx Node Manager all'avvio di Windows
ukrainian.StartupIconDesc=Запускати Keryx Node Manager під час входу в Windows

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked
Name: "startupicon"; Description: "{cm:StartupIconDesc}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
; Everything from the self-contained publish output.
Source: "{#MyPublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "..\README.md"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{group}\{cm:UninstallProgram,{#MyAppName}}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon
; NOTE: --minimized is not yet a recognized launch argument (App.xaml.cs only checks for --mock
; today - see PROJECT_STATUS.md). This shortcut currently launches the app normally, not
; minimized to tray, until that flag is implemented.
Name: "{userstartup}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: startupicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#MyAppName}}"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
; Deliberately does NOT delete %LocalAppData%\KeryxNodeManager (settings/profiles/logs/models
; metadata) on a normal uninstall - brief §24 "не удалять models/blockchain data при обычном
; обновлении". A future [Code] section can prompt "also delete user data?" per brief §24; not
; implemented in this pass, so a normal uninstall today simply leaves that folder behind.

[Code]
function InitializeSetup(): Boolean;
begin
  Result := True;
end;

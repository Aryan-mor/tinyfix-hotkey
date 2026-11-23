; Per-user TinyFix setup. No administrator (PrivilegesRequired=lowest).
; AppId is new for this C# app: do not reuse the old Python app's id or its "TinyFix Hotkey" folder.
; The app itself writes HKCU\...\Run\TinyFix on first launch; uninstall deletes that value.

#ifndef AppVersion
  #error Pass /DAppVersion=...
#endif
#ifndef SourceDir
  #error Pass /DSourceDir=... (the published TinyFix.exe)
#endif
#ifndef OutputDir
  #error Pass /DOutputDir=...
#endif
#ifndef RepoRoot
  #error Pass /DRepoRoot=...
#endif

[Setup]
AppId={{48DD80A9-44A4-429F-972E-ACD32EC4DF11}
AppName=TinyFix
AppVersion={#AppVersion}
AppPublisher=TinyFix contributors
DefaultDirName={localappdata}\Programs\TinyFix
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64
ArchitecturesInstallIn64BitMode=x64
OutputDir={#OutputDir}
OutputBaseFilename=TinyFix-{#AppVersion}-win-x64-Setup
SetupIconFile={#RepoRoot}\assets\tinyfix.ico
UninstallDisplayIcon={app}\TinyFix.exe
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
CloseApplications=yes
RestartApplications=no

[Tasks]
Name: desktopicon; Description: "Create a desktop shortcut"; GroupDescription: "Additional shortcuts:"; Flags: unchecked

[Files]
Source: "{#SourceDir}\TinyFix.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#RepoRoot}\LICENSE"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#RepoRoot}\THIRD-PARTY-NOTICES.md"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{userprograms}\TinyFix"; Filename: "{app}\TinyFix.exe"
Name: "{userdesktop}\TinyFix"; Filename: "{app}\TinyFix.exe"; Tasks: desktopicon

[Registry]
; ValueType none: the installer does not create the value. uninsdeletevalue removes it on uninstall.
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: none; ValueName: "TinyFix"; Flags: uninsdeletevalue

[Run]
Filename: "{app}\TinyFix.exe"; Description: "Launch TinyFix"; Flags: nowait postinstall skipifsilent

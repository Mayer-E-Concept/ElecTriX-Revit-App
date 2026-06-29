; setup.iss -- ME-Tools installer (Revit 2025 / net8.0-windows)
; Mayer E-Concept SRL
; Build the installer:  open in Inno Setup 6 -> Compile   (or run ISCC.exe setup.iss)
;
; Installs METools.dll + METools.addin into the all-users Revit 2025 Addins folder:
;   C:\ProgramData\Autodesk\Revit\Addins\2025
;
; NOTE: every Source/DestDir entry is a SINGLE line (Inno requirement).

#define AppName     "ME-Tools"
#define AppVersion  "1.1.0"
#define Publisher   "Mayer E-Concept SRL"

; --- adjust these absolute paths to your machine if they differ ---------------
#define ProjectDir "X:\02_sabloane\01_Revit\ElecTriX-Revit-App"
#define DllPath     ProjectDir + "\bin\Release\net8.0-windows\METools.dll"
#define AddinPath   ProjectDir + "\METools_2025.addin"
#define OutDir      ProjectDir + "\installer_output"
; ------------------------------------------------------------------------------

[Setup]
; Keep this AppId STABLE across versions so upgrades replace cleanly. Do not change it.
AppId={{B3F2C9A4-7E61-4D8B-9C0A-2F5E1A6D4B77}}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher={#Publisher}
AppPublisherURL=https://mayer-econcept.ro
DefaultDirName={commonappdata}\Autodesk\Revit\Addins\2025
DisableDirPage=yes
DisableProgramGroupPage=yes
PrivilegesRequired=admin
ArchitecturesInstallIn64BitMode=x64
OutputDir={#OutDir}
OutputBaseFilename=setup_metools_v{#AppVersion}
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
; Prompt to close Revit if it is holding the DLL open.
CloseApplications=yes
RestartApplications=no
UninstallDisplayName={#AppName} {#AppVersion}

[Files]
Source: "{#DllPath}"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#AddinPath}"; DestDir: "{app}"; DestName: "METools.addin"; Flags: ignoreversion
; Optional: only if your code reads this from the install folder (usually it does NOT - it lives in %APPDATA%).
; Source: "{#ProjectDir}\standard_worksets.json"; DestDir: "{app}"; Flags: ignoreversion onlyifdoesntexist

[UninstallDelete]
Type: files; Name: "{app}\METools.dll"
Type: files; Name: "{app}\METools.addin"

[Messages]
WelcomeLabel2=This will install [name/ver] for Autodesk Revit 2025.%n%nPlease close Revit before continuing.

; -----------------------------------------------------------------------------
; To also support Revit 2024 / 2026 later: duplicate the two [Files] lines with
; DestDir pointing at {commonappdata}\Autodesk\Revit\Addins\2024 (or 2026) and
; the matching METools_2024.addin / METools_2026.addin, ideally behind [Tasks]
; checkboxes so the user picks which Revit versions to target.
; -----------------------------------------------------------------------------

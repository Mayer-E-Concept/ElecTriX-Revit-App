; setup.iss -- ME-Tools installer (Revit 2024 / 2025 / 2026)
; Mayer E-Concept SRL
; Build the installer:  open in Inno Setup 6 -> Compile   (or run ISCC.exe setup.iss)
;
; Revit 2024 runs on .NET Framework 4.8; Revit 2025 and 2026 both run on .NET 8.
; These are genuinely different compiled binaries (see METools.csproj's
; <TargetFrameworks>net48;net8.0-windows</TargetFrameworks>), so this script
; installs whichever one(s) match the Revit version(s) the user selects below.
; Build BOTH configurations in Release before compiling this (or just the ones
; you're about to ship) -- the net8.0-windows one covers 2025 and 2026 both.
;
; NOTE: every Source/DestDir entry is a SINGLE line (Inno requirement).

#define AppName     "ME-Tools"
#define AppVersion  "1.6.0"
#define Publisher   "Mayer E-Concept SRL"

; --- adjust this absolute path to your machine if it differs ------------------
#define ProjectDir "X:\02_sabloane\01_Revit\ElecTriX-Revit-App"
#define OutDir      ProjectDir + "\installer_output"
; net48 = Revit 2024; net8.0-windows = Revit 2025 AND 2026 (same binary, used twice)
#define Dll2024Path ProjectDir + "\bin\Release\net48\METools.dll"
#define Dll2025Path ProjectDir + "\bin\Release\net8.0-windows\METools.dll"
; --------------------------------------------------------------------------------

[Setup]
; Keep this AppId STABLE across versions so upgrades replace cleanly. Do not change it.
AppId={{B3F2C9A4-7E61-4D8B-9C0A-2F5E1A6D4B77}}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher={#Publisher}
AppPublisherURL=https://mayer-econcept.ro
; No single {app} folder any more -- each selected Revit version gets its own
; Addins\20XX destination below, so the wizard's directory page is irrelevant.
DefaultDirName={commonappdata}\Autodesk\Revit\Addins
DisableDirPage=yes
DisableProgramGroupPage=yes
PrivilegesRequired=admin
ArchitecturesInstallIn64BitMode=x64
OutputDir={#OutDir}
OutputBaseFilename=setup_metools_v{#AppVersion}
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
; Dark-teal branded banners matching the app's own theme (see [Code] below for
; the wizard page recoloring, and installer_assets/ for how these were made).
WizardImageFile={#ProjectDir}\installer_assets\wizard_image.bmp
WizardSmallImageFile={#ProjectDir}\installer_assets\wizard_small_image.bmp
; Prompt to close Revit if it is holding a DLL open.
CloseApplications=yes
RestartApplications=no
UninstallDisplayName={#AppName} {#AppVersion}

[Tasks]
Name: "rvt2024"; Description: "Autodesk Revit 2024"; GroupDescription: "Install ME-Tools for:"
Name: "rvt2025"; Description: "Autodesk Revit 2025"; GroupDescription: "Install ME-Tools for:"
Name: "rvt2026"; Description: "Autodesk Revit 2026"; GroupDescription: "Install ME-Tools for:"

[Files]
; -- Revit 2024 (.NET Framework build) --------------------------------------
Source: "{#Dll2024Path}"; DestDir: "{commonappdata}\Autodesk\Revit\Addins\2024"; Flags: ignoreversion; Tasks: rvt2024
Source: "{#ProjectDir}\METools_2024.addin"; DestDir: "{commonappdata}\Autodesk\Revit\Addins\2024"; DestName: "METools.addin"; Flags: ignoreversion; Tasks: rvt2024
; Seeds the Settings > Worksets "standard list" on first install (the code reads
; this from [install folder]\config\standard_worksets.json, NOT %APPDATA%).
; onlyifdoesntexist so upgrading never overwrites a customer's own edited list.
Source: "{#ProjectDir}\standard_worksets.json"; DestDir: "{commonappdata}\Autodesk\Revit\Addins\2024\config"; Flags: ignoreversion onlyifdoesntexist; Tasks: rvt2024

; -- Revit 2025 (.NET 8 build) -----------------------------------------------
Source: "{#Dll2025Path}"; DestDir: "{commonappdata}\Autodesk\Revit\Addins\2025"; Flags: ignoreversion; Tasks: rvt2025
Source: "{#ProjectDir}\METools_2025.addin"; DestDir: "{commonappdata}\Autodesk\Revit\Addins\2025"; DestName: "METools.addin"; Flags: ignoreversion; Tasks: rvt2025
Source: "{#ProjectDir}\standard_worksets.json"; DestDir: "{commonappdata}\Autodesk\Revit\Addins\2025\config"; Flags: ignoreversion onlyifdoesntexist; Tasks: rvt2025

; -- Revit 2026 (same .NET 8 build as 2025, installed a second time) --------
Source: "{#Dll2025Path}"; DestDir: "{commonappdata}\Autodesk\Revit\Addins\2026"; Flags: ignoreversion; Tasks: rvt2026
Source: "{#ProjectDir}\METools_2026.addin"; DestDir: "{commonappdata}\Autodesk\Revit\Addins\2026"; DestName: "METools.addin"; Flags: ignoreversion; Tasks: rvt2026
Source: "{#ProjectDir}\standard_worksets.json"; DestDir: "{commonappdata}\Autodesk\Revit\Addins\2026\config"; Flags: ignoreversion onlyifdoesntexist; Tasks: rvt2026

; -- Project Comments: pre-fills the shared network folder so every teammate
; gets it working out of the box instead of typing the UNC path in by hand.
; onlyifdoesntexist so re-installing/upgrading never overwrites someone's own
; customized path (e.g. if a specific person needs a different folder).
Source: "{#ProjectDir}\comments-settings-default.json"; DestDir: "{userappdata}\METools"; DestName: "comments-settings.json"; Flags: ignoreversion onlyifdoesntexist

; -- Project Health Check: bundled tag family + shared-parameter definitions,
; so "Fix All" can load the ME-Tools_CircuitTag family and bind the 6 Circuit
; Tagger parameters on a project that never had them, with one click, instead
; of a manual per-project setup. One shared copy for all Revit versions
; (installed regardless of which rvtXXXX tasks are selected). These ARE
; overwritten on every install/update (no onlyifdoesntexist) since they're
; app-owned assets, not user data -- if the family or parameter file is ever
; updated, everyone should get the new copy.
Source: "{#ProjectDir}\Resources\ME-Tools_CircuitTag.rfa"; DestDir: "{commonappdata}\METools\Resources"; Flags: ignoreversion
Source: "{#ProjectDir}\Resources\METools_SharedParameters.txt"; DestDir: "{commonappdata}\METools\Resources"; Flags: ignoreversion

[UninstallDelete]
Type: files; Name: "{commonappdata}\Autodesk\Revit\Addins\2024\METools.dll"
Type: files; Name: "{commonappdata}\Autodesk\Revit\Addins\2024\METools.addin"
Type: files; Name: "{commonappdata}\Autodesk\Revit\Addins\2024\config\standard_worksets.json"
Type: dirifempty; Name: "{commonappdata}\Autodesk\Revit\Addins\2024\config"
Type: files; Name: "{commonappdata}\Autodesk\Revit\Addins\2025\METools.dll"
Type: files; Name: "{commonappdata}\Autodesk\Revit\Addins\2025\METools.addin"
Type: files; Name: "{commonappdata}\Autodesk\Revit\Addins\2025\config\standard_worksets.json"
Type: dirifempty; Name: "{commonappdata}\Autodesk\Revit\Addins\2025\config"
Type: files; Name: "{commonappdata}\Autodesk\Revit\Addins\2026\METools.dll"
Type: files; Name: "{commonappdata}\Autodesk\Revit\Addins\2026\METools.addin"
Type: files; Name: "{commonappdata}\Autodesk\Revit\Addins\2026\config\standard_worksets.json"
Type: dirifempty; Name: "{commonappdata}\Autodesk\Revit\Addins\2026\config"
Type: files; Name: "{userappdata}\METools\comments-settings.json"
Type: files; Name: "{commonappdata}\METools\Resources\ME-Tools_CircuitTag.rfa"
Type: files; Name: "{commonappdata}\METools\Resources\METools_SharedParameters.txt"
Type: dirifempty; Name: "{commonappdata}\METools\Resources"
Type: dirifempty; Name: "{commonappdata}\METools"

[Messages]
WelcomeLabel2=This will install [name/ver] for Autodesk Revit.%n%nPlease close Revit before continuing.

[Code]
{ ────────────────────────────────────────────────────────────────────────────
  Themes the setup wizard's OUTER background (the title strip at the top and
  the button bar at the bottom -- WizardForm's own background) to match the
  app's dark teal / cyan-accent theme, and leaves the middle content area
  (MainPanel: the Tasks checklist, instructions, license text, etc.) at Inno's
  normal white-with-black-text default. Earlier attempts also forced that
  middle area dark, but some of its controls kept reverting to white
  regardless, so this settles on the combination that's actually reliable:
  dark top/bottom, plain white middle -- rather than an inconsistent mix.
  ──────────────────────────────────────────────────────────────────────────── }
var
  ClrBg, ClrAccent, ClrMuted: TColor;

procedure ApplyTitleColors;
begin
  try WizardForm.PageNameLabel.Font.Color := ClrAccent; except end;
  try WizardForm.PageDescriptionLabel.Font.Color := ClrMuted; except end;
end;

procedure InitializeWizard;
begin
  { Same hex values as MeToolsTheme.cs's dark theme, converted by hand to the
    BGR-ordered TColor integer Pascal/Delphi uses internally (Inno's Pascal
    Script has no built-in RGB() function). }
  ClrBg     := $1E1E0A; { CBg     = RGB(0x0A,0x1E,0x1E) }
  ClrAccent := $D3DB54; { CAccent = RGB(0x54,0xDB,0xD3) }
  ClrMuted  := $A6A886; { CMuted  = RGB(0x86,0xA8,0xA6) }

  WizardForm.Color := ClrBg;
  ApplyTitleColors;
end;

{ PageNameLabel/PageDescriptionLabel get re-styled by Inno whenever the page
  actually changes, after InitializeWizard's one-time pass -- re-applying the
  same two colors here keeps the title area consistent on every page. }
procedure CurPageChanged(CurPageID: Integer);
begin
  ApplyTitleColors;
end;

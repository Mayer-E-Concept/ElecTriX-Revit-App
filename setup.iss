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
#define AppVersion  "1.3.0"
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

[Messages]
WelcomeLabel2=This will install [name/ver] for Autodesk Revit.%n%nPlease close Revit before continuing.

[Code]
{ ────────────────────────────────────────────────────────────────────────────
  Recolors the setup wizard to match the app's own dark teal / cyan-accent
  theme (see MeToolsTheme.cs's dark palette -- these are the same hex values).

  Being upfront about what this can and can't do: Inno Setup's wizard is a
  native Win32-style UI, not WPF, so this can't be pixel-identical to the app
  -- there's no custom title bar, no rounded buttons, no hover animations here.
  What DOES translate well: page backgrounds, text colors, the checklist/edit/
  memo boxes, and the page title in the accent cyan. The one area Inno itself
  has the least control over is button chrome -- Next/Back/Cancel often defer
  to Windows' own theme rendering for authenticity, so the button FACE may stay
  the OS default even though its text color changes. If anything still looks
  off after compiling, a screenshot is the fastest way to pin down exactly
  which control needs a follow-up fix.
  ──────────────────────────────────────────────────────────────────────────── }
var
  ClrBg, ClrSurface, ClrText, ClrMuted, ClrAccent, ClrInputBg: TColor;

procedure ApplyDarkColors(Ctrl: TWinControl); forward;

procedure InitializeWizard;
begin
  { Same hex values as MeToolsTheme.cs's dark theme -- RGB() converts to the
    BGR-ordered TColor Pascal/Delphi actually uses internally, so these numbers
    match the app's palette even though the raw integer would look different. }
  ClrBg      := RGB($0A, $1E, $1E); { CBg }
  ClrSurface := RGB($10, $2B, $2B); { CSurface }
  ClrText    := RGB($E9, $F4, $F3); { CText }
  ClrMuted   := RGB($86, $A8, $A6); { CMuted }
  ClrAccent  := RGB($54, $DB, $D3); { CAccent }
  ClrInputBg := RGB($0D, $26, $26); { CInput }

  WizardForm.Color := ClrBg;
  try WizardForm.MainPanel.Color := ClrBg; except end;

  ApplyDarkColors(WizardForm);

  { Emphasis pass: page title in accent cyan, like the app uses accent color
    for emphasis throughout, after the generic recolor above already ran. }
  try WizardForm.PageNameLabel.Font.Color := ClrAccent; except end;
  try WizardForm.PageDescriptionLabel.Font.Color := ClrMuted; except end;
end;

{ Recursively recolors every control on every wizard page. Wrapped defensively
  since not every Inno version exposes every property identically -- a failed
  cast or missing property here just skips that one control instead of
  crashing the installer. }
procedure ApplyDarkColors(Ctrl: TWinControl);
var
  I: Integer;
  C: TControl;
begin
  for I := 0 to Ctrl.ControlCount - 1 do
  begin
    C := Ctrl.Controls[I];

    try
      if C is TNewStaticText then
        TNewStaticText(C).Font.Color := ClrText
      else if C is TNewCheckListBox then
      begin
        TNewCheckListBox(C).Color := ClrSurface;
        TNewCheckListBox(C).Font.Color := ClrText;
      end
      else if C is TNewEdit then
      begin
        TNewEdit(C).Color := ClrInputBg;
        TNewEdit(C).Font.Color := ClrText;
      end
      else if C is TNewMemo then
      begin
        TNewMemo(C).Color := ClrInputBg;
        TNewMemo(C).Font.Color := ClrText;
      end
      else if C is TRichEditViewer then
      begin
        TRichEditViewer(C).Color := ClrInputBg;
        TRichEditViewer(C).Font.Color := ClrText;
      end
      else if C is TNewButton then
        TNewButton(C).Font.Color := ClrText
      else if C is TPanel then
        TPanel(C).Color := ClrBg;
    except
    end;

    if C is TWinControl then
      ApplyDarkColors(TWinControl(C));
  end;
end;

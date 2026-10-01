; setup.iss -- ME-Tools installer (Revit 2025 and/or 2027, Nexus optional)
; Mayer E-Concept SRL
; Build the installer:  open in Inno Setup 6 -> Compile   (or run ISCC.exe setup.iss)
;
; Two builds of the same add-in (see METools.csproj):
;   Revit 2025 -> bin\Release\net8.0-windows\METools.dll   -> ProgramData\Autodesk\Revit\Addins\2025
;   Revit 2027 -> bin\Release\net10.0-windows\METools.dll  -> Program Files\Autodesk\Revit\Addins\2027
;                 (Revit 2027 moved the all-users Addins folder to Program Files)
; Each version, and the Nexus assistant, is its own checkbox. On a first install
; (or an update from an installer without these choices) the versions whose
; Revit is installed on this PC are preselected; after that the last choice is
; kept, also for the silent auto-update. Unticking something removes it.
; Revit 2026 is no longer offered; an existing 2026 copy is removed.
; Build Release before compiling this.
;
; NOTE: every Source/DestDir entry is a SINGLE line (Inno requirement).

#define AppName     "ME-Tools"
#define AppVersion  "nxs_2.4.2"
#define Publisher   "Mayer E-Concept SRL"

; --- adjust this absolute path to your machine if it differs ------------------
#define ProjectDir "X:\08_Aplicatii\ElecTriX-Revit-App"
#define OutDir      ProjectDir + "\installer_output"
; net8.0-windows -> Revit 2025, net10.0-windows -> Revit 2027
#define Dll2025Path ProjectDir + "\bin\Release\net8.0-windows\METools.dll"
#define Dll2027Path ProjectDir + "\bin\Release\net10.0-windows\METools.dll"
; --------------------------------------------------------------------------------

; --- Nexus (the standalone assistant) -- a SEPARATE project, its own .exe,
; not something built as part of METools.csproj. Assumes it's been published
; self-contained first (so an end-user's machine needs nothing pre-installed):
;     cd <NexusProjectDir> && dotnet publish -c Release -r win-x64 --self-contained -o installer_output\nexus_publish
; If that publish command or output folder name ever changes, update
; NexusPublishDir below to match -- Inno just copies whatever's actually
; sitting there. ---------------------------------------------------------------
#define NexusProjectDir "X:\08_Aplicatii\Nexus"
#define NexusPublishDir NexusProjectDir + "\installer_output\nexus_publish"

[Setup]
; Keep this AppId STABLE across versions so upgrades replace cleanly. Do not change it.
AppId={{B3F2C9A4-7E61-4D8B-9C0A-2F5E1A6D4B77}}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher={#Publisher}
AppPublisherURL=https://mayer-econcept.ro
; No single {app} folder any more -- both Revit versions' Addins\20XX
; destinations below are fixed, so the wizard's directory page is irrelevant.
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

; The "choose what to install" screen: which Revit version(s), and whether
; the Nexus assistant comes along. No [Types] presets -- the selection is
; made in [Code] (installed Revit versions on a first install, the last
; choice afterwards). A silent install can also pass /COMPONENTS=...
[Components]
Name: "revit2025"; Description: "ElecTriX for Revit 2025"
Name: "revit2027"; Description: "ElecTriX for Revit 2027"
Name: "assistant"; Description: "Nexus -- AI assistant (Requests, Tasks, shared project chats, voice, database access, and live Revit access when the AI Connector is running)"

[Types]
Name: "custom"; Description: "Custom installation"; Flags: iscustom

[Files]
; -- Revit 2025 (.NET 8 build) ----------------------------------------------
Source: "{#Dll2025Path}"; DestDir: "{commonappdata}\Autodesk\Revit\Addins\2025"; Components: revit2025; Flags: ignoreversion
Source: "{#ProjectDir}\METools_2025.addin"; DestDir: "{commonappdata}\Autodesk\Revit\Addins\2025"; DestName: "METools.addin"; Components: revit2025; Flags: ignoreversion
; Seeds the Settings > Worksets "standard list" on first install (the code reads
; this from [install folder]\config\standard_worksets.json, NOT %APPDATA%).
; onlyifdoesntexist so upgrading never overwrites a customer's own edited list.
Source: "{#ProjectDir}\standard_worksets.json"; DestDir: "{commonappdata}\Autodesk\Revit\Addins\2025\config"; Components: revit2025; Flags: ignoreversion onlyifdoesntexist

; -- Revit 2027 (.NET 10 build) -- the all-users Addins folder is in Program Files from 2027 on
Source: "{#Dll2027Path}"; DestDir: "{commonpf}\Autodesk\Revit\Addins\2027"; Components: revit2027; Flags: ignoreversion
Source: "{#ProjectDir}\METools_2027.addin"; DestDir: "{commonpf}\Autodesk\Revit\Addins\2027"; DestName: "METools.addin"; Components: revit2027; Flags: ignoreversion
Source: "{#ProjectDir}\standard_worksets.json"; DestDir: "{commonpf}\Autodesk\Revit\Addins\2027\config"; Components: revit2027; Flags: ignoreversion onlyifdoesntexist

; -- Project Comments: pre-fills the shared network folder so every teammate
; gets it working out of the box instead of typing the UNC path in by hand.
; onlyifdoesntexist so re-installing/upgrading never overwrites someone's own
; customized path (e.g. if a specific person needs a different folder).
Source: "{#ProjectDir}\comments-settings-default.json"; DestDir: "{userappdata}\METools"; DestName: "comments-settings.json"; Components: revit2025 revit2027; Flags: ignoreversion onlyifdoesntexist

; -- Project Health Check: bundled tag family + shared-parameter definitions,
; so "Fix All" can load the ME-Tools_CircuitTag family and bind the 6 Circuit
; Tagger parameters on a project that never had them, with one click, instead
; of a manual per-project setup. One shared copy for both Revit versions.
; These ARE overwritten on every install/update (no onlyifdoesntexist) since
; they're app-owned assets, not user data -- if the family or parameter file
; is ever updated, everyone should get the new copy.
Source: "{#ProjectDir}\Resources\ME-Tools_CircuitTag.rfa"; DestDir: "{commonappdata}\METools\Resources"; Components: revit2025 revit2027; Flags: ignoreversion
Source: "{#ProjectDir}\Resources\METools_SharedParameters.txt"; DestDir: "{commonappdata}\METools\Resources"; Components: revit2025 revit2027; Flags: ignoreversion

; -- Nexus (the assistant), only copied when that Component is selected --
; installed as its own standalone app, entirely separate from ElecTriX's
; Revit-Addins destinations above, since it's meant to run on its own too
; (per its own future roadmap, not just as a Revit companion). The whole
; published, self-contained folder is copied recursively (Nexus.exe plus
; its bundled runtime and every dependency), EXCLUDING appsettings.json --
; that one real config file (API key, shared folder path, current user)
; gets its own entry below with onlyifdoesntexist, so re-running this
; installer to upgrade Nexus never wipes out someone's already-configured
; settings the way a blind wildcard copy would.
Source: "{#NexusPublishDir}\*"; DestDir: "{commonpf}\Mayer E-Concept\Nexus"; Components: assistant; Flags: ignoreversion recursesubdirs createallsubdirs; Excludes: "appsettings.json"
Source: "{#NexusPublishDir}\appsettings.json"; DestDir: "{commonpf}\Mayer E-Concept\Nexus"; Components: assistant; Flags: ignoreversion onlyifdoesntexist

; Re-running setup with something unticked removes it (Inno doesn't do that
; on its own), and the Revit 2026 copy older versions installed is removed.
[InstallDelete]
Type: files; Name: "{commonappdata}\Autodesk\Revit\Addins\2025\METools.dll"; Components: not revit2025
Type: files; Name: "{commonappdata}\Autodesk\Revit\Addins\2025\METools.addin"; Components: not revit2025
Type: files; Name: "{commonpf}\Autodesk\Revit\Addins\2027\METools.dll"; Components: not revit2027
Type: files; Name: "{commonpf}\Autodesk\Revit\Addins\2027\METools.addin"; Components: not revit2027
Type: files; Name: "{commonappdata}\Autodesk\Revit\Addins\2026\METools.dll"
Type: files; Name: "{commonappdata}\Autodesk\Revit\Addins\2026\METools.addin"
Type: files; Name: "{commonappdata}\Autodesk\Revit\Addins\2026\config\standard_worksets.json"
Type: dirifempty; Name: "{commonappdata}\Autodesk\Revit\Addins\2026\config"
Type: filesandordirs; Name: "{commonpf}\Mayer E-Concept\Nexus"; Components: not assistant

[UninstallDelete]
Type: files; Name: "{commonappdata}\Autodesk\Revit\Addins\2025\METools.dll"
Type: files; Name: "{commonappdata}\Autodesk\Revit\Addins\2025\METools.addin"
Type: files; Name: "{commonappdata}\Autodesk\Revit\Addins\2025\config\standard_worksets.json"
Type: dirifempty; Name: "{commonappdata}\Autodesk\Revit\Addins\2025\config"
Type: files; Name: "{commonpf}\Autodesk\Revit\Addins\2027\METools.dll"
Type: files; Name: "{commonpf}\Autodesk\Revit\Addins\2027\METools.addin"
Type: files; Name: "{commonpf}\Autodesk\Revit\Addins\2027\config\standard_worksets.json"
Type: dirifempty; Name: "{commonpf}\Autodesk\Revit\Addins\2027\config"
Type: files; Name: "{commonappdata}\Autodesk\Revit\Addins\2026\METools.dll"
Type: files; Name: "{commonappdata}\Autodesk\Revit\Addins\2026\METools.addin"
Type: files; Name: "{commonappdata}\Autodesk\Revit\Addins\2026\config\standard_worksets.json"
Type: dirifempty; Name: "{commonappdata}\Autodesk\Revit\Addins\2026\config"
Type: files; Name: "{userappdata}\METools\comments-settings.json"
Type: files; Name: "{commonappdata}\METools\Resources\ME-Tools_CircuitTag.rfa"
Type: files; Name: "{commonappdata}\METools\Resources\METools_SharedParameters.txt"
Type: dirifempty; Name: "{commonappdata}\METools\Resources"
Type: dirifempty; Name: "{commonappdata}\METools"
; Nexus's whole install folder -- filesandordirs rather than listing each
; file individually, since a self-contained publish is dozens of files
; (the bundled runtime plus every dependency), not just Nexus.exe itself.
; A full uninstall removing the assistant's own settings (appsettings.json
; included) along with everything else is the expected behavior here --
; "uninstall" means gone, not "gone except the config."
Type: filesandordirs; Name: "{commonpf}\Mayer E-Concept\Nexus"
Type: dirifempty; Name: "{commonpf}\Mayer E-Concept"

[Icons]
; Only created when the assistant component was actually installed --
; Nexus is meant to be usable on its own, not just launched from Revit's
; ribbon, so it gets a normal Start Menu entry like any other standalone
; app, not just the in-Revit button OpenNexusCommand.cs provides.
Name: "{group}\Nexus"; Filename: "{commonpf}\Mayer E-Concept\Nexus\Nexus.exe"; WorkingDir: "{commonpf}\Mayer E-Concept\Nexus"; Components: assistant

[Messages]
WelcomeLabel2=This will install [name/ver] for Autodesk Revit 2025 and/or 2027, with the Nexus assistant if you like.%n%nPlease close Revit before continuing.
SelectComponentsDesc=Which Revit versions should get ElecTriX, and should the Nexus assistant be installed?
SelectComponentsLabel2=Tick the Revit versions you use (the ones installed on this PC are ticked already) and whether you want the Nexus assistant. Anything you untick is removed.

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

{ ── Which components start ticked ────────────────────────────────────────────
  Inno restores the previous selection by itself -- but installers before
  2.4.2 had a single "electrix" component, so for those (and first installs)
  the Revit versions installed on this PC are ticked instead. Runs from
  InitializeWizard, which also runs for the silent auto-update. }
function RevitInstalled(Version: String): Boolean;
begin
  Result := FileExists(ExpandConstant('{commonpf}\Autodesk\Revit ' + Version + '\Revit.exe'));
end;

function PreviousComponents: String;
begin
  Result := '';
  RegQueryStringValue(HKLM, 'Software\Microsoft\Windows\CurrentVersion\Uninstall\{B3F2C9A4-7E61-4D8B-9C0A-2F5E1A6D4B77}_is1',
    'Inno Setup: Selected Components', Result);
end;

procedure PreselectComponents;
var
  Prev: String;
begin
  Prev := PreviousComponents;
  if Pos('revit20', Prev) > 0 then Exit; { chosen with this installer before -- Inno keeps it }

  if RevitInstalled('2025') or not RevitInstalled('2027') then
    WizardSelectComponents('revit2025')
  else
    WizardSelectComponents('!revit2025');
  if RevitInstalled('2027') then
    WizardSelectComponents('revit2027')
  else
    WizardSelectComponents('!revit2027');
  { First install: the assistant is ticked; an update keeps whatever was chosen before. }
  if (Prev = '') or (Pos('assistant', Prev) > 0) then
    WizardSelectComponents('assistant')
  else
    WizardSelectComponents('!assistant');
end;

{ At least one Revit version, or the assistant on its own. }
function NextButtonClick(CurPageID: Integer): Boolean;
begin
  Result := True;
  if (CurPageID = wpSelectComponents) and not WizardIsComponentSelected('revit2025')
     and not WizardIsComponentSelected('revit2027') and not WizardIsComponentSelected('assistant') then
  begin
    MsgBox('Please select at least one Revit version (or the Nexus assistant).', mbError, MB_OK);
    Result := False;
  end;
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
  PreselectComponents;
end;

{ PageNameLabel/PageDescriptionLabel get re-styled by Inno whenever the page
  actually changes, after InitializeWizard's one-time pass -- re-applying the
  same two colors here keeps the title area consistent on every page. }
procedure CurPageChanged(CurPageID: Integer);
begin
  ApplyTitleColors;
end;

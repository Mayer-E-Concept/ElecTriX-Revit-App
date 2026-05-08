; setup.iss — ME-Tools Revit Add-in Installer
; Mayer E-Concept SRL
; Build: F9 in Inno Setup Compiler

#define ProjectDir "X:\02_sabloane\01_Revit\11_Revit_AddOn"
#define AppVersion "1.0.6-beta"

[Setup]
AppName=ME-Tools fuer Revit
AppVersion={#AppVersion}
AppPublisher=Mayer E-Concept SRL
AppPublisherURL=https://mayer-econcept.ro
AppSupportURL=https://mayer-econcept.ro
AppCopyright=Copyright 2025 Mayer E-Concept SRL
VersionInfoCompany=Mayer E-Concept SRL
VersionInfoDescription=ME-Tools fuer Autodesk Revit
VersionInfoVersion=1.0.0.0
DefaultDirName={autopf}\METools
DefaultGroupName=ME-Tools
OutputDir={#ProjectDir}\installer_output
OutputBaseFilename=setup_metools_{#AppVersion}
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
MinVersion=10.0
ArchitecturesInstallIn64BitMode=x64

[Types]
Name: "r2025"; Description: "Revit 2025 (empfohlen)"
Name: "r2026"; Description: "Revit 2026"
Name: "all";   Description: "Alle Versionen (2025 + 2026)"
Name: "custom"; Description: "Benutzerdefiniert"; Flags: iscustom

[Components]
Name: "r2025"; Description: "Revit 2025"; Types: r2025 all custom
Name: "r2026"; Description: "Revit 2026"; Types: r2026 all custom

[Files]
; ── Revit 2025 ────────────────────────────────────────────────────────────
Source: "{#ProjectDir}\bin\x64\Release\net8.0-windows\METools.dll"; DestDir: "{commonappdata}\Autodesk\Revit\Addins\2025"; DestName: "METools.dll"; Flags: ignoreversion; Components: r2025
Source: "{#ProjectDir}\METools.addin";                               DestDir: "{commonappdata}\Autodesk\Revit\Addins\2025"; DestName: "METools.addin"; Flags: ignoreversion; Components: r2025
Source: "{#ProjectDir}\Icons\*";                                     DestDir: "{commonappdata}\Autodesk\Revit\Addins\2025\Icons";  Flags: ignoreversion recursesubdirs; Components: r2025
Source: "{#ProjectDir}\standard_worksets.json";               DestDir: "{commonappdata}\Autodesk\Revit\Addins\2025\config"; Flags: ignoreversion onlyifdoesntexist; Components: r2025

; ── Revit 2026 ────────────────────────────────────────────────────────────
Source: "{#ProjectDir}\bin\x64\Release\net8.0-windows\METools.dll"; DestDir: "{commonappdata}\Autodesk\Revit\Addins\2026"; DestName: "METools.dll"; Flags: ignoreversion; Components: r2026
Source: "{#ProjectDir}\METools.addin";                               DestDir: "{commonappdata}\Autodesk\Revit\Addins\2026"; DestName: "METools.addin"; Flags: ignoreversion; Components: r2026
Source: "{#ProjectDir}\Icons\*";                                     DestDir: "{commonappdata}\Autodesk\Revit\Addins\2026\Icons";  Flags: ignoreversion recursesubdirs; Components: r2026
Source: "{#ProjectDir}\standard_worksets.json";               DestDir: "{commonappdata}\Autodesk\Revit\Addins\2026\config"; Flags: ignoreversion onlyifdoesntexist; Components: r2026

[UninstallDelete]
Type: files;         Name: "{commonappdata}\Autodesk\Revit\Addins\2025\METools.dll"
Type: files;         Name: "{commonappdata}\Autodesk\Revit\Addins\2025\METools.addin"
Type: filesandordirs; Name: "{commonappdata}\Autodesk\Revit\Addins\2025\Icons"
Type: files;         Name: "{commonappdata}\Autodesk\Revit\Addins\2026\METools.dll"
Type: files;         Name: "{commonappdata}\Autodesk\Revit\Addins\2026\METools.addin"
Type: filesandordirs; Name: "{commonappdata}\Autodesk\Revit\Addins\2026\Icons"

[Messages]
WelcomeLabel1=Willkommen bei ME-Tools fuer Revit
WelcomeLabel2=ME-Tools erweitert Autodesk Revit um intelligente Werkzeuge fuer die Elektroplanung.%n%nSie erhalten einen kostenlosen Beta-Zugang fuer 30 Tage.%n%nNach Ablauf erhalten Sie von Mayer E-Concept SRL einen Freischaltcode.%n%nKlicken Sie Weiter um fortzufahren.

; setup.iss — ME-Tools Revit Add-in Installer
; Mayer E-Concept SRL — supports Revit 2024, 2025, 2026
;
; Voraussetzung: Inno Setup 6 (kostenlos) → https://jrsoftware.org/isinfo.php
; Bauen: In Inno Setup Compiler öffnen → Compile → setup_metools.exe

[Setup]
AppName=ME-Tools für Revit
AppVersion=1.0.0-beta
AppPublisher=Mayer E-Concept SRL
AppPublisherURL=https://mayer-econcept.ro
DefaultDirName={autopf}\METools
DefaultGroupName=ME-Tools
OutputDir=.\installer_output
OutputBaseFilename=setup_metools_v1_0_0_beta
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
MinVersion=10.0
ArchitecturesInstallIn64BitMode=x64

[Types]
Name: "r2025";  Description: "Revit 2025 (empfohlen)"
Name: "r2024";  Description: "Revit 2024"
Name: "r2026";  Description: "Revit 2026"
Name: "all";    Description: "Alle Versionen (2024, 2025, 2026)"
Name: "custom"; Description: "Benutzerdefiniert"; Flags: iscustom

[Components]
Name: "r2024"; Description: "Revit 2024"; Types: r2024 all custom
Name: "r2025"; Description: "Revit 2025"; Types: r2025 all custom
Name: "r2026"; Description: "Revit 2026"; Types: r2026 all custom

[Files]
; Revit 2024
Source: ".\bin\Release2024\net48\METools.dll";        DestDir: "{commonappdata}\Autodesk\Revit\Addins\2024"; DestName: "METools.dll";   Flags: ignoreversion; Components: r2024
Source: ".\METools_2024.addin";                       DestDir: "{commonappdata}\Autodesk\Revit\Addins\2024"; DestName: "METools.addin"; Flags: ignoreversion; Components: r2024
Source: ".\Icons\*";                                  DestDir: "{commonappdata}\Autodesk\Revit\Addins\2024\Icons"; Flags: ignoreversion recursesubdirs; Components: r2024

; Revit 2025
Source: ".\bin\Release2025\net8.0-windows\METools.dll"; DestDir: "{commonappdata}\Autodesk\Revit\Addins\2025"; DestName: "METools.dll";   Flags: ignoreversion; Components: r2025
Source: ".\METools_2025.addin";                         DestDir: "{commonappdata}\Autodesk\Revit\Addins\2025"; DestName: "METools.addin"; Flags: ignoreversion; Components: r2025
Source: ".\Icons\*";                                    DestDir: "{commonappdata}\Autodesk\Revit\Addins\2025\Icons"; Flags: ignoreversion recursesubdirs; Components: r2025

; Revit 2026
Source: ".\bin\Release2026\net8.0-windows\METools.dll"; DestDir: "{commonappdata}\Autodesk\Revit\Addins\2026"; DestName: "METools.dll";   Flags: ignoreversion; Components: r2026
Source: ".\METools_2026.addin";                         DestDir: "{commonappdata}\Autodesk\Revit\Addins\2026"; DestName: "METools.addin"; Flags: ignoreversion; Components: r2026
Source: ".\Icons\*";                                    DestDir: "{commonappdata}\Autodesk\Revit\Addins\2026\Icons"; Flags: ignoreversion recursesubdirs; Components: r2026

[UninstallDelete]
Type: files;        Name: "{commonappdata}\Autodesk\Revit\Addins\2024\METools.dll"
Type: files;        Name: "{commonappdata}\Autodesk\Revit\Addins\2024\METools.addin"
Type: filesandordirs; Name: "{commonappdata}\Autodesk\Revit\Addins\2024\Icons"
Type: files;        Name: "{commonappdata}\Autodesk\Revit\Addins\2025\METools.dll"
Type: files;        Name: "{commonappdata}\Autodesk\Revit\Addins\2025\METools.addin"
Type: filesandordirs; Name: "{commonappdata}\Autodesk\Revit\Addins\2025\Icons"
Type: files;        Name: "{commonappdata}\Autodesk\Revit\Addins\2026\METools.dll"
Type: files;        Name: "{commonappdata}\Autodesk\Revit\Addins\2026\METools.addin"
Type: filesandordirs; Name: "{commonappdata}\Autodesk\Revit\Addins\2026\Icons"

[Messages]
WelcomeLabel1=Willkommen bei ME-Tools für Revit
WelcomeLabel2=ME-Tools erweitert Revit um intelligente Leuchten-Platzierungswerkzeuge.%n%nSie erhalten einen 30-tägigen Beta-Zugang. Danach erhalten Sie einen Freischaltcode von Mayer E-Concept SRL.%n%nWeiter klicken um fortzufahren.

; setup.iss — ME-Tools Revit Add-in Installer
; Mayer E-Concept SRL
; Build: F9 in Inno Setup Compiler

#define ProjectDir "X:\02_sabloane\01_Revit\11_Revit_AddOn"

[Setup]
AppName=ME-Tools fuer Revit
AppVersion=1.0.0-beta
AppPublisher=Mayer E-Concept SRL
AppPublisherURL=https://mayer-econcept.ro
DefaultDirName={autopf}\METools
DefaultGroupName=ME-Tools
OutputDir={#ProjectDir}\installer_output
OutputBaseFilename=setup_metools_v1_0_0_beta
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
Source: "{#ProjectDir}\bin\x64\Release\net8.0-windows\METools.dll"; DestDir: "{commonappdata}\Autodesk\Revit\Addins\2025"; DestName: "METools.dll"; Flags: ignoreversion; Components: r2025
Source: "{#ProjectDir}\METools_2025.addin"; DestDir: "{commonappdata}\Autodesk\Revit\Addins\2025"; DestName: "METools.addin"; Flags: ignoreversion; Components: r2025
Source: "{#ProjectDir}\Icons\*"; DestDir: "{commonappdata}\Autodesk\Revit\Addins\2025\Icons"; Flags: ignoreversion recursesubdirs; Components: r2025
Source: "{#ProjectDir}\bin\x64\Release\net8.0-windows\METools.dll"; DestDir: "{commonappdata}\Autodesk\Revit\Addins\2026"; DestName: "METools.dll"; Flags: ignoreversion; Components: r2026
Source: "{#ProjectDir}\METools_2026.addin"; DestDir: "{commonappdata}\Autodesk\Revit\Addins\2026"; DestName: "METools.addin"; Flags: ignoreversion; Components: r2026
Source: "{#ProjectDir}\Icons\*"; DestDir: "{commonappdata}\Autodesk\Revit\Addins\2026\Icons"; Flags: ignoreversion recursesubdirs; Components: r2026

[UninstallDelete]
Type: files; Name: "{commonappdata}\Autodesk\Revit\Addins\2025\METools.dll"
Type: files; Name: "{commonappdata}\Autodesk\Revit\Addins\2025\METools.addin"
Type: filesandordirs; Name: "{commonappdata}\Autodesk\Revit\Addins\2025\Icons"
Type: files; Name: "{commonappdata}\Autodesk\Revit\Addins\2026\METools.dll"
Type: files; Name: "{commonappdata}\Autodesk\Revit\Addins\2026\METools.addin"
Type: filesandordirs; Name: "{commonappdata}\Autodesk\Revit\Addins\2026\Icons"

[Messages]
WelcomeLabel1=Willkommen bei ME-Tools fuer Revit
WelcomeLabel2=ME-Tools erweitert Revit um intelligente Leuchten-Platzierungswerkzeuge.%n%nSie erhalten einen kostenlosen 30-taegigen Beta-Zugang.%n%nNach Ablauf erhalten Sie einen Freischaltcode von Mayer E-Concept SRL.%n%nKlicken Sie Weiter um fortzufahren.

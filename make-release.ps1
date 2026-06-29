# make-release.ps1 -- ME-Tools release packaging
# Mayer E-Concept SRL
#
# Order matters: build -> obfuscate -> RE-SIGN -> compile installer.
# Obfuscation rewrites the DLL and removes the Authenticode signature, so the
# DLL must be signed AGAIN after obfuscating (not before).
#
# Run from an elevated PowerShell:
#   powershell -ExecutionPolicy Bypass -File make-release.ps1
#
# Set $Obfuscate = $false to skip obfuscation (recommended for the first beta:
# ship the signed DLL, gather opinions, add obfuscation once it is proven stable).

$ErrorActionPreference = "Stop"

# ---- paths (adjust to your machine) -----------------------------------------
$ProjectDir = "X:\02_sabloane\01_Revit\11_Revit_AddOn"
$Csproj     = Join-Path $ProjectDir "METools.csproj"
$OutDir     = Join-Path $ProjectDir "bin\Release\net8.0-windows"
$Dll        = Join-Path $OutDir "METools.dll"
$ObfCfg     = Join-Path $ProjectDir "METools.Obfuscar.xml"
$ObfDll     = Join-Path $OutDir "obf\METools.dll"
$Pfx        = Join-Path $ProjectDir "signing\mec-codesign.pfx"
$PfxPass    = "MEC2025"
$Iss        = Join-Path $ProjectDir "setup.iss"
$Iscc       = "C:\Program Files (x86)\Inno Setup 6\ISCC.exe"

$Obfuscate  = $false     # <-- set to $false to skip obfuscation
# -----------------------------------------------------------------------------

function Find-SignTool {
    $cands = Get-ChildItem "C:\Program Files (x86)\Windows Kits\10\bin" -Recurse -Filter signtool.exe -ErrorAction SilentlyContinue |
             Where-Object { $_.FullName -match "x64" }
    if ($cands) { return ($cands | Sort-Object FullName -Descending | Select-Object -First 1).FullName }
    throw "signtool.exe not found. Install the Windows 10/11 SDK."
}

Write-Host "1/5  Building Release..." -ForegroundColor Cyan
dotnet build $Csproj -c Release

if ($Obfuscate) {
    Write-Host "2/5  Obfuscating (Obfuscar)..." -ForegroundColor Cyan
    obfuscar.console $ObfCfg
    if (-not (Test-Path $ObfDll)) { throw "Obfuscated DLL not found at $ObfDll" }
    Write-Host "      Replacing build output with obfuscated DLL..." -ForegroundColor Cyan
    Copy-Item $ObfDll $Dll -Force
} else {
    Write-Host "2/5  Obfuscation SKIPPED (`$Obfuscate = `$false)." -ForegroundColor Yellow
}

Write-Host "3/5  Re-signing the DLL..." -ForegroundColor Cyan
$signtool = Find-SignTool
& $signtool sign /f $Pfx /p $PfxPass /fd SHA256 /tr http://timestamp.digicert.com /td SHA256 $Dll

Write-Host "4/5  Verifying signature..." -ForegroundColor Cyan
& $signtool verify /pa $Dll

Write-Host "5/5  Compiling installer (Inno Setup)..." -ForegroundColor Cyan
if (-not (Test-Path $Iscc)) { throw "ISCC.exe not found. Install Inno Setup 6." }
& $Iscc $Iss

Write-Host ""
Write-Host "Done. Installer is in: $ProjectDir\installer_output" -ForegroundColor Green
Write-Host "Share that setup_metools_vX.X.X.exe with your testers." -ForegroundColor Green

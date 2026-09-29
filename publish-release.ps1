# publish-release.ps1 -- ME-Tools
# Mayer E-Concept SRL
#
# Puts a new version on the update feed that UpdateChecker.cs reads on every
# Revit start: copies installer_output\setup_metools_v<AppVersion>.exe to the
# releases folder on the share and writes latest.json next to it.
#
#   1. Bump  #define AppVersion  in setup.iss (e.g. "nxs_2.3.7").
#   2. powershell -ExecutionPolicy Bypass -File publish-release.ps1 -Build -Notes "What changed"
#
# -Build   runs "dotnet build -c Release" and Inno Setup's ISCC first. Without
#          it, the installer must already be built (Inno Setup -> Compile).
#          Nexus is taken from whatever is in its nexus_publish folder, same
#          as a manual compile -- publish Nexus first if it changed.
# -Notes   shown in the update dialog.
param(
    [string]$Notes = "",
    [switch]$Build,
    # Only for testing the script somewhere else -- UpdateChecker.cs reads the default.
    [string]$FeedDir = "\\Database\MEC_Database\02_sabloane\01_Revit\ElecTriX-Revit-App\releases"
)
$ErrorActionPreference = "Stop"
$Root = $PSScriptRoot

$iss = Get-Content (Join-Path $Root "setup.iss") -Raw
if ($iss -notmatch '#define\s+AppVersion\s+"([^"]+)"') { throw "No '#define AppVersion' in setup.iss" }
$tag = $Matches[1]
if ($tag -notmatch '\d+(\.\d+)+') { throw "AppVersion '$tag' has no version number" }
$version = $Matches[0]

if ($Build) {
    Write-Host "Building METools.dll $version ..."
    & dotnet build (Join-Path $Root "METools.csproj") -c Release
    if ($LASTEXITCODE -ne 0) { throw "dotnet build failed" }
    $iscc = @("${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe", "$env:ProgramFiles\Inno Setup 6\ISCC.exe") |
        Where-Object { Test-Path $_ } | Select-Object -First 1
    if (-not $iscc) { throw "Inno Setup 6 (ISCC.exe) not found" }
    Write-Host "Compiling installer ..."
    & $iscc (Join-Path $Root "setup.iss")
    if ($LASTEXITCODE -ne 0) { throw "ISCC failed" }
}

$installerName = "setup_metools_v$tag.exe"
$installer = Join-Path $Root "installer_output\$installerName"
if (-not (Test-Path $installer)) { throw "Installer not found: $installer -- build it first (or use -Build)" }

# The installer must contain the current DLL, and that DLL must carry this version.
$dll = Join-Path $Root "bin\Release\net8.0-windows\METools.dll"
if (Test-Path $dll) {
    $dllVersion = (Get-Item $dll).VersionInfo.FileVersion
    if (-not $dllVersion.StartsWith($version)) { throw "METools.dll is version $dllVersion, setup.iss says $version -- rebuild first" }
    if ((Get-Item $installer).LastWriteTime -lt (Get-Item $dll).LastWriteTime) {
        throw "The installer is older than METools.dll -- recompile setup.iss (or use -Build)"
    }
}

# Refuse to go backwards or republish the same number by accident.
$feedFile = Join-Path $FeedDir "latest.json"
if (Test-Path $feedFile) {
    $current = Get-Content $feedFile -Raw | ConvertFrom-Json
    if ([version]$current.version -ge [version]$version) {
        throw "The feed already has version $($current.version) -- bump AppVersion in setup.iss first"
    }
}

New-Item -ItemType Directory -Force $FeedDir | Out-Null
Copy-Item $installer (Join-Path $FeedDir $installerName) -Force
$feed = [ordered]@{ version = $version; installer = $installerName; notes = $Notes }
# UTF-8 without BOM, so the add-in's JSON reader never sees a stray BOM.
[System.IO.File]::WriteAllText($feedFile, ($feed | ConvertTo-Json), (New-Object System.Text.UTF8Encoding $false))

Write-Host "Published $installerName (version $version) to $FeedDir"
Write-Host "Every Revit with an older ME-Tools (2.3.7 or later, which has the update check) offers it on its next start."

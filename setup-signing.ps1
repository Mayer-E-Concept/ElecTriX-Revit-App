# ──────────────────────────────────────────────────────────────────────
#  ElecTriX — Setup Code Signing (einmalig ausführen)
#  Mayer E-Concept SRL
#
#  Erstellt ein Self-Signed Code-Signing-Zertifikat auf Namen
#  "Mayer E-Concept SRL" und installiert es als Trusted Publisher
#  + Trusted Root, damit Revit keinen Warning-Dialog mehr zeigt und
#  "Publisher: Mayer E-Concept SRL" anzeigt.
#
#  Ausführen:
#    1. PowerShell als Administrator öffnen
#    2. Dieses Script mit Execution Policy Bypass starten:
#       powershell -ExecutionPolicy Bypass -File setup-signing.ps1
#
#  Nach der einmaligen Einrichtung signiert jeder Build automatisch
#  (siehe METools.csproj → Target SignAssembly).
# ──────────────────────────────────────────────────────────────────────

$ErrorActionPreference = "Stop"

$Subject          = "CN=Mayer E-Concept SRL, O=Mayer E-Concept SRL, C=RO"
$FriendlyName     = "Mayer E-Concept SRL Code Signing"
$PfxPath          = Join-Path $PSScriptRoot "signing\mec-codesign.pfx"
$PfxPasswordPlain = "MEC2025"   # ggf. anpassen — muss mit csproj übereinstimmen
$CerPath          = Join-Path $PSScriptRoot "signing\mec-codesign.cer"

Write-Host "==========================================" -ForegroundColor Cyan
Write-Host "ElecTriX Code-Signing Setup" -ForegroundColor Cyan
Write-Host "==========================================" -ForegroundColor Cyan
Write-Host ""

# Admin-Check
$currentPrincipal = New-Object Security.Principal.WindowsPrincipal([Security.Principal.WindowsIdentity]::GetCurrent())
if (-not $currentPrincipal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    Write-Host "FEHLER: Dieses Script muss als Administrator gestartet werden." -ForegroundColor Red
    Write-Host "Rechtsklick auf PowerShell → 'Als Administrator ausführen'." -ForegroundColor Yellow
    exit 1
}

# Signing-Ordner
$signingDir = Split-Path $PfxPath -Parent
if (-not (Test-Path $signingDir)) {
    New-Item -Path $signingDir -ItemType Directory -Force | Out-Null
}

Write-Host "1/5  Prüfe vorhandene Zertifikate..." -ForegroundColor Green
$existing = Get-ChildItem -Path Cert:\CurrentUser\My |
    Where-Object { $_.Subject -like "*Mayer E-Concept SRL*" -and $_.HasPrivateKey }

if ($existing) {
    Write-Host "     Zertifikat existiert bereits: $($existing.Thumbprint)" -ForegroundColor Yellow
    $cert = $existing | Select-Object -First 1
} else {
    Write-Host "2/5  Erstelle Self-Signed Zertifikat..." -ForegroundColor Green
    $cert = New-SelfSignedCertificate `
        -Type CodeSigningCert `
        -Subject $Subject `
        -FriendlyName $FriendlyName `
        -KeyAlgorithm RSA `
        -KeyLength 2048 `
        -HashAlgorithm SHA256 `
        -KeyUsage DigitalSignature `
        -NotAfter (Get-Date).AddYears(10) `
        -CertStoreLocation "Cert:\CurrentUser\My"
    Write-Host "     Thumbprint: $($cert.Thumbprint)" -ForegroundColor Green
}

Write-Host "3/5  Exportiere PFX (mit Private Key) nach $PfxPath..." -ForegroundColor Green
$pwd = ConvertTo-SecureString -String $PfxPasswordPlain -Force -AsPlainText
Export-PfxCertificate -Cert $cert -FilePath $PfxPath -Password $pwd | Out-Null

Write-Host "4/5  Exportiere CER (Public Key) nach $CerPath..." -ForegroundColor Green
Export-Certificate -Cert $cert -FilePath $CerPath | Out-Null

Write-Host "5/5  Installiere als Trusted Publisher und Trusted Root..." -ForegroundColor Green
# Trusted Publisher — damit Revit den Publisher anzeigt und nicht warnt
Import-Certificate -FilePath $CerPath -CertStoreLocation Cert:\LocalMachine\TrustedPublisher | Out-Null
# Trusted Root — damit die Signatur als gültig verifiziert wird
Import-Certificate -FilePath $CerPath -CertStoreLocation Cert:\LocalMachine\Root | Out-Null

Write-Host ""
Write-Host "==========================================" -ForegroundColor Cyan
Write-Host "Fertig! Nächste Schritte:" -ForegroundColor Cyan
Write-Host "==========================================" -ForegroundColor Cyan
Write-Host "  1. Visual Studio schließen und neu öffnen"
Write-Host "  2. Projekt rebuild — nach dem Build wird die DLL"
Write-Host "     automatisch signiert (Target 'SignAssembly' in csproj)."
Write-Host "  3. Revit neu starten — der Sicherheits-Dialog zeigt jetzt"
Write-Host "     'Publisher: Mayer E-Concept SRL'"
Write-Host ""
Write-Host "Hinweis:" -ForegroundColor Yellow
Write-Host "  Das PFX (Private Key) liegt unter:" -ForegroundColor Yellow
Write-Host "  $PfxPath" -ForegroundColor Yellow
Write-Host "  NICHT ins Git einchecken — signing/ ist im _gitignore." -ForegroundColor Yellow
Write-Host ""

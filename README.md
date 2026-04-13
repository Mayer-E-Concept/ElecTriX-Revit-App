# ME-Tools für Autodesk Revit

**Mayer E-Concept SRL** — Intelligente Werkzeuge für die Revit-Planung

---

## Funktionen

- **Lamp Placer** — Automatische Leuchtenplatzierung im Deckenspiegel
  - Raumanordnung: flächenbasiert oder manuelles Raster
  - Linienanordnung: Anzahl eingeben, alles andere automatisch
  - Lampenlänge wird automatisch aus der gewählten Familie gelesen
  - Along Line / Perpendicular Orientierung
  - Kreuzlinie als Orientierungshilfe während des Zeichnens

- **Family Placer** — Allgemeines Werkzeug zur Familienplatzierung

- **Fix Level** — Level-Korrektur für Elemente

- **Konfiguration** — Projektspezifische Einstellungen

---

## Unterstützte Revit-Versionen

| Version | .NET | Status |
|---------|------|--------|
| Revit 2024 | .NET 4.8 | ✅ Unterstützt |
| Revit 2025 | .NET 8.0 | ✅ Unterstützt |
| Revit 2026 | .NET 8.0 | ✅ Unterstützt |

---

## Installation (Endbenutzer)

1. `setup_metools_vX.X.X.exe` herunterladen
2. Installer ausführen, gewünschte Revit-Version wählen
3. Revit neu starten
4. ME-Tools Tab erscheint in der Revit-Ribbonleiste

### Lizenz / Beta-Zugang

Das Add-in läuft 30 Tage kostenlos als Beta.  
Für einen dauerhaften Freischaltcode: **info@mayer-econcept.ro**

---

## Entwicklung (für mich)

### Bauen

```
# Revit 2025 (Debug / Entwicklung)
Build → Debug

# Revit 2024 Release
Build → Release2024

# Revit 2025 Release  
Build → Release2025

# Revit 2026 Release
Build → Release2026
```

### Installer erstellen (nach Release Build)

1. Inno Setup installieren: https://jrsoftware.org/isinfo.php
2. `setup.iss` in Inno Setup öffnen
3. "Compile" → `installer_output\setup_metools_vX.X.X.exe`

### Freischaltcode generieren

```
KeyGenerator.exe
→ Maschinen-ID eingeben
→ Code wird angezeigt und an Kunden gesendet
```

---

## Projektstruktur

```
METools/
├── Icons/                  ← Alle Icons (PNG)
├── LampPlacer/
│   ├── LampPlacerModels.cs
│   ├── LampPlacerHandler.cs
│   └── LampPlacerWindow.cs
├── Licensing/
│   ├── LicenseManager.cs
│   ├── LicenseWindow.cs
│   └── LicenseCheck.cs
├── App.cs
├── METools.csproj
├── METools_2024.addin
├── METools_2025.addin
├── METools_2026.addin
└── setup.iss
```

---

© 2025 Mayer E-Concept SRL · Alle Rechte vorbehalten

using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace METools.FamilyPlacer
{
    /// <summary>
    /// Ein Regex-Pattern das einen Panel-Namen in einen Match-Prefix für Raumnummern übersetzt.
    /// Beispiele:
    ///   Regex = "Wohnung\s*(\d+)",  Ersetzung = "W$1"   →  "Verteiler Wohnung 3"  →  "W3"
    ///   Regex = "Haus\s*(\d+)",     Ersetzung = "H$1"   →  "Haus 2 Verteiler"     →  "H2"
    ///   Regex = "^UV\s+(\w+)",      Ersetzung = "$1"    →  "UV Keller"            →  "Keller"
    /// </summary>
    public class MatchPrefixPattern
    {
        public string Regex      { get; set; } = "";
        public string Ersetzung  { get; set; } = "";
        public string Beschreibung { get; set; } = "";
    }

    /// <summary>
    /// Globale Settings für ElecTriX. Projekt-agnostisch, in %APPDATA% gespeichert.
    /// Vom User mit Notepad editierbar (Settings-Button im Dialog).
    /// </summary>
    public class ElecTriXSettings
    {
        /// <summary>Dateiformat-Version für zukünftige Migrationen.</summary>
        public int Version { get; set; } = 1;

        // ─── Default-Werte für die Scheme-Parameter ──────────────────────
        /// <summary>
        /// Template für Vorsicherung. Platzhalter:
        ///   {prefix}   = Circuit Prefix des Panels (z.B. 'UV Wohnung 1')
        ///   {sep}      = Circuit Prefix Separator (z.B. '-')
        ///   {matchpfx} = Abgeleiteter Match-Prefix (z.B. 'W1')
        ///   {raumnr}   = Raumnummer (z.B. 'W1-E1')
        ///   {raumname} = Raumname (z.B. 'Wohnzimmer')
        /// Leer lassen um Parameter nicht automatisch zu füllen.
        /// </summary>
        public string VorsicherungTemplate { get; set; } = "{prefix}{sep}VS";

        /// <summary>Template für FI-Kreis. Platzhalter wie Vorsicherung.</summary>
        public string FIKreisTemplate      { get; set; } = "{prefix}{sep}FI";

        /// <summary>Template für Sicherung (Haupt-Kreis). Leer = use Raum-spezifischer Wert.</summary>
        public string SicherungTemplate    { get; set; } = "";

        /// <summary>Template für Schaltkreis-Shared-Parameter. Leer = gleicher Wert wie Sicherung.</summary>
        public string SchaltkreisTemplate  { get; set; } = "";

        /// <summary>
        /// Template für den Circuit Load Name (Stromkreisbezeichnung).
        /// Platzhalter: {geraet}, {raumname}, {raumnr}, {prefix}, {sep}
        /// Default: nur Raumname — damit die Bezeichnung sich nicht mit der
        /// Circuit Number überschneidet (die oft Gerät und Sicherung via
        /// Naming-Scheme enthält).
        /// Beispiele für Alternativen:
        ///   "{raumname}"                 → "Flur"
        ///   "{geraet} {raumname}"        → "Steckdose Flur"
        ///   "{raumname} ({raumnr})"      → "Flur (W1-E1)"
        ///   "{geraet} {raumname} [{raumnr}]" → "Licht Flur [W1-E1]"
        /// </summary>
        public string LoadNameTemplate     { get; set; } = "{raumname}";

        // ─── Geräte-Bezeichnungen pro Revit-Kategorie ────────────────────
        /// <summary>
        /// BuiltInCategory-ID (int) → Gerätebezeichnung.
        /// Wird verwendet um den 'Gerät'-Parameter und LoadName zu befüllen.
        /// Wenn ein Stromkreis mehrere Kategorien mischt, werden die Labels
        /// mit ' &amp; ' verbunden.
        /// </summary>
        public Dictionary<string, string> KategorieLabels { get; set; } = new Dictionary<string, string>
        {
            { "-2001060", "Steckdose" },       // OST_ElectricalFixtures
            { "-2001120", "Licht" },           // OST_LightingFixtures
            { "-2001040", "Verteiler" },       // OST_ElectricalEquipment
        };

        /// <summary>Default-Label wenn keine Kategorie-Matches.</summary>
        public string DefaultGeraetLabel { get; set; } = "Stromkreis";

        /// <summary>Trennzeichen beim Mischen mehrerer Gerätearten.</summary>
        public string GeraeteMixTrenner { get; set; } = " &amp; ";

        // ─── Match-Prefix-Ableitung ─────────────────────────────────────
        /// <summary>
        /// Reihenfolge: erste passende Regex gewinnt. Nicht matchende
        /// fallen durch auf den Fallback: CircuitPrefix übernehmen wenn kurz.
        /// User kann eigene Patterns hinzufügen z.B. für eigene Namensschemata.
        /// </summary>
        public List<MatchPrefixPattern> MatchPrefixPatterns { get; set; } = new List<MatchPrefixPattern>
        {
            new MatchPrefixPattern { Regex = @"Wohnung\s*(\d+)", Ersetzung = "W$1",
                                     Beschreibung = "'Verteiler Wohnung 3' → 'W3'" },
            new MatchPrefixPattern { Regex = @"Haus\s*(\d+)",    Ersetzung = "H$1",
                                     Beschreibung = "'Haus 2 HV' → 'H2'" },
            new MatchPrefixPattern { Regex = @"Apartment\s*(\d+)", Ersetzung = "A$1",
                                     Beschreibung = "'Apartment 5' → 'A5'" },
            new MatchPrefixPattern { Regex = @"^UG\d*",          Ersetzung = "UG",
                                     Beschreibung = "'UG Verteiler' → 'UG'" },
        };

        /// <summary>
        /// Maximale Länge um den CircuitPrefix direkt als MatchPrefix zu übernehmen
        /// wenn keine Regex matched (z.B. 'UVx' → 'UVx', 'TEL' → 'TEL').
        /// </summary>
        public int FallbackMaxPrefixLaenge { get; set; } = 10;

        // ─── Parameter-Namen (Shared Parameters) ─────────────────────────
        /// <summary>
        /// Welche Shared Parameters an Electrical Circuits gebunden sind und
        /// beschrieben werden sollen. User kann Parameter hinzufügen oder entfernen.
        /// Key = interner Schlüssel, Value = Parametername (case-sensitive!) in Revit.
        /// </summary>
        public Dictionary<string, string> CircuitSharedParameters { get; set; } = new Dictionary<string, string>
        {
            { "Vorsicherung", "Vorsicherung" },
            { "FIKreis",      "FI-Kreis" },
            { "Sicherung",    "Sicherung" },
            { "Geraet",       "Gerät" },
            { "Schaltkreis",  "Schaltkreis" },
        };

        // ─── Raum-Lookup-Verhalten ───────────────────────────────────────
        /// <summary>
        /// Z-Offsets (in Fuß) die beim Raum-Lookup probiert werden wenn fi.Space
        /// und fi.Room leer sind. Wird auf Level.Elevation addiert.
        /// </summary>
        public List<double> ZProbeOffsetsFt { get; set; } = new List<double>
        {
            1.0, 3.0, 5.0, 0.1, 0.0, -0.5, 6.0, 8.0, 10.0,
        };

        /// <summary>
        /// Wenn true: der Raum-Lookup nutzt auch die Bounding-Box des Bauteils,
        /// nicht nur den Location-Point. Hilfreich für Wand-gehostete Bauteile
        /// deren Location-Point genau auf der Wand liegt.
        /// </summary>
        public bool UseBoundingBoxFallback { get; set; } = true;
    }

    /// <summary>
    /// Lädt und speichert ElecTriXSettings aus %APPDATA%\METools\electrix-settings.json.
    /// User kann die Datei direkt im Notepad editieren.
    /// </summary>
    public static class ElecTriXSettingsStorage
    {
        public static string LastError { get; private set; } = "";

        /// <summary>Absoluter Pfad zur JSON-Datei.</summary>
        public static string GetSettingsPath()
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            var dir = Path.Combine(appData, "METools");
            try { Directory.CreateDirectory(dir); } catch { }
            return Path.Combine(dir, "electrix-settings.json");
        }

        /// <summary>
        /// Lädt Settings aus der JSON-Datei. Wenn nicht vorhanden oder
        /// invalide → neues ElecTriXSettings mit Defaults.
        /// </summary>
        public static ElecTriXSettings Laden()
        {
            LastError = "";
            var path = GetSettingsPath();
            if (!File.Exists(path))
            {
                // Beim ersten Start: Defaults in Datei schreiben damit User sie findet
                var defaults = new ElecTriXSettings();
                try { Speichern(defaults); } catch { }
                return defaults;
            }
            try
            {
                var json = File.ReadAllText(path);
                if (string.IsNullOrWhiteSpace(json)) return new ElecTriXSettings();
                var opts = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                var s = JsonSerializer.Deserialize<ElecTriXSettings>(json, opts);
                return s ?? new ElecTriXSettings();
            }
            catch (Exception ex)
            {
                LastError = "Settings-Laden: " + ex.Message;
                return new ElecTriXSettings();
            }
        }

        /// <summary>Speichert Settings als hübsch formatiertes JSON.</summary>
        public static bool Speichern(ElecTriXSettings s)
        {
            LastError = "";
            try
            {
                var opts = new JsonSerializerOptions
                {
                    WriteIndented = true,
                    Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
                };
                var json = JsonSerializer.Serialize(s ?? new ElecTriXSettings(), opts);
                File.WriteAllText(GetSettingsPath(), json);
                return true;
            }
            catch (Exception ex)
            {
                LastError = "Settings-Speichern: " + ex.Message;
                return false;
            }
        }

        /// <summary>Öffnet die Settings-Datei im Standard-Editor (Notepad).</summary>
        public static void OeffneInEditor()
        {
            var path = GetSettingsPath();
            if (!File.Exists(path)) Speichern(new ElecTriXSettings());
            try
            {
                var psi = new System.Diagnostics.ProcessStartInfo(path) { UseShellExecute = true };
                System.Diagnostics.Process.Start(psi);
            }
            catch (Exception ex) { LastError = "Editor öffnen: " + ex.Message; }
        }
    }

    /// <summary>
    /// Hilfsmethoden die Templates mit konkreten Werten auffüllen.
    /// </summary>
    public static class TemplateEngine
    {
        /// <summary>
        /// Ersetzt Platzhalter in <paramref name="template"/>. Unbekannte Platzhalter
        /// bleiben stehen. Leere Werte werden durch leeren String ersetzt.
        /// </summary>
        public static string Fuelle(string template, Dictionary<string, string> werte)
        {
            if (string.IsNullOrEmpty(template) || werte == null) return template ?? "";
            var result = template;
            foreach (var kv in werte)
            {
                result = result.Replace("{" + kv.Key + "}", kv.Value ?? "");
            }
            return result;
        }
    }
}

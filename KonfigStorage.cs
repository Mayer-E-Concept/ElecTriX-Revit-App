using System;
using System.IO;
using System.Text.RegularExpressions;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.ExtensibleStorage;

namespace METools.FamilyPlacer
{
    /// <summary>
    /// Persistiert die Circuit-Konfiguration robust, doppelt:
    ///   1. ExtensibleStorage am ProjectInformation-Element (wandert mit der RVT)
    ///   2. Backup als Datei in %APPDATA%\METools\configs\{project}.circuits.json
    ///      (überlebt auch RVT-nicht-gespeichert-Fall)
    ///
    /// Beim Laden wird immer die neuere Quelle bevorzugt (Timestamp-Vergleich).
    /// Beim Speichern wird nach dem Commit sofort verifiziert.
    /// </summary>
    public static class KonfigStorage
    {
        private static readonly Guid SCHEMA_GUID = new Guid("8D4B7C2F-3A91-4E5F-9D1B-AE2025C1C17A");
        private const string SCHEMA_NAME   = "METoolsCircuitConfig";
        private const string FIELD_JSON    = "ConfigJson";
        private const string FIELD_TS      = "Timestamp";
        private const string OLD_PARAM     = "SK_Konfiguration";

        public static string LastError { get; private set; } = "";
        public static string LastInfo  { get; private set; } = "";

        // ── Schema ──────────────────────────────────────────────────────
        private static Schema GetOrCreateSchema()
        {
            var existing = Schema.Lookup(SCHEMA_GUID);
            if (existing != null) return existing;

            var builder = new SchemaBuilder(SCHEMA_GUID);
            builder.SetSchemaName(SCHEMA_NAME);
            builder.SetReadAccessLevel(AccessLevel.Public);
            builder.SetWriteAccessLevel(AccessLevel.Public);
            builder.SetVendorId("MayerEConceptSRL");
            builder.AddSimpleField(FIELD_JSON, typeof(string));
            builder.AddSimpleField(FIELD_TS,   typeof(string));
            return builder.Finish();
        }

        // ── Backup-Datei-Pfad ──────────────────────────────────────────
        public static string GetBackupPath(Document doc)
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            var dir = Path.Combine(appData, "METools", "configs");
            try { Directory.CreateDirectory(dir); } catch { }

            string name;
            try { name = Path.GetFileNameWithoutExtension(doc.PathName); }
            catch { name = null; }
            if (string.IsNullOrWhiteSpace(name))
            {
                try { name = doc.Title; } catch { name = "unsaved"; }
            }
            // Ungültige Dateinamen-Zeichen entfernen
            name = Regex.Replace(name ?? "unsaved", @"[^\w\-\. ]", "_");
            return Path.Combine(dir, name + ".circuits.json");
        }

        // ── Laden ───────────────────────────────────────────────────────
        /// <summary>
        /// Liest in dieser Reihenfolge: ES, Datei-Backup, alter Parameter.
        /// Bei Konflikt: neuere Timestamp gewinnt.
        /// </summary>
        public static string Laden(Document doc)
        {
            LastError = "";
            LastInfo  = "";
            if (doc == null) return null;

            string esJson = null; DateTime esTs = DateTime.MinValue;
            string fiJson = null; DateTime fiTs = DateTime.MinValue;

            // 1) ExtensibleStorage
            try
            {
                var schema = Schema.Lookup(SCHEMA_GUID);
                if (schema != null && doc.ProjectInformation != null)
                {
                    var entity = doc.ProjectInformation.GetEntity(schema);
                    if (entity != null && entity.IsValid())
                    {
                        esJson = entity.Get<string>(FIELD_JSON);
                        var ts = entity.Get<string>(FIELD_TS);
                        DateTime.TryParse(ts, out esTs);
                    }
                }
            }
            catch (Exception ex) { LastError = "ES-Laden: " + ex.Message; }

            // 2) Datei-Backup
            try
            {
                var path = GetBackupPath(doc);
                if (File.Exists(path))
                {
                    fiJson = File.ReadAllText(path);
                    fiTs   = File.GetLastWriteTimeUtc(path);
                }
            }
            catch (Exception ex)
            {
                if (string.IsNullOrEmpty(LastError))
                    LastError = "Backup-Laden: " + ex.Message;
            }

            // Vergleich: neuere Quelle gewinnt
            if (!string.IsNullOrWhiteSpace(esJson) && !string.IsNullOrWhiteSpace(fiJson))
            {
                LastInfo = esTs >= fiTs ? "Geladen aus ES (neuer)" : "Geladen aus Backup-Datei (neuer)";
                return esTs >= fiTs ? esJson : fiJson;
            }
            if (!string.IsNullOrWhiteSpace(esJson)) { LastInfo = "Geladen aus ES"; return esJson; }
            if (!string.IsNullOrWhiteSpace(fiJson)) { LastInfo = "Geladen aus Backup-Datei"; return fiJson; }

            // 3) Alt-Parameter
            try
            {
                var param = doc.ProjectInformation?.LookupParameter(OLD_PARAM);
                var json  = param?.AsString();
                if (!string.IsNullOrWhiteSpace(json))
                {
                    LastInfo = "Migriert aus altem Parameter";
                    return json;
                }
            }
            catch { }

            LastInfo = "Nichts gespeichert gefunden";
            return null;
        }

        // ── Speichern ───────────────────────────────────────────────────
        /// <summary>
        /// Schreibt parallel in ES und Backup-Datei. Verifiziert danach
        /// durch sofortiges Re-Lesen, dass die Daten angekommen sind.
        /// </summary>
        public static bool Speichern(Document doc, string json)
        {
            LastError = "";
            LastInfo  = "";

            if (doc == null)              { LastError = "Kein aktives Dokument"; return false; }
            if (doc.IsReadOnly)           { LastError = "Dokument ist schreibgeschützt"; return false; }
            if (doc.ProjectInformation == null) { LastError = "ProjectInformation nicht verfügbar"; return false; }

            var timestamp = DateTime.UtcNow.ToString("o");
            bool esOk = false;
            bool fileOk = false;

            // 1) ExtensibleStorage
            try
            {
                var schema = GetOrCreateSchema();
                using (var tx = new Transaction(doc, "ME-Tools: SK-Konfig speichern"))
                {
                    tx.Start();
                    var entity = new Entity(schema);
                    entity.Set<string>(FIELD_JSON, json ?? "");
                    entity.Set<string>(FIELD_TS,   timestamp);
                    doc.ProjectInformation.SetEntity(entity);
                    var status = tx.Commit();
                    esOk = status == TransactionStatus.Committed;
                    if (!esOk) LastError = "ES-Transaction: " + status;
                }
            }
            catch (Exception ex)
            {
                LastError = "ES-Schreiben: " + ex.GetType().Name + ": " + ex.Message;
            }

            // 2) Datei-Backup (immer versuchen, auch wenn ES fehlgeschlagen)
            try
            {
                var path = GetBackupPath(doc);
                File.WriteAllText(path, json ?? "");
                fileOk = true;
            }
            catch (Exception ex)
            {
                if (string.IsNullOrEmpty(LastError))
                    LastError = "Backup-Schreiben: " + ex.Message;
            }

            // 3) Verifikation: sofort re-lesen
            if (esOk || fileOk)
            {
                try
                {
                    var reread = Laden(doc);
                    if (string.IsNullOrWhiteSpace(reread))
                    {
                        LastError = "Nach Save konnten keine Daten gelesen werden — Speichern hat nicht funktioniert.";
                        return false;
                    }
                    if (reread.Length != (json ?? "").Length)
                    {
                        LastInfo = "Gespeichert — aber re-read hatte andere Länge (" +
                                   reread.Length + " vs " + (json ?? "").Length + ")";
                    }
                }
                catch (Exception ex) { LastError = "Verify: " + ex.Message; return false; }
            }

            if (esOk && fileOk)
            {
                LastInfo = "Gespeichert: ES + Backup-Datei";
                return true;
            }
            if (fileOk)
            {
                LastInfo = "Gespeichert: nur Backup-Datei (ES fehlgeschlagen — RVT speichern empfohlen)";
                return true;   // Datei-Backup reicht für Persistenz
            }
            if (esOk)
            {
                LastInfo = "Gespeichert: nur ES (Backup-Datei fehlgeschlagen)";
                return true;
            }

            return false;
        }
    }
}

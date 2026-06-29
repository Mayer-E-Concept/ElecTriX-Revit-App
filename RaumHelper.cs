using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace METools.FamilyPlacer
{
    public static class RaumHelper
    {
        // ─── Synonymtabelle ───────────────────────────────────────────
        private static readonly Dictionary<string, List<string>> Synonyme = new(StringComparer.OrdinalIgnoreCase)
        {
            ["Flur"]          = new() { "flur","diele","eingang","korridor","vorraum","gang","windfang" },
            ["Wohnen"]        = new() { "wohnen","wohnzimmer","wohnraum","wohnen/essen","wohn/ess","wohn-ess","living" },
            ["Schlafen"]      = new() { "schlafen","schlafzimmer","schlafraum","sz","master bedroom","elternschlafzimmer" },
            ["Küche"]         = new() { "küche","kueche","kü","ku","kitchen","teeküche","teeküche" },
            ["Bad"]           = new() { "bad","badezimmer","duschbad","wc","dusche","nassraum","bathroom","gäste-wc","gästewc","gäste wc" },
            ["Kinderzimmer"]  = new() { "kind","kinderzimmer","kiz","kinderraum","ki","kinder","jugendraum" },
            ["Abstellraum"]   = new() { "abstellraum","abstell","hwr","hauswirtschaft","hauswirtschaftsraum","lager","ar","abst","nebenraum" },
            ["Balkon"]        = new() { "balkon","loggia","terrasse","dachterrasse","balkon/loggia" },
            ["Büro"]          = new() { "büro","arbeitszimmer","homeoffice","office","arbeit" },
            ["Esszimmer"]     = new() { "esszimmer","essen","dining","speisezimmer" },
        };

        // ─── Sonderkürzel-Bezeichnungen ──────────────────────────────
        public static readonly Dictionary<string, string> SonderKuerzelBezeichnung = new(StringComparer.OrdinalIgnoreCase)
        {
            ["KS"] = "Kochstelle",
            ["BO"] = "Backofen",
            ["WM"] = "Waschmaschine",
            ["TR"] = "Trockner",
            ["GS"] = "Geschirrspüler",
            ["KU"] = "Kühlschrank",
        };

        public static readonly HashSet<string> StandardKuerzel = new(StringComparer.OrdinalIgnoreCase)
        {
            "KS","BO","WM","TR","GS","KU"
        };

        /// <summary>
        /// Gibt den kanonischen Raumtyp zurück (z.B. "SZ" → "Schlafen").
        /// Wenn kein Synonym gefunden: Original zurückgeben.
        /// </summary>
        public static string Normalisiere(string raumName)
        {
            if (string.IsNullOrWhiteSpace(raumName)) return raumName;
            var clean = raumName.Trim();

            foreach (var kvp in Synonyme)
                if (kvp.Value.Contains(clean, StringComparer.OrdinalIgnoreCase)
                    || kvp.Key.Equals(clean, StringComparison.OrdinalIgnoreCase))
                    return kvp.Key;

            return clean;
        }

        /// <summary>
        /// Gibt die bekannten Synonyme für einen kanonischen Namen zurück.
        /// </summary>
        public static List<string> GetSynonyme(string kanonisch)
        {
            if (Synonyme.TryGetValue(kanonisch, out var liste))
                return liste.Select(s => char.ToUpper(s[0]) + s[1..]).Take(3).ToList();
            return new();
        }
    }

    // ─── Schema-Erkennung ─────────────────────────────────────────────
    public static class SchemaHelper
    {
        public enum SchemaTyp
        {
            Unbekannt,
            HausWohnungNummer,   // 1.3.2
            WohnungNummer,       // 1.01
            WohnungPrefix,       // W01-Flur
            NummerKompakt,       // 101, 201
            NurNummer            // 1, 2, 3
        }

        public class SchemaErgebnis
        {
            public SchemaTyp Typ { get; set; }
            public string Beschreibung { get; set; }
            public string Beispiel { get; set; }
            public Func<string, string> WohnungsIdExtraktor { get; set; }
            public double Konfidenz { get; set; }
        }

        private static readonly List<(SchemaTyp Typ, Regex Muster, string Beschreibung, string Beispiel, Func<Match, string> Extraktor)> Schemata = new()
        {
            (
                SchemaTyp.HausWohnungNummer,
                new Regex(@"^(\d+)\.(\d+)\.(\d+)$"),
                "Haus.Wohnung.Nummer",
                "1.3.2 → Haus 1, Wohnung 3",
                m => $"W-{m.Groups[2].Value.PadLeft(2,'0')}"
            ),
            (
                SchemaTyp.WohnungNummer,
                new Regex(@"^(\d+)\.(\d{2,})$"),
                "Wohnung.Nummer",
                "1.01 → Wohnung 1",
                m => $"W-{m.Groups[1].Value.PadLeft(2,'0')}"
            ),
            (
                SchemaTyp.WohnungPrefix,
                new Regex(@"^(W\d+)[-\s]", RegexOptions.IgnoreCase),
                "Wohnungs-Prefix",
                "W01-Flur → Wohnung W01",
                m => m.Groups[1].Value.ToUpper()
            ),
            (
                SchemaTyp.NummerKompakt,
                new Regex(@"^(\d)(\d{2})$"),
                "Kompaktnummer",
                "101 → Wohnung 1",
                m => $"W-{m.Groups[1].Value.PadLeft(2,'0')}"
            ),
        };

        /// <summary>
        /// Analysiert alle Raumnummern und erkennt das Schema automatisch.
        /// </summary>
        public static SchemaErgebnis ErkenneSchema(IEnumerable<string> raumnummern)
        {
            var nummern = raumnummern.Where(n => !string.IsNullOrWhiteSpace(n)).ToList();
            if (nummern.Count == 0)
                return new SchemaErgebnis { Typ = SchemaTyp.Unbekannt, Beschreibung = "No room numbers found", Konfidenz = 0 };

            foreach (var schema in Schemata)
            {
                int treffer = nummern.Count(n => schema.Muster.IsMatch(n.Trim()));
                double konfidenz = (double)treffer / nummern.Count;

                if (konfidenz >= 0.6)
                {
                    return new SchemaErgebnis
                    {
                        Typ = schema.Typ,
                        Beschreibung = schema.Beschreibung,
                        Beispiel = schema.Beispiel,
                        Konfidenz = konfidenz,
                        WohnungsIdExtraktor = num =>
                        {
                            var m = schema.Muster.Match(num.Trim());
                            return m.Success ? schema.Extraktor(m) : "W-??";
                        }
                    };
                }
            }

            return new SchemaErgebnis
            {
                Typ = SchemaTyp.Unbekannt,
                Beschreibung = "Schema not detected — assign manually",
                Konfidenz = 0,
                WohnungsIdExtraktor = _ => "W-??"
            };
        }

        /// <summary>Extrahiert Wohnungs-ID aus einer Raumnummer mit bekanntem Schema.</summary>
        public static string ExtrahiereWohnungsId(string raumnummer, SchemaErgebnis schema)
        {
            if (schema?.WohnungsIdExtraktor == null || string.IsNullOrWhiteSpace(raumnummer))
                return "W-??";
            return schema.WohnungsIdExtraktor(raumnummer);
        }
    }

    // ─── Projektparameter Helper ──────────────────────────────────────
    public static class ParameterHelper
    {
        public const string KonfigParamName = "SK_Konfiguration";

        public static string LeseParameter(Autodesk.Revit.DB.Document doc)
        {
            try
            {
                var param = doc.ProjectInformation.LookupParameter(KonfigParamName);
                return param?.AsString() ?? string.Empty;
            }
            catch { return string.Empty; }
        }

        public static bool SchreibeParameter(Autodesk.Revit.DB.Document doc, string json)
        {
            try
            {
                using var tx = new Autodesk.Revit.DB.Transaction(doc, "SK-Konfiguration speichern");
                tx.Start();
                var param = doc.ProjectInformation.LookupParameter(KonfigParamName);
                if (param == null || param.IsReadOnly) { tx.RollBack(); return false; }
                param.Set(json);
                tx.Commit();
                return true;
            }
            catch { return false; }
        }
    }
}

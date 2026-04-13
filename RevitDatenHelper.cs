using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Electrical;


namespace METools.FamilyPlacer
{
    public static class RevitDatenHelper
    {
        private const string PARAM_SONDERSTECKDOSE = "Sondersteckdose";

        // ─── Räume ────────────────────────────────────────────────────
        public static List<(string Nummer, string Name)> LeseAlleRaeume(Document doc)
        {
            return new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_Rooms)
                .WhereElementIsNotElementType()
                .Cast<Autodesk.Revit.DB.Architecture.Room>()
                .Where(r => r.Area > 0)
                .Select(r =>
                {
                    var nummer = r.get_Parameter(BuiltInParameter.ROOM_NUMBER)?.AsString() ?? "";
                    var name   = r.get_Parameter(BuiltInParameter.ROOM_NAME)?.AsString() ?? "";
                    return (nummer, name);
                })
                .ToList();
        }

        public static List<string> LeseEinzigartigeRaumtypen(Document doc)
        {
            return LeseAlleRaeume(doc)
                .Select(r => RaumHelper.Normalisiere(r.Name))
                .Where(n => !string.IsNullOrWhiteSpace(n))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(n => n)
                .ToList();
        }

        public static List<string> LeseAlleRaumnummern(Document doc)
        {
            return LeseAlleRaeume(doc)
                .Select(r => r.Nummer)
                .Where(n => !string.IsNullOrWhiteSpace(n))
                .Distinct()
                .ToList();
        }

        // ─── Sondersteckdosen ─────────────────────────────────────────
        public static HashSet<string> LeseSonderkuerzel(Document doc)
        {
            var kategorien = new[]
            {
                BuiltInCategory.OST_ElectricalFixtures,
                BuiltInCategory.OST_ElectricalEquipment,
            };

            var kuerzel = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var kat in kategorien)
            {
                var elemente = new FilteredElementCollector(doc)
                    .OfCategory(kat)
                    .WhereElementIsNotElementType()
                    .ToElements();

                foreach (var el in elemente)
                {
                    var param = el.LookupParameter(PARAM_SONDERSTECKDOSE);
                    var wert  = param?.AsString()?.Trim().ToUpper();
                    if (!string.IsNullOrWhiteSpace(wert))
                        kuerzel.Add(wert);
                }
            }

            // Standardkürzel immer einschließen
            foreach (var k in RaumHelper.StandardKuerzel)
                kuerzel.Add(k);

            return kuerzel;
        }

        // ─── Verteiler ────────────────────────────────────────────────
        public static List<(string Id, string Name, XYZ Position)> LeseVerteiler(Document doc)
        {
            return new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_ElectricalEquipment)
                .WhereElementIsNotElementType()
                .Where(el =>
                {
                    var name = el.Name ?? "";
                    return name.StartsWith("V-", StringComparison.OrdinalIgnoreCase)
                        || name.StartsWith("V_", StringComparison.OrdinalIgnoreCase)
                        || name.ToUpper().Contains("VERTEILER")
                        || name.ToUpper().Contains("VTLR");
                })
                .Select(el =>
                {
                    var pos = (el.Location as LocationPoint)?.Point ?? XYZ.Zero;
                    return (el.UniqueId, el.Name, pos);
                })
                .ToList();
        }

        /// <summary>
        /// Versucht für jeden Verteiler den Raum/Wohnung automatisch zu ermitteln
        /// (Verteiler liegt im Flur der Wohnung → GetRoomAtPoint).
        /// </summary>
        public static List<VerteilerzuordnungEintrag> ErmittleVerteilerzuordnung(
            Document doc,
            SchemaHelper.SchemaErgebnis schema)
        {
            var verteiler = LeseVerteiler(doc);
            var raeume    = LeseAlleRaeume(doc);
            var ergebnis  = new List<VerteilerzuordnungEintrag>();

            // Phase für GetRoomAtPoint
            Phase phase = null;
            try
            {
                var phases = new FilteredElementCollector(doc)
                    .OfClass(typeof(Phase))
                    .Cast<Phase>()
                    .ToList();
                phase = phases.LastOrDefault();
            }
            catch { }

            foreach (var (id, name, pos) in verteiler)
            {
                string wohnungsId = "W-??";
                bool automatisch  = false;

                // Versuche Raum am Verteiler-Standort zu finden
                if (phase != null && pos != XYZ.Zero)
                {
                    try
                    {
                        var raumAmPunkt = doc.GetRoomAtPoint(pos, phase);
                        if (raumAmPunkt != null)
                        {
                            var nummer = raumAmPunkt
                                .get_Parameter(BuiltInParameter.ROOM_NUMBER)?.AsString() ?? "";
                            wohnungsId   = SchemaHelper.ExtrahiereWohnungsId(nummer, schema);
                            automatisch  = wohnungsId != "W-??";
                        }
                    }
                    catch { }
                }

                // Räume dieser Wohnung zusammenstellen
                var raumNamenDerWohnung = raeume
                    .Where(r =>
                    {
                        var wid = SchemaHelper.ExtrahiereWohnungsId(r.Nummer, schema);
                        return wid == wohnungsId;
                    })
                    .Select(r => RaumHelper.Normalisiere(r.Name))
                    .Distinct()
                    .ToList();

                ergebnis.Add(new VerteilerzuordnungEintrag
                {
                    WohnungsId          = wohnungsId,
                    VerteilerId         = id,
                    VerteilerName       = name,
                    Raeume              = raumNamenDerWohnung,
                    AutomatischErkannt  = automatisch,
                });
            }

            return ergebnis.OrderBy(e => e.WohnungsId).ToList();
        }
    }
}

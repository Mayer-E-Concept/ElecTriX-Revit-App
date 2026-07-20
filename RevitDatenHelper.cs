using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Electrical;

namespace METools.FamilyPlacer
{
    /// <summary>Detaillierte Info zu einem Verteiler im Modell.</summary>
    public class VerteilerDetails
    {
        public ElementId ElementId          { get; set; }
        public string    Name               { get; set; }
        public string    CircuitPrefix      { get; set; }
        public string    CircuitPrefixSeparator { get; set; }
        public string    NamingSchemeName   { get; set; }
        public ElementId NamingSchemeId     { get; set; }
        public XYZ       Position           { get; set; }
    }

    /// <summary>Circuit Naming Scheme im Projekt.</summary>
    public class NamingSchemeInfo
    {
        public ElementId Id   { get; set; }
        public string    Name { get; set; }
    }

    public static class RevitDatenHelper
    {
        private const string PARAM_SONDERSTECKDOSE = "Sondersteckdose";

        // Parameter-IDs aus Revit API (verifiziert via Nonica)
        private const int PARAM_CIRCUIT_PREFIX            = -1140085;
        private const int PARAM_CIRCUIT_PREFIX_SEPARATOR  = -1140086;
        private const int PARAM_CIRCUIT_NAMING            = -1140087;

        // ─── Räume + MEP-Spaces ───────────────────────────────────────
        public static List<(string Nummer, string Name)> LeseAlleRaeume(Document doc)
        {
            var list = new List<(string, string)>();

            // Architektur-Räume
            try
            {
                foreach (var r in new FilteredElementCollector(doc)
                    .OfCategory(BuiltInCategory.OST_Rooms)
                    .WhereElementIsNotElementType()
                    .Cast<Autodesk.Revit.DB.Architecture.Room>()
                    .Where(r => r.Area > 0))
                {
                    var nummer = r.get_Parameter(BuiltInParameter.ROOM_NUMBER)?.AsString() ?? "";
                    var name   = r.get_Parameter(BuiltInParameter.ROOM_NAME)?.AsString() ?? "";
                    if (!string.IsNullOrWhiteSpace(nummer) || !string.IsNullOrWhiteSpace(name))
                        list.Add((nummer, name));
                }
            }
            catch { }

            // MEP-Spaces
            try
            {
                foreach (var el in new FilteredElementCollector(doc)
                    .OfCategory(BuiltInCategory.OST_MEPSpaces)
                    .WhereElementIsNotElementType())
                {
                    var sp = el as Autodesk.Revit.DB.Mechanical.Space;
                    if (sp == null || sp.Area <= 0) continue;
                    var nummer = sp.get_Parameter(BuiltInParameter.ROOM_NUMBER)?.AsString() ?? "";
                    var name   = sp.get_Parameter(BuiltInParameter.ROOM_NAME)?.AsString() ?? "";
                    if (!string.IsNullOrWhiteSpace(nummer) || !string.IsNullOrWhiteSpace(name))
                        list.Add((nummer, name));
                }
            }
            catch { }

            return list;
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
            return LeseAlleRaumnummern(LeseAlleRaeume(doc));
        }

        // Same logic as above, but against an already-fetched room list — avoids a
        // second full Rooms+MEPSpaces scan when a caller (e.g. KonfigViewModel.LadeDaten)
        // already needs the raw room list for something else too.
        public static List<string> LeseAlleRaumnummern(List<(string Nummer, string Name)> raeume)
        {
            return raeume
                .Select(r => r.Nummer)
                .Where(n => !string.IsNullOrWhiteSpace(n))
                .Distinct()
                .ToList();
        }

        // ─── Diagnose ────────────────────────────────────────────────
        public class DiagnoseErgebnis
        {
            public int    AnzahlRaeume      { get; set; }
            public int    AnzahlMEPSpaces   { get; set; }
            public int    UnplatzierteRaum { get; set; }
            public int    GesamtPlatziert   { get; set; }
            public bool   AllesDefaultName  { get; set; }
            public string KurzText          { get; set; } = "";
            public string DetailText        { get; set; } = "";
        }

        // raeumeVorgefetcht: optional pre-fetched room list (see KonfigViewModel.LadeDaten,
        // which already needs this same data right after calling this method) -- avoids
        // this method also calling LeseAlleRaeume internally, which would otherwise be a
        // second full Rooms+MEPSpaces scan on top of the counting scan just above it.
        // The counting scan itself (rooms/spaces below) stays separate regardless, since
        // it needs the raw Room/Space objects for the placed/unplaced breakdown, which
        // LeseAlleRaeume's already-filtered (Nummer, Name) tuples don't carry.
        public static DiagnoseErgebnis ErstelleDiagnose(Document doc, List<(string Nummer, string Name)> raeumeVorgefetcht = null)
        {
            var d = new DiagnoseErgebnis();

            try
            {
                var rooms = new FilteredElementCollector(doc)
                    .OfCategory(BuiltInCategory.OST_Rooms)
                    .WhereElementIsNotElementType()
                    .Cast<Autodesk.Revit.DB.Architecture.Room>().ToList();
                d.AnzahlRaeume       = rooms.Count(r => r.Area > 0);
                d.UnplatzierteRaum   = rooms.Count(r => r.Area <= 0);
            }
            catch { }

            try
            {
                var spaces = new FilteredElementCollector(doc)
                    .OfCategory(BuiltInCategory.OST_MEPSpaces)
                    .WhereElementIsNotElementType().ToList();
                d.AnzahlMEPSpaces = spaces.Count(s => (s as Autodesk.Revit.DB.Mechanical.Space)?.Area > 0);
            }
            catch { }

            d.GesamtPlatziert = d.AnzahlRaeume + d.AnzahlMEPSpaces;

            var alle = raeumeVorgefetcht ?? LeseAlleRaeume(doc);
            d.AllesDefaultName = alle.Count > 0
                && alle.All(r => string.IsNullOrWhiteSpace(r.Name)
                              || r.Name.Equals("Raum", StringComparison.OrdinalIgnoreCase)
                              || r.Name.Equals("Room", StringComparison.OrdinalIgnoreCase));

            if (d.GesamtPlatziert == 0)
                d.KurzText = "No rooms or MEP spaces in the model";
            else if (d.AnzahlRaeume > 0 && d.AnzahlMEPSpaces > 0)
                d.KurzText = $"{d.AnzahlRaeume} rooms + {d.AnzahlMEPSpaces} MEP spaces";
            else if (d.AnzahlRaeume > 0)
                d.KurzText = $"{d.AnzahlRaeume} rooms found";
            else
                d.KurzText = $"{d.AnzahlMEPSpaces} MEP spaces found";

            if (d.UnplatzierteRaum > 0)
                d.KurzText += $" ({d.UnplatzierteRaum} unplaced ignored)";

            d.DetailText = $"Rooms: {d.AnzahlRaeume}, MEP spaces: {d.AnzahlMEPSpaces}";
            return d;
        }

        // ─── Sondersteckdosen ─────────────────────────────────────────
        public static HashSet<string> LeseSonderkuerzel(Document doc)
        {
            var kat = new[] { BuiltInCategory.OST_ElectricalFixtures, BuiltInCategory.OST_ElectricalEquipment };
            var k = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var c in kat)
            {
                try
                {
                    foreach (var el in new FilteredElementCollector(doc).OfCategory(c)
                        .WhereElementIsNotElementType().ToElements())
                    {
                        var p = el.LookupParameter(PARAM_SONDERSTECKDOSE);
                        var w = p?.AsString()?.Trim().ToUpperInvariant();
                        if (!string.IsNullOrWhiteSpace(w)) k.Add(w);
                    }
                }
                catch { }
            }
            foreach (var s in RaumHelper.StandardKuerzel) k.Add(s);
            return k;
        }

        // ─── Verteiler mit allen Details ──────────────────────────────
        /// <summary>
        /// Liest alle Verteiler mit Panel-Name (user-definiert), Circuit Prefix,
        /// Separator und aktuell zugewiesenem Naming Scheme. Toleranter Filter:
        /// akzeptiert alle FamilyInstances in OST_ElectricalEquipment.
        /// </summary>
        public static List<VerteilerDetails> LeseVerteilerDetails(Document doc)
        {
            const int PARAM_PANEL_NAME = -1140078;  // RBS_ELEC_PANEL_NAME_PARAM

            var result = new List<VerteilerDetails>();
            try
            {
                foreach (var fi in new FilteredElementCollector(doc)
                    .OfCategory(BuiltInCategory.OST_ElectricalEquipment)
                    .WhereElementIsNotElementType()
                    .OfClass(typeof(FamilyInstance))
                    .Cast<FamilyInstance>())
                {
                    // Panel Name-Parameter (das ist der *echte* Verteilername wie im
                    // Eigenschafteneditor sichtbar) — fallback auf fi.Name
                    string panelName = LesePrefix(fi, PARAM_PANEL_NAME, "Panel Name");
                    if (string.IsNullOrWhiteSpace(panelName)) panelName = fi.Name ?? "";

                    string circuitPrefix          = LesePrefix(fi, PARAM_CIRCUIT_PREFIX,           "Circuit Prefix");
                    string circuitPrefixSeparator = LesePrefix(fi, PARAM_CIRCUIT_PREFIX_SEPARATOR, "Circuit Prefix Separator");

                    // Heuristik: echter Verteiler braucht entweder Circuit Prefix, Panel Name,
                    // oder ist mindestens eine ElectricalEquipment-Instanz (toleranter Filter)
                    bool istEchterVerteiler =
                        !string.IsNullOrWhiteSpace(circuitPrefix)
                        || !string.IsNullOrWhiteSpace(panelName)
                        || (fi.MEPModel is ElectricalEquipment);
                    if (!istEchterVerteiler) continue;

                    var d = new VerteilerDetails
                    {
                        ElementId              = fi.Id,
                        Name                   = panelName,
                        CircuitPrefix          = circuitPrefix,
                        CircuitPrefixSeparator = circuitPrefixSeparator,
                        Position               = (fi.Location as LocationPoint)?.Point ?? XYZ.Zero,
                    };

                    // Naming-Scheme (Param ist ElementId-Typ)
                    try
                    {
                        Parameter p = null;
                        try { p = fi.get_Parameter((BuiltInParameter)PARAM_CIRCUIT_NAMING); } catch { }
                        if (p == null) p = fi.LookupParameter("Circuit Naming");
                        if (p != null && p.StorageType == StorageType.ElementId)
                        {
                            var sid = p.AsElementId();
                            if (sid != null && sid.IntegerValue > 0)
                            {
                                d.NamingSchemeId = sid;
                                var scheme = doc.GetElement(sid);
                                d.NamingSchemeName = scheme?.Name ?? "";
                            }
                        }
                    }
                    catch { }

                    result.Add(d);
                }
            }
            catch { }
            return result;
        }

        private static string LesePrefix(FamilyInstance fi, int paramId, string nameFallback = null)
        {
            try
            {
                Parameter p = null;
                try { p = fi.get_Parameter((BuiltInParameter)paramId); } catch { }
                if (p == null && !string.IsNullOrEmpty(nameFallback))
                    p = fi.LookupParameter(nameFallback);
                return p?.AsString() ?? "";
            }
            catch { return ""; }
        }

        // ─── Stromkreise ──────────────────────────────────────────────
        /// <summary>Info zu einem bestehenden Stromkreis im Modell.</summary>
        public class StromkreisInfo
        {
            public ElementId    Id                 { get; set; }
            public string       LoadName           { get; set; } = "";
            public string       CircuitNumber      { get; set; } = "";
            public string       LoadClassification { get; set; } = "";
            public string       PanelName          { get; set; } = "";
            public List<string> Raumnummern        { get; set; } = new List<string>();
            public int          AnzahlBauteile     { get; set; }
        }

        /// <summary>
        /// Liest alle ElectricalSystems (Power-Circuits) im Dokument.
        /// Liefert Display-Info zur Anzeige im Panels-Tab.
        /// </summary>
        public static List<StromkreisInfo> LeseAlleStromkreise(Document doc)
        {
            // Parameter-IDs aus Revit 2025 (verifiziert via Nonica auf Circuit 28590951)
            const int PARAM_CIRCUIT_NUMBER         = -1140103;  // Circuit Number
            const int PARAM_LOAD_NAME              = -1140089;  // Load Name
            const int PARAM_LOAD_CLASSIFICATION    = -1140120;  // Load Classification
            const int PARAM_CIRCUIT_PANEL          = -1140104;  // Panel

            var result = new List<StromkreisInfo>();
            try
            {
                foreach (var sys in new FilteredElementCollector(doc)
                    .OfClass(typeof(ElectricalSystem))
                    .Cast<ElectricalSystem>())
                {
                    if (sys.SystemType != ElectricalSystemType.PowerCircuit) continue;

                    var info = new StromkreisInfo
                    {
                        Id                 = sys.Id,
                        LoadName           = LeseStringParam(sys, PARAM_LOAD_NAME,           "Load Name"),
                        CircuitNumber      = LeseStringParam(sys, PARAM_CIRCUIT_NUMBER,      "Circuit Number"),
                        LoadClassification = LeseStringParam(sys, PARAM_LOAD_CLASSIFICATION, "Load Classification"),
                        PanelName          = LeseStringParam(sys, PARAM_CIRCUIT_PANEL,       "Panel"),
                    };

                    // Räume aus den Bauteilen sammeln
                    try
                    {
                        var raumSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                        foreach (Element el in sys.Elements)
                        {
                            info.AnzahlBauteile++;
                            if (el is FamilyInstance fi)
                            {
                                string rn = "";
                                try { rn = fi.Space?.get_Parameter(BuiltInParameter.ROOM_NUMBER)?.AsString() ?? ""; } catch { }
                                if (string.IsNullOrWhiteSpace(rn))
                                    try { rn = fi.Room?.get_Parameter(BuiltInParameter.ROOM_NUMBER)?.AsString() ?? ""; } catch { }
                                if (!string.IsNullOrWhiteSpace(rn)) raumSet.Add(rn);
                            }
                        }
                        info.Raumnummern.AddRange(raumSet);
                    }
                    catch { }

                    result.Add(info);
                }
            }
            catch { }
            return result.OrderBy(s => s.PanelName, StringComparer.OrdinalIgnoreCase)
                         .ThenBy(s => s.LoadName, StringComparer.OrdinalIgnoreCase)
                         .ToList();
        }

        /// <summary>
        /// Liest einen String-Parameter über BuiltInParameter-Cast, mit LookupParameter-Fallback.
        /// Robustes Pattern für Parameter die je nach Revit-Version unterschiedlich heißen.
        /// </summary>
        private static string LeseStringParam(Element el, int paramId, string nameFallback)
        {
            try
            {
                Parameter p = null;
                try { p = el.get_Parameter((BuiltInParameter)paramId); } catch { }
                if (p == null && !string.IsNullOrEmpty(nameFallback))
                    p = el.LookupParameter(nameFallback);
                if (p == null) return "";
                if (p.StorageType == StorageType.String) return p.AsString() ?? "";
                return p.AsValueString() ?? "";
            }
            catch { return ""; }
        }

        // ─── Circuit Naming Schemes ───────────────────────────────────
        /// <summary>
        /// Listet alle im Projekt definierten Circuit Naming Schemes.
        /// </summary>
        public static List<NamingSchemeInfo> LeseVerfuegbareNamingSchemes(Document doc)
        {
            var result = new List<NamingSchemeInfo>();
            try
            {
                foreach (var el in new FilteredElementCollector(doc)
                    .OfClass(typeof(CircuitNamingScheme)))
                {
                    result.Add(new NamingSchemeInfo { Id = el.Id, Name = el.Name ?? "" });
                }
            }
            catch { }
            return result.OrderBy(s => s.Name, StringComparer.OrdinalIgnoreCase).ToList();
        }

        // ─── Verteilerzuordnung (bestehend, minimal angepasst) ────────
        public static List<(string Id, string Name, XYZ Position)> LeseVerteiler(Document doc)
        {
            return LeseVerteilerDetails(doc)
                .Select(v => (v.ElementId.IntegerValue.ToString(), v.Name, v.Position))
                .ToList();
        }

        public static List<VerteilerzuordnungEintrag> ErmittleVerteilerzuordnung(
            Document doc, SchemaHelper.SchemaErgebnis schema)
        {
            var verteiler = LeseVerteiler(doc);
            var raeume    = LeseAlleRaeume(doc);
            var ergebnis  = new List<VerteilerzuordnungEintrag>();

            Phase phase = null;
            try { phase = new FilteredElementCollector(doc).OfClass(typeof(Phase)).Cast<Phase>().LastOrDefault(); }
            catch { }

            foreach (var (id, name, pos) in verteiler)
            {
                string wohnungsId = "W-??";
                bool automatisch  = false;

                if (phase != null && pos != XYZ.Zero)
                {
                    try
                    {
                        var raum = doc.GetRoomAtPoint(pos, phase);
                        if (raum != null)
                        {
                            var nummer = raum.get_Parameter(BuiltInParameter.ROOM_NUMBER)?.AsString() ?? "";
                            wohnungsId  = SchemaHelper.ExtrahiereWohnungsId(nummer, schema);
                            automatisch = wohnungsId != "W-??";
                        }
                    }
                    catch { }
                }

                var raumNamen = raeume
                    .Where(r => SchemaHelper.ExtrahiereWohnungsId(r.Nummer, schema) == wohnungsId)
                    .Select(r => RaumHelper.Normalisiere(r.Name))
                    .Distinct().ToList();

                ergebnis.Add(new VerteilerzuordnungEintrag
                {
                    WohnungsId         = wohnungsId,
                    VerteilerId        = id,
                    VerteilerName      = name,
                    Raeume             = raumNamen,
                    AutomatischErkannt = automatisch,
                });
            }
            return ergebnis.OrderBy(e => e.WohnungsId).ToList();
        }
    }
}

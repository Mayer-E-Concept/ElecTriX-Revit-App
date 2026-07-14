// Statistics/StatisticsCommand.cs -- ME-Tools | Project element statistics
// Mayer E-Concept SRL
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using System;
using System.Collections.Generic;
using System.Linq;

namespace METools
{
    // One counted category row.
    public class StatRow
    {
        public string Section;
        public string Label;
        public int    Count;
        public bool   Highlight;
        public double LengthM;  // total metres for Cable & Containment rows (0 = use Count)

        public StatRow(string section, string label, int count, bool highlight = false)
        {
            Section   = section;
            Label     = label;
            Count     = count;
            Highlight = highlight;
        }

        // Constructor for length-based rows
        public StatRow(string section, string label, double lengthM)
        {
            Section = section;
            Label   = label;
            LengthM = lengthM;
        }
    }

    // Counts elements per category. All reads only - no transaction needed.
    public static class StatisticsCollector
    {
        // Fetches a category's elements once so multiple views (total count, by-type,
        // by-workset) can be derived from the same scan instead of re-querying the
        // document for each one. Matches Cnt()'s own filter exactly (WhereElementIsNotElementType).
        private static List<Element> FetchCategory(Document doc, BuiltInCategory cat)
        {
            try
            {
                return new FilteredElementCollector(doc)
                    .OfCategory(cat).WhereElementIsNotElementType().ToElements().ToList();
            }
            catch { return new List<Element>(); }
        }

        private static int Cnt(Document doc, BuiltInCategory cat)
        {
            try
            {
                return new FilteredElementCollector(doc)
                    .OfCategory(cat)
                    .WhereElementIsNotElementType()
                    .GetElementCount();
            }
            catch { return 0; }
        }

        // Same count, against an already-fetched element list.
        private static int Cnt(List<Element> elements) => elements.Count;

        // Sum CURVE_ELEM_LENGTH in metres for a category (run lengths only, no fittings)
        private static double SumLengthM(Document doc, BuiltInCategory cat)
        {
            try
            {
                double totalFt = 0;
                foreach (var el in new FilteredElementCollector(doc)
                    .OfCategory(cat).WhereElementIsNotElementType().ToElements())
                {
                    try
                    {
                        var p = el.get_Parameter(BuiltInParameter.CURVE_ELEM_LENGTH);
                        if (p != null && p.HasValue && p.StorageType == StorageType.Double)
                            totalFt += p.AsDouble();
                    }
                    catch { }
                }
                return UnitUtils.ConvertFromInternalUnits(totalFt, UnitTypeId.Meters);
            }
            catch { return 0; }
        }

        // Returns type-name -> count for all instances of a category.
        // Sorted alphabetically by name (not by count) so exports read as a clean list.
        public static List<(string TypeName, int Count)> CountByType(Document doc, BuiltInCategory cat)
        {
            try
            {
                return CountByType(new FilteredElementCollector(doc)
                    .OfCategory(cat).WhereElementIsNotElementType()
                    .OfClass(typeof(FamilyInstance)).Cast<FamilyInstance>().ToList());
            }
            catch { return new List<(string, int)>(); }
        }

        // Same grouping, against an already-fetched element list (filtered to
        // FamilyInstance the same way the Document overload above does).
        public static List<(string TypeName, int Count)> CountByType(List<Element> elements)
            => CountByType(elements.OfType<FamilyInstance>().ToList());

        private static List<(string TypeName, int Count)> CountByType(List<FamilyInstance> instances)
        {
            try
            {
                return instances
                    .GroupBy(fi => fi.Symbol?.Name ?? "(unknown)")
                    .Select(g => (g.Key, g.Count()))
                    .OrderBy(x => x.Item1, StringComparer.OrdinalIgnoreCase)
                    .ToList();
            }
            catch { return new List<(string, int)>(); }
        }

        // Returns workset-name -> count for all instances of a category.
        // Only meaningful in workshared (collaborative) projects; returns empty otherwise.
        public static List<(string WorksetName, int Count)> CountByWorkset(Document doc, BuiltInCategory cat)
        {
            try
            {
                if (!doc.IsWorkshared) return new List<(string, int)>();
                return CountByWorkset(doc, new FilteredElementCollector(doc)
                    .OfCategory(cat).WhereElementIsNotElementType().ToElements().ToList());
            }
            catch { return new List<(string, int)>(); }
        }

        // Same grouping, against an already-fetched element list.
        public static List<(string WorksetName, int Count)> CountByWorkset(Document doc, List<Element> elements)
        {
            try
            {
                if (!doc.IsWorkshared) return new List<(string, int)>();
                var wsTable = doc.GetWorksetTable();
                return elements
                    .GroupBy(el =>
                    {
                        try { return wsTable.GetWorkset(el.WorksetId)?.Name ?? "(unknown)"; }
                        catch { return "(unknown)"; }
                    })
                    .Select(g => (g.Key, g.Count()))
                    .OrderBy(x => x.Item1, StringComparer.OrdinalIgnoreCase)
                    .ToList();
            }
            catch { return new List<(string, int)>(); }
        }

        // Groups elements by CAx_Trassenbezugsebene (the level name string set by Fix Level).
        // Falls back to the Revit built-in level name if the CAx param is empty.
        public static List<(string LevelName, int Sockets, int Switches, int Lamps)> CountByFloor(Document doc)
        {
            var result = new Dictionary<string, (int s, int sw, int l)>(StringComparer.OrdinalIgnoreCase);
            void Add(BuiltInCategory cat, int slot)
            {
                try
                {
                    var instances = new FilteredElementCollector(doc)
                        .OfCategory(cat).WhereElementIsNotElementType()
                        .OfClass(typeof(FamilyInstance)).Cast<FamilyInstance>();
                    foreach (var fi in instances)
                    {
                        string lvl = "";
                        try { lvl = fi.LookupParameter("CAx_Trassenbezugsebene")?.AsString() ?? ""; } catch { }
                        if (string.IsNullOrWhiteSpace(lvl))
                            try { lvl = (doc.GetElement(fi.LevelId) as Level)?.Name ?? "Unknown"; } catch { lvl = "Unknown"; }
                        if (!result.TryGetValue(lvl, out var t)) t = (0, 0, 0);
                        result[lvl] = slot == 0 ? (t.s + 1, t.sw, t.l)
                                    : slot == 1 ? (t.s, t.sw + 1, t.l)
                                                : (t.s, t.sw, t.l + 1);
                    }
                }
                catch { }
            }
            Add(BuiltInCategory.OST_ElectricalFixtures, 0);
            Add(BuiltInCategory.OST_LightingDevices,    1);
            Add(BuiltInCategory.OST_LightingFixtures,   2);
            return result
                .OrderBy(kv => kv.Key)
                .Select(kv => (kv.Key, kv.Value.s, kv.Value.sw, kv.Value.l))
                .ToList();
        }

        public static List<StatRow> Collect(Document doc)
        {
            var r = new List<StatRow>();
            if (doc == null) return r;

            // Mapping for this project (verified): sockets = Electrical Fixtures,
            // switches = Lighting Devices, lamps = Lighting Fixtures.
            // Each category is fetched once and reused below for the total count,
            // the by-type breakdown, and the by-workset breakdown — previously each
            // of those ran its own separate document-wide scan of the same category.
            var socketEls = FetchCategory(doc, BuiltInCategory.OST_ElectricalFixtures);
            var switchEls = FetchCategory(doc, BuiltInCategory.OST_LightingDevices);
            var lampEls   = FetchCategory(doc, BuiltInCategory.OST_LightingFixtures);

            int sockets  = Cnt(socketEls);
            int switches = Cnt(switchEls);
            int lamps    = Cnt(lampEls);

            // Highlight tiles
            r.Add(new StatRow("Highlights", "Sockets",  sockets,  true));
            r.Add(new StatRow("Highlights", "Switches", switches, true));
            r.Add(new StatRow("Highlights", "Lamps",    lamps,    true));

            // Electrical - totals
            r.Add(new StatRow("Electrical", "Lamps (Lighting Fixtures)",    lamps));
            r.Add(new StatRow("Electrical", "Sockets (Electrical Fixtures)", sockets));
            r.Add(new StatRow("Electrical", "Switches (Lighting Devices)",  switches));

            // Sockets by type
            foreach (var (tn, cnt) in CountByType(socketEls))
                r.Add(new StatRow("Sockets by type", tn, cnt));

            // Switches by type
            foreach (var (tn, cnt) in CountByType(switchEls))
                r.Add(new StatRow("Switches by type", tn, cnt));

            // Sockets by workset (workshared projects only)
            foreach (var (ws, cnt) in CountByWorkset(doc, socketEls))
                r.Add(new StatRow("Sockets by workset", ws, cnt));

            // Switches by workset (workshared projects only)
            foreach (var (ws, cnt) in CountByWorkset(doc, switchEls))
                r.Add(new StatRow("Switches by workset", ws, cnt));

            // Lamps by workset (workshared projects only)
            foreach (var (ws, cnt) in CountByWorkset(doc, lampEls))
                r.Add(new StatRow("Lamps by workset", ws, cnt));

            // Per-floor breakdown
            foreach (var (lvl, soc, sw, lmp) in CountByFloor(doc))
            {
                if (soc + sw + lmp == 0) continue;
                r.Add(new StatRow("Per floor", lvl + " — Sockets",  soc));
                r.Add(new StatRow("Per floor", lvl + " — Switches", sw));
                r.Add(new StatRow("Per floor", lvl + " — Lamps",    lmp));
            }
            r.Add(new StatRow("Electrical", "Electrical Equipment / Panels", Cnt(doc, BuiltInCategory.OST_ElectricalEquipment)));
            r.Add(new StatRow("Electrical", "Electrical Circuits",          Cnt(doc, BuiltInCategory.OST_ElectricalCircuit)));
            r.Add(new StatRow("Electrical", "Fire Alarm Devices",          Cnt(doc, BuiltInCategory.OST_FireAlarmDevices)));
            r.Add(new StatRow("Electrical", "Data Devices",                Cnt(doc, BuiltInCategory.OST_DataDevices)));
            r.Add(new StatRow("Electrical", "Communication Devices",       Cnt(doc, BuiltInCategory.OST_CommunicationDevices)));
            r.Add(new StatRow("Electrical", "Security Devices",            Cnt(doc, BuiltInCategory.OST_SecurityDevices)));
            r.Add(new StatRow("Electrical", "Nurse Call Devices",          Cnt(doc, BuiltInCategory.OST_NurseCallDevices)));
            r.Add(new StatRow("Electrical", "Telephone Devices",           Cnt(doc, BuiltInCategory.OST_TelephoneDevices)));

            // Cable & containment -- trays and conduits show total length in metres
            r.Add(new StatRow("Cable & Containment", "Cable Trays",         SumLengthM(doc, BuiltInCategory.OST_CableTray)));
            r.Add(new StatRow("Cable & Containment", "Cable Tray Fittings", Cnt(doc, BuiltInCategory.OST_CableTrayFitting)));
            r.Add(new StatRow("Cable & Containment", "Conduits",            SumLengthM(doc, BuiltInCategory.OST_Conduit)));
            r.Add(new StatRow("Cable & Containment", "Conduit Fittings",    Cnt(doc, BuiltInCategory.OST_ConduitFitting)));
            r.Add(new StatRow("Cable & Containment", "Wires",               Cnt(doc, BuiltInCategory.OST_Wire)));

            // Mechanical & plumbing
            r.Add(new StatRow("Mechanical & Plumbing", "Mechanical Equipment", Cnt(doc, BuiltInCategory.OST_MechanicalEquipment)));
            r.Add(new StatRow("Mechanical & Plumbing", "Ducts",               Cnt(doc, BuiltInCategory.OST_DuctCurves)));
            r.Add(new StatRow("Mechanical & Plumbing", "Air Terminals",       Cnt(doc, BuiltInCategory.OST_DuctTerminal)));
            r.Add(new StatRow("Mechanical & Plumbing", "Pipes",               Cnt(doc, BuiltInCategory.OST_PipeCurves)));
            r.Add(new StatRow("Mechanical & Plumbing", "Plumbing Fixtures",   Cnt(doc, BuiltInCategory.OST_PlumbingFixtures)));
            r.Add(new StatRow("Mechanical & Plumbing", "Sprinklers",          Cnt(doc, BuiltInCategory.OST_Sprinklers)));

            // Spaces & levels
            r.Add(new StatRow("Spaces & Levels", "Rooms",      Cnt(doc, BuiltInCategory.OST_Rooms)));
            r.Add(new StatRow("Spaces & Levels", "MEP Spaces", Cnt(doc, BuiltInCategory.OST_MEPSpaces)));
            int levels = 0;
            try { levels = new FilteredElementCollector(doc).OfClass(typeof(Level)).GetElementCount(); }
            catch { }
            r.Add(new StatRow("Spaces & Levels", "Levels", levels));

            return r;
        }
    }

    // Recomputes statistics on the Revit API thread (for the Refresh button).
    public class StatisticsHandler : IExternalEventHandler
    {
        public Action<List<StatRow>, string> OnResult;

        public void Execute(UIApplication app)
        {
            try
            {
                var doc  = app.ActiveUIDocument.Document;
                var rows = StatisticsCollector.Collect(doc);
                OnResult?.Invoke(rows, doc.Title ?? "");
            }
            catch { }
        }

        public string GetName() => "ME-Tools Statistics Refresh";
    }

    [Transaction(TransactionMode.Manual)]
    public class StatisticsCommand : IExternalCommand
    {
        private static StatisticsWindow _window;

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            Open(commandData.Application);
            return Result.Succeeded;
        }

        public static void Open(UIApplication uiApp)
        {
            if (!METools.LicenseManager.CheckAccessOrExplain()) return;

            var uidoc = uiApp.ActiveUIDocument;
            var doc   = uidoc.Document;

            if (_window != null && _window.IsVisible)
            { _window.Activate(); _window.Focus(); return; }

            AppSwitcher.Ensure();
            MeToolsWindowBase.RevitHandle = uiApp.MainWindowHandle;

            var rows    = StatisticsCollector.Collect(doc);
            var handler = new StatisticsHandler();
            var ev      = ExternalEvent.Create(handler);

            _window = new StatisticsWindow(ev, handler, rows, doc.Title ?? "");
            _window.Closed += (s, e) => _window = null;
            _window.Show();
        }
    }
}

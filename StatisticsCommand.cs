// Statistics/StatisticsCommand.cs -- ME-Tools | Project element statistics
// Mayer E-Concept SRL
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

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

        // Groups elements by level, in priority order:
        //  1. CAx_Trassenbezugsebene -- the office-defined schedule level string set by
        //     Fix Level; authoritative when present.
        //  2. INSTANCE_SCHEDULE_ONLY_LEVEL_PARAM -- Revit's own built-in "Schedule Level"
        //     field. Same fallback FixLevelCommand.CurrentLevelId already relies on.
        //  3. The instance's own placement Level (LevelId).
        //  4. Nearest real Level by Z-elevation -- confirmed via live model inspection
        //     that some elements (e.g. ones Family Placer's last-resort placement path
        //     created with no host, no workplane, and no level association at all) have
        //     NONE of the above set, despite sitting at a perfectly real, sensible Z
        //     position. Rather than let every one of those fall into "Unknown", this
        //     finds whichever real Level in the project is physically closest -- the
        //     same technique LampPlacerHandler.GetNearestLevel already uses.
        //  "Unknown" only once all four genuinely have nothing usable.
        private static string ResolveFloorLevelName(Document doc, FamilyInstance fi, List<Level> allLevels)
        {
            try
            {
                var lvl = fi.LookupParameter("CAx_Trassenbezugsebene")?.AsString();
                if (!string.IsNullOrWhiteSpace(lvl)) return lvl;
            }
            catch { }

            try
            {
                var p = fi.get_Parameter(BuiltInParameter.INSTANCE_SCHEDULE_ONLY_LEVEL_PARAM);
                if (p != null)
                {
                    var id = p.AsElementId();
                    if (id != null && id != ElementId.InvalidElementId)
                    {
                        var name = (doc.GetElement(id) as Level)?.Name;
                        if (!string.IsNullOrWhiteSpace(name)) return name;
                    }
                }
            }
            catch { }

            try
            {
                var name = (doc.GetElement(fi.LevelId) as Level)?.Name;
                if (!string.IsNullOrWhiteSpace(name)) return name;
            }
            catch { }

            try
            {
                if (allLevels.Count > 0 && fi.Location is LocationPoint lp)
                {
                    var nearest = allLevels.OrderBy(l => Math.Abs(l.Elevation - lp.Point.Z)).First();
                    if (!string.IsNullOrWhiteSpace(nearest.Name)) return nearest.Name;
                }
            }
            catch { }

            return "Unknown";
        }

        // Natural sort: splits each name into text/number runs so "Obergeschoss 10" sorts
        // after "Obergeschoss 2" instead of before it (plain string sort compares the "1"
        // before the "2" and gets that backwards). "Unknown" is always forced last,
        // deterministically, rather than however it happens to fall alphabetically.
        private static List<string> NaturalSortKey(string s)
        {
            var parts = new List<string>();
            var current = new StringBuilder();
            bool? lastWasDigit = null;
            foreach (var ch in s)
            {
                bool isDigit = char.IsDigit(ch);
                if (lastWasDigit != null && isDigit != lastWasDigit)
                {
                    parts.Add(current.ToString());
                    current.Clear();
                }
                current.Append(ch);
                lastWasDigit = isDigit;
            }
            if (current.Length > 0) parts.Add(current.ToString());
            return parts;
        }

        private static int CompareFloorNames(string a, string b)
        {
            bool aUnknown = string.Equals(a, "Unknown", StringComparison.OrdinalIgnoreCase);
            bool bUnknown = string.Equals(b, "Unknown", StringComparison.OrdinalIgnoreCase);
            if (aUnknown != bUnknown) return aUnknown ? 1 : -1; // Unknown always last
            if (aUnknown && bUnknown) return 0;

            var pa = NaturalSortKey(a);
            var pb = NaturalSortKey(b);
            for (int i = 0; i < Math.Min(pa.Count, pb.Count); i++)
            {
                bool numA = int.TryParse(pa[i], out int na);
                bool numB = int.TryParse(pb[i], out int nb);
                int cmp = (numA && numB) ? na.CompareTo(nb)
                                          : string.Compare(pa[i], pb[i], StringComparison.OrdinalIgnoreCase);
                if (cmp != 0) return cmp;
            }
            return pa.Count.CompareTo(pb.Count);
        }

        public static List<(string LevelName, int Sockets, int Switches, int Lamps)> CountByFloor(Document doc)
        {
            var result = new Dictionary<string, (int s, int sw, int l)>(StringComparer.OrdinalIgnoreCase);
            // Fetched once, not per-element, for the geometric fallback in ResolveFloorLevelName.
            var allLevels = new FilteredElementCollector(doc).OfClass(typeof(Level)).Cast<Level>().ToList();
            void Add(BuiltInCategory cat, int slot)
            {
                try
                {
                    var instances = new FilteredElementCollector(doc)
                        .OfCategory(cat).WhereElementIsNotElementType()
                        .OfClass(typeof(FamilyInstance)).Cast<FamilyInstance>();
                    foreach (var fi in instances)
                    {
                        string lvl = ResolveFloorLevelName(doc, fi, allLevels);
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
            var keys = result.Keys.ToList();
            keys.Sort(CompareFloorNames);
            return keys
                .Select(k => (k, result[k].s, result[k].sw, result[k].l))
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

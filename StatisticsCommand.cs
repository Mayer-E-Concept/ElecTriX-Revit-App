// Statistics/StatisticsCommand.cs -- ME-Tools | Project element statistics
// Mayer E-Concept SRL
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;

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

    // -- Snapshot + compare -------------------------------------------------
    // StatRow uses plain public fields (not { get; set; } properties), which
    // System.Text.Json does not serialize by default -- rather than reach for
    // JsonSerializerOptions.IncludeFields and depend on that being set
    // correctly everywhere this is touched, a small dedicated DTO with real
    // properties keeps the snapshot file's shape independent of StatRow's own
    // internal representation.
    public class StatSnapshotRow
    {
        public string Section { get; set; } = "";
        public string Label   { get; set; } = "";
        public int    Count   { get; set; }
        public double LengthM { get; set; }
    }

    public class StatSnapshot
    {
        public string DocTitle { get; set; } = "";
        public DateTime SavedAtUtc { get; set; }
        public List<StatSnapshotRow> Rows { get; set; } = new List<StatSnapshotRow>();
    }

    // One changed row between a saved snapshot and the current count.
    public class StatDiffRow
    {
        public string Section  { get; set; } = "";
        public string Label    { get; set; } = "";
        public double OldValue { get; set; }
        public double NewValue { get; set; }
        public bool   IsLength { get; set; } // true = show as metres, false = whole count
        public double Delta => NewValue - OldValue;
    }

    // One JSON file per project (keyed by the same stable, model-stamped
    // project id CommentsStorage already uses), stored locally per-machine --
    // deliberately NOT on the shared network folder, since "compare to my own
    // last look" is a personal reference point, not a team-shared one.
    public static class StatisticsSnapshotStorage
    {
        private static readonly string DataDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "METools");

        private static string SnapshotPath(string projectId) => Path.Combine(DataDir, $"stats-snapshot-{projectId}.json");

        public static void Save(string projectId, List<StatRow> rows, string docTitle)
        {
            if (string.IsNullOrEmpty(projectId)) return;
            try
            {
                Directory.CreateDirectory(DataDir);
                var snap = new StatSnapshot
                {
                    DocTitle   = docTitle ?? "",
                    SavedAtUtc = DateTime.UtcNow,
                    Rows       = rows.Select(r => new StatSnapshotRow
                    {
                        Section = r.Section, Label = r.Label, Count = r.Count, LengthM = r.LengthM,
                    }).ToList(),
                };
                var json = JsonSerializer.Serialize(snap, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(SnapshotPath(projectId), json);
            }
            catch { }
        }

        public static StatSnapshot Load(string projectId)
        {
            if (string.IsNullOrEmpty(projectId)) return null;
            try
            {
                var path = SnapshotPath(projectId);
                if (!File.Exists(path)) return null;
                return JsonSerializer.Deserialize<StatSnapshot>(File.ReadAllText(path));
            }
            catch { return null; }
        }

        // Only the rows that actually changed (added, removed, or a different
        // count/length) -- an unchanged project shows nothing here, which is
        // the point: this is for spotting what moved, not re-showing everything.
        public static List<StatDiffRow> ComputeDiff(List<StatRow> current, StatSnapshot old)
        {
            var result = new List<StatDiffRow>();
            if (old?.Rows == null) return result;

            string Key(string section, string label) => section + "\u0001" + label;

            var oldMap = old.Rows.ToDictionary(r => Key(r.Section, r.Label), r => r);
            var seen = new HashSet<string>();

            foreach (var r in current)
            {
                string key = Key(r.Section, r.Label);
                seen.Add(key);
                bool isLength = r.LengthM > 0;
                double newVal = isLength ? r.LengthM : r.Count;

                double oldVal = 0;
                bool oldIsLength = isLength;
                if (oldMap.TryGetValue(key, out var oldRow))
                {
                    oldIsLength = oldRow.LengthM > 0;
                    oldVal = oldIsLength ? oldRow.LengthM : oldRow.Count;
                }

                if (Math.Abs(newVal - oldVal) > 0.001)
                    result.Add(new StatDiffRow { Section = r.Section, Label = r.Label, OldValue = oldVal, NewValue = newVal, IsLength = isLength });
            }

            // Rows present in the snapshot but gone entirely now (e.g. a floor
            // that had fixtures before and has none at all now, so Collect()
            // never emits a row for it -- still worth surfacing as a removal).
            foreach (var kv in oldMap)
            {
                if (seen.Contains(kv.Key)) continue;
                var oldRow = kv.Value;
                bool isLength = oldRow.LengthM > 0;
                double oldVal = isLength ? oldRow.LengthM : oldRow.Count;
                if (oldVal == 0) continue;
                result.Add(new StatDiffRow { Section = oldRow.Section, Label = oldRow.Label, OldValue = oldVal, NewValue = 0, IsLength = isLength });
            }

            return result;
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

        // Takes the already-fetched element lists (same ones Collect() uses for
        // totals/by-type/by-workset) instead of re-scanning the document for
        // these same 3 categories a second time -- this used to run its own
        // independent FilteredElementCollector per category, duplicating work
        // Collect() had already just done a few lines above it.
        public static List<(string LevelName, int Sockets, int Switches, int Lamps)> CountByFloor(
            Document doc, List<Element> socketEls, List<Element> switchEls, List<Element> lampEls)
        {
            var result = new Dictionary<string, (int s, int sw, int l)>(StringComparer.OrdinalIgnoreCase);
            // Fetched once, not per-element, for the geometric fallback in ResolveFloorLevelName.
            var allLevels = new FilteredElementCollector(doc).OfClass(typeof(Level)).Cast<Level>().ToList();
            void Add(List<Element> els, int slot)
            {
                foreach (var fi in els.OfType<FamilyInstance>())
                {
                    try
                    {
                        string lvl = ResolveFloorLevelName(doc, fi, allLevels);
                        if (!result.TryGetValue(lvl, out var t)) t = (0, 0, 0);
                        result[lvl] = slot == 0 ? (t.s + 1, t.sw, t.l)
                                    : slot == 1 ? (t.s, t.sw + 1, t.l)
                                                : (t.s, t.sw, t.l + 1);
                    }
                    catch { }
                }
            }
            Add(socketEls, 0);
            Add(switchEls, 1);
            Add(lampEls,   2);
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
            foreach (var (lvl, soc, sw, lmp) in CountByFloor(doc, socketEls, switchEls, lampEls))
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
        public Action<string> OnSnapshotSaved; // invoked with the project id right after a successful save

        // Set true before Raise() to also save a snapshot of this refresh --
        // needs a valid API context because GetOrCreateProjectId() may need
        // to stamp a new id into the document the first time it's called.
        public bool SaveSnapshotRequested { get; set; }

        public void Execute(UIApplication app)
        {
            try
            {
                var doc  = app.ActiveUIDocument.Document;
                var rows = StatisticsCollector.Collect(doc);

                if (SaveSnapshotRequested)
                {
                    SaveSnapshotRequested = false;
                    var projectId = METools.Comments.CommentsStorage.GetOrCreateProjectId(doc);
                    StatisticsSnapshotStorage.Save(projectId, rows, doc.Title ?? "");
                    OnSnapshotSaved?.Invoke(projectId);
                }

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
            // No license gate here on purpose -- Free tier: always usable, licensed or not.

            var uidoc = uiApp.ActiveUIDocument;
            var doc   = uidoc.Document;

            if (_window != null && _window.IsVisible)
            { _window.Activate(); _window.Focus(); return; }

            AppSwitcher.Ensure();
            MeToolsWindowBase.RevitHandle = uiApp.MainWindowHandle;

            var rows    = StatisticsCollector.Collect(doc);
            var handler = new StatisticsHandler();
            var ev      = ExternalEvent.Create(handler);

            // Open() runs inside IExternalCommand.Execute() -- a valid API
            // context -- so it's safe to call this here even on a project
            // that hasn't been stamped with a project id yet (which needs a
            // transaction the first time).
            var projectId = METools.Comments.CommentsStorage.GetOrCreateProjectId(doc);
            var snapshot  = StatisticsSnapshotStorage.Load(projectId);

            _window = new StatisticsWindow(ev, handler, rows, doc.Title ?? "", projectId, snapshot);
            _window.Closed += (s, e) => _window = null;
            _window.Show();
        }
    }
}

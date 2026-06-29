// FixLevelCommand.cs -- ME-Tools | Fix Level
// Mayer E-Concept SRL
//
// Opens a window (like the other tools) where you pick which categories to fix.
// Sockets/switches get the storey's FFB (finished-floor) level; ceiling lamps get
// the storey's UKD (underside-of-ceiling) level; wall-mounted lamps are skipped.
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using System;
using System.Collections.Generic;
using System.Linq;

namespace METools
{
    public enum FixLevelScope { ActiveView, Storey, WholeModel }

    public class FixLevelRequest
    {
        public bool Sockets       = true;   // OST_ElectricalFixtures -> FFB
        public bool Switches      = true;   // OST_LightingDevices    -> FFB
        public bool Lamps         = true;   // OST_LightingFixtures   -> ceiling = UKD, wall = skip
        public bool SkipWallLamps = true;
        public FixLevelScope Scope  = FixLevelScope.ActiveView;
        public bool DryRun          = false;  // preview only, no transaction
    }

    public class FixLevelHandler : IExternalEventHandler
    {
        // Level-name prefixes for this office's convention. FFB = finished floor,
        // UKD = underside of ceiling, RFB = raw floor. Same storey suffix pairs them.
        private const string FFB = "FFB";
        private const string UKD = "UKD";

        public FixLevelRequest Request { get; set; } = new FixLevelRequest();
        public Action<string> OnDone { get; set; }

        public void Execute(UIApplication app)
        {
            try { Run(app); }
            catch (Exception ex) { OnDone?.Invoke("Error: " + ex.Message); }
        }

        public string GetName() => "ME-Tools Fix Level";

        private void Run(UIApplication app)
        {
            var uidoc = app.ActiveUIDocument;
            var doc   = uidoc.Document;
            var view  = uidoc.ActiveView;

            Level viewLevel = (view as ViewPlan)?.GenLevel;
            if (viewLevel == null)
            { OnDone?.Invoke("Open a floor plan view first (the active view has no level)."); return; }

            var levels = new FilteredElementCollector(doc).OfClass(typeof(Level)).Cast<Level>().ToList();

            string suffix = SuffixOf(viewLevel.Name);
            Level ffbTarget = FindLevel(levels, FFB, suffix) ?? viewLevel;
            Level ukdTarget = FindLevel(levels, UKD, suffix) ?? NearestAbove(levels, viewLevel, UKD) ?? viewLevel;

            // Storey level set (any prefix, same suffix) for the "current storey" scope.
            var storeyLevelIds = new HashSet<ElementId>(
                levels.Where(l => SuffixOf(l.Name) == suffix).Select(l => l.Id));

            int sockets = 0, switches = 0, lampsCeil = 0, wallSkipped = 0;

            if (Request.DryRun)
            {
                // Preview pass: count without modifying anything
                if (Request.Sockets)
                    sockets = Collect(doc, view, BuiltInCategory.OST_ElectricalFixtures, storeyLevelIds).Count();
                if (Request.Switches)
                    switches = Collect(doc, view, BuiltInCategory.OST_LightingDevices, storeyLevelIds).Count();
                if (Request.Lamps)
                    foreach (var fi in Collect(doc, view, BuiltInCategory.OST_LightingFixtures, storeyLevelIds))
                    {
                        if (Request.SkipWallLamps && IsWallLamp(fi)) { wallSkipped++; continue; }
                        lampsCeil++;
                    }
            }
            else
            {
                using (var tx = new Transaction(doc, "ME-Tools: Fix Level"))
                {
                    tx.Start();

                    if (Request.Sockets)
                        foreach (var fi in Collect(doc, view, BuiltInCategory.OST_ElectricalFixtures, storeyLevelIds))
                            if (SetSchedLevel(fi, ffbTarget)) sockets++;

                    if (Request.Switches)
                        foreach (var fi in Collect(doc, view, BuiltInCategory.OST_LightingDevices, storeyLevelIds))
                            if (SetSchedLevel(fi, ffbTarget)) switches++;

                    if (Request.Lamps)
                        foreach (var fi in Collect(doc, view, BuiltInCategory.OST_LightingFixtures, storeyLevelIds))
                        {
                            if (Request.SkipWallLamps && IsWallLamp(fi)) { wallSkipped++; continue; }
                            if (SetSchedLevel(fi, ukdTarget)) lampsCeil++;
                        }

                    tx.Commit();
                }
            }

            string prefix = Request.DryRun ? "PREVIEW (no changes made)\n" : "";
            string msg = prefix
                       + $"Storey '{suffix}'  (FFB: {ffbTarget.Name}, UKD: {ukdTarget.Name})\n"
                       + $"Sockets  -> FFB : {sockets}\n"
                       + $"Switches -> FFB : {switches}\n"
                       + $"Ceiling lamps -> UKD: {lampsCeil}"
                       + (Request.SkipWallLamps ? $"   (wall lamps skipped: {wallSkipped})" : "");
            OnDone?.Invoke(msg);
        }

        private IEnumerable<FamilyInstance> Collect(Document doc, View view,
            BuiltInCategory cat, HashSet<ElementId> storeyLevelIds)
        {
            IEnumerable<FamilyInstance> all;
            if (Request.Scope == FixLevelScope.ActiveView)
                all = new FilteredElementCollector(doc, view.Id)
                    .OfCategory(cat).OfClass(typeof(FamilyInstance)).Cast<FamilyInstance>();
            else
                all = new FilteredElementCollector(doc)
                    .OfCategory(cat).OfClass(typeof(FamilyInstance)).Cast<FamilyInstance>();

            if (Request.Scope == FixLevelScope.Storey)
                all = all.Where(fi => storeyLevelIds.Contains(CurrentLevelId(fi)));

            return all.ToList();
        }

        private static ElementId CurrentLevelId(FamilyInstance fi)
        {
            try
            {
                var p = fi.get_Parameter(BuiltInParameter.INSTANCE_SCHEDULE_ONLY_LEVEL_PARAM);
                if (p != null)
                {
                    var id = p.AsElementId();
                    if (id != null && id != ElementId.InvalidElementId) return id;
                }
            }
            catch { }
            try { return fi.LevelId ?? ElementId.InvalidElementId; }
            catch { return ElementId.InvalidElementId; }
        }

        // Changes the effective level of an already-placed Auxalia CAx FamilyInstance.
        //
        // Strategy:
        // 1) Move the element's Z position to the target level elevation. This is what
        //    Revit's "Edit Work Plane" UI does internally — physically relocating the
        //    element forces Revit to update the Work Plane and Schedule Level automatically.
        // 2) Also update CAx_Trassenbezugsebene + CAx_Ebenenhöhenwert (the writable
        //    CAx params your office schedules read from).
        private static bool SetSchedLevel(FamilyInstance fi, Level targetLevel)
        {
            if (fi == null || targetLevel == null) return false;
            var doc = fi.Document;
            bool set = false;

            // Strategy 1: MoveElement to target level Z.
            // For work-plane-based families (Work Plane = None, not wall-hosted),
            // moving the Z forces Revit to reassign the work plane to the level plane
            // at the new Z — exactly what "Edit Work Plane" does in the UI.
            try
            {
                if (fi.Location is LocationPoint lp)
                {
                    double currentZ = lp.Point.Z;
                    double targetZ  = targetLevel.Elevation;
                    double dZ = targetZ - currentZ;
                    if (Math.Abs(dZ) > 1e-6)   // only move if there's an actual difference
                    {
                        ElementTransformUtils.MoveElement(doc, fi.Id, new XYZ(0, 0, dZ));
                        set = true;
                    }
                    else
                    {
                        set = true;  // already at the right Z — count as success
                    }
                }
            }
            catch { }

            // Strategy 2: CAx_Trassenbezugsebene (level name string) — drives CAx schedules
            try
            {
                var pTrasse = fi.LookupParameter("CAx_Trassenbezugsebene");
                if (pTrasse != null && !pTrasse.IsReadOnly && pTrasse.StorageType == StorageType.String)
                { pTrasse.Set(targetLevel.Name); set = true; }
            }
            catch { }

            // Strategy 3: CAx_Ebenenhöhenwert (level elevation double) — drives CAx schedules
            try
            {
                var pEb = fi.LookupParameter("CAx_Ebenenh\u00f6henwert");
                if (pEb != null && !pEb.IsReadOnly && pEb.StorageType == StorageType.Double)
                { pEb.Set(targetLevel.Elevation); set = true; }
            }
            catch { }

            // Strategy 4: Built-in schedule level param (works on some non-CAx families)
            try
            {
                var p = fi.get_Parameter(BuiltInParameter.INSTANCE_SCHEDULE_ONLY_LEVEL_PARAM);
                if (p != null && !p.IsReadOnly && p.StorageType == StorageType.ElementId)
                { p.Set(targetLevel.Id); set = true; }
            }
            catch { }

            return set;
        }

        // A lamp counts as wall-mounted if it has an offset above its level/host,
        // OR its family/type name (or CAx type) says wall. Catches both the 1850 mm
        // "Wall-lamp-rectangle" and the offset-0 "wall light" families.
        private static bool IsWallLamp(FamilyInstance fi)
        {
            double offMm = 0;
            try
            {
                var pe = fi.get_Parameter(BuiltInParameter.INSTANCE_ELEVATION_PARAM);
                if (pe != null && pe.StorageType == StorageType.Double)
                    offMm = UnitUtils.ConvertFromInternalUnits(pe.AsDouble(), UnitTypeId.Millimeters);
                var ph = fi.LookupParameter("Offset from Host");
                if (ph != null && ph.StorageType == StorageType.Double)
                    offMm = Math.Max(offMm, UnitUtils.ConvertFromInternalUnits(ph.AsDouble(), UnitTypeId.Millimeters));
            }
            catch { }
            if (offMm > 50.0) return true;

            string n = (((fi.Symbol != null && fi.Symbol.Family != null ? fi.Symbol.Family.Name : "") ?? "")
                      + " " + ((fi.Symbol != null ? fi.Symbol.Name : "") ?? "")).ToLowerInvariant();
            if (n.Contains("wall") || n.Contains("wand") || n.Contains("w-auslass")) return true;

            try
            {
                var pt = fi.LookupParameter("CAx Type_Englisch");
                if (pt != null && pt.StorageType == StorageType.String
                    && ((pt.AsString()) ?? "").ToLowerInvariant().Contains("wall"))
                    return true;
            }
            catch { }
            return false;
        }

        // "FFB 1.OG" -> "1.OG"; "UKD EG" -> "EG"; unknown prefix -> whole name.
        private static string SuffixOf(string levelName)
        {
            if (string.IsNullOrEmpty(levelName)) return "";
            foreach (var pre in new[] { FFB, UKD, "RFB" })
                if (levelName.StartsWith(pre, StringComparison.OrdinalIgnoreCase))
                    return levelName.Substring(pre.Length).Trim();
            return levelName.Trim();
        }

        private static Level FindLevel(List<Level> levels, string prefix, string suffix)
        {
            return levels.FirstOrDefault(l =>
                l.Name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                && string.Equals(l.Name.Substring(prefix.Length).Trim(), suffix, StringComparison.OrdinalIgnoreCase));
        }

        private static Level NearestAbove(List<Level> levels, Level from, string prefix)
        {
            return levels.Where(l => l.Name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                                  && l.Elevation > from.Elevation)
                         .OrderBy(l => l.Elevation)
                         .FirstOrDefault();
        }
    }

    [Transaction(TransactionMode.Manual)]
    public class FixLevelCommand : IExternalCommand
    {
        private static FixLevelWindow _window;

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            Open(commandData.Application);
            return Result.Succeeded;
        }

        public static void Open(UIApplication uiApp)
        {
            if (_window != null && _window.IsVisible)
            { _window.Activate(); _window.Focus(); return; }

            MeToolsWindowBase.RevitHandle = uiApp.MainWindowHandle;

            string activeLevel = "";
            try { activeLevel = (uiApp.ActiveUIDocument.ActiveView as ViewPlan)?.GenLevel?.Name ?? ""; }
            catch { }

            var handler = new FixLevelHandler();
            var ev      = ExternalEvent.Create(handler);

            _window = new FixLevelWindow(ev, handler, activeLevel);
            _window.Closed += (s, e) => _window = null;
            _window.Show();
        }
    }
}

// LampPlacerHandler.cs — ME-Tools | Lamp Placer
// Mayer E-Concept SRL
using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using Autodesk.Revit.DB.Structure;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using OperationCanceledException = Autodesk.Revit.Exceptions.OperationCanceledException;

namespace METools.LampPlacer
{
    public class RoomFilter : ISelectionFilter
    {
        public bool AllowElement(Element e) => e is Room;
        public bool AllowReference(Reference r, XYZ p) => false;
    }

    public class LineFilter : ISelectionFilter
    {
        public bool AllowElement(Element e) => e is CurveElement;
        public bool AllowReference(Reference r, XYZ p) => false;
    }

    public class LampPlacerHandler : IExternalEventHandler
    {
        public LampRequest    Request  { get; set; } = new LampRequest();
        public Action<string> OnStatus  { get; set; }
        public Action<int>    OnPlaced  { get; set; }
        public Action<bool>   OnWaiting { get; set; }  // true=waiting for input, false=done
        public Func<double, double?> OnPromptSpacing { get; set; }  // mm in -> mm out (null = cancel)
        public Func<double, double?> OnPromptWallOffset { get; set; } // mm in -> mm out (null = cancel)
        private double? _wallOffsetFt;                                // set for wall-mounted lamps off a floor reference
        public Func<string> OnPromptPreset { get; set; }              // returns preset name | "" (use current family) | null (cancel)

        public void Execute(UIApplication app)
        {
            var uidoc = app.ActiveUIDocument;
            var doc   = uidoc.Document;

            // For placement actions, confirm the reference (ceiling) level matches the active storey.
            _wallOffsetFt = null;
            bool isPlacement = Request.Action == LampAction.PlaceSingle
                            || Request.Action == LampAction.PlaceMulti
                            || Request.Action == LampAction.PlaceLine
                            || Request.Action == LampAction.PlaceGrid;
            if (isPlacement)
            {
                var dec = METools.LevelGuard.CheckLampReference(
                    uidoc, doc, Request.Config?.FallbackLevelId ?? ElementId.InvalidElementId);
                if (dec == METools.LampLevelDecision.Cancel)
                { OnStatus?.Invoke("Cancelled (level check)."); return; }

                // Kept a floor level as the reference -> wall-mounted lamps: ask the offset from host.
                if (dec == METools.LampLevelDecision.ProceedFloorRef)
                {
                    double? mm = OnPromptWallOffset?.Invoke(1800.0);
                    if (mm == null) { OnStatus?.Invoke("Cancelled (offset)."); return; }
                    _wallOffsetFt = ToFeet(mm.Value);
                }
            }

            if (Request.Action == LampAction.Redistribute)
            { Redistribute(uidoc, doc); return; }

            if (Request.Action == LampAction.RefreshRoom)
            { RefreshRoom(uidoc, doc); return; }

            if (Request.Action == LampAction.UpdatePreset)
            { UpdatePreset(uidoc, doc); return; }

            if (Request.Action == LampAction.PlaceLine)
            { PlaceAlongLine(uidoc, doc); return; }

            if (Request.Action == LampAction.PlaceGrid)
            { PlaceGrid(uidoc, doc); return; }

            var sym = doc.GetElement(Request.SymbolId) as FamilySymbol;
            if (sym == null) { OnStatus?.Invoke("Family not found."); return; }

            var rooms = new List<Room>();
            try
            {
                if (Request.Action == LampAction.PlaceMulti)
                {
                    var refs = uidoc.Selection.PickObjects(ObjectType.Element,
                        new RoomFilter(), "Click the room(s) to fill, then press ESC");
                    rooms = refs.Select(r => doc.GetElement(r) as Room).Where(r => r != null).ToList();
                }
                else
                {
                    var r = uidoc.Selection.PickObject(ObjectType.Element,
                        new RoomFilter(), "Click a room to place lamps");
                    if (doc.GetElement(r) is Room rm) rooms.Add(rm);
                }
            }
            catch (OperationCanceledException) { OnStatus?.Invoke("Cancelled."); return; }
            catch (Exception ex)               { OnStatus?.Invoke($"Error: {ex.Message}"); return; }

            if (!rooms.Any()) { OnStatus?.Invoke("No rooms selected."); return; }

            // Place in Room (single): offer the saved room presets.
            if (Request.Action == LampAction.PlaceSingle && rooms.Count == 1 && LampPresetStore.All().Count > 0)
            {
                string chosen = OnPromptPreset?.Invoke();
                if (chosen == null) { OnStatus?.Invoke("Cancelled."); return; }
                if (!string.IsNullOrEmpty(chosen))
                { PlacePreset(uidoc, doc, rooms[0], LampPresetStore.Get(chosen)); return; }
                // empty -> fall through to the selected single family
            }

            var cfg = Request.Config;
            double wallFt   = ToFeet(cfg.WallMargin);
            double offsetFt = ToFeet(cfg.UKDOffset);

            // Fallback-Level einmal auflösen
            Level fallbackLvl = ResolveFallbackLevel(doc, cfg);

            // Fetched once up front instead of inside the room loop below --
            // GetNearestLevel(doc, ...) was re-scanning every Level in the
            // document once per room. Same pattern already used for the grid/
            // line placement loops further down in this file.
            var allLevels = fallbackLvl != null
                ? null
                : new FilteredElementCollector(doc).OfClass(typeof(Level)).Cast<Level>().ToList();

            var plan = new List<Planned>();
            foreach (var room in rooms)
            {
                var    bb     = room.get_BoundingBox(null);
                double floorZ = room.Level?.Elevation ?? 0;
                double ukdZ;
                if (fallbackLvl != null && _wallOffsetFt.HasValue)
                    ukdZ = fallbackLvl.Elevation + _wallOffsetFt.Value;   // wall lamp: offset above the floor
                else
                    ukdZ = ((fallbackLvl != null)
                                    ? fallbackLvl.Elevation
                                    : (bb != null ? bb.Max.Z : GetUKD(room)))
                                - offsetFt;
                var    level  = fallbackLvl ?? GetNearestLevel(allLevels, ukdZ);
                if (bb == null) continue;
                double roomW  = bb.Max.X - bb.Min.X;
                double roomD  = bb.Max.Y - bb.Min.Y;
                double area   = UnitUtils.ConvertFromInternalUnits(room.Area, UnitTypeId.SquareMeters);
                int rows, cols; CalcGrid(cfg, area, roomW, roomD, out rows, out cols);
                double angle  = CalcAngle(cfg.Rotation, roomW, roomD);
                var pts = CalcPoints(bb, wallFt, rows, cols, ukdZ);
                foreach (var pt in pts)
                {
                    if (!IsInRoom(room, new XYZ(pt.X, pt.Y, floorZ + 0.5))) continue;
                    plan.Add(new Planned { Pt = pt, Angle = angle, Lvl = level, Rm = room, Ukd = ukdZ });
                }
            }
            RunPlacement(uidoc, doc, sym, cfg, plan, "Place in Room");
        }

        // ── Grid calculation ─────────────────────────────────────────────────
        private void CalcGrid(LampConfig cfg, double areaSqm, double roomW, double roomD,
                              out int rows, out int cols)
        {
            if (cfg.Distribution == DistributionMode.ManualGrid)
            { rows = Math.Max(1, cfg.ManualRows); cols = Math.Max(1, cfg.ManualCols); return; }

            // Adaptive margin
            double wallFt = ToFeet(cfg.WallMargin);
            double margW  = Math.Min(wallFt, roomW * 0.25);
            double margD  = Math.Min(wallFt, roomD * 0.25);
            double availW = Math.Max(roomW * 0.5, roomW - 2.0 * margW);
            double availD = Math.Max(roomD * 0.5, roomD - 2.0 * margD);

            // Total lamps from area
            double avail_m2 = UnitUtils.ConvertFromInternalUnits(availW * availD, UnitTypeId.SquareMeters);
            if (areaSqm <= 0) areaSqm = avail_m2;
            int nTotal = Math.Max(1, (int)Math.Ceiling(avail_m2 / cfg.SqmPerLamp));

            // ★ Corridor: aspect ratio > 2.5 → all lamps in one line
            double ratio = Math.Max(roomW, roomD) / Math.Min(roomW, roomD);
            if (ratio > 2.5)
            {
                if (roomW >= roomD) { rows = 1; cols = nTotal; }
                else                { cols = 1; rows = nTotal; }
                return;
            }

            // Normal room: aspect-ratio grid
            double aspect = availW / availD;
            cols = Math.Max(1, (int)Math.Round(Math.Sqrt(nTotal * aspect)));
            rows = Math.Max(1, (int)Math.Round((double)nTotal / cols));
        }

        // Grid split targeting an exact lamp count (used for preset entries).
        private void CalcGridForCount(LampConfig cfg, int count, double roomW, double roomD,
                                      out int rows, out int cols)
        {
            int nTotal = Math.Max(1, count);
            double wallFt = ToFeet(cfg.WallMargin);
            double margW  = Math.Min(wallFt, roomW * 0.25);
            double margD  = Math.Min(wallFt, roomD * 0.25);
            double availW = Math.Max(roomW * 0.5, roomW - 2.0 * margW);
            double availD = Math.Max(roomD * 0.5, roomD - 2.0 * margD);
            double ratio  = Math.Max(roomW, roomD) / Math.Min(roomW, roomD);
            if (ratio > 2.5)
            {
                if (roomW >= roomD) { rows = 1; cols = nTotal; }
                else                { cols = 1; rows = nTotal; }
                return;
            }
            double aspect = availW / availD;
            cols = Math.Max(1, (int)Math.Round(Math.Sqrt(nTotal * aspect)));
            rows = Math.Max(1, (int)Math.Round((double)nTotal / cols));
        }

        private ElementId ResolveSymbolId(Document doc, string family, string type)
        {
            try
            {
                var q = new FilteredElementCollector(doc).OfClass(typeof(FamilySymbol)).Cast<FamilySymbol>()
                            .Where(sm => (sm.Family != null ? sm.Family.Name : "") == family).ToList();
                FamilySymbol sym = string.IsNullOrEmpty(type)
                    ? q.FirstOrDefault()
                    : (q.FirstOrDefault(sm => (sm.Name ?? "") == type) ?? q.FirstOrDefault());
                return sym != null ? sym.Id : ElementId.InvalidElementId;
            }
            catch { return ElementId.InvalidElementId; }
        }

        // Places a room preset: each entry's family is spread independently by its count,
        // then the room is renamed to the preset's name.
        private void PlacePreset(UIDocument uidoc, Document doc, Room room, LampPreset preset)
        {
            if (preset == null || preset.Entries == null || preset.Entries.Count == 0)
            { OnStatus?.Invoke("Empty preset."); return; }

            var cfg = Request.Config;
            double wallFt   = ToFeet(cfg.WallMargin);
            double offsetFt = ToFeet(cfg.UKDOffset);
            Level fallbackLvl = ResolveFallbackLevel(doc, cfg);

            var bb = room.get_BoundingBox(null);
            if (bb == null) { OnStatus?.Invoke("Room has no bounding box."); return; }
            double floorZ = room.Level?.Elevation ?? 0;
            double ukdZ = (fallbackLvl != null && _wallOffsetFt.HasValue)
                ? fallbackLvl.Elevation + _wallOffsetFt.Value
                : ((fallbackLvl != null) ? fallbackLvl.Elevation : bb.Max.Z) - offsetFt;
            var level = fallbackLvl ?? GetNearestLevel(doc, ukdZ);
            double roomW = bb.Max.X - bb.Min.X, roomD = bb.Max.Y - bb.Min.Y;
            double angle = CalcAngle(cfg.Rotation, roomW, roomD);

            var plan = new List<Planned>();
            foreach (var entry in preset.Entries)
            {
                var esym = doc.GetElement(ResolveSymbolId(doc, entry.FamilyName, entry.TypeName)) as FamilySymbol;
                if (esym == null) continue;
                int count = Math.Max(1, entry.Count);
                int rows, cols; CalcGridForCount(cfg, count, roomW, roomD, out rows, out cols);
                var pts = CalcPoints(bb, wallFt, rows, cols, ukdZ);
                int added = 0;
                foreach (var pt in pts)
                {
                    if (added >= count) break;
                    if (!IsInRoom(room, new XYZ(pt.X, pt.Y, floorZ + 0.5))) continue;
                    plan.Add(new Planned { Pt = pt, Angle = angle, Lvl = level, Rm = room, Ukd = ukdZ, Sym = esym });
                    added++;
                }
                if (added == 0)   // grid fell outside an odd-shaped room -> drop one at the centre
                {
                    var c = new XYZ((bb.Min.X + bb.Max.X) / 2.0, (bb.Min.Y + bb.Max.Y) / 2.0, ukdZ);
                    plan.Add(new Planned { Pt = c, Angle = angle, Lvl = level, Rm = room, Ukd = ukdZ, Sym = esym });
                }
            }

            if (plan.Count == 0) { OnStatus?.Invoke("Nothing to place for this preset."); return; }

            int placed = RunPlacement(uidoc, doc, null, cfg, plan, "Preset: " + preset.Name);

            if (placed > 0)
                using (var tx = new Transaction(doc, "ME-Tools: Rename Room"))
                {
                    tx.Start();
                    try
                    {
                        var np = room.get_Parameter(BuiltInParameter.ROOM_NAME);
                        if (np != null && !np.IsReadOnly) np.Set(preset.Name);
                    }
                    catch { }
                    tx.Commit();
                }
        }


        // ── Refresh Room: delete existing lamps in room + re-place ───────────
        // Re-applies a (possibly edited) preset to a room that already has it:
        // deletes the existing fixtures inside the room, then places the preset fresh.
        private void UpdatePreset(UIDocument uidoc, Document doc)
        {
            var preset = LampPresetStore.Get(Request.PresetName);
            if (preset == null || preset.Entries == null || preset.Entries.Count == 0)
            { OnStatus?.Invoke("Preset is empty or not found."); return; }

            Room room = null;
            try
            {
                var r = uidoc.Selection.PickObject(
                    Autodesk.Revit.UI.Selection.ObjectType.Element,
                    new RoomFilter(), "Click the room to update with preset '" + preset.Name + "'");
                room = doc.GetElement(r) as Room;
            }
            catch (OperationCanceledException) { OnStatus?.Invoke("Cancelled."); return; }
            catch (Exception ex)               { OnStatus?.Invoke("Error: " + ex.Message); return; }
            if (room == null) { OnStatus?.Invoke("No room selected."); return; }

            double floorZ = room.Level?.Elevation ?? 0;

            // Find existing fixtures inside the room (lighting + fire alarm) to clear out.
            var cats = new[] {
                BuiltInCategory.OST_LightingFixtures,
                BuiltInCategory.OST_LightingDevices,
                BuiltInCategory.OST_FireAlarmDevices
            };
            var toDelete = new List<ElementId>();
            foreach (var cat in cats)
            {
                var instances = new FilteredElementCollector(doc)
                    .OfClass(typeof(FamilyInstance)).OfCategory(cat).Cast<FamilyInstance>();
                foreach (var fi in instances)
                {
                    XYZ testPt = null;
                    if (fi.Location is LocationPoint lp)
                        testPt = new XYZ(lp.Point.X, lp.Point.Y, floorZ + 0.5);
                    else if (fi.HostFace != null)
                    {
                        try
                        {
                            var bb2 = fi.get_BoundingBox(null);
                            if (bb2 != null)
                            {
                                var ctr = (bb2.Min + bb2.Max) / 2.0;
                                testPt = new XYZ(ctr.X, ctr.Y, floorZ + 0.5);
                            }
                        }
                        catch { }
                    }
                    if (testPt == null) continue;
                    try { if (room.IsPointInRoom(testPt)) toDelete.Add(fi.Id); } catch { }
                }
            }

            if (toDelete.Count > 0)
                using (var tx = new Transaction(doc, "ME-Tools: Clear Room For Preset Update"))
                {
                    tx.Start();
                    foreach (var id in toDelete) try { doc.Delete(id); } catch { }
                    tx.Commit();
                }

            // Place the preset fresh (its own transactions handle activation/placement/rename).
            PlacePreset(uidoc, doc, room, preset);
            OnStatus?.Invoke("Updated room with preset '" + preset.Name + "'.");
        }

        private void RefreshRoom(UIDocument uidoc, Document doc)
        {
            // 1. User wählt einen Raum
            Room room = null;
            try
            {
                var r = uidoc.Selection.PickObject(
                    Autodesk.Revit.UI.Selection.ObjectType.Element,
                    new RoomFilter(), "Click a room to refresh lamps");
                room = doc.GetElement(r) as Room;
            }
            catch (OperationCanceledException) { OnStatus?.Invoke("Cancelled."); return; }
            catch (Exception ex)               { OnStatus?.Invoke($"Error: {ex.Message}"); return; }

            if (room == null) { OnStatus?.Invoke("No room selected."); return; }

            var cfg     = Request.Config;
            var sym     = doc.GetElement(Request.SymbolId) as FamilySymbol;
            if (sym == null) { OnStatus?.Invoke("Family not found."); return; }

            double floorZ = room.Level?.Elevation ?? 0;
            Level fallbackLvl = ResolveFallbackLevel(doc, cfg);

            // 2. ALLE Leuchten im Raum finden (unabhängig von Familie)
            var cats = new[] {
                BuiltInCategory.OST_LightingFixtures,
                BuiltInCategory.OST_LightingDevices
            };
            var toDelete = new List<ElementId>();
            foreach (var cat in cats)
            {
                var instances = new FilteredElementCollector(doc)
                    .OfClass(typeof(FamilyInstance))
                    .OfCategory(cat)
                    .Cast<FamilyInstance>();

                foreach (var fi in instances)
                {
                    // Position: direkt oder über Host (face-based)
                    XYZ testPt = null;
                    if (fi.Location is LocationPoint lp)
                        testPt = new XYZ(lp.Point.X, lp.Point.Y, floorZ + 0.5);
                    else if (fi.HostFace != null)
                    {
                        try
                        {
                            var bb2 = fi.get_BoundingBox(null);
                            if (bb2 != null)
                            {
                                var ctr = (bb2.Min + bb2.Max) / 2.0;
                                testPt = new XYZ(ctr.X, ctr.Y, floorZ + 0.5);
                            }
                        }
                        catch { }
                    }
                    if (testPt == null) continue;
                    try { if (room.IsPointInRoom(testPt)) toDelete.Add(fi.Id); } catch { }
                }
            }

            // 3. Aktivieren falls nötig
            if (!sym.IsActive)
            { using (var t = new Transaction(doc,"Activate")){t.Start();sym.Activate();t.Commit();} }

            // 4. Löschen + Neu platzieren in einer Transaction
            double offsetFt = ToFeet(cfg.UKDOffset);
            double wallFt   = ToFeet(cfg.WallMargin);
            // Height: selected Reference Level is authoritative; room bounding-box
            // top is only a fallback (it collapses when volume computation is off).
            var    bb       = room.get_BoundingBox(null);
            if (bb == null) { OnStatus?.Invoke("Room has no bounding box."); return; }
            double ukdZ;
            if (fallbackLvl != null && _wallOffsetFt.HasValue)
                ukdZ = fallbackLvl.Elevation + _wallOffsetFt.Value;   // wall lamp: offset above the floor
            else
                ukdZ = ((fallbackLvl != null)
                                  ? fallbackLvl.Elevation
                                  : (bb != null ? bb.Max.Z : GetUKD(room)))
                              - offsetFt;
            var    level    = fallbackLvl ?? GetNearestLevel(doc, ukdZ);

            double roomW = bb.Max.X - bb.Min.X;
            double roomD = bb.Max.Y - bb.Min.Y;
            double area  = UnitUtils.ConvertFromInternalUnits(room.Area, UnitTypeId.SquareMeters);

            int rows, cols;
            CalcGrid(cfg, area, roomW, roomD, out rows, out cols);
            double angle = CalcAngle(cfg.Rotation, roomW, roomD);
            var pts = CalcPoints(bb, wallFt, rows, cols, ukdZ);

            int placed = 0;
            // Fetched once, not once per lamp -- TryPlaceOnFace (via PlaceLampInstance)
            // was re-scanning every View3D in the document on every single placement
            // whenever "Place on Face" mode was selected. Only bothered with at all
            // when that mode is actually active, to avoid the collector call entirely
            // for the (more common) work-plane placement path.
            View3D v3dForFace = Request.Config.Surface == PlacementSurface.Face ? GetAny3DView(doc) : null;
            using (var tx = new Transaction(doc, "ME-Tools: Refresh Room Lamps"))
            {
                tx.Start();

                // Alte löschen
                foreach (var id in toDelete)
                    try { doc.Delete(id); } catch { }

                // Neu platzieren
                foreach (var pt in pts)
                {
                    if (!IsInRoom(room, new XYZ(pt.X, pt.Y, floorZ + 0.5))) continue;
                    FamilyInstance inst = null;
                    try { inst = PlaceLampInstance(doc, sym, pt, level, room, ukdZ, v3dForFace); }
                    catch { continue; }
                    if (inst == null) continue;
                    doc.Regenerate();
                    try { ElementTransformUtils.RotateElement(doc, inst.Id,
                            Line.CreateBound(pt, pt + XYZ.BasisZ), angle); } catch { }
                    if (level != null)
                        try { var p = inst.get_Parameter(BuiltInParameter.INSTANCE_SCHEDULE_ONLY_LEVEL_PARAM);
                            if (p != null && !p.IsReadOnly) p.Set(level.Id); } catch { }
                    placed++;
                }
                tx.Commit();
            }

            OnStatus?.Invoke($"Refreshed: {toDelete.Count} deleted, {placed} new lamps placed.");
            OnPlaced?.Invoke(placed);
        }


        private enum PlaceDecision { PlaceAll, SkipNear, Cancel }
        private struct Planned { public XYZ Pt; public double Angle; public Level Lvl; public Room Rm; public double Ukd; public FamilySymbol Sym; }

        // Set by PlaceAlongLine so PlaceDimensions can use the actual guide line endpoints
        // rather than the room bounding box. Null = use room/spread.
        private XYZ _guideLineStart, _guideLineEnd;

        // -- Existing lighting-fixture locations (XY) for overlap detection --
        private List<XYZ> ExistingFixtureXY(Document doc)
        {
            var list = new List<XYZ>();
            try
            {
                var col = new FilteredElementCollector(doc)
                    .OfCategory(BuiltInCategory.OST_LightingFixtures).WhereElementIsNotElementType();
                foreach (var e in col)
                    if (e.Location is LocationPoint lp) list.Add(lp.Point);
            }
            catch { }
            return list;
        }

        private bool NearAny(XYZ p, List<XYZ> ex, double tolFt)
        {
            double t2 = tolFt * tolFt;
            foreach (var e in ex)
            {
                double dx = p.X - e.X, dy = p.Y - e.Y;
                if (dx * dx + dy * dy <= t2) return true;
            }
            return false;
        }

        // -- Preview count + overlap warning. Returns the user's choice. --
        private PlaceDecision ConfirmPlace(int total, int conflicts, string op)
        {
            var td = new TaskDialog("Lamp Placer")
            {
                MainInstruction = "Place " + total + " lamp(s)?",
                MainContent     = op + (conflicts > 0
                    ? "\n" + conflicts + " would land within the overlap distance of an existing fixture."
                    : ""),
                AllowCancellation = true
            };
            if (conflicts > 0)
            {
                td.CommonButtons = TaskDialogCommonButtons.Cancel;
                td.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, "Place all " + total);
                td.AddCommandLink(TaskDialogCommandLinkId.CommandLink2, "Skip the " + conflicts + " near existing fixtures");
                var r = td.Show();
                if (r == TaskDialogResult.CommandLink1) return PlaceDecision.PlaceAll;
                if (r == TaskDialogResult.CommandLink2) return PlaceDecision.SkipNear;
                return PlaceDecision.Cancel;
            }
            td.CommonButtons = TaskDialogCommonButtons.Yes | TaskDialogCommonButtons.No;
            td.DefaultButton = TaskDialogResult.Yes;
            return td.Show() == TaskDialogResult.Yes ? PlaceDecision.PlaceAll : PlaceDecision.Cancel;
        }

        // -- Shared finalize: confirm -> single undo group -> place -> CSV log --
        private int RunPlacement(UIDocument uidoc, Document doc, FamilySymbol sym, LampConfig cfg, List<Planned> plan, string op)
        {
            if (plan == null || plan.Count == 0) { OnStatus?.Invoke("Nothing to place."); return 0; }

            // Capture guide line endpoints for this run's PlaceDimensions, then clear
            // so they don't bleed into the next area/grid/preset placement.
            var guideStart = _guideLineStart;
            var guideEnd   = _guideLineEnd;
            _guideLineStart = null;
            _guideLineEnd   = null;

            var    existing  = ExistingFixtureXY(doc);
            double tolFt     = ToFeet(cfg.OverlapThreshold);
            int    conflicts = existing.Count > 0 ? plan.Count(pl => NearAny(pl.Pt, existing, tolFt)) : 0;

            var decision = ConfirmPlace(plan.Count, conflicts, op);
            if (decision == PlaceDecision.Cancel) { OnStatus?.Invoke("Cancelled."); return -1; }
            bool skipNear = decision == PlaceDecision.SkipNear;

            var placedIds = new List<ElementId>();
            // Same fix as RefreshRoom above: fetched once for this whole batch,
            // not once per lamp, and only when face placement is actually selected.
            View3D v3dForFace = Request.Config.Surface == PlacementSurface.Face ? GetAny3DView(doc) : null;
            using (var tg = new TransactionGroup(doc, "ME-Tools: " + op))
            {
                tg.Start();
                using (var tx = new Transaction(doc, "ME-Tools: " + op))
                {
                    tx.Start();
                    if (sym != null && !sym.IsActive) { sym.Activate(); doc.Regenerate(); }
                    foreach (var pl in plan)
                    {
                        if (skipNear && NearAny(pl.Pt, existing, tolFt)) continue;
                        var s = pl.Sym ?? sym;
                        if (s == null) continue;
                        if (!s.IsActive) { s.Activate(); doc.Regenerate(); }
                        FamilyInstance inst = null;
                        try { inst = PlaceLampInstance(doc, s, pl.Pt, pl.Lvl, pl.Rm, pl.Ukd, v3dForFace); }
                        catch { continue; }
                        if (inst == null) continue;
                        doc.Regenerate();
                        // Always rotate: Deg0 = -PI/4 corrects the family's native 45-degree
                        // tilt to horizontal. Skipping when angle==0 would leave the 45-deg tilt.
                        try { ElementTransformUtils.RotateElement(doc, inst.Id,
                            Line.CreateBound(pl.Pt, pl.Pt + XYZ.BasisZ), pl.Angle); } catch { }
                        if (pl.Lvl != null)
                            try { var p = inst.get_Parameter(BuiltInParameter.INSTANCE_SCHEDULE_ONLY_LEVEL_PARAM);
                                if (p != null && !p.IsReadOnly) p.Set(pl.Lvl.Id); } catch { }
                        placedIds.Add(inst.Id);
                    }
                    tx.Commit();
                }
                tg.Assimilate();
            }

            string log = ExportLog(doc, op, placedIds);
            string msg = "Done: " + placedIds.Count + " lamps placed (" + op + ").";
            if (!string.IsNullOrEmpty(log)) msg += " Log saved to Documents/METools.";

            // Dimensions based on selected mode
            if (placedIds.Count > 0)
            {
                if (cfg.Dimensions == DimensionMode.Auto)
                    PlaceDimensions(doc, placedIds, plan, guideStart, guideEnd);
                else if (cfg.Dimensions == DimensionMode.Custom)
                    PlaceCustomDimensions(uidoc, doc);  // uidoc now available
                // DimensionMode.None: skip
            }

            OnStatus?.Invoke(msg);
            OnPlaced?.Invoke(placedIds.Count);
            return placedIds.Count;
        }

        // ── Custom dimension placement ──────────────────────────────────────
        // User picks pt1 (lamp/element), then pt2 (anywhere on a wall face).
        // The tool draws a dimension measuring the PERPENDICULAR distance from
        // pt1 to the wall line through pt2. It does this by:
        // 1) Finding the wall's run direction via nearby room boundary segments
        //    (which mirror the walls in the linked Architektur file).
        // 2) Projecting pt1 onto the wall line → perpendicular foot pt2_proj.
        // 3) Dimensioning pt1 → pt2_proj (true shortest distance to wall).
        // This gives correct distances even when the user clicks a corner.
        private void PlaceCustomDimensions(UIDocument uidoc, Document doc)
        {
            var view = doc.ActiveView as ViewPlan;
            if (view == null) { OnStatus?.Invoke("Custom dims: open a floor plan view first."); return; }

            var dimType = new FilteredElementCollector(doc)
                .OfClass(typeof(DimensionType)).Cast<DimensionType>()
                .FirstOrDefault(dt => dt.StyleType == DimensionStyleType.Linear);
            if (dimType == null) { OnStatus?.Invoke("Custom dims: no linear dimension type found."); return; }

            // Collect wall centerline segments from the linked Architectural model.
            // Walls are not in the active MEP document, so we query all RevitLinkInstances
            // for their Wall elements. Each wall's LocationCurve gives us the centerline
            // which we use to identify wall direction and position.
            // Falls back to room boundary segments if no linked walls are found.
            var boundarySegments = new List<(XYZ Start, XYZ End)>();
            try
            {
                // Try linked files first
                bool foundLinkedWalls = false;
                foreach (var link in new FilteredElementCollector(doc)
                    .OfClass(typeof(RevitLinkInstance)).Cast<RevitLinkInstance>())
                {
                    try
                    {
                        var linkDoc = link.GetLinkDocument();
                        if (linkDoc == null) continue;
                        var transform = link.GetTotalTransform();
                        foreach (var wall in new FilteredElementCollector(linkDoc)
                            .OfClass(typeof(Wall)).Cast<Wall>())
                        {
                            try
                            {
                                if (wall.Location is LocationCurve lc && lc.Curve != null)
                                {
                                    var c = lc.Curve;
                                    // Transform from linked doc coordinates to host coordinates
                                    var ptA = transform.OfPoint(c.GetEndPoint(0));
                                    var ptB = transform.OfPoint(c.GetEndPoint(1));
                                    boundarySegments.Add((ptA, ptB));
                                    foundLinkedWalls = true;
                                }
                            }
                            catch { }
                        }
                    }
                    catch { }
                }

                // Fallback: room boundary segments from active doc
                if (!foundLinkedWalls)
                {
                    var opts = new SpatialElementBoundaryOptions
                        { SpatialElementBoundaryLocation = SpatialElementBoundaryLocation.Finish };
                    foreach (var room in new FilteredElementCollector(doc)
                        .OfCategory(BuiltInCategory.OST_Rooms).WhereElementIsNotElementType()
                        .Cast<Room>().Where(r => r.Area > 0))
                    {
                        foreach (var loop in room.GetBoundarySegments(opts))
                            foreach (var seg in loop)
                            {
                                try
                                {
                                    var c = seg.GetCurve();
                                    if (c != null)
                                        boundarySegments.Add((c.GetEndPoint(0), c.GetEndPoint(1)));
                                }
                                catch { }
                            }
                    }
                }
            }
            catch { }

            OnStatus?.Invoke($"Custom dim ready. {boundarySegments.Count} wall segments loaded. Pick FIRST point (ESC to finish).");

            double z    = view.GenLevel?.Elevation ?? 0;
            int    made = 0;

            while (true)
            {
                XYZ pt1 = null, pt2raw = null;
                try
                {
                    OnStatus?.Invoke($"Custom dim {made + 1}: pick FIRST point — lamp or element (ESC to finish).");
                    pt1 = uidoc.Selection.PickPoint(
                        Autodesk.Revit.UI.Selection.ObjectSnapTypes.Midpoints |
                        Autodesk.Revit.UI.Selection.ObjectSnapTypes.Endpoints |
                        Autodesk.Revit.UI.Selection.ObjectSnapTypes.Centers,
                        "Pick lamp / element center (ESC to finish)");
                }
                catch { break; }

                try
                {
                    OnStatus?.Invoke($"Custom dim {made + 1}: pick SECOND point — click anywhere on the wall.");
                    pt2raw = uidoc.Selection.PickPoint(
                        Autodesk.Revit.UI.Selection.ObjectSnapTypes.Nearest |
                        Autodesk.Revit.UI.Selection.ObjectSnapTypes.Midpoints |
                        Autodesk.Revit.UI.Selection.ObjectSnapTypes.Endpoints,
                        "Click anywhere on the wall face");
                }
                catch { break; }

                if (pt1 == null || pt2raw == null) break;

                // Find the wall the user clicked on: the boundary segment where pt2raw
                // is closest to lying ON the segment line (smallest perpendicular distance
                // from pt2raw to the segment's infinite line). Then project pt1
                // perpendicularly onto that wall line.
                XYZ wallDir = null;
                XYZ wallOrigin = null;
                XYZ pt2     = pt2raw;
                double bestSegDist = double.MaxValue;

                foreach (var (segA, segB) in boundarySegments)
                {
                    var segVec = (segB - segA);
                    double segLen = segVec.GetLength();
                    if (segLen < 1e-6) continue;
                    var segDirN = segVec.Normalize();

                    // Distance from pt2raw to the infinite line of this segment
                    var toP2 = pt2raw - segA;
                    double projLen = toP2.DotProduct(segDirN);
                    var foot = segA + segDirN.Multiply(projLen);
                    double distToLine = (pt2raw - foot).GetLength();

                    // Only consider this segment if pt2raw is actually near its line
                    // (within 800mm) — this identifies which wall face was clicked.
                    // Use a generous tolerance since room boundary = wall centerline,
                    // and users typically click on the wall face (offset from centerline).
                    if (distToLine < bestSegDist && distToLine < ToFeet(800))
                    {
                        bestSegDist = distToLine;
                        wallDir    = segDirN;
                        wallOrigin = segA;
                    }
                }

                if (wallDir != null)
                {
                    // Wall normal = direction perpendicular to wall (in plan)
                    // This is the direction the dimension should measure
                    var wallNorm = new XYZ(-wallDir.Y, wallDir.X, 0).Normalize();

                    // Signed distance from pt1 to the wall line, measured along wall normal
                    double dNorm = (wallOrigin - pt1).DotProduct(wallNorm);

                    // pt2 = pt1 shifted along wall normal by that distance → lands on wall line
                    pt2 = new XYZ(pt1.X + wallNorm.X * dNorm,
                                  pt1.Y + wallNorm.Y * dNorm,
                                  pt1.Z);
                    double distMm = UnitUtils.ConvertFromInternalUnits(Math.Abs((wallOrigin - pt1).DotProduct(wallNorm)), UnitTypeId.Millimeters);
                    OnStatus?.Invoke($"Wall found ({bestSegDist*304.8:0}mm from click). Perpendicular distance: {distMm:0}mm");
                }
                else
                {
                    // Fallback: no boundary segment found near click.
                    // Snap dimension direction to the dominant axis (X or Y).
                    var rough = (pt2raw - pt1);
                    if (rough.GetLength() < 1e-6) { OnStatus?.Invoke("Points coincident — try again."); continue; }
                    if (Math.Abs(rough.X) >= Math.Abs(rough.Y))
                    {
                        // Mostly horizontal vector → horizontal dimension (measure X distance)
                        pt2 = new XYZ(pt2raw.X, pt1.Y, pt1.Z);
                        OnStatus?.Invoke("No wall found near click — using horizontal fallback.");
                    }
                    else
                    {
                        // Mostly vertical vector → vertical dimension (measure Y distance)
                        pt2 = new XYZ(pt1.X, pt2raw.Y, pt1.Z);
                        OnStatus?.Invoke("No wall found near click — using vertical fallback.");
                    }
                }

                double dist = (pt2 - pt1).GetLength();
                if (dist < 1e-6) { OnStatus?.Invoke("Points are coincident — try again."); continue; }

                try
                {
                    using (var tx = new Transaction(doc, "ME-Tools: Custom Dimension"))
                    {
                        tx.Start();
                        double tick   = ToFeet(200);
                        var    dir    = (pt2 - pt1).Normalize();
                        // Perpendicular direction for tick marks (90° from dimension direction)
                        var    perp   = new XYZ(-dir.Y, dir.X, 0).Normalize();
                        double offset = ToFeet(400);  // how far to offset the dim line

                        var dl1 = doc.Create.NewDetailCurve(view, Line.CreateBound(
                            new XYZ(pt1.X, pt1.Y, z) + perp.Multiply(offset - tick),
                            new XYZ(pt1.X, pt1.Y, z) + perp.Multiply(offset + tick)));
                        var dl2 = doc.Create.NewDetailCurve(view, Line.CreateBound(
                            new XYZ(pt2.X, pt2.Y, z) + perp.Multiply(offset - tick),
                            new XYZ(pt2.X, pt2.Y, z) + perp.Multiply(offset + tick)));

                        var arr = new ReferenceArray();
                        arr.Append(dl1.GeometryCurve.Reference);
                        arr.Append(dl2.GeometryCurve.Reference);

                        var dimLine = Line.CreateBound(
                            new XYZ(pt1.X, pt1.Y, z) + perp.Multiply(offset),
                            new XYZ(pt2.X, pt2.Y, z) + perp.Multiply(offset));

                        doc.Create.NewDimension(view, dimLine, arr, dimType);
                        tx.Commit();
                        made++;
                    }
                }
                catch (Exception ex) { OnStatus?.Invoke("Dim failed: " + ex.Message); }
            }

            OnStatus?.Invoke($"Custom dimensions: {made} created.");
        }

        // ── Dimension placement ──────────────────────────────────────────────
        // Creates a chain dimension: wallEnd ─ lamp ─ lamp ─ ... ─ lamp ─ wallEnd
        // in a single multi-segment Revit dimension string.
        // For Line mode: chain follows the actual guide line direction + endpoints.
        // For Area/Grid mode: chain follows the dominant lamp-spread axis + room BB.
        private void PlaceDimensions(Document doc, List<ElementId> lampIds, List<Planned> plan,
                                     XYZ guideStart, XYZ guideEnd)
        {
            var view = doc.ActiveView as ViewPlan;
            if (view == null) return;

            var dimType = new FilteredElementCollector(doc)
                .OfClass(typeof(DimensionType)).Cast<DimensionType>()
                .FirstOrDefault(dt => dt.StyleType == DimensionStyleType.Linear);
            if (dimType == null) return;

            // Collect placed lamp XY positions
            var lampPts = new List<XYZ>();
            Room room = null;
            for (int i = 0; i < lampIds.Count && i < plan.Count; i++)
            {
                var fi = doc.GetElement(lampIds[i]) as FamilyInstance;
                if (fi == null || !(fi.Location is LocationPoint lp)) continue;
                lampPts.Add(lp.Point);
                if (room == null) room = plan[i].Rm;
            }
            if (lampPts.Count == 0) return;

            double z = view.GenLevel?.Elevation ?? lampPts[0].Z;

            // ── Determine chain direction and endpoints ─────────────────────
            XYZ chainDir, chainStart, chainEnd;

            if (guideStart != null && guideEnd != null)
            {
                // Line mode: use the actual guide line
                var raw = guideEnd - guideStart;
                chainDir   = raw.GetLength() > 1e-6 ? raw.Normalize() : XYZ.BasisX;
                chainStart = guideStart;
                chainEnd   = guideEnd;
            }
            else
            {
                // Area/Grid mode: dominant spread axis
                double spanX = lampPts.Max(p => p.X) - lampPts.Min(p => p.X);
                double spanY = lampPts.Max(p => p.Y) - lampPts.Min(p => p.Y);
                chainDir = spanX >= spanY ? XYZ.BasisX : XYZ.BasisY;

                // Chain endpoints from room bounding box
                var bb = room?.get_BoundingBox(null);
                if (chainDir.X > 0.5)   // X axis
                {
                    double midY = lampPts.Average(p => p.Y);
                    double x0 = bb?.Min.X ?? lampPts.Min(p => p.X) - ToFeet(500);
                    double x1 = bb?.Max.X ?? lampPts.Max(p => p.X) + ToFeet(500);
                    chainStart = new XYZ(x0, midY, z);
                    chainEnd   = new XYZ(x1, midY, z);
                }
                else                    // Y axis
                {
                    double midX = lampPts.Average(p => p.X);
                    double y0 = bb?.Min.Y ?? lampPts.Min(p => p.Y) - ToFeet(500);
                    double y1 = bb?.Max.Y ?? lampPts.Max(p => p.Y) + ToFeet(500);
                    chainStart = new XYZ(midX, y0, z);
                    chainEnd   = new XYZ(midX, y1, z);
                }
            }

            // ── Sort lamps along the chain ──────────────────────────────────
            var ordered = lampPts
                .OrderBy(p => (p - chainStart).DotProduct(chainDir))
                .ToList();

            // ── Perpendicular offset for the dimension line ─────────────────
            var perpDir = new XYZ(-chainDir.Y, chainDir.X, 0);
            if (perpDir.GetLength() < 1e-6) perpDir = XYZ.BasisY;
            perpDir = perpDir.Normalize();
            double perpOff = ToFeet(700);   // offset from lamp row
            double tick    = ToFeet(300);   // tick half-length

            // ── Build station list: chainStart, each lamp, chainEnd ─────────
            // Stations are absolute XYZ points on the chain line
            var stations = new List<XYZ>();
            stations.Add(new XYZ(chainStart.X, chainStart.Y, z));
            foreach (var p in ordered)
            {
                // Project lamp onto chain line through chainStart
                double d = (p - chainStart).DotProduct(chainDir);
                stations.Add(chainStart + chainDir.Multiply(d));
            }
            stations.Add(new XYZ(chainEnd.X, chainEnd.Y, z));

            // Offset all stations perpendicular for the dim line
            var dimPts = stations.Select(st => st + perpDir.Multiply(-perpOff)).ToList();

            int made = 0, failed = 0;
            using (var tx = new Transaction(doc, "ME-Tools: Lamp Dimensions"))
            {
                tx.Start();
                try
                {
                    var refs = new List<Reference>();
                    foreach (var dp in dimPts)
                    {
                        try
                        {
                            // Tick perpendicular to chain at each station
                            var p1 = dp + perpDir.Multiply(-tick);
                            var p2 = dp + perpDir.Multiply( tick);
                            if ((p2 - p1).GetLength() < 1e-6) { refs.Add(null); continue; }
                            var dl = doc.Create.NewDetailCurve(view, Line.CreateBound(p1, p2));
                            refs.Add(dl.GeometryCurve.Reference);
                        }
                        catch { refs.Add(null); }
                    }

                    var arr = new ReferenceArray();
                    foreach (var r in refs) if (r != null) arr.Append(r);

                    if (arr.Size >= 2)
                    {
                        var dStart = dimPts.First();
                        var dEnd   = dimPts.Last();
                        // Ensure the line is not degenerate
                        if ((dEnd - dStart).GetLength() > 1e-6)
                        {
                            try
                            {
                                doc.Create.NewDimension(view,
                                    Line.CreateBound(dStart, dEnd), arr, dimType);
                                made++;
                            }
                            catch { failed++; }
                        }
                    }
                }
                catch { failed++; }
                tx.Commit();
            }

            OnStatus?.Invoke($"Dimensions: {made} created (static — re-run after moving lamps).");
        }

        // -- Write a CSV log of the placed lamps to Documents/METools. Returns the path. --
        private string ExportLog(Document doc, string op, List<ElementId> ids)
        {
            if (ids == null || ids.Count == 0) return "";
            try
            {
                string dir = System.IO.Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "METools");
                System.IO.Directory.CreateDirectory(dir);
                string file = System.IO.Path.Combine(dir,
                    "LampPlacer_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".csv");
                var sb = new System.Text.StringBuilder();
                sb.AppendLine("ElementId,Operation,Family,Type,Level,Room,X_mm,Y_mm,Z_mm");
                foreach (var id in ids)
                {
                    var fi = doc.GetElement(id) as FamilyInstance;
                    if (fi == null) continue;
                    string fam = fi.Symbol?.Family?.Name ?? "";
                    string typ = fi.Symbol?.Name ?? "";
                    string lvl = (doc.GetElement(fi.LevelId) as Level)?.Name ?? "";
                    string room = "";
                    try { var rm = fi.Room; if (rm != null) room = rm.Name; } catch { }
                    double xm = 0, ym = 0, zm = 0;
                    if (fi.Location is LocationPoint lp)
                    {
                        xm = UnitUtils.ConvertFromInternalUnits(lp.Point.X, UnitTypeId.Millimeters);
                        ym = UnitUtils.ConvertFromInternalUnits(lp.Point.Y, UnitTypeId.Millimeters);
                        zm = UnitUtils.ConvertFromInternalUnits(lp.Point.Z, UnitTypeId.Millimeters);
                    }
                    sb.AppendLine(string.Join(",", id.ToString(), Csv(op), Csv(fam), Csv(typ),
                        Csv(lvl), Csv(room), xm.ToString("0"), ym.ToString("0"), zm.ToString("0")));
                }
                System.IO.File.WriteAllText(file, sb.ToString());
                return file;
            }
            catch { return ""; }
        }

        private string Csv(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            if (s.IndexOf(',') >= 0 || s.IndexOf('"') >= 0) return "\"" + s.Replace("\"", "\"\"") + "\"";
            return s;
        }

        // -- Place a Rows x Cols grid inside a user-clicked 4-corner area ------
        private void PlaceGrid(UIDocument uidoc, Document doc)
        {
            var cfg = Request.Config;
            var sym = doc.GetElement(Request.SymbolId) as FamilySymbol;
            if (sym == null) { OnStatus?.Invoke("Family not found."); return; }

            // 1. Pick the 4 corners of the area
            var corners = new List<XYZ>();
            try
            {
                for (int i = 0; i < 4; i++)
                    corners.Add(uidoc.Selection.PickPoint("Click corner " + (i + 1) + " of 4 (in order) - defines the grid area"));
            }
            catch (OperationCanceledException) { OnStatus?.Invoke("Cancelled."); return; }
            catch (Exception ex) { OnStatus?.Invoke("Error: " + ex.Message); return; }

            // order the corners around their centroid so the quad is not self-intersecting
            double cx = corners.Average(p => p.X), cy = corners.Average(p => p.Y);
            corners.Sort((a, b) => Math.Atan2(a.Y - cy, a.X - cx)
                                   .CompareTo(Math.Atan2(b.Y - cy, b.X - cx)));
            XYZ A = corners[0], B = corners[1], C = corners[2], D = corners[3];

            // 2. Reference level (authoritative for height) + rooms fallback
            Level  fallbackLvl  = ResolveFallbackLevel(doc, cfg);
            double fallbackElev = fallbackLvl?.Elevation ?? 0;
            double offsetFt     = ToFeet(cfg.UKDOffset);
            double roomTestZ    = fallbackElev + 0.5;
            var allRooms = new FilteredElementCollector(doc)
                .OfClass(typeof(SpatialElement)).OfCategory(BuiltInCategory.OST_Rooms)
                .Cast<Room>().Where(r => r != null && r.Area > 0).ToList();
            // Fetched once up front instead of inside the grid loop below — GetNearestLevel
            // was re-scanning every Level in the document on every single grid point.
            var allLevels = fallbackLvl != null
                ? null
                : new FilteredElementCollector(doc).OfClass(typeof(Level)).Cast<Level>().ToList();

            int rows = Math.Max(1, cfg.ManualRows);
            int cols = Math.Max(1, cfg.ManualCols);

            // rotation: Auto aligns lamps to the first quad edge (A->B)
            double baseAng = Math.Atan2(B.Y - A.Y, B.X - A.X);
            double angle = cfg.Rotation == RotationMode.Deg90 ? Math.PI / 2.0
                         : cfg.Rotation == RotationMode.Deg0  ? 0.0
                         :                                      baseAng;

            // 4. Build grid points (bilinear over the quad), then place via shared pipeline
            var plan = new List<Planned>();
            for (int r = 0; r < rows; r++)
            {
                double v = (r + 0.5) / rows;
                for (int ci = 0; ci < cols; ci++)
                {
                    double u = (ci + 0.5) / cols;
                    XYZ top = A + (B - A) * u;   // A->B edge
                    XYZ bot = D + (C - D) * u;   // D->C edge
                    XYZ q   = top + (bot - top) * v;

                    Room room = allRooms.FirstOrDefault(rm =>
                    { try { return rm.IsPointInRoom(new XYZ(q.X, q.Y, roomTestZ)); } catch { return false; } });

                    double ukdZ; Level level;
                    if (fallbackLvl != null) { ukdZ = _wallOffsetFt.HasValue ? fallbackElev + _wallOffsetFt.Value : fallbackElev - offsetFt; level = fallbackLvl; }
                    else if (room != null)
                    {
                        var bb = room.get_BoundingBox(null);
                        ukdZ  = (bb != null ? bb.Max.Z : GetUKD(room)) - offsetFt;
                        level = GetNearestLevel(allLevels, ukdZ);
                    }
                    else { continue; }

                    plan.Add(new Planned { Pt = new XYZ(q.X, q.Y, ukdZ), Angle = angle, Lvl = level, Rm = room, Ukd = ukdZ });
                }
            }
            RunPlacement(uidoc, doc, sym, cfg, plan, "Place Grid");
        }

        // -- Place lamps along guide lines drawn with Revit's Detail Line tool --
        private void PlaceAlongLine(UIDocument uidoc, Document doc)
        {
            var cfg = Request.Config;
            var sym = doc.GetElement(Request.SymbolId) as FamilySymbol;
            if (sym == null) { OnStatus?.Invoke("Family not found."); return; }

            // 1. Select the guide line(s) drawn with Revit's Detail Line tool
            OnWaiting?.Invoke(true);   // tell the window we are waiting for selection
            IList<Reference> refs;
            try { refs = uidoc.Selection.PickObjects(ObjectType.Element, new LineFilter(),
                    "Click the guide line(s) you drew, then click Finish (green check) at the top"); }
            catch (OperationCanceledException) { OnWaiting?.Invoke(false); OnStatus?.Invoke("Cancelled."); return; }
            catch (Exception ex)               { OnWaiting?.Invoke(false); OnStatus?.Invoke("Error: " + ex.Message); return; }
            OnWaiting?.Invoke(false);  // selection done

            var guideIds  = new List<ElementId>();
            var polylines = new List<List<XYZ>>();
            foreach (var r in refs)
            {
                var ce  = doc.GetElement(r) as CurveElement;
                var crv = ce?.GeometryCurve;
                if (crv == null) continue;
                var pts = crv.Tessellate();
                if (pts == null || pts.Count < 2) continue;
                polylines.Add(new List<XYZ>(pts));
                guideIds.Add(ce.Id);
            }
            if (polylines.Count == 0) { OnStatus?.Invoke("No usable guide lines selected."); return; }

            // 2. Resolve reference level + rooms for per-point UKD
            Level  fallbackLvl  = ResolveFallbackLevel(doc, cfg);
            double fallbackElev = fallbackLvl?.Elevation ?? 0;
            double offsetFt     = ToFeet(cfg.UKDOffset);
            double roomTestZ    = fallbackElev + 0.5;
            var allRooms = new FilteredElementCollector(doc)
                .OfClass(typeof(SpatialElement)).OfCategory(BuiltInCategory.OST_Rooms)
                .Cast<Room>().Where(r => r != null && r.Area > 0).ToList();
            // Fetched once up front instead of inside the per-spot loop below — same fix
            // as the grid-placement method just above.
            var allLevels = fallbackLvl != null
                ? null
                : new FilteredElementCollector(doc).OfClass(typeof(Level)).Cast<Level>().ToList();

            // 4. Build placement points (XY + tangent) per mode
            var spots = new List<KeyValuePair<XYZ, double>>();
            if (cfg.LineMode == LineMode.ByCount)
            {
                // Centered subdivision: divide the line into N equal segments and
                // place one lamp at the center of each. This makes the inter-lamp
                // gap L/N and the end-gap (line start/end to first/last lamp)
                // exactly HALF the inter-lamp gap -- e.g. for N=3, lamp 2 sits
                // exactly at the line's center; for N=4, the midpoint between
                // lamps 2 and 3 sits exactly at the line's center. Previously
                // this used L/(N+1) with lamps at 1..N, which made the end-gaps
                // equal to the FULL inter-lamp gap instead of half of it.
                int n = Math.Max(1, cfg.LineCount);
                foreach (var chain in polylines)
                {
                    double L = PolylineLength(chain);
                    double d = L / n;
                    for (int i = 0; i < n; i++)
                    { double tan; XYZ p = PointAtArcLength(chain, d * (i + 0.5), out tan);
                      spots.Add(new KeyValuePair<XYZ, double>(p, tan)); }
                }
            }
            else // BySpacing
            {
                // Reduces to the same centered-subdivision placement as By
                // Count (see above) -- "spacing" here just decides what COUNT
                // to subdivide the line into, computed directly from line
                // length / spacing, with no prompt at all. This is what
                // removes every issue reported with the old flow: no
                // first-lamp click to anchor from (so the first lamp is
                // never stuck wherever you happened to click), no overshoot
                // past the line's end (impossible by construction -- centered
                // subdivision only ever places within [0, L]), and no
                // interruption asking to confirm a count or re-enter spacing --
                // it just places as many as fit.
                double spacingFt = ToFeet(cfg.LineSpacing);
                if (spacingFt <= 0) spacingFt = ToFeet(2000.0); // defensive fallback, shouldn't normally hit

                foreach (var chain in polylines)
                {
                    double L = PolylineLength(chain);
                    int n = Math.Max(1, (int)Math.Round(L / spacingFt));
                    double d = L / n;
                    for (int i = 0; i < n; i++)
                    { double tan; XYZ p = PointAtArcLength(chain, d * (i + 0.5), out tan);
                      spots.Add(new KeyValuePair<XYZ, double>(p, tan)); }
                }
            }

            // 5. Resolve each spot to a planned lamp, then place via the shared pipeline
            var plan = new List<Planned>();
            foreach (var spot in spots)
            {
                XYZ pt2d = spot.Key; double tangent = spot.Value;
                Room room = allRooms.FirstOrDefault(r =>
                { try { return r.IsPointInRoom(new XYZ(pt2d.X, pt2d.Y, roomTestZ)); } catch { return false; } });

                double ukdZ; Level level;
                if (fallbackLvl != null) { ukdZ = _wallOffsetFt.HasValue ? fallbackElev + _wallOffsetFt.Value : fallbackElev - offsetFt; level = fallbackLvl; }
                else if (room != null)
                {
                    var bb = room.get_BoundingBox(null);
                    ukdZ  = (bb != null ? bb.Max.Z : GetUKD(room)) - offsetFt;
                    level = GetNearestLevel(allLevels, ukdZ);
                }
                else { continue; }

                // Rotation: use CalcAngle (respects the selected rotation mode / Project North)
                // as the lamp's orientation. For "Perpendicular to line" we add 90deg to the
                // base angle so the lamp faces across the guide line instead of along it.
                // We do NOT use the raw tangent as the rotation — that would rotate every lamp
                // to face the guide line direction, ignoring the chosen orientation.
                double baseAngle = CalcAngle(cfg.Rotation, 1, 1);
                double rotAngle  = cfg.LineRotation == LineRotation.Perpendicular
                    ? baseAngle + Math.PI / 2.0 : baseAngle;
                plan.Add(new Planned { Pt = new XYZ(pt2d.X, pt2d.Y, ukdZ), Angle = rotAngle, Lvl = level, Rm = room, Ukd = ukdZ });
            }

            // Store guide line endpoints for PlaceDimensions
            if (polylines.Count > 0)
            {
                double tan0, tanN;
                _guideLineStart = PointAtArcLength(polylines[0], 0, out tan0);
                _guideLineEnd   = PointAtArcLength(polylines[0], PolylineLength(polylines[0]), out tanN);
            }
            else { _guideLineStart = null; _guideLineEnd = null; }

            int placed = RunPlacement(uidoc, doc, sym, cfg, plan, "Place Along Line");

            // 6. Offer to delete the selected guide lines (only if placement was not cancelled)
            if (placed >= 0 && guideIds.Count > 0)
            {
                var keep = new TaskDialog("Lamp Placer")
                {
                    MainInstruction = "Keep the guide lines?",
                    MainContent     = placed + " lamps placed along " + polylines.Count + " guide line(s).",
                    CommonButtons   = TaskDialogCommonButtons.Yes | TaskDialogCommonButtons.No,
                    DefaultButton   = TaskDialogResult.Yes
                };
                if (keep.Show() != TaskDialogResult.Yes)
                    using (var tx = new Transaction(doc, "ME-Tools: Delete Guide Lines"))
                    { tx.Start(); try { doc.Delete(guideIds); } catch { } tx.Commit(); }
            }
        }

        // -- Project a point (XY) onto a polyline; returns arc-length, sets nearest distance --
        // -- Point (+ tangent angle) at an arc-length along a polyline (extends past the end) --
        private XYZ PointAtArcLength(List<XYZ> chain, double s, out double tangent)
        {
            tangent = 0;
            if (chain == null || chain.Count == 0) return XYZ.Zero;
            if (chain.Count == 1) return chain[0];
            double acc = 0;
            for (int i = 0; i + 1 < chain.Count; i++)
            {
                XYZ a = chain[i], b = chain[i + 1];
                double dx = b.X - a.X, dy = b.Y - a.Y;
                double segLen = Math.Sqrt(dx * dx + dy * dy);
                tangent = Math.Atan2(dy, dx);
                bool last = (i + 2 >= chain.Count);
                if (s <= acc + segLen || last)
                {
                    double t = segLen > 1e-9 ? (s - acc) / segLen : 0;   // may exceed 1 on last seg (extend)
                    return new XYZ(a.X + dx * t, a.Y + dy * t, a.Z);
                }
                acc += segLen;
            }
            return chain[chain.Count - 1];
        }

        // -- Total XY length of a polyline --
        private double PolylineLength(List<XYZ> chain)
        {
            double total = 0;
            for (int i = 0; i + 1 < chain.Count; i++)
            {
                double dx = chain[i + 1].X - chain[i].X, dy = chain[i + 1].Y - chain[i].Y;
                total += Math.Sqrt(dx * dx + dy * dy);
            }
            return total;
        }

        // ── Find any usable 3D view for face raycasting ──────────────────
        private View3D GetAny3DView(Document doc)
        {
            return new FilteredElementCollector(doc).OfClass(typeof(View3D))
                .Cast<View3D>().FirstOrDefault(v => v != null && !v.IsTemplate);
        }

        // ── Host a lamp on the ceiling/floor face above the point ──────────
        private FamilyInstance TryPlaceOnFace(Document doc, FamilySymbol sym, XYZ pt, View3D v3d)
        {
            try
            {
                if (v3d == null) return null;
                var filter = new LogicalOrFilter(
                    new ElementCategoryFilter(BuiltInCategory.OST_Ceilings),
                    new ElementCategoryFilter(BuiltInCategory.OST_Floors));
                var ri = new ReferenceIntersector(filter, FindReferenceTarget.Face, v3d);
                var origin = new XYZ(pt.X, pt.Y, pt.Z - ToFeet(50));  // start just below the ceiling
                var hit = ri.FindNearest(origin, XYZ.BasisZ);          // ray straight up
                var faceRef = hit?.GetReference();
                if (faceRef == null) return null;
                var hitPt = origin + XYZ.BasisZ * hit.Proximity;
                return doc.Create.NewFamilyInstance(faceRef, hitPt, XYZ.BasisX, sym);
            }
            catch { return null; }
        }

        // ── Lamp placement: pt.Z = ukdZ, kein zusätzlicher Parameter ────────
        private FamilyInstance PlaceLampInstance(Document doc, FamilySymbol sym,
            XYZ pt, Level level, Room room, double ukdZ, View3D v3dForFacePlacement)
        {
            // Place on Face (opt-in): host on the ceiling/floor face above the point
            if (Request.Config.Surface == PlacementSurface.Face)
            {
                var onFace = TryPlaceOnFace(doc, sym, pt, v3dForFacePlacement);
                if (onFace != null) { doc.Regenerate(); return onFace; }
                // no face found -> fall through to work-plane placement below
            }

            // pt.Z ist bereits auf ukdZ gesetzt — direkt platzieren
            // Für face-based Familien: Elevation-Parameter nach Platzierung korrigieren
            FamilyInstance inst = level != null
                ? doc.Create.NewFamilyInstance(pt, sym, level, StructuralType.NonStructural)
                : doc.Create.NewFamilyInstance(pt, sym, StructuralType.NonStructural);
            if (inst == null) return null;

            doc.Regenerate();

            // Force the lamp to the target height (ukdZ). NewFamilyInstance ignores the
            // point's Z for level-/work-plane-based families, so drive the geometry via
            // "Elevation from Level" and then snap the instance to ukdZ as a safety net.
            try
            {
                double levelElev = level?.Elevation ?? 0;
                double target    = ukdZ - levelElev;   // Elevation from Level for Z = ukdZ

                var pe = inst.get_Parameter(BuiltInParameter.INSTANCE_ELEVATION_PARAM);
                if (pe != null && !pe.IsReadOnly && pe.StorageType == StorageType.Double)
                    pe.Set(target);
                else
                {
                    var ph = inst.get_Parameter(BuiltInParameter.INSTANCE_FREE_HOST_OFFSET_PARAM);
                    if (ph != null && !ph.IsReadOnly && ph.StorageType == StorageType.Double)
                        ph.Set(target);
                }
                doc.Regenerate();

                // Guaranteed snap: move whatever residual Z remains so geometry sits at ukdZ.
                if (inst.Location is LocationPoint lp)
                {
                    double dz = ukdZ - lp.Point.Z;
                    if (Math.Abs(dz) > 1e-6)
                        ElementTransformUtils.MoveElement(doc, inst.Id, new XYZ(0, 0, dz));
                }
            }
            catch { }

            return inst;
        }

                // ── Redistribute selected lamps

        // ── Redistribute selected lamps ───────────────────────────────────────
        private void Redistribute(UIDocument uidoc, Document doc)
        {
            var selected = uidoc.Selection.GetElementIds()
                .Select(id => doc.GetElement(id) as FamilyInstance)
                .Where(fi => fi?.Location is LocationPoint)
                .OrderBy(fi => ((LocationPoint)fi.Location).Point.X)
                .ThenBy(fi => ((LocationPoint)fi.Location).Point.Y)
                .ToList();

            if (selected.Count < 2)
            { OnStatus?.Invoke("Select at least 2 lamps to redistribute."); return; }

            var pts   = selected.Select(fi => ((LocationPoint)fi.Location).Point).ToList();
            double avgZ = pts.Average(p => p.Z);

            double spanX = pts.Max(p => p.X) - pts.Min(p => p.X);
            double spanY = pts.Max(p => p.Y) - pts.Min(p => p.Y);

            // Determine main axis
            bool alongX = spanX >= spanY;

            List<XYZ> newPts;
            double angle = 0;

            if (alongX)
            {
                // Redistribute evenly along X, keep Y as average
                double startX = pts.Min(p => p.X);
                double endX   = pts.Max(p => p.X);
                double avgY   = pts.Average(p => p.Y);
                double step   = (endX - startX) / (selected.Count - 1);
                newPts = Enumerable.Range(0, selected.Count)
                    .Select(i => new XYZ(startX + i * step, avgY, avgZ)).ToList();
                angle = 0; // lamps along X axis
            }
            else
            {
                // Redistribute evenly along Y, keep X as average
                var sortedY = selected
                    .OrderBy(fi => ((LocationPoint)fi.Location).Point.Y).ToList();
                pts = sortedY.Select(fi => ((LocationPoint)fi.Location).Point).ToList();
                selected = sortedY;
                double startY = pts.Min(p => p.Y);
                double endY   = pts.Max(p => p.Y);
                double avgX   = pts.Average(p => p.X);
                double step   = (endY - startY) / (selected.Count - 1);
                newPts = Enumerable.Range(0, selected.Count)
                    .Select(i => new XYZ(avgX, startY + i * step, avgZ)).ToList();
                angle = Math.PI / 2;
            }

            using (var tx = new Transaction(doc, "ME-Tools: Redistribute Lamps"))
            {
                tx.Start();
                for (int i = 0; i < selected.Count; i++)
                {
                    try
                    {
                        var fi    = selected[i];
                        var oldPt = ((LocationPoint)fi.Location).Point;
                        // Move to new position (keep Z)
                        var target = new XYZ(newPts[i].X, newPts[i].Y, oldPt.Z);
                        ElementTransformUtils.MoveElement(doc, fi.Id, target - oldPt);
                        doc.Regenerate();
                        // Apply rotation if needed
                        if (Math.Abs(angle) > 0.001)
                        {
                            var axis = Line.CreateBound(target, target + XYZ.BasisZ);
                            var current = fi.GetTransform();
                            double curAngle = Math.Atan2(current.BasisX.Y, current.BasisX.X);
                            double delta = angle - curAngle;
                            if (Math.Abs(delta) > 0.001)
                                ElementTransformUtils.RotateElement(doc, fi.Id, axis, delta);
                        }
                    }
                    catch { }
                }
                tx.Commit();
            }
            OnStatus?.Invoke($"Redistributed {selected.Count} lamps evenly.");
        }

        private List<XYZ> CalcPoints(BoundingBoxXYZ bb, double wallFt, int rows, int cols, double z)
        {
            var pts   = new List<XYZ>();
            double cx = (bb.Min.X + bb.Max.X) / 2.0;
            double cy = (bb.Min.Y + bb.Max.Y) / 2.0;
            double roomW = bb.Max.X - bb.Min.X;
            double roomD = bb.Max.Y - bb.Min.Y;

            double margW  = Math.Min(wallFt, roomW * 0.25);
            double margD  = Math.Min(wallFt, roomD * 0.25);
            double availW = Math.Max(roomW * 0.5, roomW - 2.0 * margW);
            double availD = Math.Max(roomD * 0.5, roomD - 2.0 * margD);

            double stepX  = cols > 1 ? availW / (cols - 1) : 0;
            double stepY  = rows > 1 ? availD / (rows - 1) : 0;
            double startX = cols == 1 ? cx : cx - availW / 2.0;
            double startY = rows == 1 ? cy : cy - availD / 2.0;

            for (int r = 0; r < rows; r++)
                for (int c = 0; c < cols; c++)
                    pts.Add(new XYZ(startX + c * stepX, startY + r * stepY, z));
            return pts;
        }

        private double CalcAngle(RotationMode mode, double roomW, double roomD)
        {
            // The "D outlet for lighting" family has a native 45-degree tilt in plan.
            // Deg0 corrects this to horizontal with -PI/4 (-45 deg).
            // Deg90 = horizontal rotated 90 deg = -PI/4 + PI/2 = +PI/4.
            // Auto picks the axis that aligns with the longer room dimension.
            switch (mode)
            {
                case RotationMode.Deg90: return Math.PI / 2.0;  // X-shape rotated 90
                case RotationMode.Deg0:  return 0.0;             // native = X shape (default)
                default: // Auto: align to long room axis
                    return roomD > roomW ? Math.PI / 2.0 : 0.0;
            }
        }

        // ── Helpers ─────────────────────────────────────────────────────────
        private double GetUKD(Room room)
        {
            try { return (room.UpperLimit?.Elevation ?? 0) + room.LimitOffset; }
            catch { return 0; }
        }

        private bool IsInRoom(Room room, XYZ pt)
        { try { return room.IsPointInRoom(pt); } catch { return true; } }

        private Level GetNearestLevel(Document doc, double z)
        {
            try { return new FilteredElementCollector(doc).OfClass(typeof(Level))
                .Cast<Level>().OrderBy(l => Math.Abs(l.Elevation - z)).FirstOrDefault(); }
            catch { return null; }
        }

        // Same lookup as above, but against an already-fetched level list — used inside
        // per-point placement loops (grid/line modes) so the document isn't re-scanned
        // for every single point. Identical selection logic, just given the list instead
        // of re-querying it.
        private Level GetNearestLevel(List<Level> levels, double z)
        {
            try { return levels.OrderBy(l => Math.Abs(l.Elevation - z)).FirstOrDefault(); }
            catch { return null; }
        }

        /// <summary>
        /// Löst die Fallback-Ebene auf.
        /// Priorität: explizit konfigurierte Ebene → aktive View-Ebene → unterste Projekt-Ebene.
        /// </summary>
        private Level ResolveFallbackLevel(Document doc, LampConfig cfg)
        {
            try
            {
                if (cfg.FallbackLevelId != null && cfg.FallbackLevelId != ElementId.InvalidElementId)
                {
                    if (doc.GetElement(cfg.FallbackLevelId) is Level lvl) return lvl;
                }
            }
            catch { }

            try
            {
                if (doc.ActiveView is ViewPlan vp && vp.GenLevel != null) return vp.GenLevel;
            }
            catch { }

            try
            {
                return new FilteredElementCollector(doc).OfClass(typeof(Level))
                    .Cast<Level>().OrderBy(l => l.Elevation).FirstOrDefault();
            }
            catch { return null; }
        }

        private double ToFeet(double mm)
            => UnitUtils.ConvertToInternalUnits(mm, UnitTypeId.Millimeters);

        public string GetName() => "ME-Tools LampPlacer Handler";
    }
}

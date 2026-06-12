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
        public Action<string> OnStatus { get; set; }
        public Action<int>    OnPlaced { get; set; }
        public Func<double, double?> OnPromptSpacing { get; set; }  // mm in -> mm out (null = cancel)

        public void Execute(UIApplication app)
        {
            var uidoc = app.ActiveUIDocument;
            var doc   = uidoc.Document;

            if (Request.Action == LampAction.Redistribute)
            { Redistribute(uidoc, doc); return; }

            if (Request.Action == LampAction.RefreshRoom)
            { RefreshRoom(uidoc, doc); return; }

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
                        new RoomFilter(), "Click rooms — ESC when done");
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

            var cfg = Request.Config;
            double wallFt   = ToFeet(cfg.WallMargin);
            double offsetFt = ToFeet(cfg.UKDOffset);

            // Fallback-Level einmal auflösen
            Level fallbackLvl = ResolveFallbackLevel(doc, cfg);

            var plan = new List<Planned>();
            foreach (var room in rooms)
            {
                var    bb     = room.get_BoundingBox(null);
                double floorZ = room.Level?.Elevation ?? 0;
                double ukdZ   = ((fallbackLvl != null)
                                    ? fallbackLvl.Elevation
                                    : (bb != null ? bb.Max.Z : GetUKD(room)))
                                - offsetFt;
                var    level  = fallbackLvl ?? GetNearestLevel(doc, ukdZ);
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
            RunPlacement(doc, sym, cfg, plan, "Place in Room");
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


        // ── Refresh Room: delete existing lamps in room + re-place ───────────
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
            double ukdZ     = ((fallbackLvl != null)
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
                    try { inst = PlaceLampInstance(doc, sym, pt, level, room, ukdZ); }
                    catch { continue; }
                    if (inst == null) continue;
                    doc.Regenerate();
                    if (Math.Abs(angle) > 0.001)
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
        private struct Planned { public XYZ Pt; public double Angle; public Level Lvl; public Room Rm; public double Ukd; }

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
        private int RunPlacement(Document doc, FamilySymbol sym, LampConfig cfg, List<Planned> plan, string op)
        {
            if (plan == null || plan.Count == 0) { OnStatus?.Invoke("Nothing to place."); return 0; }

            var    existing  = ExistingFixtureXY(doc);
            double tolFt     = ToFeet(cfg.OverlapThreshold);
            int    conflicts = existing.Count > 0 ? plan.Count(pl => NearAny(pl.Pt, existing, tolFt)) : 0;

            var decision = ConfirmPlace(plan.Count, conflicts, op);
            if (decision == PlaceDecision.Cancel) { OnStatus?.Invoke("Cancelled."); return -1; }
            bool skipNear = decision == PlaceDecision.SkipNear;

            var placedIds = new List<ElementId>();
            using (var tg = new TransactionGroup(doc, "ME-Tools: " + op))
            {
                tg.Start();
                using (var tx = new Transaction(doc, "ME-Tools: " + op))
                {
                    tx.Start();
                    if (!sym.IsActive) { sym.Activate(); doc.Regenerate(); }
                    foreach (var pl in plan)
                    {
                        if (skipNear && NearAny(pl.Pt, existing, tolFt)) continue;
                        FamilyInstance inst = null;
                        try { inst = PlaceLampInstance(doc, sym, pl.Pt, pl.Lvl, pl.Rm, pl.Ukd); }
                        catch { continue; }
                        if (inst == null) continue;
                        doc.Regenerate();
                        if (Math.Abs(pl.Angle) > 0.001)
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
            OnStatus?.Invoke(msg);
            OnPlaced?.Invoke(placedIds.Count);
            return placedIds.Count;
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
                    corners.Add(uidoc.Selection.PickPoint("Click corner " + (i + 1) + " of 4 (area for the grid)"));
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
                    if (fallbackLvl != null) { ukdZ = fallbackElev - offsetFt; level = fallbackLvl; }
                    else if (room != null)
                    {
                        var bb = room.get_BoundingBox(null);
                        ukdZ  = (bb != null ? bb.Max.Z : GetUKD(room)) - offsetFt;
                        level = GetNearestLevel(doc, ukdZ);
                    }
                    else { continue; }

                    plan.Add(new Planned { Pt = new XYZ(q.X, q.Y, ukdZ), Angle = angle, Lvl = level, Rm = room, Ukd = ukdZ });
                }
            }
            RunPlacement(doc, sym, cfg, plan, "Place Grid");
        }

        // -- Place lamps along guide lines drawn with Revit's Detail Line tool --
        private void PlaceAlongLine(UIDocument uidoc, Document doc)
        {
            var cfg = Request.Config;
            var sym = doc.GetElement(Request.SymbolId) as FamilySymbol;
            if (sym == null) { OnStatus?.Invoke("Family not found."); return; }

            // 1. Select the guide line(s) drawn with Revit's Detail Line tool
            IList<Reference> refs;
            try { refs = uidoc.Selection.PickObjects(ObjectType.Element, new LineFilter(),
                    "Select the guide line(s) - ESC when done"); }
            catch (OperationCanceledException) { OnStatus?.Invoke("Cancelled."); return; }
            catch (Exception ex) { OnStatus?.Invoke("Error: " + ex.Message); return; }

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

            // 4. Build placement points (XY + tangent) per mode
            var spots = new List<KeyValuePair<XYZ, double>>();
            if (cfg.LineMode == LineMode.ByCount)
            {
                // Even division: N lamps, equal end-gaps AND equal inter-lamp gaps = L/(N+1).
                int n = Math.Max(1, cfg.LineCount);
                foreach (var chain in polylines)
                {
                    double L = PolylineLength(chain);
                    double d = L / (n + 1);
                    for (int i = 1; i <= n; i++)
                    { double tan; XYZ p = PointAtArcLength(chain, d * i, out tan);
                      spots.Add(new KeyValuePair<XYZ, double>(p, tan)); }
                }
            }
            else // BySpacing: click first lamp, prompt distance, click as many as wanted (= count)
            {
                XYZ firstPick;
                try { firstPick = uidoc.Selection.PickPoint("Click the position of the FIRST lamp"); }
                catch (OperationCanceledException) { firstPick = null; }
                catch (Exception) { firstPick = null; }

                if (firstPick != null)
                {
                    List<XYZ> path = null; double bestDist = double.MaxValue, s0 = 0;
                    foreach (var chain in polylines)
                    { double dd; double s = ProjectOnPolyline(chain, firstPick, out dd);
                      if (dd < bestDist) { bestDist = dd; path = chain; s0 = s; } }
                    if (path == null) { path = polylines[0]; s0 = 0; }

                    // Wall margin: first lamp kept at least WallMargin from the nearest line end
                    double wallFt   = ToFeet(cfg.WallMargin);
                    double totalLen = PolylineLength(path);
                    double loS = wallFt, hiS = totalLen - wallFt;
                    if (loS > hiS) { loS = hiS = totalLen / 2.0; }
                    if (s0 < loS) s0 = loS; else if (s0 > hiS) s0 = hiS;

                    double? mm = OnPromptSpacing?.Invoke(cfg.LineSpacing);
                    if (mm != null && mm.Value > 0)
                    {
                        double d = ToFeet(mm.Value);
                        // Count = number of lamp clicks (first + extras); spacing forced to d.
                        int count = 1;
                        while (true)
                        {
                            try { uidoc.Selection.PickPoint("Click to add a lamp (ESC when done) - spacing is applied automatically"); }
                            catch (OperationCanceledException) { break; }
                            catch (Exception) { break; }
                            count++;
                        }
                        for (int k = 0; k < count; k++)
                        { double tan; XYZ p = PointAtArcLength(path, s0 + d * k, out tan);
                          spots.Add(new KeyValuePair<XYZ, double>(p, tan)); }
                    }
                    else OnStatus?.Invoke("Cancelled - no spacing entered.");
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
                if (fallbackLvl != null) { ukdZ = fallbackElev - offsetFt; level = fallbackLvl; }
                else if (room != null)
                {
                    var bb = room.get_BoundingBox(null);
                    ukdZ  = (bb != null ? bb.Max.Z : GetUKD(room)) - offsetFt;
                    level = GetNearestLevel(doc, ukdZ);
                }
                else { continue; }

                double rotAngle = cfg.LineRotation == LineRotation.Perpendicular
                    ? tangent + Math.PI / 2.0 : tangent;
                plan.Add(new Planned { Pt = new XYZ(pt2d.X, pt2d.Y, ukdZ), Angle = rotAngle, Lvl = level, Rm = room, Ukd = ukdZ });
            }

            int placed = RunPlacement(doc, sym, cfg, plan, "Place Along Line");

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
        private double ProjectOnPolyline(List<XYZ> chain, XYZ p, out double minDist)
        {
            minDist = double.MaxValue; double bestS = 0, acc = 0;
            for (int i = 0; i + 1 < chain.Count; i++)
            {
                XYZ a = chain[i], b = chain[i + 1];
                double dx = b.X - a.X, dy = b.Y - a.Y;
                double segLen2 = dx * dx + dy * dy;
                double segLen = Math.Sqrt(segLen2);
                double t = segLen2 > 1e-12 ? ((p.X - a.X) * dx + (p.Y - a.Y) * dy) / segLen2 : 0;
                if (t < 0) t = 0; else if (t > 1) t = 1;
                double px = a.X + dx * t, py = a.Y + dy * t;
                double d = Math.Sqrt((p.X - px) * (p.X - px) + (p.Y - py) * (p.Y - py));
                if (d < minDist) { minDist = d; bestS = acc + segLen * t; }
                acc += segLen;
            }
            return bestS;
        }

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
        private FamilyInstance TryPlaceOnFace(Document doc, FamilySymbol sym, XYZ pt)
        {
            try
            {
                var v3d = GetAny3DView(doc);
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
            XYZ pt, Level level, Room room, double ukdZ)
        {
            // Place on Face (opt-in): host on the ceiling/floor face above the point
            if (Request.Config.Surface == PlacementSurface.Face)
            {
                var onFace = TryPlaceOnFace(doc, sym, pt);
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
            switch (mode)
            {
                case RotationMode.Deg90: return Math.PI / 2.0;
                case RotationMode.Deg0:  return 0;
                default: // Auto: rotate 90° if room is portrait
                    return roomD > roomW ? Math.PI / 2.0 : 0;
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

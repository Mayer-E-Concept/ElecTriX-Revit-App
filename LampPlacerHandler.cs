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

    public class LampPlacerHandler : IExternalEventHandler
    {
        public LampRequest    Request  { get; set; } = new LampRequest();
        public Action<string> OnStatus { get; set; }
        public Action<int>    OnPlaced { get; set; }

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

            var sym = doc.GetElement(Request.SymbolId) as FamilySymbol;
            if (sym == null) { OnStatus?.Invoke("Family not found."); return; }
            if (!sym.IsActive)
            { using (var t = new Transaction(doc,"Activate")){t.Start();sym.Activate();t.Commit();} }

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

            int total = 0;
            using (var tx = new Transaction(doc, "ME-Tools: Place Lamps"))
            {
                tx.Start();
                foreach (var room in rooms)
                {
                    // BoundingBox.Max.Z = echte UKD des Raums (zuverlässig für alle Geschosse)
                    var    bb     = room.get_BoundingBox(null);
                    double ukdZ   = (bb != null ? bb.Max.Z : GetUKD(room)) - offsetFt;
                    double floorZ = room.Level?.Elevation ?? 0;
                    // Level nearest zur UKD → "Höhe von Ebene = 0, Versatz = 0"
                    var    level  = GetNearestLevel(doc, ukdZ) ?? fallbackLvl;
                    if (bb == null) continue;

                    double roomW  = bb.Max.X - bb.Min.X;
                    double roomD  = bb.Max.Y - bb.Min.Y;
                    double area   = UnitUtils.ConvertFromInternalUnits(
                        room.Area, UnitTypeId.SquareMeters);

                    // Calculate grid
                    int rows, cols;
                    CalcGrid(cfg, area, roomW, roomD, out rows, out cols);

                    // Auto rotation angle
                    double angle = CalcAngle(cfg.Rotation, roomW, roomD);

                    // Grid points centered in room
                    var pts = CalcPoints(bb, wallFt, rows, cols, ukdZ);

                    foreach (var pt in pts)
                    {
                        if (!IsInRoom(room, new XYZ(pt.X, pt.Y, floorZ + 0.5))) continue;
                        FamilyInstance inst = null;
                        try { inst = PlaceLampInstance(doc, sym, pt, level, room, ukdZ); }
                        catch { continue; }
                        if (inst == null) continue;
                        doc.Regenerate();
                        // Rotate
                        if (Math.Abs(angle) > 0.001)
                            try { ElementTransformUtils.RotateElement(doc, inst.Id,
                                Line.CreateBound(pt, pt + XYZ.BasisZ), angle); } catch { }
                        // Level
                        if (level != null)
                            try { var p = inst.get_Parameter(BuiltInParameter.INSTANCE_SCHEDULE_ONLY_LEVEL_PARAM);
                                if (p != null && !p.IsReadOnly) p.Set(level.Id); } catch { }
                        total++;
                    }
                }
                tx.Commit();
            }
            OnStatus?.Invoke($"Done: {rooms.Count} room(s), {total} lamps placed.");
            OnPlaced?.Invoke(total);
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
            // BoundingBox.Max.Z = echte UKD des Raums
            var    bb       = room.get_BoundingBox(null);
            if (bb == null) { OnStatus?.Invoke("Room has no bounding box."); return; }
            double ukdZ     = (bb != null ? bb.Max.Z : GetUKD(room)) - offsetFt;
            var    level    = GetNearestLevel(doc, ukdZ) ?? fallbackLvl;

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


        // ── Place lamps along a user-defined line ────────────────────────────
        private void PlaceAlongLine(UIDocument uidoc, Document doc)
        {
            var cfg = Request.Config;
            var sym = doc.GetElement(Request.SymbolId) as FamilySymbol;
            if (sym == null) { OnStatus?.Invoke("Family not found."); return; }

            // 1. Zwei Punkte picken
            XYZ p1, p2;
            try
            {
                p1 = uidoc.Selection.PickPoint("Click start point of the line");
                p2 = uidoc.Selection.PickPoint("Click end point of the line");
            }
            catch (OperationCanceledException) { OnStatus?.Invoke("Cancelled."); return; }
            catch (Exception ex)               { OnStatus?.Invoke($"Error: {ex.Message}"); return; }

            double length = p1.DistanceTo(p2);
            if (length < ToFeet(1.0)) { OnStatus?.Invoke("Line too short."); return; }

            XYZ dir = (p2 - p1).Normalize();

            // 2. Punkte entlang der Linie berechnen
            var linePts = new List<XYZ>();
            if (cfg.LineMode == LineMode.ByCount)
            {
                int n = Math.Max(1, cfg.LineCount);
                if (n == 1)
                    linePts.Add((p1 + p2) / 2.0);
                else
                {
                    double step = length / (n - 1);
                    for (int i = 0; i < n; i++) linePts.Add(p1 + dir * (step * i));
                }
            }
            else // BySpacing
            {
                double spacing = ToFeet(Math.Max(1.0, cfg.LineSpacing));
                if (spacing <= 0) { OnStatus?.Invoke("Spacing must be greater than 0."); return; }
                int n = Math.Max(1, (int)Math.Floor(length / spacing) + 1);
                // auf der Linie zentrieren
                double total    = spacing * (n - 1);
                double startOff = (length - total) / 2.0;
                for (int i = 0; i < n; i++)
                    linePts.Add(p1 + dir * (startOff + spacing * i));
            }

            // 3. Symbol aktivieren
            if (!sym.IsActive)
            { using (var t = new Transaction(doc, "Activate")) { t.Start(); sym.Activate(); t.Commit(); } }

            // 4. Fallback-Level auflösen (reliable backup wenn kein Raum/Slab am Punkt)
            Level fallbackLvl = ResolveFallbackLevel(doc, cfg);
            double fallbackElev = fallbackLvl?.Elevation ?? 0;

            // Räume für UKD-Bestimmung pro Punkt
            double roomTestZ = fallbackElev + 0.5;
            var allRooms = new FilteredElementCollector(doc)
                .OfClass(typeof(SpatialElement))
                .OfCategory(BuiltInCategory.OST_Rooms)
                .Cast<Room>()
                .Where(r => r != null && r.Area > 0)
                .ToList();

            double offsetFt  = ToFeet(cfg.UKDOffset);
            double lineAngle = Math.Atan2(dir.Y, dir.X);

            // Line-Rotation: AlongLine = entlang der Linie, Perpendicular = 90° dazu
            double rotAngle = cfg.LineRotation == LineRotation.Perpendicular
                ? lineAngle + Math.PI / 2.0
                : lineAngle;

            int placed = 0, fallbackUsed = 0;
            using (var tx = new Transaction(doc, "ME-Tools: Place Lamps Along Line"))
            {
                tx.Start();
                foreach (var pt2d in linePts)
                {
                    // Raum am Punkt finden
                    Room room = allRooms.FirstOrDefault(r =>
                    {
                        try { return r.IsPointInRoom(new XYZ(pt2d.X, pt2d.Y, roomTestZ)); }
                        catch { return false; }
                    });

                    double ukdZ;
                    Level  level;
                    if (room != null)
                    {
                        var bb = room.get_BoundingBox(null);
                        ukdZ  = (bb != null ? bb.Max.Z : GetUKD(room)) - offsetFt;
                        level = GetNearestLevel(doc, ukdZ) ?? fallbackLvl;
                    }
                    else
                    {
                        // Kein Raum → Fallback-Level als zuverlässige Ebene
                        if (fallbackLvl == null) { continue; } // kein Fallback definiert → skippen
                        ukdZ  = fallbackElev - offsetFt;
                        level = fallbackLvl;
                        fallbackUsed++;
                    }

                    var pt = new XYZ(pt2d.X, pt2d.Y, ukdZ);
                    FamilyInstance inst = null;
                    try { inst = PlaceLampInstance(doc, sym, pt, level, room, ukdZ); }
                    catch { continue; }
                    if (inst == null) continue;
                    doc.Regenerate();

                    if (Math.Abs(rotAngle) > 0.001)
                        try { ElementTransformUtils.RotateElement(doc, inst.Id,
                            Line.CreateBound(pt, pt + XYZ.BasisZ), rotAngle); } catch { }

                    if (level != null)
                        try { var p = inst.get_Parameter(BuiltInParameter.INSTANCE_SCHEDULE_ONLY_LEVEL_PARAM);
                            if (p != null && !p.IsReadOnly) p.Set(level.Id); } catch { }

                    placed++;
                }
                tx.Commit();
            }

            string msg = $"Done: {placed} lamps placed along line.";
            if (fallbackUsed > 0) msg += $" ({fallbackUsed} used reference level fallback)";
            OnStatus?.Invoke(msg);
            OnPlaced?.Invoke(placed);
        }


        // ── Lamp placement: pt.Z = ukdZ, kein zusätzlicher Parameter ────────
        private FamilyInstance PlaceLampInstance(Document doc, FamilySymbol sym,
            XYZ pt, Level level, Room room, double ukdZ)
        {
            // pt.Z ist bereits auf ukdZ gesetzt — direkt platzieren
            // Für face-based Familien: Elevation-Parameter nach Platzierung korrigieren
            FamilyInstance inst = level != null
                ? doc.Create.NewFamilyInstance(pt, sym, level, StructuralType.NonStructural)
                : doc.Create.NewFamilyInstance(pt, sym, StructuralType.NonStructural);
            if (inst == null) return null;

            doc.Regenerate();

            // Nur für face-based Familien die Höhe explizit setzen
            // (bei level-based ist pt.Z bereits korrekt)
            try
            {
                var placementType = sym.Family.FamilyPlacementType;
                if (placementType == FamilyPlacementType.OneLevelBasedHosted ||
                    placementType == FamilyPlacementType.WorkPlaneBased)
                {
                    double levelElev = level?.Elevation ?? 0;
                    double offset    = ukdZ - levelElev;
                    var p = inst.get_Parameter(BuiltInParameter.INSTANCE_FREE_HOST_OFFSET_PARAM)
                         ?? inst.get_Parameter(BuiltInParameter.INSTANCE_ELEVATION_PARAM);
                    if (p != null && !p.IsReadOnly && p.StorageType == StorageType.Double)
                        p.Set(offset);
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

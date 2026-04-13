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

            switch (Request.Action)
            {
                case LampAction.RefreshRoom:  RefreshRooms(uidoc, doc, false); return;
                case LampAction.RefreshMulti: RefreshRooms(uidoc, doc, true);  return;
                case LampAction.RotateRoom:   RotateRoom(uidoc, doc);          return;
                case LampAction.PlaceLine:    PlaceLine(uidoc, doc);           return;
            }

            var sym = doc.GetElement(Request.SymbolId) as FamilySymbol;
            if (sym == null) { OnStatus?.Invoke("Family not found."); return; }
            if (!sym.IsActive)
            { using (var t = new Transaction(doc, "Activate")) { t.Start(); sym.Activate(); t.Commit(); } }

            var rooms = new List<Room>();
            try
            {
                if (Request.Action == LampAction.PlaceMulti)
                {
                    var refs = uidoc.Selection.PickObjects(ObjectType.Element,
                        new RoomFilter(), "Click rooms — press ESC when done");
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

            var    cfg     = Request.Config;
            double wallFt   = ToFeet(cfg.WallMargin);
            double offsetFt = ToFeet(cfg.UKDOffset);
            int    total    = 0;

            using (var tx = new Transaction(doc, "ME-Tools: Place Lamps"))
            {
                tx.Start();
                foreach (var room in rooms)
                {
                    var bb = room.get_BoundingBox(null);
                    if (bb == null) continue;
                    double ceilingZ = bb.Max.Z - offsetFt;
                    double floorZ   = room.Level?.Elevation ?? 0;
                    var    level    = room.Level ?? GetNearestLevel(doc, floorZ);
                    double roomW    = bb.Max.X - bb.Min.X;
                    double roomD    = bb.Max.Y - bb.Min.Y;
                    double area     = UnitUtils.ConvertFromInternalUnits(room.Area, UnitTypeId.SquareMeters);

                    int rows, cols;
                    CalcGrid(cfg, area, roomW, roomD, out rows, out cols);
                    double angle = CalcAngle(cfg.Rotation, roomW, roomD);
                    var pts = CalcGridPoints(bb, wallFt, rows, cols, ceilingZ);

                    foreach (var pt in pts)
                    {
                        if (!IsInRoom(room, new XYZ(pt.X, pt.Y, floorZ + 0.5))) continue;
                        FamilyInstance inst = null;
                        try { inst = PlaceLampInstance(doc, sym, pt, level, ceilingZ); }
                        catch { continue; }
                        if (inst == null) continue;
                        doc.Regenerate();
                        if (Math.Abs(angle) > 0.001)
                            try { ElementTransformUtils.RotateElement(doc, inst.Id,
                                Line.CreateBound(pt, pt + XYZ.BasisZ), angle); } catch { }
                        SetScheduleLevel(inst, level);
                        total++;
                    }
                }
                tx.Commit();
            }
            OnStatus?.Invoke($"Done: {rooms.Count} room(s), {total} lamps placed.");
            OnPlaced?.Invoke(total);
        }

        // ─────────────────────────────────────────────────────────────────────
        // LINE PLACEMENT
        //
        // Formula (DIALux-style, count is the only user input):
        //
        //   axis   = lineLength / count
        //   center_i = axis * (i + 0.5)   for i = 0 .. count-1
        //
        // Visual:  |--axis/2--|<lamp>|--gap--|<lamp>|--gap--|<lamp>|--axis/2--|
        //          axis/2 = distance from line endpoint to lamp CENTER
        //          gap    = axis - lampLen  (between lamp edges)
        //          margin = axis/2 - lampLen/2  (from line endpoint to lamp EDGE)
        //
        // Constraint: axis >= lampLen  (lamp must fit in its segment)
        //   → count <= lineLength / lampLen
        //   → count is auto-reduced if needed
        //
        // Lamp length read automatically from selected family.
        // Ortho-snap: snaps to 0°/45°/90° if within ±5°.
        // H+V crosshair guide lines at start point, deleted with placement.
        // ─────────────────────────────────────────────────────────────────────
        private void PlaceLine(UIDocument uidoc, Document doc)
        {
            var sym = doc.GetElement(Request.SymbolId) as FamilySymbol;
            if (sym == null) { OnStatus?.Invoke("Family not found."); return; }
            if (!sym.IsActive)
            { using (var t = new Transaction(doc, "Activate")) { t.Start(); sym.Activate(); t.Commit(); } }

            var cfg = Request.Config;

            // ── Read lamp length from family ──────────────────────────────────
            double lampLenFt = ReadLampLength(sym);
            double lampLenMm = UnitUtils.ConvertFromInternalUnits(lampLenFt, UnitTypeId.Millimeters);

            OnStatus?.Invoke($"Lamp length: {lampLenMm:0} mm  (from family)  ·  Click start point...");

            // ── Pick start point ──────────────────────────────────────────────
            XYZ ptStart;
            try
            {
                ptStart = uidoc.Selection.PickPoint(
                    ObjectSnapTypes.Endpoints | ObjectSnapTypes.Midpoints |
                    ObjectSnapTypes.Nearest   | ObjectSnapTypes.Perpendicular |
                    ObjectSnapTypes.WorkPlaneGrid,
                    "Click start point of lamp line");
            }
            catch (OperationCanceledException) { OnStatus?.Invoke("Cancelled."); return; }
            catch (Exception ex)               { OnStatus?.Invoke($"Error: {ex.Message}"); return; }

            // ── H+V crosshair guide at start (visible while picking end) ──────
            var tempIds    = new List<ElementId>();
            var activeView = doc.ActiveView;
            bool canDetail = activeView?.ViewType != ViewType.ThreeD
                          && activeView?.SketchPlane != null;
            if (canDetail)
            {
                try
                {
                    using (var tp = new Transaction(doc, "ME-Tools: Guide"))
                    {
                        tp.Start();
                        double ext = ToFeet(50000);
                        var cb = activeView.CropBox;
                        if (cb != null)
                            ext = Math.Max(Math.Abs(cb.Max.X - cb.Min.X),
                                          Math.Abs(cb.Max.Y - cb.Min.Y)) * 0.6;
                        var hLine = Line.CreateBound(
                            new XYZ(ptStart.X - ext, ptStart.Y, ptStart.Z),
                            new XYZ(ptStart.X + ext, ptStart.Y, ptStart.Z));
                        var vLine = Line.CreateBound(
                            new XYZ(ptStart.X, ptStart.Y - ext, ptStart.Z),
                            new XYZ(ptStart.X, ptStart.Y + ext, ptStart.Z));
                        foreach (var l in new[] { hLine, vLine })
                        {
                            var dc = doc.Create.NewDetailCurve(activeView, l);
                            if (dc != null) tempIds.Add(dc.Id);
                        }
                        tp.Commit();
                    }
                }
                catch { tempIds.Clear(); }
            }

            // ── Pick end point ────────────────────────────────────────────────
            XYZ ptEnd;
            try
            {
                OnStatus?.Invoke("Click end point  ·  snaps to 0°/45°/90° within ±5°  ·  diagonal OK");
                ptEnd = uidoc.Selection.PickPoint(
                    ObjectSnapTypes.Endpoints | ObjectSnapTypes.Midpoints |
                    ObjectSnapTypes.Nearest   | ObjectSnapTypes.Perpendicular |
                    ObjectSnapTypes.WorkPlaneGrid,
                    "Click end point of lamp line");
            }
            catch (OperationCanceledException) { CleanupTempIds(doc, tempIds); OnStatus?.Invoke("Cancelled."); return; }
            catch (Exception ex)               { CleanupTempIds(doc, tempIds); OnStatus?.Invoke($"Error: {ex.Message}"); return; }

            // ── Validate & ortho-snap ─────────────────────────────────────────
            XYZ rawVec = ptEnd - ptStart;
            if (rawVec.GetLength() < ToFeet(100))
            { CleanupTempIds(doc, tempIds); OnStatus?.Invoke("Line too short."); return; }

            XYZ    dir     = SnapTo45(rawVec.Normalize());
            double lineLen = rawVec.GetLength();

            // ── Compute positions ─────────────────────────────────────────────
            // axis = lineLen / count  →  center_i = axis * (i + 0.5)
            // Constraint: axis >= lampLen  →  count <= lineLen / lampLen
            int count = Math.Max(1, cfg.LineCount);

            // Reduce count if lamps don't fit (axis must be >= lampLen)
            while (count > 1 && lineLen / count < lampLenFt)
                count--;

            double axisFt    = lineLen / count;
            double axisHalf  = axisFt / 2.0;

            // Center positions: axis/2, 3*axis/2, 5*axis/2, ...
            // = axis * (i + 0.5)
            var offsets = new List<double>();
            for (int i = 0; i < count; i++)
                offsets.Add(axisFt * (i + 0.5));

            // ── Rotation ──────────────────────────────────────────────────────
            // Lamp body perpendicular to refDir(1,0,0).
            // AlongLine: +90° aligns lamp length with line direction.
            double lineAngle = Math.Atan2(dir.Y, dir.X);
            if (cfg.LineOrientation == LineOrientation.AlongLine)
                lineAngle += Math.PI / 2.0;

            double ceilingZ = ptStart.Z;
            var    level    = GetNearestLevel(doc, ceilingZ - ToFeet(2500));
            double angleDeg = Math.Atan2(dir.Y, dir.X) * 180.0 / Math.PI;

            // ── Place lamps — delete guide lines in same transaction ───────────
            int placed = 0;
            using (var tx = new Transaction(doc, "ME-Tools: Place Lamps on Line"))
            {
                tx.Start();
                foreach (var id in tempIds) try { doc.Delete(id); } catch { }

                foreach (double offset in offsets)
                {
                    var pt = new XYZ(
                        ptStart.X + dir.X * offset,
                        ptStart.Y + dir.Y * offset,
                        ceilingZ);

                    FamilyInstance inst = null;
                    try { inst = PlaceLampInstance(doc, sym, pt, level, ceilingZ); }
                    catch { continue; }
                    if (inst == null) continue;
                    doc.Regenerate();

                    if (Math.Abs(lineAngle) > 0.001)
                        try
                        {
                            var axis = Line.CreateBound(pt, pt + XYZ.BasisZ);
                            ElementTransformUtils.RotateElement(doc, inst.Id, axis, lineAngle);
                        }
                        catch { }

                    SetScheduleLevel(inst, level);
                    placed++;
                }
                tx.Commit();
            }

            double gapMm    = UnitUtils.ConvertFromInternalUnits(axisFt - lampLenFt, UnitTypeId.Millimeters);
            double marginMm = UnitUtils.ConvertFromInternalUnits(axisHalf - lampLenFt / 2.0, UnitTypeId.Millimeters);
            double axisMm   = UnitUtils.ConvertFromInternalUnits(axisFt, UnitTypeId.Millimeters);
            OnStatus?.Invoke(
                $"Line: {placed} lamps  ·  {angleDeg:0}°  ·  " +
                $"axis {axisMm:0} mm  ·  gap {gapMm:0} mm  ·  margin {marginMm:0} mm");
            OnPlaced?.Invoke(placed);
        }

        // ── Read lamp length from family ──────────────────────────────────────
        // 1. "Length" parameter  2. BoundingBox  3. fallback 1500mm
        private double ReadLampLength(FamilySymbol sym)
        {
            try
            {
                var p = sym.LookupParameter("Length");
                if (p != null && p.StorageType == StorageType.Double && p.AsDouble() > 0.001)
                    return p.AsDouble();
            }
            catch { }
            try
            {
                var bb = sym.get_BoundingBox(null);
                if (bb != null)
                {
                    double longest = Math.Max(Math.Abs(bb.Max.X - bb.Min.X),
                                             Math.Abs(bb.Max.Y - bb.Min.Y));
                    if (longest > 0.01) return longest;
                }
            }
            catch { }
            return ToFeet(1500);
        }

        // ── Ortho-snap ────────────────────────────────────────────────────────
        private static XYZ SnapTo45(XYZ dir, double snapDeg = 5.0)
        {
            double angle   = Math.Atan2(dir.Y, dir.X) * 180.0 / Math.PI;
            double nearest = Math.Round(angle / 45.0) * 45.0;
            double diff    = Math.Abs(angle - nearest) % 360.0;
            if (diff > 180.0) diff = 360.0 - diff;
            if (diff <= snapDeg)
            {
                double rad = nearest * Math.PI / 180.0;
                return new XYZ(Math.Round(Math.Cos(rad), 6), Math.Round(Math.Sin(rad), 6), 0);
            }
            return dir;
        }

        private static void CleanupTempIds(Document doc, List<ElementId> ids)
        {
            if (ids == null || !ids.Any()) return;
            try
            {
                using (var t = new Transaction(doc, "ME-Tools: Cleanup"))
                { t.Start(); foreach (var id in ids) try { doc.Delete(id); } catch { } t.Commit(); }
            }
            catch { }
        }

        // ─────────────────────────────────────────────────────────────────────
        // LAMP PLACEMENT — face-based via floor/ceiling underside
        // ─────────────────────────────────────────────────────────────────────
        private FamilyInstance PlaceLampInstance(Document doc, FamilySymbol sym,
            XYZ pt, Level level, double ceilingZ)
        {
            var  type      = sym.Family.FamilyPlacementType;
            bool faceBased = type == FamilyPlacementType.OneLevelBasedHosted
                          || type == FamilyPlacementType.WorkPlaneBased
                          || type == FamilyPlacementType.TwoLevelsBased
                          || type == FamilyPlacementType.Invalid;

            if (faceBased)
            {
                var faceRef = FindFloorBottomFaceAt(doc, pt, ceilingZ);
                if (faceRef != null)
                {
                    try
                    {
                        var inst = doc.Create.NewFamilyInstance(faceRef, pt, new XYZ(1, 0, 0), sym);
                        if (inst != null) return inst;
                    }
                    catch { }
                }
            }

            try
            {
                FamilyInstance inst = level != null
                    ? doc.Create.NewFamilyInstance(pt, sym, level, StructuralType.NonStructural)
                    : doc.Create.NewFamilyInstance(pt, sym, StructuralType.NonStructural);
                if (inst == null) return null;
                doc.Regenerate();
                if (level != null)
                {
                    double off = ceilingZ - level.Elevation;
                    var pOff = inst.get_Parameter(BuiltInParameter.INSTANCE_FREE_HOST_OFFSET_PARAM)
                            ?? inst.get_Parameter(BuiltInParameter.INSTANCE_ELEVATION_PARAM);
                    if (pOff != null && !pOff.IsReadOnly && pOff.StorageType == StorageType.Double)
                        pOff.Set(off);
                }
                return inst;
            }
            catch { return null; }
        }

        private Reference FindFloorBottomFaceAt(Document doc, XYZ pt, double ceilingZ)
        {
            const double tolFt = 1.0;
            var opts = new Options { ComputeReferences = true, DetailLevel = ViewDetailLevel.Fine };
            var elements = new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_Floors).WhereElementIsNotElementType().Cast<Element>()
                .Concat(new FilteredElementCollector(doc)
                    .OfCategory(BuiltInCategory.OST_Ceilings).WhereElementIsNotElementType().Cast<Element>());

            foreach (var el in elements)
            {
                var bb = el.get_BoundingBox(null);
                if (bb == null || pt.X < bb.Min.X || pt.X > bb.Max.X ||
                    pt.Y < bb.Min.Y || pt.Y > bb.Max.Y) continue;
                if (Math.Abs(bb.Min.Z - ceilingZ) > tolFt) continue;
                var r = GetBottomFace(el, opts);
                if (r != null) return r;
            }
            return null;
        }

        private Reference GetBottomFace(Element elem, Options opts)
        {
            try
            {
                foreach (var obj in elem.get_Geometry(opts) ?? Enumerable.Empty<GeometryObject>())
                {
                    if (!(obj is Solid solid) || solid.Volume <= 0) continue;
                    foreach (Face face in solid.Faces)
                    {
                        if (face.Reference == null) continue;
                        if (face.ComputeNormal(new UV(0.5, 0.5)).Z < -0.9) return face.Reference;
                    }
                }
            }
            catch { }
            return null;
        }

        // ─────────────────────────────────────────────────────────────────────
        // ROTATE ROOM
        // ─────────────────────────────────────────────────────────────────────
        private void RotateRoom(UIDocument uidoc, Document doc)
        {
            Room room = null;
            try
            {
                var r = uidoc.Selection.PickObject(ObjectType.Element,
                    new RoomFilter(), "Click a room — all lamps will be rotated 90°");
                room = doc.GetElement(r) as Room;
            }
            catch (OperationCanceledException) { OnStatus?.Invoke("Cancelled."); return; }
            catch (Exception ex)               { OnStatus?.Invoke($"Error: {ex.Message}"); return; }
            if (room == null) return;

            double floorZ = room.Level?.Elevation ?? 0;
            var lamps = GetLampsInRoom(doc, room, floorZ);
            if (!lamps.Any()) { OnStatus?.Invoke("No lamps found in room."); return; }

            using (var tx = new Transaction(doc, "ME-Tools: Rotate 90°"))
            {
                tx.Start();
                int n = 0;
                foreach (var fi in lamps)
                {
                    try
                    {
                        if (!(fi.Location is LocationPoint lp)) continue;
                        ElementTransformUtils.RotateElement(doc, fi.Id,
                            Line.CreateBound(lp.Point, lp.Point + XYZ.BasisZ), Math.PI / 2.0);
                        n++;
                    }
                    catch { }
                }
                tx.Commit();
                OnStatus?.Invoke($"Rotated {n} lamps 90°.");
            }
        }

        // ─────────────────────────────────────────────────────────────────────
        // REFRESH ROOMS
        // ─────────────────────────────────────────────────────────────────────
        private void RefreshRooms(UIDocument uidoc, Document doc, bool multi)
        {
            var rooms = new List<Room>();
            try
            {
                if (multi)
                {
                    var refs = uidoc.Selection.PickObjects(ObjectType.Element,
                        new RoomFilter(), "Click rooms — ESC when done");
                    rooms = refs.Select(r => doc.GetElement(r) as Room).Where(r => r != null).ToList();
                }
                else
                {
                    var r = uidoc.Selection.PickObject(ObjectType.Element,
                        new RoomFilter(), "Click a room to refresh lamps");
                    if (doc.GetElement(r) is Room rm) rooms.Add(rm);
                }
            }
            catch (OperationCanceledException) { OnStatus?.Invoke("Cancelled."); return; }
            catch (Exception ex)               { OnStatus?.Invoke($"Error: {ex.Message}"); return; }

            if (!rooms.Any()) { OnStatus?.Invoke("No rooms selected."); return; }

            var cfg = Request.Config;
            var sym = doc.GetElement(Request.SymbolId) as FamilySymbol;
            if (sym == null) { OnStatus?.Invoke("Family not found."); return; }
            if (!sym.IsActive)
            { using (var t = new Transaction(doc, "Activate")) { t.Start(); sym.Activate(); t.Commit(); } }

            double offsetFt = ToFeet(cfg.UKDOffset);
            double wallFt   = ToFeet(cfg.WallMargin);
            int    totDel = 0, totPlace = 0;

            foreach (var room in rooms)
            {
                double floorZ   = room.Level?.Elevation ?? 0;
                var    toDelete = GetLampsInRoom(doc, room, floorZ).Select(fi => fi.Id).ToList();
                var    bb       = room.get_BoundingBox(null);
                if (bb == null) continue;

                double ceilingZ = bb.Max.Z - offsetFt;
                var    level    = room.Level ?? GetNearestLevel(doc, floorZ);
                double roomW    = bb.Max.X - bb.Min.X;
                double roomD    = bb.Max.Y - bb.Min.Y;
                double area     = UnitUtils.ConvertFromInternalUnits(room.Area, UnitTypeId.SquareMeters);

                int rows, cols;
                CalcGrid(cfg, area, roomW, roomD, out rows, out cols);
                double angle = CalcAngle(cfg.Rotation, roomW, roomD);
                var pts = CalcGridPoints(bb, wallFt, rows, cols, ceilingZ);

                using (var tx = new Transaction(doc, "ME-Tools: Refresh Room"))
                {
                    tx.Start();
                    foreach (var id in toDelete) try { doc.Delete(id); } catch { }
                    totDel += toDelete.Count;

                    foreach (var pt in pts)
                    {
                        if (!IsInRoom(room, new XYZ(pt.X, pt.Y, floorZ + 0.5))) continue;
                        FamilyInstance inst = null;
                        try { inst = PlaceLampInstance(doc, sym, pt, level, ceilingZ); }
                        catch { continue; }
                        if (inst == null) continue;
                        doc.Regenerate();
                        if (Math.Abs(angle) > 0.001)
                            try { ElementTransformUtils.RotateElement(doc, inst.Id,
                                Line.CreateBound(pt, pt + XYZ.BasisZ), angle); } catch { }
                        SetScheduleLevel(inst, level);
                        totPlace++;
                    }
                    tx.Commit();
                }
            }
            OnStatus?.Invoke($"Refreshed {rooms.Count} room(s): {totDel} deleted, {totPlace} placed.");
            OnPlaced?.Invoke(totPlace);
        }

        // ─────────────────────────────────────────────────────────────────────
        // HELPERS
        // ─────────────────────────────────────────────────────────────────────
        private List<FamilyInstance> GetLampsInRoom(Document doc, Room room, double floorZ)
        {
            var result = new List<FamilyInstance>();
            var cats   = new[] { BuiltInCategory.OST_LightingFixtures, BuiltInCategory.OST_LightingDevices };
            foreach (var cat in cats)
            {
                foreach (var fi in new FilteredElementCollector(doc)
                    .OfClass(typeof(FamilyInstance)).OfCategory(cat).Cast<FamilyInstance>())
                {
                    XYZ tp = null;
                    if (fi.Location is LocationPoint lp)
                        tp = new XYZ(lp.Point.X, lp.Point.Y, floorZ + 0.5);
                    else
                    {
                        var bb2 = fi.get_BoundingBox(null);
                        if (bb2 != null)
                            tp = new XYZ((bb2.Min.X + bb2.Max.X) / 2,
                                        (bb2.Min.Y + bb2.Max.Y) / 2, floorZ + 0.5);
                    }
                    if (tp == null) continue;
                    try { if (room.IsPointInRoom(tp)) result.Add(fi); } catch { }
                }
            }
            return result;
        }

        private void SetScheduleLevel(FamilyInstance inst, Level level)
        {
            if (inst == null || level == null) return;
            try
            {
                var p = inst.get_Parameter(BuiltInParameter.INSTANCE_SCHEDULE_ONLY_LEVEL_PARAM);
                if (p != null && !p.IsReadOnly) p.Set(level.Id);
            }
            catch { }
        }

        private void CalcGrid(LampConfig cfg, double areaSqm, double roomW, double roomD,
                              out int rows, out int cols)
        {
            if (cfg.Distribution == DistributionMode.ManualGrid)
            { rows = Math.Max(1, cfg.ManualRows); cols = Math.Max(1, cfg.ManualCols); return; }

            double wallFt = ToFeet(cfg.WallMargin);
            double margW  = Math.Min(wallFt, roomW * 0.25);
            double margD  = Math.Min(wallFt, roomD * 0.25);
            double availW = Math.Max(roomW * 0.5, roomW - 2.0 * margW);
            double availD = Math.Max(roomD * 0.5, roomD - 2.0 * margD);
            double m2     = UnitUtils.ConvertFromInternalUnits(availW * availD, UnitTypeId.SquareMeters);
            if (areaSqm <= 0) areaSqm = m2;
            int nTotal = Math.Max(1, (int)Math.Ceiling(m2 / cfg.SqmPerLamp));
            double ratio = Math.Max(roomW, roomD) / Math.Min(roomW, roomD);
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

        private List<XYZ> CalcGridPoints(BoundingBoxXYZ bb, double wallFt, int rows, int cols, double z)
        {
            var pts  = new List<XYZ>();
            double cx = (bb.Min.X + bb.Max.X) / 2.0, cy = (bb.Min.Y + bb.Max.Y) / 2.0;
            double rW = bb.Max.X - bb.Min.X, rD = bb.Max.Y - bb.Min.Y;
            double mW = Math.Min(wallFt, rW * 0.25), mD = Math.Min(wallFt, rD * 0.25);
            double aW = Math.Max(rW * 0.5, rW - 2.0 * mW), aD = Math.Max(rD * 0.5, rD - 2.0 * mD);
            double sX = cols > 1 ? aW / (cols - 1) : 0, sY = rows > 1 ? aD / (rows - 1) : 0;
            double x0 = cols == 1 ? cx : cx - aW / 2.0, y0 = rows == 1 ? cy : cy - aD / 2.0;
            for (int r = 0; r < rows; r++)
                for (int c = 0; c < cols; c++)
                    pts.Add(new XYZ(x0 + c * sX, y0 + r * sY, z));
            return pts;
        }

        private double CalcAngle(RotationMode mode, double roomW, double roomD)
        {
            switch (mode)
            {
                case RotationMode.Deg90: return Math.PI / 2.0;
                case RotationMode.Deg0:  return 0;
                default: return roomD > roomW ? Math.PI / 2.0 : 0;
            }
        }

        private bool IsInRoom(Room room, XYZ pt)
        { try { return room.IsPointInRoom(pt); } catch { return true; } }

        private Level GetNearestLevel(Document doc, double z)
        {
            try { return new FilteredElementCollector(doc).OfClass(typeof(Level))
                .Cast<Level>().OrderBy(l => Math.Abs(l.Elevation - z)).FirstOrDefault(); }
            catch { return null; }
        }

        private double ToFeet(double mm)
            => UnitUtils.ConvertToInternalUnits(mm, UnitTypeId.Millimeters);

        public string GetName() => "ME-Tools LampPlacer Handler";
    }
}

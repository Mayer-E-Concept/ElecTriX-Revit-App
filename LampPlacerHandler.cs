// LampPlacerHandler.cs — ME-Tools | Lamp Placer
// Mayer E-Concept SRL
using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using Autodesk.Revit.DB.Mechanical;
using Autodesk.Revit.DB.Structure;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using OperationCanceledException = Autodesk.Revit.Exceptions.OperationCanceledException;

namespace METools.LampPlacer
{
    // ── Selection filters ────────────────────────────────────────────────────
    public class RoomOrSpaceFilter : ISelectionFilter
    {
        public bool AllowElement(Element e) => e is Room || e is Space;
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

        // ── Execute ───────────────────────────────────────────────────────
        public void Execute(UIApplication app)
        {
            var uidoc = app.ActiveUIDocument;
            var doc   = uidoc.Document;

            if (Request.Action == LampAction.Redistribute)
            { Redistribute(uidoc, doc); return; }

            if (Request.Action == LampAction.RefreshRoom)
            { RefreshRoom(uidoc, doc); return; }

            if (Request.Action == LampAction.RefreshMulti)
            { RefreshMulti(uidoc, doc); return; }

            if (Request.Action == LampAction.PlaceOnLine)
            { PlaceOnLine(uidoc, doc); return; }

            // ── Room / Space placement ────────────────────────────────────
            var sym = doc.GetElement(Request.SymbolId) as FamilySymbol;
            if (sym == null) { OnStatus?.Invoke("Family not found."); return; }
            if (!sym.IsActive)
            { using (var t = new Transaction(doc,"Activate")){t.Start();sym.Activate();t.Commit();} }

            var spaces = new List<SpatialElement>();
            try
            {
                if (Request.Action == LampAction.PlaceMulti)
                {
                    var refs = uidoc.Selection.PickObjects(ObjectType.Element,
                        new RoomOrSpaceFilter(), "Click rooms or MEP spaces — ESC when done");
                    spaces = refs.Select(r => doc.GetElement(r) as SpatialElement)
                                 .Where(s => s != null).ToList();
                }
                else
                {
                    var r = uidoc.Selection.PickObject(ObjectType.Element,
                        new RoomOrSpaceFilter(), "Click a room to place lamps");
                    if (doc.GetElement(r) is SpatialElement sp) spaces.Add(sp);
                }
            }
            catch (OperationCanceledException) { OnStatus?.Invoke("Cancelled."); return; }
            catch (Exception ex)               { OnStatus?.Invoke($"Error: {ex.Message}"); return; }

            if (!spaces.Any()) { OnStatus?.Invoke("No rooms selected."); return; }

            var cfg = Request.Config;
            double wallFt   = ToFeet(cfg.WallMargin);
            double offsetFt = ToFeet(cfg.UKDOffset);

            bool isFaceBased = sym.Family.FamilyPlacementType == FamilyPlacementType.WorkPlaneBased
                            || sym.Family.FamilyPlacementType == FamilyPlacementType.OneLevelBasedHosted;

            int total = 0;
            using (var tx = new Transaction(doc, "ME-Tools: Place Lamps"))
            {
                tx.Start();
                foreach (var space in spaces)
                {
                    var bb = space.get_BoundingBox(null);
                    if (bb == null) continue;

                    // UKD: bb.Max.Z works when room upper limit = UKD level.
                    // Fallback: find highest level between room floor and bb.Max.Z.
                    double ukdZ   = ResolveUKDZ(doc, bb, space) - offsetFt;
                    double floorZ = space.Level?.Elevation ?? bb.Min.Z;
                    var    level  = GetNearestLevel(doc, ukdZ);

                    double roomW = bb.Max.X - bb.Min.X;
                    double roomD = bb.Max.Y - bb.Min.Y;
                    double area  = UnitUtils.ConvertFromInternalUnits(space.Area, UnitTypeId.SquareMeters);

                    int rows, cols;
                    CalcGrid(cfg, area, roomW, roomD, out rows, out cols);
                    double angle = CalcAngle(cfg.Rotation, roomW, roomD);
                    var pts = CalcPoints(bb, wallFt, rows, cols, ukdZ, space, floorZ);

                    foreach (var pt in pts)
                    {
                        if (!IsPointInside(space, new XYZ(pt.X, pt.Y, floorZ + 0.5))) continue;
                        FamilyInstance inst = null;
                        try { inst = PlaceLampInstance(doc, sym, pt, level, ukdZ, angle); }
                        catch { continue; }
                        if (inst == null) continue;
                        doc.Regenerate();
                        if (!isFaceBased && Math.Abs(angle) > 0.001)
                            try { ElementTransformUtils.RotateElement(doc, inst.Id,
                                Line.CreateBound(pt, pt + XYZ.BasisZ), angle); } catch { }
                        if (level != null)
                            try { var p = inst.get_Parameter(BuiltInParameter.INSTANCE_SCHEDULE_ONLY_LEVEL_PARAM);
                                if (p != null && !p.IsReadOnly) p.Set(level.Id); } catch { }
                        total++;
                    }
                }
                tx.Commit();
            }
            OnStatus?.Invoke($"Done: {spaces.Count} room(s), {total} lamps placed.");
            OnPlaced?.Invoke(total);
        }

        // ── UKD resolution ────────────────────────────────────────────────
        // Strategy:
        // 1. Always check for a UKD/Decke/Ceiling-named level between floor and ceiling.
        //    → Handles UG rooms where upper limit = EG but "UKD UG" level exists at -910mm.
        // 2. If none found: use bb.Max.Z directly (old working behavior).
        //    → Handles standard rooms where upper limit IS the UKD.
        // NOTE: Never pick OG1/OG2 etc. as intermediate — only explicit UKD-named levels.
        private double ResolveUKDZ(Document doc, BoundingBoxXYZ bb, SpatialElement space)
        {
            double roomMaxZ = bb.Max.Z;
            double roomMinZ = Math.Min(space.Level?.Elevation ?? bb.Min.Z, bb.Min.Z);

            try
            {
                // Search ONLY for explicitly named ceiling levels (UKD, Decke, Ceiling)
                // to avoid accidentally picking regular floor levels (EG, OG1, OG2...)
                var ukdLevel = new FilteredElementCollector(doc)
                    .OfClass(typeof(Level))
                    .Cast<Level>()
                    .Where(l =>
                        l.Elevation > roomMinZ + 0.01 &&
                        l.Elevation < roomMaxZ - 0.01 &&
                        (l.Name.IndexOf("UKD",     StringComparison.OrdinalIgnoreCase) >= 0 ||
                         l.Name.IndexOf("Decke",   StringComparison.OrdinalIgnoreCase) >= 0 ||
                         l.Name.IndexOf("Ceiling", StringComparison.OrdinalIgnoreCase) >= 0))
                    .OrderByDescending(l => l.Elevation)
                    .FirstOrDefault();

                if (ukdLevel != null)
                    return ukdLevel.Elevation;
            }
            catch { }

            return roomMaxZ; // bb.Max.Z is the UKD (standard case)
        }

        // ── Lamp placement ────────────────────────────────────────────────
        // WorkPlaneBased: SketchPlane with -Z normal (ceiling) + FlipWorkPlane if still inverted
        // OneLevelBasedHosted: face reference from ceiling/slab
        // OneLevelBased: level-based + MoveElement to ukdZ
        private FamilyInstance PlaceLampInstance(Document doc, FamilySymbol sym,
            XYZ pt, Level level, double ukdZ, double angle)
        {
            FamilyInstance inst = null;
            var placementType = sym.Family.FamilyPlacementType;

            // lampDir = lamp long axis in plan
            // planeY chosen so Normal = lampDir × planeY = (0,0,-1) [ceiling, pointing DOWN]
            // Proof: (cos a, sin a, 0) × (sin a, -cos a, 0) = (0, 0, -1) ✓
            var lampDir   = new XYZ(Math.Cos(angle), Math.Sin(angle), 0);
            var planeY    = new XYZ(Math.Sin(angle), -Math.Cos(angle), 0);
            var ceilingPt = new XYZ(pt.X, pt.Y, ukdZ);

            try
            {
                if (placementType == FamilyPlacementType.WorkPlaneBased)
                {
                    // Ceiling plane: normal = -Z so lamp hangs from ceiling (not floor-mounted)
                    var plane = Plane.CreateByOriginAndBasis(ceilingPt, lampDir, planeY);
                    var sp    = SketchPlane.Create(doc, plane);
                    inst = doc.Create.NewFamilyInstance(ceilingPt, sym, sp, StructuralType.NonStructural);
                }
                else if (placementType == FamilyPlacementType.OneLevelBasedHosted)
                {
                    var faceRef = FindCeilingFaceRef(doc, pt, ukdZ);
                    if (faceRef != null)
                        inst = doc.Create.NewFamilyInstance(faceRef, ceilingPt, lampDir, sym);
                    else
                    {
                        var plane = Plane.CreateByOriginAndBasis(ceilingPt, lampDir, planeY);
                        var sp    = SketchPlane.Create(doc, plane);
                        inst = doc.Create.NewFamilyInstance(ceilingPt, sym, sp, StructuralType.NonStructural);
                    }
                }
                else
                {
                    // OneLevelBased: standard placement, Z corrected below via MoveElement
                    inst = level != null
                        ? doc.Create.NewFamilyInstance(pt, sym, level, StructuralType.NonStructural)
                        : doc.Create.NewFamilyInstance(pt, sym, StructuralType.NonStructural);
                }
            }
            catch { return null; }

            if (inst == null) return null;
            doc.Regenerate();

            // OneLevelBased: move element to exact ukdZ
            if (placementType != FamilyPlacementType.WorkPlaneBased &&
                placementType != FamilyPlacementType.OneLevelBasedHosted)
            {
                try
                {
                    var locPt = (inst.Location as LocationPoint)?.Point;
                    if (locPt != null)
                    {
                        double dz = ukdZ - locPt.Z;
                        if (Math.Abs(dz) > 0.0001)
                            ElementTransformUtils.MoveElement(doc, inst.Id, new XYZ(0, 0, dz));
                    }
                }
                catch { }
            }

            return inst;
        }

        // ── FindCeilingFaceRef: search Floors (bottom) AND Ceilings near ukdZ ──
        // Searches for a downward-facing face (normal.Z < -0.9) at the UKD height.
        // Floors are searched first (more common as ceiling host in MEP),
        // then Revit ceiling elements as fallback.
        private Reference FindCeilingFaceRef(Document doc, XYZ pt, double ukdZ)
        {
            try
            {
                double tolFt = ToFeet(600); // ±600mm tolerance

                var opts = new Options { ComputeReferences = true, IncludeNonVisibleObjects = false };

                // Search Floors first (UG ceiling = bottom of EG floor slab)
                // and Ceilings as fallback
                var cats = new[]
                {
                    BuiltInCategory.OST_Floors,
                    BuiltInCategory.OST_Ceilings,
                };

                foreach (var cat in cats)
                {
                    var elems = new FilteredElementCollector(doc)
                        .OfCategory(cat)
                        .WhereElementIsNotElementType()
                        .Where(e => {
                            var b = e.get_BoundingBox(null);
                            return b != null
                                && pt.X >= b.Min.X - 1 && pt.X <= b.Max.X + 1
                                && pt.Y >= b.Min.Y - 1 && pt.Y <= b.Max.Y + 1
                                && Math.Abs(b.Min.Z - ukdZ) < tolFt; // bottom face near ukdZ
                        });

                    foreach (var elem in elems)
                    {
                        var geom = elem.get_Geometry(opts);
                        if (geom == null) continue;
                        foreach (GeometryObject go in geom)
                        {
                            var solid = go as Solid;
                            if (solid == null || solid.Faces.Size == 0) continue;
                            foreach (Face face in solid.Faces)
                            {
                                // Bottom face: normal pointing DOWN (Z < -0.9)
                                if (face.ComputeNormal(new UV(0.5, 0.5)).Z < -0.9
                                    && face.Reference != null)
                                    return face.Reference;
                            }
                        }
                    }
                }
            }
            catch { }
            return null;
        }

        // ── Grid helpers ─────────────────────────────────────────────────
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
            double avail_m2 = UnitUtils.ConvertFromInternalUnits(availW * availD, UnitTypeId.SquareMeters);
            if (areaSqm <= 0) areaSqm = avail_m2;
            int nTotal = Math.Max(1, (int)Math.Ceiling(avail_m2 / cfg.SqmPerLamp));

            double ratio = Math.Max(roomW, roomD) / Math.Max(0.01, Math.Min(roomW, roomD));
            if (ratio > 2.5)
            {
                if (roomW >= roomD) { rows = 1; cols = nTotal; }
                else                { cols = 1; rows = nTotal; }
                return;
            }
            double aspect = availW / Math.Max(0.01, availD);
            cols = Math.Max(1, (int)Math.Round(Math.Sqrt(nTotal * aspect)));
            rows = Math.Max(1, (int)Math.Round((double)nTotal / cols));
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

        // Smart points: standard grid + fallback for complex rooms
        private List<XYZ> CalcPoints(BoundingBoxXYZ bb, double wallFt,
            int rows, int cols, double z, SpatialElement space = null, double floorZ = 0)
        {
            double cx    = (bb.Min.X + bb.Max.X) / 2.0;
            double cy    = (bb.Min.Y + bb.Max.Y) / 2.0;
            double roomW = bb.Max.X - bb.Min.X;
            double roomD = bb.Max.Y - bb.Min.Y;
            double margW  = Math.Min(wallFt, roomW * 0.20);
            double margD  = Math.Min(wallFt, roomD * 0.20);
            double availW = Math.Max(roomW * 0.5, roomW - 2.0 * margW);
            double availD = Math.Max(roomD * 0.5, roomD - 2.0 * margD);
            double stepX  = cols > 1 ? availW / (cols - 1) : 0;
            double stepY  = rows > 1 ? availD / (rows - 1) : 0;
            double startX = cols == 1 ? cx : cx - availW / 2.0;
            double startY = rows == 1 ? cy : cy - availD / 2.0;
            int target = Math.Max(1, rows * cols);

            var standardPts = new List<XYZ>();
            for (int r = 0; r < rows; r++)
                for (int c = 0; c < cols; c++)
                    standardPts.Add(new XYZ(startX + c * stepX, startY + r * stepY, z));

            if (space == null) return standardPts;
            int inside = standardPts.Count(pt => IsPointInside(space, new XYZ(pt.X, pt.Y, floorZ + 0.5)));
            if (standardPts.Count == 0 || (double)inside / standardPts.Count >= 0.70)
                return standardPts;

            // Complex room: dense sampling + spread selection
            int sampleN = Math.Max(12, (int)Math.Sqrt(target) * 6);
            double sStepX = availW / Math.Max(1, sampleN - 1);
            double sStepY = availD / Math.Max(1, sampleN - 1);
            var candidates = new List<XYZ>();
            for (int r = 0; r < sampleN; r++)
                for (int c = 0; c < sampleN; c++)
                {
                    var pt = new XYZ(cx - availW/2 + c*sStepX, cy - availD/2 + r*sStepY, z);
                    if (IsPointInside(space, new XYZ(pt.X, pt.Y, floorZ + 0.5)))
                        candidates.Add(pt);
                }
            if (!candidates.Any()) return standardPts;
            if (candidates.Count <= target) return candidates;
            return SelectMaxSpread(candidates, target);
        }

        private static List<XYZ> SelectMaxSpread(List<XYZ> pts, int n)
        {
            if (pts.Count <= n) return pts;
            var sel = new List<XYZ>();
            var rem = pts.ToList();
            double cx = pts.Average(p => p.X), cy = pts.Average(p => p.Y);
            var first = rem.OrderBy(p => Math.Pow(p.X-cx,2)+Math.Pow(p.Y-cy,2)).First();
            sel.Add(first); rem.Remove(first);
            while (sel.Count < n && rem.Count > 0)
            {
                XYZ best = null; double bestD = -1;
                foreach (var p in rem)
                {
                    double d = sel.Min(s => Math.Pow(p.X-s.X,2)+Math.Pow(p.Y-s.Y,2));
                    if (d > bestD) { bestD = d; best = p; }
                }
                if (best == null) break;
                sel.Add(best); rem.Remove(best);
            }
            return sel;
        }

        // ── RefreshRoom ───────────────────────────────────────────────────
        private void RefreshRoom(UIDocument uidoc, Document doc)
        {
            SpatialElement space = null;
            try
            {
                var r = uidoc.Selection.PickObject(ObjectType.Element,
                    new RoomOrSpaceFilter(), "Click a room to refresh lamps");
                space = doc.GetElement(r) as SpatialElement;
            }
            catch (OperationCanceledException) { OnStatus?.Invoke("Cancelled."); return; }
            catch (Exception ex)               { OnStatus?.Invoke($"Error: {ex.Message}"); return; }
            if (space == null) { OnStatus?.Invoke("No room selected."); return; }

            RefreshSingleSpace(doc, space, Request.Config,
                doc.GetElement(Request.SymbolId) as FamilySymbol);
        }

        // ── RefreshMulti ──────────────────────────────────────────────────
        private void RefreshMulti(UIDocument uidoc, Document doc)
        {
            var sym = doc.GetElement(Request.SymbolId) as FamilySymbol;
            if (sym == null) { OnStatus?.Invoke("Family not found."); return; }

            int totalDeleted = 0, totalPlaced = 0;
            var cfg = Request.Config;

            while (true)
            {
                OnStatus?.Invoke(totalPlaced == 0
                    ? "Click rooms to refresh — ESC when done..."
                    : $"Refreshed {totalPlaced} lamps. Click more rooms or ESC...");

                SpatialElement space = null;
                try
                {
                    var r = uidoc.Selection.PickObject(ObjectType.Element,
                        new RoomOrSpaceFilter(), "Click a room — ESC to finish");
                    space = doc.GetElement(r) as SpatialElement;
                }
                catch (OperationCanceledException) { break; }
                catch { break; }

                if (space == null) continue;
                var (del, placed) = RefreshSingleSpace(doc, space, cfg, sym);
                totalDeleted += del; totalPlaced += placed;
            }
            OnStatus?.Invoke($"Done: {totalDeleted} deleted, {totalPlaced} placed.");
            OnPlaced?.Invoke(totalPlaced);
        }

        private (int deleted, int placed) RefreshSingleSpace(Document doc,
            SpatialElement space, LampConfig cfg, FamilySymbol sym)
        {
            if (sym == null) return (0, 0);
            if (!sym.IsActive)
            { using (var t = new Transaction(doc,"Activate")){t.Start();sym.Activate();t.Commit();} }

            double floorZ = space.Level?.Elevation ?? 0;
            var bb = space.get_BoundingBox(null);
            if (bb == null) return (0, 0);

            double ukdZ   = ResolveUKDZ(doc, bb, space) - ToFeet(cfg.UKDOffset);
            var    level  = GetNearestLevel(doc, ukdZ);

            // Find and delete existing lamps in room
            var toDelete = new List<ElementId>();
            foreach (var cat in new[] { BuiltInCategory.OST_LightingFixtures, BuiltInCategory.OST_LightingDevices })
            {
                foreach (var fi in new FilteredElementCollector(doc)
                    .OfClass(typeof(FamilyInstance)).OfCategory(cat)
                    .Cast<FamilyInstance>())
                {
                    XYZ testPt = null;
                    if (fi.Location is LocationPoint lp)
                        testPt = new XYZ(lp.Point.X, lp.Point.Y, floorZ + 0.5);
                    else if (fi.HostFace != null)
                    {
                        var b2 = fi.get_BoundingBox(null);
                        if (b2 != null) testPt = new XYZ((b2.Min.X+b2.Max.X)/2, (b2.Min.Y+b2.Max.Y)/2, floorZ+0.5);
                    }
                    if (testPt == null) continue;
                    try { if (IsPointInside(space, testPt)) toDelete.Add(fi.Id); } catch { }
                }
            }

            double roomW = bb.Max.X - bb.Min.X, roomD = bb.Max.Y - bb.Min.Y;
            double area  = UnitUtils.ConvertFromInternalUnits(space.Area, UnitTypeId.SquareMeters);
            int rows, cols;
            CalcGrid(cfg, area, roomW, roomD, out rows, out cols);
            double angle = CalcAngle(cfg.Rotation, roomW, roomD);
            var pts = CalcPoints(bb, ToFeet(cfg.WallMargin), rows, cols, ukdZ, space, floorZ);

            bool isFaceBased = sym.Family.FamilyPlacementType == FamilyPlacementType.WorkPlaneBased
                            || sym.Family.FamilyPlacementType == FamilyPlacementType.OneLevelBasedHosted;

            int placed = 0;
            using (var tx = new Transaction(doc, "ME-Tools: Refresh Room"))
            {
                tx.Start();
                foreach (var id in toDelete) try { doc.Delete(id); } catch { }
                foreach (var pt in pts)
                {
                    if (!IsPointInside(space, new XYZ(pt.X, pt.Y, floorZ + 0.5))) continue;
                    FamilyInstance inst = null;
                    try { inst = PlaceLampInstance(doc, sym, pt, level, ukdZ, angle); } catch { continue; }
                    if (inst == null) continue;
                    doc.Regenerate();
                    if (!isFaceBased && Math.Abs(angle) > 0.001)
                        try { ElementTransformUtils.RotateElement(doc, inst.Id,
                            Line.CreateBound(pt, pt + XYZ.BasisZ), angle); } catch { }
                    if (level != null)
                        try { var p = inst.get_Parameter(BuiltInParameter.INSTANCE_SCHEDULE_ONLY_LEVEL_PARAM);
                            if (p != null && !p.IsReadOnly) p.Set(level.Id); } catch { }
                    placed++;
                }
                tx.Commit();
            }
            return (toDelete.Count, placed);
        }

        // ── PlaceOnLine: pick existing CurveElement, loop until ESC ──────
        private void PlaceOnLine(UIDocument uidoc, Document doc)
        {
            var sym = doc.GetElement(Request.SymbolId) as FamilySymbol;
            if (sym == null) { OnStatus?.Invoke("Family not found."); return; }
            if (!sym.IsActive)
            { using (var t = new Transaction(doc,"Activate")){t.Start();sym.Activate();t.Commit();} }

            int totalPlaced = 0;
            while (true)
            {
                OnStatus?.Invoke(totalPlaced == 0
                    ? "Pick a model/detail line — ESC to stop..."
                    : $"{totalPlaced} placed. Pick next line or ESC...");

                Curve curve;
                try
                {
                    var r = uidoc.Selection.PickObject(ObjectType.Element,
                        new LineFilter(), "Pick a model line or detail line — ESC to finish");
                    var ce = doc.GetElement(r) as CurveElement;
                    if (ce == null) continue;
                    curve = ce.GeometryCurve;
                }
                catch (OperationCanceledException) { break; }
                catch (Exception ex) { OnStatus?.Invoke($"Error: {ex.Message}"); break; }

                var start = curve.GetEndPoint(0);
                var end   = curve.GetEndPoint(1);
                double lengthFt = start.DistanceTo(end);
                if (lengthFt < 0.01) continue;

                var cfg        = Request.Config;
                double spacingFt = ToFeet(cfg.LineSpacing);
                double offsetFt  = ToFeet(cfg.UKDOffset);

                int count; double startOffset;
                if (cfg.LineMode == LineMode.ByCount)
                {
                    count = Math.Max(1, cfg.LineCount);
                    startOffset = count == 1 ? lengthFt / 2.0 : 0;
                    spacingFt   = count > 1 ? lengthFt / (count - 1) : 0;
                }
                else
                {
                    count = Math.Max(1, (int)Math.Round(lengthFt / spacingFt));
                    startOffset = (lengthFt - (count - 1) * spacingFt) / 2.0;
                }

                var    direction = (end - start).Normalize();
                var    midPt     = start + direction * (lengthFt / 2.0);
                double z         = DetermineLineZ(doc, midPt, offsetFt);
                var    level     = GetNearestLevel(doc, z);
                double lineAngle = Math.Atan2(direction.Y, direction.X);
                double angle     = cfg.LineOrientation == LineOrientation.AlongLine
                    ? lineAngle + Math.PI / 2.0
                    : lineAngle;

                int placed = 0;
                using (var tx = new Transaction(doc, "ME-Tools: Place Lamps on Line"))
                {
                    tx.Start();
                    for (int i = 0; i < count; i++)
                    {
                        double dist = startOffset + i * spacingFt;
                        var pt = new XYZ(start.X + direction.X * dist,
                                         start.Y + direction.Y * dist, z);
                        FamilyInstance inst = null;
                        try { inst = PlaceLampInstance(doc, sym, pt, level, z, angle); } catch { continue; }
                        if (inst == null) continue;
                        doc.Regenerate();
                        if (level != null)
                            try { var p = inst.get_Parameter(BuiltInParameter.INSTANCE_SCHEDULE_ONLY_LEVEL_PARAM);
                                if (p != null && !p.IsReadOnly) p.Set(level.Id); } catch { }
                        placed++;
                    }
                    tx.Commit();
                }
                totalPlaced += placed;
            }
            OnStatus?.Invoke($"Done: {totalPlaced} lamps placed on lines.");
            OnPlaced?.Invoke(totalPlaced);
        }

        private double DetermineLineZ(Document doc, XYZ midPt, double offsetFt)
        {
            try
            {
                var spaces = new FilteredElementCollector(doc)
                    .OfClass(typeof(SpatialElement))
                    .Cast<SpatialElement>()
                    .Where(s => s.Area > 0);

                foreach (var sp in spaces)
                {
                    var bb = sp.get_BoundingBox(null);
                    if (bb == null) continue;
                    if (midPt.X < bb.Min.X || midPt.X > bb.Max.X) continue;
                    if (midPt.Y < bb.Min.Y || midPt.Y > bb.Max.Y) continue;
                    double testZ = sp.Level?.Elevation ?? 0;
                    if (!IsPointInside(sp, new XYZ(midPt.X, midPt.Y, testZ + 0.5))) continue;
                    return ResolveUKDZ(doc, bb, sp) - offsetFt;
                }
            }
            catch { }
            return midPt.Z - offsetFt;
        }

        // ── Redistribute ──────────────────────────────────────────────────
        private void Redistribute(UIDocument uidoc, Document doc)
        {
            var selected = uidoc.Selection.GetElementIds()
                .Select(id => doc.GetElement(id) as FamilyInstance)
                .Where(fi => fi?.Location is LocationPoint)
                .OrderBy(fi => ((LocationPoint)fi.Location).Point.X)
                .ThenBy(fi => ((LocationPoint)fi.Location).Point.Y)
                .ToList();
            if (selected.Count < 2) { OnStatus?.Invoke("Select at least 2 lamps to redistribute."); return; }

            var pts  = selected.Select(fi => ((LocationPoint)fi.Location).Point).ToList();
            double avgZ  = pts.Average(p => p.Z);
            double spanX = pts.Max(p => p.X) - pts.Min(p => p.X);
            double spanY = pts.Max(p => p.Y) - pts.Min(p => p.Y);
            bool alongX  = spanX >= spanY;
            List<XYZ> newPts; double angle = 0;

            if (alongX)
            {
                double startX = pts.Min(p => p.X), endX = pts.Max(p => p.X);
                double avgY   = pts.Average(p => p.Y);
                double step   = (endX - startX) / (selected.Count - 1);
                newPts = Enumerable.Range(0, selected.Count)
                    .Select(i => new XYZ(startX + i * step, avgY, avgZ)).ToList();
            }
            else
            {
                var sortedY = selected.OrderBy(fi => ((LocationPoint)fi.Location).Point.Y).ToList();
                pts = sortedY.Select(fi => ((LocationPoint)fi.Location).Point).ToList();
                selected = sortedY;
                double startY = pts.Min(p => p.Y), endY = pts.Max(p => p.Y);
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
                    try
                    {
                        var fi = selected[i]; var oldPt = ((LocationPoint)fi.Location).Point;
                        ElementTransformUtils.MoveElement(doc, fi.Id,
                            new XYZ(newPts[i].X, newPts[i].Y, oldPt.Z) - oldPt);
                        doc.Regenerate();
                        if (Math.Abs(angle) > 0.001)
                        {
                            var axis = Line.CreateBound(new XYZ(newPts[i].X,newPts[i].Y,oldPt.Z),
                                                        new XYZ(newPts[i].X,newPts[i].Y,oldPt.Z)+XYZ.BasisZ);
                            var cur = fi.GetTransform();
                            double delta = angle - Math.Atan2(cur.BasisX.Y, cur.BasisX.X);
                            if (Math.Abs(delta) > 0.001)
                                ElementTransformUtils.RotateElement(doc, fi.Id, axis, delta);
                        }
                    }
                    catch { }
                tx.Commit();
            }
            OnStatus?.Invoke($"Redistributed {selected.Count} lamps evenly.");
        }

        // ── Helpers ──────────────────────────────────────────────────────
        private bool IsPointInside(SpatialElement space, XYZ pt)
        {
            try
            {
                if (space is Room r)   return r.IsPointInRoom(pt);
                if (space is Space s)  return s.IsPointInSpace(pt);
            }
            catch { }
            return true;
        }

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

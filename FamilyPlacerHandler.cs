// FamilyPlacerHandler.cs — ME-Tools | Family Placer
// Mayer E-Concept SRL
using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using Autodesk.Revit.UI;
using OperationCanceledException = Autodesk.Revit.Exceptions.OperationCanceledException;

namespace METools.FamilyPlacer
{
    public class FamilyPlacerHandler : IExternalEventHandler
    {
        public PlacerRequest        Request     { get; set; } = new PlacerRequest();
        public List<FamilyTypeInfo> AllFamilies { get; set; } = new List<FamilyTypeInfo>();
        public Action<string>       OnStatus    { get; set; }
        public Action<int>          OnPlaced    { get; set; }

        public void Execute(UIApplication app)
        {
            PlaceNative(app.ActiveUIDocument, app.ActiveUIDocument.Document);
        }

        public string GetName() => "ME-Tools FamilyPlacer Handler";

        // ── Core: always PromptForFamilyInstancePlacement → Spacebar works ──
        private void PlaceNative(UIDocument uidoc, Document doc)
        {
            var slots = Request.Slots
                .Where(s => !string.IsNullOrEmpty(s.FamilyName)).ToList();
            if (!slots.Any()) { OnStatus?.Invoke("No families configured."); return; }

            // Confirm the user is on the level they think they are.
            if (!METools.LevelGuard.ConfirmPlacementLevel(uidoc, doc, Request.LevelId))
            { OnStatus?.Invoke("Cancelled (level check)."); return; }

            // Get first symbol — must already be active (activated at window open)
            var firstSymId = FamilyLoader.FindSymbolId(AllFamilies, slots[0].FamilyName, slots[0].TypeName);
            if (firstSymId == ElementId.InvalidElementId) { OnStatus?.Invoke("First family not found."); return; }
            var firstSym = doc.GetElement(firstSymId) as FamilySymbol;
            if (firstSym == null) { OnStatus?.Invoke("Invalid symbol."); return; }

            // Ensure active (without full activation transaction if possible)
            if (!firstSym.IsActive)
            {
                using (var tx = new Transaction(doc, "Activate"))
                { tx.Start(); firstSym.Activate(); tx.Commit(); }
            }

            // Capture placed instances
            var captured = new List<FamilyInstance>();

            // Both modes use the interactive placement (SPACEBAR rotate, face/host picking).
            // Single mode auto-ends the loop with ESC right after the first instance is placed.
            bool single = Request.Action == HandlerAction.PlaceSingle;
            IntPtr revitHwnd = IntPtr.Zero;
            try { revitHwnd = uidoc.Application.MainWindowHandle; } catch { }
            bool escSent = false;

            void OnChanged(object s, Autodesk.Revit.DB.Events.DocumentChangedEventArgs e)
            {
                foreach (var id in e.GetAddedElementIds())
                    if (doc.GetElement(id) is FamilyInstance fi && fi.Symbol?.Id == firstSymId)
                        captured.Add(fi);
                if (single && !escSent && captured.Count >= 1) { escSent = true; SendEsc(revitHwnd); }
            }

            doc.Application.DocumentChanged += OnChanged;
            try
            {
                OnStatus?.Invoke(single
                    ? "SPACEBAR rotates - click a face/point to place one"
                    : "Click positions - SPACEBAR rotates - ESC when done");
                uidoc.PromptForFamilyInstancePlacement(firstSym);
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) { OnStatus?.Invoke("Error: " + ex.Message); }
            finally { doc.Application.DocumentChanged -= OnChanged; }

            if (!captured.Any()) { OnStatus?.Invoke("Cancelled."); return; }

            // Single mode safety: if ESC arrived late, keep only the first placement.
            if (single && captured.Count > 1)
            {
                using (var tx = new Transaction(doc, "ME-Tools: Trim"))
                {
                    tx.Start();
                    for (int i = 1; i < captured.Count; i++)
                        try { doc.Delete(captured[i].Id); } catch { }
                    tx.Commit();
                }
                captured = captured.Take(1).ToList();
            }

            // Place remaining slots + set parameters
            int total = 0;
            using (var tx = new Transaction(doc, "ME-Tools: Family Stack"))
            {
                tx.Start();
                const double frameWidthMm = 75.0;

                foreach (var firstInst in captured)
                {
                    XYZ pt = GetPt(firstInst);
                    if (pt == null) continue;

                    double angle = GetAngle(firstInst);

                    // Use actual level of placed instance
                    Level level = null;
                    try { level = doc.GetElement(firstInst.LevelId) as Level; } catch { }
                    level = level ?? ResolveLevel(doc, Request.LevelId) ?? GetNearestLevel(doc, pt);

                    var wall    = GetNearestWall(doc, pt);
                    var walkDir = WallDir(wall);

                    // ★ Detect HOW the first family was placed (face vs workplane)
                    bool isHosted = false;
                    Reference hostFace   = null;
                    XYZ       faceNormal = XYZ.BasisZ;
                    try
                    {
                        isHosted = firstInst.Host != null;
                        if (isHosted)
                        {
                            hostFace = firstInst.HostFace;
                            if (hostFace != null)
                            {
                                var go = doc.GetElement(hostFace.ElementId)
                                            ?.GetGeometryObjectFromReference(hostFace);
                                if (go is Face f) faceNormal = f.ComputeNormal(UV.Zero);
                            }
                        }
                    }
                    catch { isHosted = false; }

                    // ★ Get level plane reference for proper workplane-based placement
                    Reference levelPlaneRef = null;
                    if (!isHosted && level != null)
                    {
                        try { levelPlaneRef = level.GetPlaneReference(); } catch { }
                    }

                    // Direction along wall or default
                    XYZ placeDir = walkDir.IsZeroLength() ? XYZ.BasisX : walkDir;

                    // Set params on first instance
                    SetNiveau(firstInst, slots[0].Height);
                    ApplyOffset(firstInst, slots[0]);
                    ApplyOverrides(firstInst, slots[0]);
                    total++;

                    // Place remaining slots
                    for (int i = 1; i < slots.Count; i++)
                    {
                        var slot  = slots[i];
                        var symId = FamilyLoader.FindSymbolId(AllFamilies, slot.FamilyName, slot.TypeName);
                        if (symId == ElementId.InvalidElementId) continue;
                        var sym = doc.GetElement(symId) as FamilySymbol;
                        if (sym == null) continue;

                        XYZ placePt = Request.Orientation == "Horizontal"
                            ? pt + walkDir * UnitUtils.ConvertToInternalUnits(i * frameWidthMm, UnitTypeId.Millimeters)
                            : new XYZ(pt.X, pt.Y, pt.Z);

                        FamilyInstance inst = null;

                        if (isHosted && hostFace != null)
                        {
                            // Face-based: same host face as first instance
                            try { inst = doc.Create.NewFamilyInstance(hostFace, placePt, faceNormal, sym); } catch { }
                        }
                        else if (levelPlaneRef != null)
                        {
                            // ★ Workplane-based: place ON the level plane → proper Arbeitsebene
                            try { inst = doc.Create.NewFamilyInstance(levelPlaneRef, placePt, placeDir, sym); } catch { }
                        }

                        // Fallbacks
                        if (inst == null && wall != null)
                            try { inst = doc.Create.NewFamilyInstance(placePt, sym, wall, level, StructuralType.NonStructural); } catch { }
                        if (inst == null && level != null)
                            try { inst = doc.Create.NewFamilyInstance(placePt, sym, level, StructuralType.NonStructural); } catch { }
                        if (inst == null)
                            try { inst = doc.Create.NewFamilyInstance(placePt, sym, StructuralType.NonStructural); } catch { }

                        if (inst == null) continue;
                        doc.Regenerate();

                        // Same rotation as first
                        if (Math.Abs(angle) > 0.001)
                            try
                            {
                                var axis = Line.CreateBound(placePt, placePt + XYZ.BasisZ);
                                ElementTransformUtils.RotateElement(doc, inst.Id, axis, angle);
                            }
                            catch { }

                        SetNiveau(inst, slot.Height);
                        ApplyOffset(inst, slot);
                        ApplyOverrides(inst, slot);
                        total++;
                    }
                }
                tx.Commit();
            }

            OnStatus?.Invoke($"Done: {captured.Count} position(s) × {slots.Count} = {total} families.");
            OnPlaced?.Invoke(total);
        }

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool PostMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

        // Sends ESC to Revit to end PromptForFamilyInstancePlacement after the first drop (single mode).
        private static void SendEsc(IntPtr hWnd)
        {
            if (hWnd == IntPtr.Zero) return;
            const uint WM_KEYDOWN = 0x0100, WM_KEYUP = 0x0101;
            const int  VK_ESCAPE  = 0x1B;
            try
            {
                PostMessage(hWnd, WM_KEYDOWN, (IntPtr)VK_ESCAPE, IntPtr.Zero);
                PostMessage(hWnd, WM_KEYUP,   (IntPtr)VK_ESCAPE, IntPtr.Zero);
            }
            catch { }
        }

        // ── Helpers ───────────────────────────────────────────────────────────
        private void SetNiveau(FamilyInstance inst, double valueMm)
        {
            try
            {
                var p = inst.LookupParameter("Niveau");
                if (p == null || p.IsReadOnly) return;
                if (p.StorageType == StorageType.Double)
                {
                    try
                    {
                        var uid = p.GetUnitTypeId();
                        bool isLen = uid != null && uid.TypeId != "autodesk.unit.unit:none-1.0.0" && !string.IsNullOrEmpty(uid.TypeId);
                        p.Set(isLen ? UnitUtils.ConvertToInternalUnits(valueMm, UnitTypeId.Millimeters) : valueMm);
                    }
                    catch { p.Set(valueMm); }
                }
                else if (p.StorageType == StorageType.Integer)
                    p.Set((int)valueMm);
            }
            catch { }
        }

        private void SetParamInt(FamilyInstance inst, int val, string name)
        { try { inst.LookupParameter(name)?.Set(val); } catch { } }

        // Off X -> 2DX, Off Y -> 2DY (direct mapping; matches the UI labels in both arrangements).
        private void ApplyOffset(FamilyInstance inst, FamilySlot slot)
        {
            SetParamInt(inst, slot.OffsetX, "2DX_Versatzfaktor");
            SetParamInt(inst, slot.OffsetY, "2DY_Versatzfaktor");
        }

        private void ApplyOverrides(FamilyInstance inst, FamilySlot slot)
        {
            if (slot?.ParamOverrides == null) return;
            foreach (var kv in slot.ParamOverrides)
                SetParamGeneric(inst, kv.Key, kv.Value);
        }

        private void SetParamGeneric(FamilyInstance inst, string name, string raw)
        {
            try
            {
                var p = inst.LookupParameter(name);
                if (p == null || p.IsReadOnly) return;
                switch (p.StorageType)
                {
                    case StorageType.Integer:
                        if (int.TryParse(raw, out int iv)) p.Set(iv);
                        break;
                    case StorageType.Double:
                        if (double.TryParse(raw, out double dv))
                        {
                            bool isLen = false;
                            try
                            {
                                var uid = p.GetUnitTypeId();
                                isLen = uid != null && !string.IsNullOrEmpty(uid.TypeId)
                                        && uid.TypeId != "autodesk.unit.unit:none-1.0.0";
                            }
                            catch { }
                            p.Set(isLen ? UnitUtils.ConvertToInternalUnits(dv, UnitTypeId.Millimeters) : dv);
                        }
                        break;
                    case StorageType.String:
                        p.Set(raw);
                        break;
                }
            }
            catch { }
        }

        private XYZ GetPt(FamilyInstance fi)
        { try { if (fi.Location is LocationPoint lp) return lp.Point; } catch { } return null; }

        private double GetAngle(FamilyInstance fi)
        { try { var t = fi.GetTransform(); return Math.Atan2(t.BasisX.Y, t.BasisX.X); } catch { return 0; } }

        private Level ResolveLevel(Document doc, ElementId id)
        { try { return id != null && id != ElementId.InvalidElementId ? doc.GetElement(id) as Level : null; } catch { return null; } }

        private Level GetNearestLevel(Document doc, XYZ pt)
        { try { return new FilteredElementCollector(doc).OfClass(typeof(Level)).Cast<Level>().OrderBy(l => Math.Abs(l.Elevation - pt.Z)).FirstOrDefault(); } catch { return null; } }

        private Wall GetNearestWall(Document doc, XYZ pt)
        {
            try
            {
                double r = UnitUtils.ConvertToInternalUnits(500, UnitTypeId.Millimeters);
                var bb = new Outline(pt - new XYZ(r, r, r), pt + new XYZ(r, r, r));
                return new FilteredElementCollector(doc).OfClass(typeof(Wall))
                    .WherePasses(new BoundingBoxIntersectsFilter(bb)).Cast<Wall>()
                    .OrderBy(w => WallDist(w, pt)).FirstOrDefault();
            }
            catch { return null; }
        }

        private double WallDist(Wall w, XYZ pt)
        { try { if (w.Location is LocationCurve lc) return lc.Curve.Distance(pt); } catch { } return double.MaxValue; }

        private XYZ WallDir(Wall w)
        {
            if (w == null) return XYZ.BasisX;
            try { if (w.Location is LocationCurve lc) return (lc.Curve.GetEndPoint(1) - lc.Curve.GetEndPoint(0)).Normalize(); } catch { }
            return XYZ.BasisX;
        }
    }
}

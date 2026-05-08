// CollisionCheckerHandler.cs — ME-Tools | Clash Detector  v7
// Mayer E-Concept SRL
// -----------------------------------------------------------------
// v7 changes vs v6:
//   • Linked docs now use REAL SOLID-ON-SOLID intersection
//     (SolidUtils.CreateTransformed + ElementIntersectsSolidFilter)
//     instead of bounding-box-only tests. This was the main cause
//     of the "heap of phantom collisions" the user reported.
//   • Tiny-overlap filter added for current-doc clashes so
//     near-coincident touches don't get reported as clashes.
//   • Current-doc geometry filter stays ElementIntersectsElementFilter
//     (it's already solid-accurate in this Revit version).
// -----------------------------------------------------------------
using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using Autodesk.Revit.UI;

namespace METools.ClashDetector
{
    public class ClashDetectorHandler : IExternalEventHandler
    {
        public ClashRequest              Request   { get; set; } = new ClashRequest();
        public Action<string>            OnStatus  { get; set; }
        public Action<List<ClashResult>> OnResults { get; set; }
        public Action<string>            OnHlsInfo { get; set; }
        public Action                    OnRefresh { get; set; }

        private const string INSPECTOR_VIEW = "ME-Tools Clash Inspector";
        private const string FILLED_TYPE    = "ME-Tools Clash Zone";

        // Minimum overlap volume (in feet^3) to count as a real clash.
        // ~4 cm × 4 cm × 4 cm — filters out edge-touching false positives.
        private const double MIN_OVERLAP_FT3 = 0.000074;

        public void Execute(UIApplication app)
        {
            var uidoc = app.ActiveUIDocument;
            var doc   = uidoc.Document;
            switch (Request.Action)
            {
                case ClashAction.RunCheck:        RunCheck(uidoc, doc);    break;
                case ClashAction.MarkInPlan:      MarkInPlan(uidoc, doc);  break;
                case ClashAction.ClearPlanMarkers:ClearMarkers(uidoc, doc);break;
                case ClashAction.PlaceFamily:     PlaceFamily(uidoc, doc); break;
                case ClashAction.SyncFamilies:    SyncFamilies(uidoc, doc);break;
                case ClashAction.NavigatePlan:    NavPlan(uidoc, doc);     break;
                case ClashAction.NavigateTo3D:    NavTo3D(uidoc, doc);     break;
            }
        }
        public string GetName() => "ME-Tools Clash Detector";

        // ════════════════════════════════════════════════════════════════════
        // 1 — COLLISION DETECTION
        // ════════════════════════════════════════════════════════════════════
        private void RunCheck(UIDocument uidoc, Document doc)
        {
            var req    = Request;
            var results = new List<ClashResult>();

            var activeView = uidoc.ActiveView;
            bool scopeView = req.ActiveViewOnly && activeView != null
                && (activeView is ViewPlan || activeView is ViewSection);

            var mepCats = new List<BuiltInCategory>();
            if (req.CheckCableTrays) mepCats.Add(BuiltInCategory.OST_CableTray);
            if (req.CheckConduits)   mepCats.Add(BuiltInCategory.OST_Conduit);
            if (req.CheckDucts)      mepCats.Add(BuiltInCategory.OST_DuctCurves);
            if (req.CheckPipes)      mepCats.Add(BuiltInCategory.OST_PipeCurves);
            if (!mepCats.Any()) { OnStatus?.Invoke("No MEP categories selected."); return; }

            var obsCats = new List<BuiltInCategory>();
            if (req.CheckWalls)      obsCats.Add(BuiltInCategory.OST_Walls);
            if (req.CheckFloors)     obsCats.Add(BuiltInCategory.OST_Floors);
            if (req.CheckStructural) { obsCats.Add(BuiltInCategory.OST_StructuralColumns); obsCats.Add(BuiltInCategory.OST_StructuralFraming); }
            if (!obsCats.Any()) { OnStatus?.Invoke("No obstacle categories selected."); return; }

            var allMep = (scopeView
                ? new FilteredElementCollector(doc, activeView.Id)
                : new FilteredElementCollector(doc))
                .WherePasses(new ElementMulticategoryFilter(mepCats))
                .WhereElementIsNotElementType().ToList();

            var mepElements = allMep.Where(e => !IsFlexElement(e, doc)).ToList();
            int skippedFlex = allMep.Count - mepElements.Count;

            if (!mepElements.Any())
            {
                OnResults?.Invoke(results);
                string hint = BuildHlsHint(doc, req);
                OnStatus?.Invoke("No MEP elements found."
                    + (skippedFlex > 0 ? $" ({skippedFlex} flex skipped.)" : "")
                    + (hint.Length > 0 ? " " + hint : ""));
                OnHlsInfo?.Invoke(hint);
                return;
            }
            OnHlsInfo?.Invoke(BuildHlsHint(doc, req));

            var obsCatFilter = new ElementMulticategoryFilter(obsCats);
            var seen         = new HashSet<string>();
            int index        = 0;
            var levels       = new FilteredElementCollector(doc).OfClass(typeof(Level)).Cast<Level>().ToList();

            var linkInstances = req.CheckLinkedDocs
                ? new FilteredElementCollector(doc).OfClass(typeof(RevitLinkInstance)).Cast<RevitLinkInstance>()
                    .Where(li => li.GetLinkDocument() != null).ToList()
                : new List<RevitLinkInstance>();

            for (int i = 0; i < mepElements.Count; i++)
            {
                var mep   = mepElements[i];
                var mepBB = mep.get_BoundingBox(null);
                if (i % 20 == 0) OnStatus?.Invoke($"Checking {i}/{mepElements.Count}...");

                // ── Current document: solid-accurate ──────────────────────
                if (req.CheckCurrentDoc)
                {
                    try
                    {
                        var hits = (scopeView
                            ? new FilteredElementCollector(doc, activeView.Id)
                            : new FilteredElementCollector(doc))
                            .WherePasses(obsCatFilter)
                            .WherePasses(new ElementIntersectsElementFilter(mep)).ToList();

                        foreach (var obs in hits)
                        {
                            string key = $"{mep.Id.Value}_{obs.Id.Value}";
                            if (!seen.Add(key)) continue;

                            // Tiny-overlap filter (drops edge-touches)
                            var obsBB = obs.get_BoundingBox(null);
                            if (!HasMeaningfulOverlap(mepBB, obsBB)) continue;

                            results.Add(Build(++index, mep, obs, doc, false, null, null,
                                mepBB, obsBB, NearestLevel(levels, mepBB?.Min.Z ?? 0)));
                        }
                    }
                    catch { }
                }

                // ── Linked docs: REAL solid-on-solid test (v7 fix) ─────────
                if (!linkInstances.Any() || mepBB == null) continue;

                // Build MEP solids in world coords once per MEP element
                var mepSolidsWorld = GetElementSolids(mep);
                if (!mepSolidsWorld.Any()) continue;   // no geometry → skip

                foreach (var link in linkInstances)
                {
                    var linkedDoc = link.GetLinkDocument();
                    if (linkedDoc == null) continue;
                    try
                    {
                        var linkTx    = link.GetTotalTransform();
                        var linkTxInv = linkTx.Inverse;

                        // MEP solids transformed into linked-doc local space
                        var mepSolidsLocal = new List<Solid>();
                        foreach (var s in mepSolidsWorld)
                        {
                            try { mepSolidsLocal.Add(SolidUtils.CreateTransformed(s, linkTxInv)); }
                            catch { }
                        }
                        if (!mepSolidsLocal.Any()) continue;

                        // BB pre-filter — fast, narrows candidates
                        var localBB = TransformBB(mepBB, linkTxInv);
                        var outline = new Outline(localBB.Min, localBB.Max);
                        var candidates = new FilteredElementCollector(linkedDoc)
                            .WherePasses(new ElementMulticategoryFilter(obsCats))
                            .WherePasses(new BoundingBoxIntersectsFilter(outline, 0.01))
                            .ToList();

                        // Real solid intersection test — kills phantom clashes
                        foreach (var obs in candidates)
                        {
                            bool hit = false;
                            foreach (var mepSolid in mepSolidsLocal)
                            {
                                try
                                {
                                    var filter = new ElementIntersectsSolidFilter(mepSolid);
                                    if (filter.PassesFilter(obs)) { hit = true; break; }
                                }
                                catch { }
                            }
                            if (!hit) continue;

                            string key = $"{mep.Id.Value}_L{link.Id.Value}_{obs.Id.Value}";
                            if (!seen.Add(key)) continue;

                            var obsLocalBB = obs.get_BoundingBox(null);
                            var obsWorldBB = obsLocalBB != null ? TransformBB(obsLocalBB, linkTx) : null;

                            // Tiny-overlap filter on world BBs
                            if (!HasMeaningfulOverlap(mepBB, obsWorldBB)) continue;

                            results.Add(Build(++index, mep, obs, linkedDoc, true, link.Id, linkedDoc.Title,
                                mepBB, obsWorldBB, NearestLevel(levels, mepBB.Min.Z)));
                        }
                    }
                    catch { }
                }
            }

            OnResults?.Invoke(results);
            string sfx = scopeView ? $" (view: '{activeView.Name}')" : " (all levels)";
            OnStatus?.Invoke($"Done — {results.Count} clash(es){sfx}."
                + (skippedFlex > 0 ? $"  ({skippedFlex} flex skipped)" : ""));
        }

        // ── Geometry helpers ─────────────────────────────────────────────────
        private static List<Solid> GetElementSolids(Element e)
        {
            var result = new List<Solid>();
            try
            {
                var opts = new Options { ComputeReferences = false, IncludeNonVisibleObjects = false };
                var geom = e.get_Geometry(opts);
                if (geom == null) return result;
                foreach (var g in geom)
                {
                    if (g is Solid s && s.Volume > 0) result.Add(s);
                    else if (g is GeometryInstance gi)
                    {
                        foreach (var gg in gi.GetInstanceGeometry())
                            if (gg is Solid gs && gs.Volume > 0) result.Add(gs);
                    }
                }
            }
            catch { }
            return result;
        }

        private static bool HasMeaningfulOverlap(BoundingBoxXYZ a, BoundingBoxXYZ b)
        {
            if (a == null || b == null) return true; // unknown → keep (conservative)
            double x1 = Math.Max(a.Min.X, b.Min.X), x2 = Math.Min(a.Max.X, b.Max.X);
            double y1 = Math.Max(a.Min.Y, b.Min.Y), y2 = Math.Min(a.Max.Y, b.Max.Y);
            double z1 = Math.Max(a.Min.Z, b.Min.Z), z2 = Math.Min(a.Max.Z, b.Max.Z);
            if (x2 <= x1 || y2 <= y1 || z2 <= z1) return false;
            double vol = (x2 - x1) * (y2 - y1) * (z2 - z1);
            return vol >= MIN_OVERLAP_FT3;
        }

        private static bool IsFlexElement(Element e, Document doc)
        {
            try
            {
                var typeId = e.GetTypeId();
                string fn = typeId != ElementId.InvalidElementId
                    ? (doc.GetElement(typeId) as ElementType)?.FamilyName ?? "" : "";
                string low = fn.ToLowerInvariant();
                if (low.Contains("flex") || low.Contains("leerrohr")) return true;
                long catId = e.Category?.Id?.Value ?? 0;
                if (catId == -2008050 || catId == -2008132) return true;
            }
            catch { }
            return false;
        }

        private static string BuildHlsHint(Document doc, ClashRequest req)
        {
            if (!req.CheckDucts && !req.CheckPipes) return "";
            bool hasDucts = new FilteredElementCollector(doc).OfCategory(BuiltInCategory.OST_DuctCurves).WhereElementIsNotElementType().Any();
            bool hasPipes = new FilteredElementCollector(doc).OfCategory(BuiltInCategory.OST_PipeCurves).WhereElementIsNotElementType().Any();
            if (!hasDucts && !hasPipes)
            {
                bool hasLinks = new FilteredElementCollector(doc).OfClass(typeof(RevitLinkInstance)).Any();
                return hasLinks
                    ? "No HLS pipes/ducts in current model — verify the HLS link is loaded."
                    : "No HLS elements found. Link the HLS model to detect clashes.";
            }
            return "";
        }

        // ════════════════════════════════════════════════════════════════════
        // 2 — MARK IN PLAN (FilledRegion only)
        // ════════════════════════════════════════════════════════════════════
        private void MarkInPlan(UIDocument uidoc, Document doc)
        {
            var view    = uidoc.ActiveView;
            var results = Request.ResultsToMark;
            if (results == null || !results.Any()) { OnStatus?.Invoke("No clashes to mark."); return; }
            if (!(view is ViewPlan || view is ViewSection))
            { OnStatus?.Invoke("Switch to a floor plan or section view first."); return; }

            int marked = 0;
            using (var tx = new Transaction(doc, "ME-Tools: Mark Clashes"))
            {
                var fho = tx.GetFailureHandlingOptions();
                fho.SetFailuresPreprocessor(new SilentFailures());
                tx.SetFailureHandlingOptions(fho);
                tx.Start();
                var fillId = GetOrCreateRedFillType(doc);
                foreach (var r in results)
                {
                    r.MarkerIds.Clear();
                    if (fillId == null) continue;
                    try { long id = CreateFill(doc, view, r, fillId); if (id > 0) { r.MarkerIds.Add(id); r.HasPlanMarker = true; marked++; } }
                    catch { }
                }
                tx.Commit();
            }
            OnStatus?.Invoke($"{marked} clash(es) marked in '{view.Name}'.");
            OnRefresh?.Invoke();
        }

        private static long CreateFill(Document doc, View view, ClashResult r, ElementId fillId)
        {
            double x1, y1, x2, y2;
            double z = r.IntersectionCenter?.Z ?? 0;
            if (r.OverlapMin != null && r.OverlapMax != null
                && r.OverlapMax.X > r.OverlapMin.X && r.OverlapMax.Y > r.OverlapMin.Y)
            { x1 = r.OverlapMin.X; y1 = r.OverlapMin.Y; x2 = r.OverlapMax.X; y2 = r.OverlapMax.Y; }
            else
            {
                double hw = Math.Max(r.MepWidthFt / 2.0, 0.16);
                double hh = Math.Max(r.MepHeightFt / 2.0, 0.16);
                x1 = r.IntersectionCenter.X - hw; y1 = r.IntersectionCenter.Y - hh;
                x2 = r.IntersectionCenter.X + hw; y2 = r.IntersectionCenter.Y + hh;
            }
            double mn = 0.165;
            if (x2 - x1 < mn) { double mx = (x1 + x2) / 2; x1 = mx - mn / 2; x2 = mx + mn / 2; }
            if (y2 - y1 < mn) { double my = (y1 + y2) / 2; y1 = my - mn / 2; y2 = my + mn / 2; }
            var loop = CurveLoop.Create(new List<Curve>
            {
                Line.CreateBound(new XYZ(x1, y1, z), new XYZ(x2, y1, z)),
                Line.CreateBound(new XYZ(x2, y1, z), new XYZ(x2, y2, z)),
                Line.CreateBound(new XYZ(x2, y2, z), new XYZ(x1, y2, z)),
                Line.CreateBound(new XYZ(x1, y2, z), new XYZ(x1, y1, z)),
            });
            var fr = FilledRegion.Create(doc, fillId, view.Id, new List<CurveLoop> { loop });
            return fr?.Id.Value ?? -1;
        }

        private static ElementId GetOrCreateRedFillType(Document doc)
        {
            var ex = new FilteredElementCollector(doc).OfClass(typeof(FilledRegionType))
                .Cast<FilledRegionType>().FirstOrDefault(t => t.Name == FILLED_TYPE);
            if (ex != null) return ex.Id;
            var sp = new FilteredElementCollector(doc).OfClass(typeof(FillPatternElement))
                .Cast<FillPatternElement>().FirstOrDefault(fp => fp.GetFillPattern().IsSolidFill);
            var bt = new FilteredElementCollector(doc).OfClass(typeof(FilledRegionType))
                .Cast<FilledRegionType>().FirstOrDefault();
            if (sp == null || bt == null) return null;
            try
            {
                var nt = bt.Duplicate(FILLED_TYPE) as FilledRegionType;
                if (nt == null) return null;
                nt.ForegroundPatternId = sp.Id;
                nt.ForegroundPatternColor = new Color(220, 40, 20);
                nt.BackgroundPatternId = ElementId.InvalidElementId;
                nt.IsMasking = false;
                return nt.Id;
            }
            catch { return null; }
        }

        // ════════════════════════════════════════════════════════════════════
        // 3 — CLEAR MARKERS
        // ════════════════════════════════════════════════════════════════════
        private void ClearMarkers(UIDocument uidoc, Document doc)
        {
            var results = Request.ResultsToMark; if (results == null) return;
            var toDelete = results.Where(r => r.MarkerIds != null)
                .SelectMany(r => r.MarkerIds).Select(v => new ElementId(v)).ToList();
            using (var tx = new Transaction(doc, "ME-Tools: Clear Markers"))
            {
                var fho = tx.GetFailureHandlingOptions();
                fho.SetFailuresPreprocessor(new SilentFailures());
                tx.SetFailureHandlingOptions(fho);
                tx.Start();
                if (toDelete.Any()) try { doc.Delete(toDelete); } catch { }
                foreach (var r in results) { r.HasPlanMarker = false; r.MarkerIds.Clear(); }
                tx.Commit();
            }
            OnStatus?.Invoke("Markers cleared."); OnRefresh?.Invoke();
        }

        // ════════════════════════════════════════════════════════════════════
        // 4 — PLACE OPENING FAMILY
        // ════════════════════════════════════════════════════════════════════
        private void PlaceFamily(UIDocument uidoc, Document doc)
        {
            var req  = Request;
            var list = req.ResultsToPlace?.Where(r => !r.FamilyPlaced).ToList() ?? new List<ClashResult>();
            if (!list.Any()) { OnStatus?.Invoke("No clashes selected."); return; }
            var sym = req.SymbolId != ElementId.InvalidElementId ? doc.GetElement(req.SymbolId) as FamilySymbol : null;
            if (sym == null) { OnStatus?.Invoke("Please select an opening family first."); return; }

            int placed = 0;
            using (var tx = new Transaction(doc, "ME-Tools: Place Opening Family"))
            {
                var fho = tx.GetFailureHandlingOptions();
                fho.SetFailuresPreprocessor(new SilentFailures());
                tx.SetFailureHandlingOptions(fho);
                tx.Start();
                if (!sym.IsActive) sym.Activate();
                foreach (var r in list)
                {
                    try { var inst = PlaceOne(doc, r, sym, req.CaxSettings); if (inst != null) { r.FamilyPlaced = true; r.PlacedFamilyId = inst.Id; placed++; } }
                    catch { }
                }
                tx.Commit();
            }
            OnStatus?.Invoke($"Done — {placed} opening(s) placed."); OnRefresh?.Invoke();
        }

        private FamilyInstance PlaceOne(Document doc, ClashResult r, FamilySymbol sym, CaxSettings cfg)
        {
            var center = r.IntersectionCenter;
            var level  = LevelObj(doc, center.Z);
            if (level == null) return null;

            FamilyInstance inst = null;
            if (!r.IsLinked)
            {
                var wall = doc.GetElement(r.ObstacleElementId) as Wall;
                if (wall != null)
                {
                    try
                    {
                        var mepDir = MepDir(doc.GetElement(r.MepElementId));
                        var faceRef = WallFace(wall, mepDir);
                        if (faceRef != null)
                            inst = doc.Create.NewFamilyInstance(faceRef, ProjWall(wall, center), XYZ.BasisZ, sym);
                    }
                    catch { inst = null; }
                }
            }
            if (inst == null) inst = doc.Create.NewFamilyInstance(center, sym, level, StructuralType.NonStructural);
            if (inst == null) return null;

            double wFt = r.MepWidthFt, hFt = r.MepHeightFt;
            double xUst = cfg.X_Ueberstand_mm / 304.8, zUst = cfg.Z_Ueberstand_mm / 304.8;
            double vz   = cfg.Vorzug_mm / 304.8, nz = cfg.Nachzug_mm / 304.8;

            SP(inst, "Trassenbreite", wFt);        SP(inst, "Trassenhöhe", hFt);
            SP(inst, "X_Überstand_1_User", xUst);  SP(inst, "X_Überstand_2_User", xUst);
            SP(inst, "Z_Überstand_1_User", zUst);  SP(inst, "Z_Überstand_2_User", zUst);
            SP(inst, "Vorzug", vz);                SP(inst, "Nachzug", nz);
            SP(inst, "OKB_zu_Achse", (center.Z - r.FloorLevelElevFt) * 304.8);

            double openW = wFt + 2 * xUst, openH = hFt + 2 * zUst;
            double depth = (r.ObstacleDepthFt > 0.05 ? r.ObstacleDepthFt : 0.66) + vz + nz;
            SBip(inst, BuiltInParameter.FAMILY_WIDTH_PARAM,     openW);
            SBip(inst, BuiltInParameter.FAMILY_HEIGHT_PARAM,    openH);
            SBip(inst, BuiltInParameter.FAMILY_THICKNESS_PARAM, depth);

            string tag = $"METools_MEP={r.MepElementId.Value}|OBS={r.ObstacleElementId?.Value}";
            SP(inst, "CAx_Kommentar", tag); SP(inst, "CAx_Anmerkung", tag);
            try { inst.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS)?.Set(tag); } catch { }
            return inst;
        }

        private static XYZ ProjWall(Wall wall, XYZ pt)
        {
            try { if (wall.Location is LocationCurve lc && lc.Curve is Line wl) return wl.Project(pt).XYZPoint; }
            catch { }
            return pt;
        }

        // ════════════════════════════════════════════════════════════════════
        // 5 — SYNC
        // ════════════════════════════════════════════════════════════════════
        private void SyncFamilies(UIDocument uidoc, Document doc)
        {
            int synced = 0, skipped = 0;
            using (var tx = new Transaction(doc, "ME-Tools: Sync Opening Positions"))
            {
                var fho = tx.GetFailureHandlingOptions();
                fho.SetFailuresPreprocessor(new SilentFailures());
                tx.SetFailureHandlingOptions(fho);
                tx.Start();
                foreach (var inst in new FilteredElementCollector(doc).OfClass(typeof(FamilyInstance)).Cast<FamilyInstance>())
                {
                    string cmt = ReadComment(inst);
                    if (!cmt.Contains("METools_MEP=")) continue;
                    try
                    {
                        long mepId = ParseTag(cmt, "METools_MEP=");
                        long obsId = ParseTag(cmt, "|OBS=");
                        if (mepId == 0) { skipped++; continue; }
                        var mep = doc.GetElement(new ElementId(mepId));
                        var obs = obsId != 0 ? doc.GetElement(new ElementId(obsId)) : null;
                        if (mep == null) { skipped++; continue; }
                        var newPt = OverlapCenter(mep.get_BoundingBox(null), obs?.get_BoundingBox(null));
                        if (newPt == null) { skipped++; continue; }
                        var locPt = (inst.Location as LocationPoint)?.Point;
                        if (locPt != null) { ElementTransformUtils.MoveElement(doc, inst.Id, newPt - locPt); synced++; }
                    }
                    catch { skipped++; }
                }
                tx.Commit();
            }
            OnStatus?.Invoke($"Sync — {synced} repositioned, {skipped} skipped."); OnRefresh?.Invoke();
        }

        // ════════════════════════════════════════════════════════════════════
        // 6+7 — NAVIGATE
        // ════════════════════════════════════════════════════════════════════
        private void NavPlan(UIDocument uidoc, Document doc)
        {
            var r = Request.TargetResult; if (r == null) return;
            try
            {
                var el = doc.GetElement(r.MepElementId);
                if (el == null) return;
                uidoc.Selection.SetElementIds(new List<ElementId> { el.Id });
                uidoc.ShowElements(el.Id);
                OnStatus?.Invoke($"→ {r.MepDescription}");
            }
            catch { }
        }

        private void NavTo3D(UIDocument uidoc, Document doc)
        {
            var r = Request.TargetResult; if (r == null) return;
            View3D v3D = new FilteredElementCollector(doc).OfClass(typeof(View3D)).Cast<View3D>()
                .FirstOrDefault(v => v.Name == INSPECTOR_VIEW && !v.IsTemplate);
            using (var tx = new Transaction(doc, "ME-Tools: 3D Clash Inspector"))
            {
                var fho = tx.GetFailureHandlingOptions();
                fho.SetFailuresPreprocessor(new SilentFailures());
                tx.SetFailureHandlingOptions(fho);
                tx.Start();
                if (v3D == null)
                {
                    var vft = new FilteredElementCollector(doc).OfClass(typeof(ViewFamilyType))
                        .Cast<ViewFamilyType>().FirstOrDefault(v => v.ViewFamily == ViewFamily.ThreeDimensional);
                    if (vft == null) { tx.Commit(); return; }
                    v3D = View3D.CreateIsometric(doc, vft.Id);
                    v3D.Name = INSPECTOR_VIEW;
                }
                var mepBB = doc.GetElement(r.MepElementId)?.get_BoundingBox(null);
                if (mepBB != null)
                {
                    double pad = 2.5;
                    v3D.IsSectionBoxActive = true;
                    v3D.SetSectionBox(new BoundingBoxXYZ
                    {
                        Min = mepBB.Min - new XYZ(pad, pad, pad * 0.5),
                        Max = mepBB.Max + new XYZ(pad, pad, pad * 0.5),
                    });
                }
                tx.Commit();
            }
            uidoc.RequestViewChange(v3D);
            try
            {
                var ids = new List<ElementId> { r.MepElementId };
                if (!r.IsLinked && r.ObstacleElementId != null) ids.Add(r.ObstacleElementId);
                uidoc.Selection.SetElementIds(ids);
            }
            catch { }
            OnStatus?.Invoke($"3D → {r.MepDescription}  ↔  {r.ObstacleDescription}");
        }

        // ════════════════════════════════════════════════════════════════════
        // SHARED HELPERS
        // ════════════════════════════════════════════════════════════════════
        private static ClashResult Build(int idx, Element mep, Element obs, Document obsDoc, bool isLinked, ElementId linkId, string linkName, BoundingBoxXYZ mepBB, BoundingBoxXYZ obsBB, Level lev)
        {
            XYZ oMin = null, oMax = null;
            if (mepBB != null && obsBB != null)
            {
                double x1 = Math.Max(mepBB.Min.X, obsBB.Min.X), x2 = Math.Min(mepBB.Max.X, obsBB.Max.X);
                double y1 = Math.Max(mepBB.Min.Y, obsBB.Min.Y), y2 = Math.Min(mepBB.Max.Y, obsBB.Max.Y);
                double z1 = Math.Max(mepBB.Min.Z, obsBB.Min.Z), z2 = Math.Min(mepBB.Max.Z, obsBB.Max.Z);
                if (x2 > x1 && y2 > y1) { oMin = new XYZ(x1, y1, z1); oMax = new XYZ(x2, y2, z2); }
            }
            var ctr = oMin != null
                ? new XYZ((oMin.X + oMax.X) / 2, (oMin.Y + oMax.Y) / 2, (oMin.Z + oMax.Z) / 2)
                : OverlapCenter(mepBB, obsBB);
            return new ClashResult
            {
                Index = idx,
                MepElementId      = mep.Id,
                MepCategory       = mep.Category?.Name ?? "MEP",
                MepDescription    = Desc(mep, obsDoc),
                MepWidthFt        = MepDim(mep, true,  mepBB),
                MepHeightFt       = MepDim(mep, false, mepBB),
                ObstacleDepthFt   = ObsDepth(obsBB),
                ObstacleElementId = obs.Id,
                ObstacleCategory  = obs.Category?.Name ?? "Obstacle",
                ObstacleDescription = Desc(obs, obsDoc),
                IsLinked          = isLinked,
                LinkName          = linkName,
                LinkInstanceId    = linkId,
                IntersectionCenter = ctr ?? XYZ.Zero,
                OverlapMin        = oMin,
                OverlapMax        = oMax,
                FloorLevelElevFt  = lev?.Elevation ?? 0,
                IsSelected        = true,
            };
        }

        private static double MepDim(Element mep, bool width, BoundingBoxXYZ bb)
        {
            var names = width ? new[] { "Breite", "Width", "Außenbreite" } : new[] { "Höhe", "Height", "Außenhöhe" };
            foreach (var n in names) { var p = mep.LookupParameter(n); if (p?.HasValue == true && p.StorageType == StorageType.Double && p.AsDouble() > 0.001) return p.AsDouble(); }
            foreach (var bip in width
                ? new[] { BuiltInParameter.RBS_CABLETRAY_WIDTH_PARAM, BuiltInParameter.RBS_CURVE_WIDTH_PARAM }
                : new[] { BuiltInParameter.RBS_CABLETRAY_HEIGHT_PARAM, BuiltInParameter.RBS_CURVE_HEIGHT_PARAM })
            { try { var p = mep.get_Parameter(bip); if (p?.HasValue == true && p.AsDouble() > 0.001) return p.AsDouble(); } catch { } }
            foreach (var bip in new[] { BuiltInParameter.RBS_PIPE_OUTER_DIAMETER, BuiltInParameter.RBS_CONDUIT_OUTER_DIAM_PARAM })
            { try { var p = mep.get_Parameter(bip); if (p?.HasValue == true && p.AsDouble() > 0.001) return p.AsDouble(); } catch { } }
            if (bb != null) { var d = new[] { bb.Max.X - bb.Min.X, bb.Max.Y - bb.Min.Y, bb.Max.Z - bb.Min.Z }.OrderBy(v => v).ToArray(); return width ? d[1] : d[0]; }
            return 0.492;
        }
        private static double ObsDepth(BoundingBoxXYZ bb) { if (bb == null) return 0; return Math.Min(Math.Abs(bb.Max.X - bb.Min.X), Math.Abs(bb.Max.Y - bb.Min.Y)); }
        private static XYZ    MepDir(Element mep) { if (mep?.Location is LocationCurve lc) return (lc.Curve.GetEndPoint(1) - lc.Curve.GetEndPoint(0)).Normalize(); return XYZ.BasisX; }
        private static Reference WallFace(Wall wall, XYZ dir) { try { var opts = new Options { ComputeReferences = true }; foreach (var obj in wall.get_Geometry(opts)) if (obj is Solid s) foreach (Face f in s.Faces) if (Math.Abs(f.ComputeNormal(new UV(.5, .5)).DotProduct(dir)) > 0.7) return f.Reference; } catch { } return null; }
        private static BoundingBoxXYZ TransformBB(BoundingBoxXYZ bb, Transform t)
        {
            var c = new[]
            {
                t.OfPoint(new XYZ(bb.Min.X, bb.Min.Y, bb.Min.Z)),
                t.OfPoint(new XYZ(bb.Max.X, bb.Min.Y, bb.Min.Z)),
                t.OfPoint(new XYZ(bb.Min.X, bb.Max.Y, bb.Min.Z)),
                t.OfPoint(new XYZ(bb.Max.X, bb.Max.Y, bb.Min.Z)),
                t.OfPoint(new XYZ(bb.Min.X, bb.Min.Y, bb.Max.Z)),
                t.OfPoint(new XYZ(bb.Max.X, bb.Min.Y, bb.Max.Z)),
                t.OfPoint(new XYZ(bb.Min.X, bb.Max.Y, bb.Max.Z)),
                t.OfPoint(new XYZ(bb.Max.X, bb.Max.Y, bb.Max.Z)),
            };
            return new BoundingBoxXYZ
            {
                Min = new XYZ(c.Min(p => p.X), c.Min(p => p.Y), c.Min(p => p.Z)),
                Max = new XYZ(c.Max(p => p.X), c.Max(p => p.Y), c.Max(p => p.Z)),
            };
        }
        private static XYZ OverlapCenter(BoundingBoxXYZ a, BoundingBoxXYZ b)
        {
            if (a == null && b == null) return XYZ.Zero;
            if (a == null) return (b.Min + b.Max) / 2.0;
            if (b == null) return (a.Min + a.Max) / 2.0;
            return new XYZ(
                (Math.Max(a.Min.X, b.Min.X) + Math.Min(a.Max.X, b.Max.X)) / 2.0,
                (Math.Max(a.Min.Y, b.Min.Y) + Math.Min(a.Max.Y, b.Max.Y)) / 2.0,
                (Math.Max(a.Min.Z, b.Min.Z) + Math.Min(a.Max.Z, b.Max.Z)) / 2.0);
        }
        private static Level NearestLevel(List<Level> lvls, double z) => lvls.OrderBy(l => Math.Abs(l.Elevation - z)).FirstOrDefault();
        private static Level LevelObj(Document doc, double z) => new FilteredElementCollector(doc).OfClass(typeof(Level)).Cast<Level>().OrderBy(l => Math.Abs(l.Elevation - z)).FirstOrDefault();
        private static string Desc(Element e, Document doc) { if (e == null) return "?"; try { var et = doc.GetElement(e.GetTypeId()) as ElementType; if (et != null) return $"{et.FamilyName} – {et.Name}"; } catch { } return $"ID {e.Id.Value}"; }
        private static void SP(Element e, string n, double v) { try { e.LookupParameter(n)?.Set(v); } catch { } }
        private static void SP(Element e, string n, string v) { try { e.LookupParameter(n)?.Set(v); } catch { } }
        private static void SBip(Element e, BuiltInParameter b, double v) { try { e.get_Parameter(b)?.Set(v); } catch { } }
        private static string ReadComment(Element e)
        {
            foreach (var n in new[] { "CAx_Kommentar", "CAx_Anmerkung" })
            { var p = e.LookupParameter(n); if (p != null) return p.AsString() ?? ""; }
            try { return e.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS)?.AsString() ?? ""; } catch { return ""; }
        }
        private static long ParseTag(string t, string tag)
        {
            int s = t.IndexOf(tag, StringComparison.Ordinal);
            if (s < 0) return 0;
            s += tag.Length;
            int e2 = t.IndexOfAny(new[] { '|', ' ' }, s);
            string num = e2 < 0 ? t.Substring(s) : t.Substring(s, e2 - s);
            return long.TryParse(num, out long v) ? v : 0;
        }
        private class SilentFailures : IFailuresPreprocessor
        {
            public FailureProcessingResult PreprocessFailures(FailuresAccessor a)
            {
                foreach (var f in a.GetFailureMessages())
                    if (f.GetSeverity() == FailureSeverity.Warning) a.DeleteWarning(f);
                return FailureProcessingResult.Continue;
            }
        }
    }
}

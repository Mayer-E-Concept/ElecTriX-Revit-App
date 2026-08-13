// CollisionCheckerHandler.cs -- ME-Tools | Collision Checker (conduits/cable trays vs walls)
// Mayer E-Concept SRL
//
// Detection is two-phase:
//   1. FAST pass: a bounding-box overlap check (Outline.Intersects)
//      between each run and each wall, to avoid running the precise check
//      on every pair in the model. Deliberately NOT
//      ElementIntersectsElementFilter -- that filter requires both
//      elements to have valid closed solid geometry, and cable trays
//      (depending on their fitting/shape) don't reliably have that,
//      which silently dropped real cable-tray/wall collisions in testing
//      even though conduits (always a simple cylinder) worked fine.
//   2. PRECISE pass, only for boxes that overlap: Face.Intersect(Curve,
//      out IntersectionResultArray) against the wall's side faces, which
//      returns the exact 3D point(s) where the run's centerline crosses
//      the wall -- needed for the list, the red highlight, "go to", and
//      hole placement. This only needs the run's *curve* (always present
//      via LocationCurve) and the wall's face geometry (always reliable),
//      so it doesn't have the solid-geometry gap the fast filter had.
//
// The link between a placed hole and the run it belongs to (so the hole
// can follow the run if it's later moved -- see CollisionCheckerWatcher)
// is stored via Extensible Storage on a single per-document DataStorage
// element, as a Map<string,string> of hole UniqueId -> "runUniqueId|
// wallUniqueId" (keyed by hole, not run, since one run can cross more
// than one wall -- see the schema section below for why that matters).
// Not on the hole instances themselves individually, because that would
// need scanning every instance of a family whose category isn't known
// ahead of time; a DataStorage element is trivial to find and cheap to
// read/write regardless of what family the hole turns out to be.
using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Electrical;
using Autodesk.Revit.DB.ExtensibleStorage;
using Autodesk.Revit.DB.Structure;
using Autodesk.Revit.UI;

namespace METools.CollisionChecker
{
    public class CollisionCheckerHandler : IExternalEventHandler
    {
        public CollisionCheckerRequest Request { get; set; } = new CollisionCheckerRequest();
        public Action<string>            OnStatus { get; set; }
        public Action<PlaceHolesResult>  OnDone   { get; set; }

        public string GetName() => "ME-Tools Collision Checker";

        public void Execute(UIApplication app)
        {
            var req = Request;
            var doc = app.ActiveUIDocument?.Document;
            if (doc == null || req == null || req.Action == CollisionCheckerAction.None) return;

            if (req.Action == CollisionCheckerAction.PlaceHoles)
                ExecutePlaceHoles(doc, req);
            else if (req.Action == CollisionCheckerAction.MoveHoles)
                ExecuteMoveHoles(doc, req);
            else if (req.Action == CollisionCheckerAction.MarkCollisions)
                ExecuteMarkCollisions(doc, app.ActiveUIDocument?.ActiveView?.Id, req);
            else if (req.Action == CollisionCheckerAction.MarkPlumbingSolved)
                ExecuteMarkPlumbingSolved(doc, req);
        }

        // ═════════════════════════════════════════════════════════════════
        // READ-ONLY: scanning. Called directly from the window (no
        // ExternalEvent), same convention as BatchParamsHandler's scan
        // helpers -- plain reads don't need the round trip.
        // ═════════════════════════════════════════════════════════════════

        private static readonly BuiltInCategory[] RunCategories =
        {
            BuiltInCategory.OST_Conduit,
            BuiltInCategory.OST_CableTray,
        };

        public static List<Element> GetScopedElements(Document doc, UIDocument uiDoc, ScanScope scope, IEnumerable<BuiltInCategory> categories)
        {
            try
            {
                IEnumerable<Element> baseSet;
                switch (scope)
                {
                    case ScanScope.CurrentSelection:
                        baseSet = uiDoc.Selection.GetElementIds().Select(id => doc.GetElement(id)).Where(e => e != null);
                        break;
                    case ScanScope.ActiveView:
                        var view = uiDoc.ActiveView;
                        baseSet = view == null ? Enumerable.Empty<Element>()
                            : new FilteredElementCollector(doc, view.Id).WhereElementIsNotElementType();
                        break;
                    case ScanScope.WholeModel:
                    default:
                        baseSet = new FilteredElementCollector(doc).WhereElementIsNotElementType();
                        break;
                }
                var catIds = new HashSet<int>(categories.Select(c => (int)c));
                return baseSet.Where(e => e.Category != null && catIds.Contains((int)e.Category.Id.Value)).ToList();
            }
            catch { return new List<Element>(); }
        }

        // ═════════════════════════════════════════════════════════════════
        // READ-ONLY: imported IFC/CAD architecture (Insert > Import CAD,
        // not Link) -- an ImportInstance has no Wall category and no
        // per-entity IFC semantics at all once inside Revit (a well-known,
        // widely-reported IFC-import limitation, not something this add-in
        // can fix), so "which of these solids are walls" can't be answered
        // by category the way it can for a real Wall. This uses a
        // geometric heuristic instead: find pairs of large, roughly
        // vertical, anti-parallel planar faces on the same solid, spaced
        // apart by something in a plausible wall-thickness range -- i.e.
        // "the two broad sides of a slab" -- and treat that pair the same
        // way a real wall's own two side faces are already treated above.
        //
        // Deliberately NOT also checking overall height: a solid's own
        // GetBoundingBox() is in ITS OWN local frame plus a Transform, and
        // re-deriving true world-space extents from that under an arbitrary
        // rotation needs all 8 corners transformed, not just Min/Max --
        // skipped here to avoid getting that subtly wrong with no way to
        // test it live. Thickness + face size alone is already a fairly
        // specific signal; if this starts flagging something that clearly
        // isn't a wall (a thick countertop, say), a height check can be
        // added once that's confirmed against a real project.
        // ═════════════════════════════════════════════════════════════════

        private const double ImportedWallMinThicknessFt = 40.0 / 304.8;   // ~40mm -- thin partition
        private const double ImportedWallMaxThicknessFt = 600.0 / 304.8;  // ~600mm -- generous upper bound
        private const double ImportedWallMinFaceAreaSqFt = 0.5 / (0.3048 * 0.3048); // ~0.5 m^2 -- excludes trim/reveal-sized faces

        internal class ImportedWallCandidate
        {
            public ElementId ImportInstanceId { get; set; }
            public string    ImportDisplayName { get; set; } = "";
            public PlanarFace FaceA { get; set; }
            public PlanarFace FaceB { get; set; }
            public double    ThicknessFt { get; set; }
        }

        // Every ImportInstance in the doc, with no name filtering -- an
        // earlier version of this only matched names containing ".ifc",
        // on the assumption architectural coordination backgrounds would
        // actually be IFC. Checked against two real, live projects and
        // found neither had ever imported a .ifc at all -- both use
        // imported DWGs instead (named per firm/architect convention,
        // which differs project to project: "ARC_..." in one, "ARH_..."
        // in the other, so no name pattern is reliable either). The
        // person picks which import is "the architecture" in the UI
        // instead of this trying to guess.
        internal static List<ImportInstance> GetAllImportInstances(Document doc)
        {
            try
            {
                return new FilteredElementCollector(doc)
                    .OfClass(typeof(ImportInstance))
                    .Cast<ImportInstance>()
                    .ToList();
            }
            catch { return new List<ImportInstance>(); }
        }

        // Every RevitLinkInstance in the doc -- covers a genuinely LINKED
        // .ifc (or .rvt) rather than an imported one; see
        // FindWallLikeElementsInLink for how its walls get found. Unloaded
        // links are skipped here (nothing to check yet) rather than
        // included with a confusing empty result later.
        internal static List<RevitLinkInstance> GetAllLoadedRevitLinks(Document doc)
        {
            try
            {
                return new FilteredElementCollector(doc)
                    .OfClass(typeof(RevitLinkInstance))
                    .Cast<RevitLinkInstance>()
                    .Where(li => { try { return li.GetLinkDocument() != null; } catch { return false; } })
                    .ToList();
            }
            catch { return new List<RevitLinkInstance>(); }
        }

        internal static List<ImportedWallCandidate> FindWallLikeSolidsInImport(ImportInstance inst)
        {
            var result = new List<ImportedWallCandidate>();
            string displayName = (inst.Name ?? "").TrimEnd();
            try
            {
                var options = new Options { ComputeReferences = false, IncludeNonVisibleObjects = false };
                var geomElem = inst.get_Geometry(options);
                if (geomElem == null) return result;

                foreach (var solid in FlattenToSolids(geomElem))
                {
                    if (TryFindWallFacePair(solid, out var faceA, out var faceB, out var thickness))
                    {
                        result.Add(new ImportedWallCandidate
                        {
                            ImportInstanceId  = inst.Id,
                            ImportDisplayName = displayName,
                            FaceA = faceA,
                            FaceB = faceB,
                            ThicknessFt = thickness,
                        });
                    }
                }
            }
            catch { }
            return result;
        }

        // Shared by both the ImportInstance path above and the linked-
        // document path below -- given any single Solid, looks for a pair
        // of large, roughly-vertical, anti-parallel planar faces spaced
        // apart by something in a plausible wall-thickness range (see the
        // class-level remarks above FindWallLikeSolidsInImport for why this
        // heuristic exists at all instead of just checking a category).
        private static bool TryFindWallFacePair(Solid solid, out PlanarFace faceA, out PlanarFace faceB, out double thicknessFt)
        {
            faceA = null; faceB = null; thicknessFt = 0;
            try
            {
                if (solid == null || solid.Volume < 1e-6 || solid.Faces == null) return false;

                var candidates = new List<PlanarFace>();
                foreach (Face f in solid.Faces)
                {
                    if (f is PlanarFace pf && pf.Area >= ImportedWallMinFaceAreaSqFt
                        && Math.Abs(pf.FaceNormal.Z) < 0.3)
                        candidates.Add(pf);
                }
                if (candidates.Count < 2) return false;

                for (int i = 0; i < candidates.Count; i++)
                {
                    for (int j = i + 1; j < candidates.Count; j++)
                    {
                        var nA = candidates[i].FaceNormal;
                        var nB = candidates[j].FaceNormal;
                        if (nA.DotProduct(nB) > -0.9) continue; // not close enough to opposite-facing

                        double thickness = Math.Abs((candidates[j].Origin - candidates[i].Origin).DotProduct(nA));
                        if (thickness < ImportedWallMinThicknessFt || thickness > ImportedWallMaxThicknessFt) continue;

                        faceA = candidates[i]; faceB = candidates[j]; thicknessFt = thickness;
                        return true; // one wall-slab reading per solid is enough
                    }
                }
            }
            catch { }
            return false;
        }

        // Walls in a LINKED architectural model -- unlike an imported file,
        // a link has its own real Document you can query, and (checked live
        // against a real project) its walls very often come through as
        // DirectShape elements correctly categorized OST_Walls rather than
        // genuine Wall-class instances -- a well-known consequence of how
        // Revit's IFC linker converts IFC entities. Filtering by CATEGORY
        // instead of by class (OfClass(typeof(Wall)) would miss every one of
        // these) picks up both that case and a genuine native Wall the same
        // way, if the link happens to be an ordinary linked .rvt instead.
        //
        // Category alone is enough to know "this is a wall" here -- unlike
        // the imported-file case, there's no need to also guess via
        // thickness/face-shape. TryFindWallFacePair is still used, but only
        // to find WHICH two faces of an already-known wall are its own two
        // broad sides, for the same Face.Intersect crossing check every
        // other wall type in this file uses.
        //
        // SolidUtils.CreateTransformed brings each solid from the link's own
        // local coordinate system into the host document's, using the
        // link's placement transform -- from that point on this is
        // identical to the imported-file case: same face-pair search, same
        // Face.Intersect against a host-space run curve, no further
        // transform bookkeeping needed anywhere downstream.
        internal static List<ImportedWallCandidate> FindWallLikeElementsInLink(RevitLinkInstance linkInst)
        {
            var result = new List<ImportedWallCandidate>();
            try
            {
                var linkDoc = linkInst.GetLinkDocument();
                if (linkDoc == null) return result; // link unloaded -- nothing to check

                var transform = linkInst.GetTotalTransform();
                var typeName = (linkDoc.GetElement(linkInst.GetTypeId()) as RevitLinkType)?.Name ?? linkInst.Name ?? "";
                if (typeName.EndsWith(".rvt", StringComparison.OrdinalIgnoreCase))
                    typeName = typeName.Substring(0, typeName.Length - 4);

                var options = new Options { ComputeReferences = false, IncludeNonVisibleObjects = false };
                var wallElements = new FilteredElementCollector(linkDoc)
                    .OfCategory(BuiltInCategory.OST_Walls)
                    .WhereElementIsNotElementType()
                    .ToElements();

                foreach (var el in wallElements)
                {
                    try
                    {
                        var geomElem = el.get_Geometry(options);
                        if (geomElem == null) continue;

                        foreach (var localSolid in FlattenToSolids(geomElem))
                        {
                            Solid hostSolid;
                            try { hostSolid = SolidUtils.CreateTransformed(localSolid, transform); }
                            catch { continue; }

                            if (TryFindWallFacePair(hostSolid, out var faceA, out var faceB, out var thickness))
                            {
                                result.Add(new ImportedWallCandidate
                                {
                                    ImportInstanceId  = linkInst.Id, // the link instance itself, resolvable in the HOST doc -- the link's own wall element's id is only valid inside linkDoc
                                    ImportDisplayName = typeName,
                                    FaceA = faceA,
                                    FaceB = faceB,
                                    ThicknessFt = thickness,
                                });
                                break; // one wall-slab reading per element is enough
                            }
                        }
                    }
                    catch { }
                }
            }
            catch { }
            return result;
        }

        // ── Plumbing clash detection ─────────────────────────────────────
        // A genuinely different check from everything above: not a run
        // crossing a fixed wall, but two routed MEP systems (this
        // document's own cable trays/conduits, and pipes/fittings from a
        // linked plumbing model) potentially overlapping in open space.
        internal class PlumbingCandidate
        {
            public string Description = "";
            public string UniqueId = ""; // the linked pipe/fitting's own UniqueId, within its own link document -- see the solved-clash schema below
            public BoundingBoxXYZ HostBBox;
        }

        private static readonly BuiltInCategory[] PlumbingCategories =
        {
            BuiltInCategory.OST_PipeCurves,
            BuiltInCategory.OST_FlexPipeCurves,
            BuiltInCategory.OST_PipeFitting,
            BuiltInCategory.OST_PipeAccessory,
            BuiltInCategory.OST_PlumbingFixtures,
        };

        // Pipes/fittings from a linked plumbing model, with bounding boxes
        // already transformed into host-document coordinates. Confirmed
        // live against a real project: like the architecture walls
        // elsewhere in this file, pipes/fittings from an IFC-linked
        // plumbing model come through as generic DirectShape elements
        // (2344 pipe curves, 2295 fittings, all DirectShape, none of them
        // a real Pipe/MechanicalFitting class instance with a Diameter
        // parameter to read) -- so there's no exact diameter or precise
        // centerline to read the way a native Revit Pipe would offer.
        // A bounding-box overlap test is a reasonable, well-established
        // first pass for this kind of clash detection (plenty of BIM
        // coordination tools start exactly here), at the cost of
        // occasionally flagging a near-miss for an oddly-angled run as if
        // it were a real clash -- worth a quick visual check on each
        // flagged row rather than trusting this as pixel-precise.
        internal static List<PlumbingCandidate> FindPlumbingElementsInLink(RevitLinkInstance linkInst)
        {
            var result = new List<PlumbingCandidate>();
            try
            {
                var linkDoc = linkInst?.GetLinkDocument();
                if (linkDoc == null) return result;
                var transform = linkInst.GetTotalTransform();

                foreach (var cat in PlumbingCategories)
                {
                    List<Element> elements;
                    try
                    {
                        elements = new FilteredElementCollector(linkDoc)
                            .OfCategory(cat)
                            .WhereElementIsNotElementType()
                            .ToElements()
                            .ToList();
                    }
                    catch { continue; }

                    string catLabel = cat == BuiltInCategory.OST_PipeCurves ? "Pipe"
                                     : cat == BuiltInCategory.OST_FlexPipeCurves ? "Flex Pipe"
                                     : cat == BuiltInCategory.OST_PipeFitting ? "Pipe Fitting"
                                     : cat == BuiltInCategory.OST_PipeAccessory ? "Pipe Accessory"
                                     : "Plumbing Fixture";

                    foreach (var el in elements)
                    {
                        try
                        {
                            var localBox = el.get_BoundingBox(null);
                            if (localBox == null) continue;

                            // Transform all 8 corners, not just Min/Max --
                            // the link's placement transform can include a
                            // rotation, and transforming only the two
                            // diagonal corners would produce a box that
                            // doesn't actually contain the real
                            // transformed shape whenever a rotation is
                            // involved (same reasoning already applied to
                            // rotated hosts elsewhere in this file).
                            XYZ min = null, max = null;
                            for (int xi = 0; xi < 2; xi++)
                            for (int yi = 0; yi < 2; yi++)
                            for (int zi = 0; zi < 2; zi++)
                            {
                                var corner = new XYZ(
                                    xi == 0 ? localBox.Min.X : localBox.Max.X,
                                    yi == 0 ? localBox.Min.Y : localBox.Max.Y,
                                    zi == 0 ? localBox.Min.Z : localBox.Max.Z);
                                var hp = transform.OfPoint(corner);
                                min = min == null ? hp : new XYZ(Math.Min(min.X, hp.X), Math.Min(min.Y, hp.Y), Math.Min(min.Z, hp.Z));
                                max = max == null ? hp : new XYZ(Math.Max(max.X, hp.X), Math.Max(max.Y, hp.Y), Math.Max(max.Z, hp.Z));
                            }
                            if (min == null || max == null) continue;

                            result.Add(new PlumbingCandidate
                            {
                                Description = catLabel,
                                UniqueId = el.UniqueId ?? "",
                                HostBBox = new BoundingBoxXYZ { Min = min, Max = max },
                            });
                        }
                        catch { }
                    }
                }
            }
            catch { }
            return result;
        }

        // For each run, checks its bounding box against every plumbing
        // candidate's -- unlike the wall-crossing check's 600mm quick-
        // filter tolerance (deliberately generous, since that's only a
        // candidate filter ahead of a separate precise Face.Intersect
        // step), this tolerance is small and IS the actual detection
        // signal, so a generous one here would flag near-misses as real
        // clashes.
        internal static List<CollisionInfo> ScanForPlumbingClashes(Document doc, List<Element> runs, RevitLinkInstance plumbingLink)
        {
            var result = new List<CollisionInfo>();
            if (doc == null || runs == null || plumbingLink == null) return result;

            var candidates = FindPlumbingElementsInLink(plumbingLink);
            if (candidates.Count == 0) return result;

            var solvedKeys = ReadSolvedPlumbingClashKeys(doc);
            const double clashToleranceFt = 5.0 / 304.8; // ~5mm, geometry-rounding allowance only

            foreach (var run in runs)
            {
                try
                {
                    var runBox = run.get_BoundingBox(null);
                    if (runBox == null) continue;
                    var outline = new Outline(runBox.Min, runBox.Max);

                    ElementId levelId = ElementId.InvalidElementId;
                    string levelName = "";
                    try
                    {
                        levelId = ResolveLevelId(doc, run);
                        levelName = (doc.GetElement(levelId) as Level)?.Name ?? "";
                    }
                    catch { }

                    foreach (var cand in candidates)
                    {
                        try
                        {
                            if (cand.HostBBox == null) continue;
                            if (!outline.Intersects(new Outline(cand.HostBBox.Min, cand.HostBBox.Max), clashToleranceFt)) continue;

                            // The overlap region's own center -- roughly
                            // where the two systems actually meet, rather
                            // than either element's unrelated centroid.
                            var ovMin = new XYZ(
                                Math.Max(runBox.Min.X, cand.HostBBox.Min.X),
                                Math.Max(runBox.Min.Y, cand.HostBBox.Min.Y),
                                Math.Max(runBox.Min.Z, cand.HostBBox.Min.Z));
                            var ovMax = new XYZ(
                                Math.Min(runBox.Max.X, cand.HostBBox.Max.X),
                                Math.Min(runBox.Max.Y, cand.HostBBox.Max.Y),
                                Math.Min(runBox.Max.Z, cand.HostBBox.Max.Z));
                            var clashPoint = new XYZ((ovMin.X + ovMax.X) / 2.0, (ovMin.Y + ovMax.Y) / 2.0, (ovMin.Z + ovMax.Z) / 2.0);
                            var combinedKey = $"{run.UniqueId}|{cand.UniqueId}";

                            result.Add(new CollisionInfo
                            {
                                Kind = CollisionKind.PlumbingClash,
                                ElementId = run.Id,
                                ElementCategory = run.Category?.Name ?? "",
                                ElementTypeName = (doc.GetElement(run.GetTypeId()) as ElementType)?.Name ?? "",
                                PlumbingElementDescription = cand.Description,
                                PlumbingElementUniqueId = cand.UniqueId,
                                IsSolved = solvedKeys.Contains(combinedKey),
                                LevelId = levelId,
                                LevelName = levelName,
                                Point = clashPoint,
                                IsExternalGeometry = true, // no host-document Wall/hole placement applies to this kind of row either
                            });
                        }
                        catch { }
                    }
                }
                catch { }
            }
            return result;
        }

        // GetInstanceGeometry() (used here, not GetSymbolGeometry()) returns
        // geometry already in the project's own coordinate system -- exactly
        // what's needed to compare directly against host-document run
        // curves with no extra transform. Confirmed against Autodesk's own
        // API remarks: its one real caveat is that the References inside
        // this copy can't be used to create new elements that reference the
        // original (e.g. dimensions) -- irrelevant here, since Face.Intersect
        // for a crossing POINT doesn't need a persisted Reference at all.
        private static IEnumerable<Solid> FlattenToSolids(GeometryElement geomElem)
        {
            foreach (GeometryObject obj in geomElem)
            {
                if (obj is Solid s && s.Volume > 1e-9)
                {
                    yield return s;
                }
                else if (obj is GeometryInstance gi)
                {
                    GeometryElement inner = null;
                    try { inner = gi.GetInstanceGeometry(); } catch { }
                    if (inner != null)
                        foreach (var s2 in FlattenToSolids(inner))
                            yield return s2;
                }
            }
        }

        // Same shape as FindCrossingPoint below, but against an arbitrary
        // face pair instead of a real Wall's own side faces -- there's no
        // FindNearApproachPoint-style fallback here (that one leans on
        // Wall.Location/Wall.Width, neither of which exist for an imported
        // solid); a run that stops just short of imported architecture
        // rather than crossing it isn't caught by this path yet.
        internal static XYZ FindCrossingPointOnFacePair(Face faceA, Face faceB, Curve runCurve)
        {
            var points = new List<XYZ>();
            foreach (var face in new[] { faceA, faceB })
            {
                try
                {
                    var faceResult = face.Intersect(runCurve, out IntersectionResultArray hits);
                    if (faceResult != SetComparisonResult.Overlap || hits == null) continue;
                    foreach (IntersectionResult hit in hits) points.Add(hit.XYZPoint);
                }
                catch { }
            }
            if (points.Count == 0) return null;
            if (points.Count == 1) return points[0];

            XYZ a = points[0], b = points[0];
            double best = -1;
            for (int i = 0; i < points.Count; i++)
                for (int j = i + 1; j < points.Count; j++)
                {
                    var d = points[i].DistanceTo(points[j]);
                    if (d > best) { best = d; a = points[i]; b = points[j]; }
                }
            return (a + b) * 0.5;
        }

        // Runs the two-phase detection described at the top of this file and
        // returns one CollisionInfo per point where a run's centerline
        // crosses a wall. scope applies to the runs being checked; every
        // wall in the whole model is always considered as a potential
        // obstacle regardless of scope, since a run in view/selection scope
        // can still be crossing a wall that itself isn't in that scope.
        public static List<CollisionInfo> ScanForCollisions(Document doc, UIDocument uiDoc, ScanScope scope, ElementId architectureSourceId = null, bool architectureSourceIsLink = false, ElementId holeSymbolId = null, ElementId plumbingLinkId = null)
        {
            var result = new List<CollisionInfo>();
            try
            {
                var runs  = GetScopedElements(doc, uiDoc, scope, RunCategories);
                var walls = new FilteredElementCollector(doc).OfClass(typeof(Wall)).WhereElementIsNotElementType().Cast<Wall>().ToList();

                // Built once per scan, not per run -- geometry parsing on an
                // architectural source (imported file or linked model) can
                // be nontrivial, and every run needs to check against the
                // same fixed candidate set regardless. Only the ONE source
                // the person picked in the UI is parsed -- not every import
                // or link in the project, since a typical project has
                // dozens (electrical schemas, other disciplines'
                // backgrounds, structural/furniture/MEP links, etc.) and
                // most aren't "the architecture" at all.
                var importedCandidates = new List<ImportedWallCandidate>();
                if (architectureSourceId != null && architectureSourceId != ElementId.InvalidElementId)
                {
                    if (architectureSourceIsLink)
                    {
                        var chosenLink = doc.GetElement(architectureSourceId) as RevitLinkInstance;
                        if (chosenLink != null)
                            importedCandidates.AddRange(FindWallLikeElementsInLink(chosenLink));
                    }
                    else
                    {
                        var chosenImport = doc.GetElement(architectureSourceId) as ImportInstance;
                        if (chosenImport != null)
                            importedCandidates.AddRange(FindWallLikeSolidsInImport(chosenImport));
                    }
                }

                // Independent of the wall-crossing check below -- appended
                // to result before the early-return guard on the next line,
                // so plumbing clashes still get found even on a scan where
                // there happen to be no walls or architecture candidates at
                // all (someone checking ONLY for plumbing clashes, say).
                if (plumbingLinkId != null && plumbingLinkId != ElementId.InvalidElementId)
                {
                    var plumbingLink = doc.GetElement(plumbingLinkId) as RevitLinkInstance;
                    if (plumbingLink != null)
                        result.AddRange(ScanForPlumbingClashes(doc, runs, plumbingLink));
                }

                if (runs.Count == 0 || (walls.Count == 0 && importedCandidates.Count == 0)) return result;

                // Existing hole links, re-keyed by (run, wall) instead of
                // by hole, so a fresh scan can recognize "this crossing
                // already has a hole from a previous session" instead of
                // reporting every single collision as new every time --
                // ScanForCollisions previously had no way to know about
                // anything placed outside the current in-memory session.
                var linkByRunAndWall = new Dictionary<(string RunUid, string WallUid), string>();
                var holeUidsByRun = new Dictionary<string, List<string>>();
                foreach (var kv in ReadHoleLinkMap(doc))
                {
                    linkByRunAndWall[(kv.Value.RunUniqueId, kv.Value.WallUniqueId)] = kv.Key;
                    if (!holeUidsByRun.TryGetValue(kv.Value.RunUniqueId, out var list))
                    {
                        list = new List<string>();
                        holeUidsByRun[kv.Value.RunUniqueId] = list;
                    }
                    list.Add(kv.Key);
                }

                // A THIRD tier, below the two above: holes that were never
                // placed by this tool at all -- placed by hand before this
                // add-in existed, or by someone not using it, so there's no
                // link map entry for them whatsoever. Confirmed as a real,
                // live case (a project with existing "-CAx WD..." opening
                // markers already in place before Collision Checker's
                // first-ever scan there). Matched purely by proximity to
                // the SAME family currently selected as the Hole Family --
                // any type of that family counts, not just an exact type
                // match, since a legacy hole may have been placed with a
                // different type of the same family than what's configured
                // for new placements now.
                var existingHoleInstances = GetExistingHoleInstances(doc, holeSymbolId);

                foreach (var run in runs)
                {
                    Curve runCurve;
                    try { runCurve = (run.Location as LocationCurve)?.Curve; }
                    catch { runCurve = null; }
                    if (runCurve == null) continue;

                    // Bounding-box pass first -- cheap, and (unlike
                    // ElementIntersectsElementFilter, deliberately not used
                    // here) doesn't require either element to have valid
                    // closed solid geometry, just narrows down which walls
                    // are even worth a precise check. Tolerance is generous
                    // (~600mm) on purpose: this is only a candidate filter
                    // for performance, and too tight a tolerance here would
                    // silently exclude a run before FindNearApproachPoint's
                    // own (separately bounded) fallback ever got a chance to
                    // run -- exactly what was hiding real collisions for a
                    // run modeled to stop a little short of the wall face.
                    var runBox = run.get_BoundingBox(null);
                    if (runBox == null) continue;
                    var outline = new Outline(runBox.Min, runBox.Max);
                    const double quickFilterToleranceFt = 600.0 / 304.8;

                    foreach (var wall in walls)
                    {
                        try
                        {
                            var wallBox = wall.get_BoundingBox(null);
                            if (wallBox == null) continue;
                            if (!outline.Intersects(new Outline(wallBox.Min, wallBox.Max), quickFilterToleranceFt)) continue;

                            var point = FindCrossingPoint(doc, wall, runCurve);
                            if (point == null) continue;

                            var info = new CollisionInfo
                            {
                                ElementId       = run.Id,
                                WallId          = wall.Id,
                                ElementCategory = run.Category?.Name ?? "",
                                ElementTypeName = TypeNameOf(doc, run),
                                WallTypeName    = TypeNameOf(doc, wall),
                                Point           = point,
                                LevelId         = ResolveLevelIdByElevation(doc, point.Z),
                                LevelName       = ResolveLevelName(doc, ResolveLevelIdByElevation(doc, point.Z)),
                            };

                            if (linkByRunAndWall.TryGetValue((run.UniqueId, wall.UniqueId), out var holeUid))
                            {
                                try
                                {
                                    var holeEl = doc.GetElement(holeUid);
                                    if (holeEl != null) info.HoleInstanceId = holeEl.Id;
                                }
                                catch { }
                            }

                            // Fallback: the exact wall this scan resolved
                            // the crossing against may not be the same Wall
                            // object the original hole was linked to (most
                            // likely right at a corner where two walls
                            // meet, or after a wall was edited) -- check
                            // this run's other existing holes by physical
                            // proximity instead of only by exact wall
                            // match. Compares against each candidate's own
                            // real bounding box, not its Location Point --
                            // see the remarks above GetExistingHoleInstances
                            // for why: for at least one real hole family in
                            // active use, those two can be several METERS
                            // apart.
                            if (!info.HasHole && holeUidsByRun.TryGetValue(run.UniqueId, out var candidateHoleUids))
                            {
                                const double proximityToleranceFt = 300.0 / 304.8; // ~300mm
                                foreach (var candidateUid in candidateHoleUids)
                                {
                                    try
                                    {
                                        var holeEl = doc.GetElement(candidateUid);
                                        var holeBBox = holeEl?.get_BoundingBox(null);
                                        if (IsPointNearBoundingBox(point, holeBBox, proximityToleranceFt))
                                        {
                                            info.HoleInstanceId = holeEl.Id;
                                            break;
                                        }
                                    }
                                    catch { }
                                }
                            }

                            // Third tier: a hole this tool never placed or
                            // linked at all -- see the remarks above
                            // existingHoleInstances. Matched purely by
                            // proximity, same tolerance as the fallback
                            // just above.
                            if (!info.HasHole)
                                info.HoleInstanceId = FindNearbyExistingHole(existingHoleInstances, point);

                            result.Add(info);
                        }
                        catch { }
                    }

                    // Imported architecture (CAD/IFC, whichever the person
                    // picked): no cheap bounding-box
                    // pre-filter here (a Face doesn't expose one in the
                    // same 3D-Outline shape the wall/run check above uses),
                    // but importedCandidates is normally a short list --
                    // one entry per detected wall-slab in the import, not
                    // per wall times per run -- so checking all of them
                    // directly is fine.
                    foreach (var cand in importedCandidates)
                    {
                        try
                        {
                            var point = FindCrossingPointOnFacePair(cand.FaceA, cand.FaceB, runCurve);
                            if (point == null) continue;

                            // "Wall direction" for hole rotation, derived the
                            // same way a real wall's own centerline direction
                            // is used elsewhere -- here there's no centerline,
                            // only the face normal, so this rotates that
                            // normal's horizontal component 90 degrees to get
                            // a horizontal vector running ALONG the slab
                            // instead of through it (matching how the
                            // rotation logic in ExecutePlaceHoles is already
                            // used for the native-wall case).
                            var n = cand.FaceA.FaceNormal;
                            var horiz = new XYZ(n.X, n.Y, 0);
                            var wallDir = horiz.GetLength() > 1e-6
                                ? new XYZ(-horiz.Y, horiz.X, 0).Normalize()
                                : XYZ.BasisX;

                            var info = new CollisionInfo
                            {
                                ElementId         = run.Id,
                                WallId            = cand.ImportInstanceId,
                                ElementCategory   = run.Category?.Name ?? "",
                                ElementTypeName   = TypeNameOf(doc, run),
                                WallTypeName      = string.IsNullOrEmpty(cand.ImportDisplayName)
                                                        ? "Imported architecture" : cand.ImportDisplayName,
                                Point             = point,
                                LevelId           = ResolveLevelIdByElevation(doc, point.Z),
                                LevelName         = ResolveLevelName(doc, ResolveLevelIdByElevation(doc, point.Z)),
                                IsExternalGeometry = true,
                                ImportedWallThicknessFt = cand.ThicknessFt,
                                ImportedWallDirection   = wallDir,
                            };

                            // Same lookup as the native-wall case above --
                            // ImportInstance/RevitLinkInstance UniqueId
                            // works the same way as any other element's
                            // for this purpose.
                            if (linkByRunAndWall.TryGetValue((run.UniqueId, doc.GetElement(cand.ImportInstanceId)?.UniqueId ?? ""), out var impHoleUid))
                            {
                                try
                                {
                                    var holeEl = doc.GetElement(impHoleUid);
                                    if (holeEl != null) info.HoleInstanceId = holeEl.Id;
                                }
                                catch { }
                            }

                            // Third tier -- see the remarks above
                            // existingHoleInstances. No run-scoped proximity
                            // fallback tier here the way the native-wall
                            // case has one above (that one leans on
                            // holeUidsByRun, which is keyed from the SAME
                            // link map this tier is specifically for
                            // holes that were never in), so this is the
                            // only fallback tier for the imported/linked
                            // architecture case.
                            if (!info.HasHole)
                                info.HoleInstanceId = FindNearbyExistingHole(existingHoleInstances, point);

                            result.Add(info);
                        }
                        catch { }
                    }
                }
            }
            catch { }
            return result;
        }

        // Exact 3D point where curve crosses the wall, using Face.Intersect
        // against the wall's two side faces (confirmed against Autodesk's
        // own docs -- IntersectionResult.XYZPoint is the evaluated point).
        // If both side faces are crossed (the normal case for a run that
        // fully penetrates the wall), returns the midpoint between the two
        // outermost crossings -- i.e. the center of the wall's thickness,
        // which is where a "hole here" marker belongs. Falls back to
        // whichever single face was crossed if only one was (e.g. the run
        // ends inside the wall).
        internal static XYZ FindCrossingPoint(Document doc, Wall wall, Curve runCurve)
        {
            var points = new List<XYZ>();
            foreach (var shell in new[] { ShellLayerType.Exterior, ShellLayerType.Interior })
            {
                try
                {
                    var faces = HostObjectUtils.GetSideFaces(wall, shell);
                    foreach (var faceRef in faces)
                    {
                        var face = wall.GetGeometryObjectFromReference(faceRef) as Face;
                        if (face == null) continue;
                        var faceResult = face.Intersect(runCurve, out IntersectionResultArray hits);
                        if (faceResult != SetComparisonResult.Overlap || hits == null) continue;
                        foreach (IntersectionResult hit in hits)
                            points.Add(hit.XYZPoint);
                    }
                }
                catch { }
            }
            if (points.Count == 0)
                return FindNearApproachPoint(wall, runCurve); // fully missed both faces -- try the more forgiving fallback below
            if (points.Count == 1) return points[0];

            // Span the two points that are furthest apart (handles the rare
            // case of more than 2 hits -- an angled run through a thick or
            // multi-layer wall -- without picking an arbitrary pair).
            XYZ a = points[0], b = points[0];
            double best = -1;
            for (int i = 0; i < points.Count; i++)
                for (int j = i + 1; j < points.Count; j++)
                {
                    var d = points[i].DistanceTo(points[j]);
                    if (d > best) { best = d; a = points[i]; b = points[j]; }
                }
            return (a + b) * 0.5;
        }

        // Fallback for a run that doesn't cleanly cross either of the
        // wall's faces -- e.g. a cable tray explicitly routed to stop right
        // at (or just short of) a wall rather than modeled straight through
        // it, which is common practice and something the strict
        // Face.Intersect check above can't see at all, since there's no
        // actual face crossing to find in that case.
        //
        // Finds the closest-approach point between the run's centerline and
        // the wall's own centerline (both treated as straight lines -- the
        // dominant real-world case for conduit/tray runs and orthogonal
        // walls; curved walls or non-linear runs just don't get this
        // fallback and rely on the precise check above only). If that
        // closest approach falls within roughly the wall's half-thickness
        // plus a small clearance allowance, and within both curves' actual
        // bounded extents (not off the end of either one), it's treated as
        // a genuine collision using that closest-approach point.
        private static XYZ FindNearApproachPoint(Wall wall, Curve runCurve)
        {
            try
            {
                if (!(runCurve is Line runLine)) return null;
                var wallCurve = (wall.Location as LocationCurve)?.Curve;
                if (!(wallCurve is Line wallLine)) return null;

                XYZ p1 = runLine.GetEndPoint(0), d1 = runLine.GetEndPoint(1) - p1;
                XYZ p2 = wallLine.GetEndPoint(0), d2 = wallLine.GetEndPoint(1) - p2;
                double len1 = d1.GetLength(), len2 = d2.GetLength();
                if (len1 < 1e-9 || len2 < 1e-9) return null;

                var w0 = p1 - p2;
                double a = d1.DotProduct(d1), b = d1.DotProduct(d2), c = d2.DotProduct(d2);
                double d = d1.DotProduct(w0), e = d2.DotProduct(w0);
                double denom = a * c - b * b;
                if (Math.Abs(denom) < 1e-9) return null; // parallel (or nearly so) -- no single closest point, skip

                double t = (b * e - c * d) / denom;
                double s = (a * e - b * d) / denom;
                // Clamp to each curve's own bounded extent (t/s are in
                // "d1"/"d2" units here, i.e. already 0-1 across each curve's
                // actual length) -- a closest approach off the end of either
                // curve isn't a real collision.
                double tClamped = Math.Max(0, Math.Min(1, t));
                double sClamped = Math.Max(0, Math.Min(1, s));
                if (Math.Abs(tClamped - t) > 1e-6 || Math.Abs(sClamped - s) > 1e-6) return null;

                XYZ closest1 = p1 + d1.Multiply(tClamped);
                XYZ closest2 = p2 + d2.Multiply(sClamped);
                double dist = closest1.DistanceTo(closest2);

                // Half the wall's thickness, plus ~50mm clearance allowance
                // for a run that was modeled to stop just short of the wall
                // face rather than exactly at it.
                double toleranceFt = wall.Width / 2.0 + (50.0 / 304.8);
                if (dist > toleranceFt) return null;

                return (closest1 + closest2) * 0.5;
            }
            catch { return null; }
        }

        private static string TypeNameOf(Document doc, Element el)
        {
            try { return doc.GetElement(el.GetTypeId())?.Name ?? ""; }
            catch { return ""; }
        }

        private static ElementId ResolveLevelId(Document doc, Element el)
        {
            try { return el.LevelId ?? ElementId.InvalidElementId; }
            catch { return ElementId.InvalidElementId; }
        }

        // Which level a given Z-height physically sits on, by comparing
        // against every level's own elevation -- the level whose elevation
        // is the highest one still at or below this Z. This is deliberately
        // NOT the run's own Reference Level property: an MEP element's
        // Reference Level plus its own vertical offset (shown as "Middle
        // Elevation" in Revit's own UI) doesn't reliably match where it
        // physically sits, especially when that offset is large enough to
        // put it into a different level's usual range -- exactly what was
        // sending "Go To" to a level whose own plan view doesn't show
        // anything useful at this spot.
        private static ElementId ResolveLevelIdByElevation(Document doc, double z)
        {
            try
            {
                Level best = null;
                foreach (var lvl in new FilteredElementCollector(doc).OfClass(typeof(Level)).Cast<Level>())
                {
                    if (lvl.Elevation <= z && (best == null || lvl.Elevation > best.Elevation))
                        best = lvl;
                }
                // Nothing at or below z (e.g. below the lowest level) -- fall
                // back to whichever level is closest overall rather than
                // reporting no level at all.
                if (best == null)
                    best = new FilteredElementCollector(doc).OfClass(typeof(Level)).Cast<Level>()
                        .OrderBy(l => Math.Abs(l.Elevation - z)).FirstOrDefault();
                return best?.Id ?? ElementId.InvalidElementId;
            }
            catch { return ElementId.InvalidElementId; }
        }

        private static string ResolveLevelName(Document doc, ElementId levelId)
        {
            if (levelId == null || levelId == ElementId.InvalidElementId) return "";
            try { return (doc.GetElement(levelId) as Level)?.Name ?? ""; }
            catch { return ""; }
        }

        // Every loaded family/type in the document -- deliberately not
        // filtered by category, since the hole-marker family's category
        // wasn't known ahead of time (could be Generic Model, Specialty
        // Equipment, or something else entirely).
        public static List<HoleSymbolOption> GetHoleSymbolOptions(Document doc)
        {
            try
            {
                return new FilteredElementCollector(doc)
                    .OfClass(typeof(FamilySymbol))
                    .Cast<FamilySymbol>()
                    .Select(fs => new HoleSymbolOption { SymbolId = fs.Id, FamilyName = fs.Family?.Name ?? "?", TypeName = fs.Name })
                    .OrderBy(o => o.FamilyName, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(o => o.TypeName, StringComparer.OrdinalIgnoreCase)
                    .ToList();
            }
            catch { return new List<HoleSymbolOption>(); }
        }

        // ═════════════════════════════════════════════════════════════════
        // WRITE: hole placement (ExternalEvent)
        // ═════════════════════════════════════════════════════════════════
        private void ExecutePlaceHoles(Document doc, CollisionCheckerRequest req)
        {
            var result = new PlaceHolesResult();
            var symbol = doc.GetElement(req.HoleSymbolId) as FamilySymbol;
            if (symbol == null) { Report("No hole family/type selected."); OnDone?.Invoke(result); return; }
            if (req.Collisions == null || req.Collisions.Count == 0) { Report("Nothing selected to place."); OnDone?.Invoke(result); return; }

            using (var tx = new Transaction(doc, "ME-Tools: Place Collision Holes"))
            {
                tx.Start();
                try
                {
                    if (!symbol.IsActive) symbol.Activate();

                    var placementType = symbol.Family?.FamilyPlacementType ?? FamilyPlacementType.Invalid;

                    foreach (var c in req.Collisions)
                    {
                        var attempts = new List<string>();
                        try
                        {
                            // No real host-document Wall to host on -- true
                            // whether this came from an imported file (no
                            // per-entity geometry at all) or a linked model
                            // (its wall elements live in a different
                            // Document; Revit can't face-host a family in
                            // THIS document on a face from another one), so
                            // this skips straight to the same non-hosted
                            // tiers (3/4) the native-wall path below falls
                            // back to only as a last resort. Direction/
                            // thickness come from what was captured at scan
                            // time (see ScanForCollisions), not re-derived
                            // here.
                            if (c.IsExternalGeometry)
                            {
                                var impRun = doc.GetElement(c.ElementId);
                                var importEl = doc.GetElement(c.WallId);
                                if (impRun == null || importEl == null || c.Point == null)
                                {
                                    result.Skipped++;
                                    continue;
                                }

                                Level impLevel = null;
                                try { impLevel = doc.GetElement(c.LevelId) as Level; } catch { }
                                if (impLevel == null)
                                {
                                    try { impLevel = new FilteredElementCollector(doc).OfClass(typeof(Level)).Cast<Level>().FirstOrDefault(); }
                                    catch { }
                                }

                                FamilyInstance impInstance = null;
                                if (impLevel != null)
                                {
                                    try { impInstance = doc.Create.NewFamilyInstance(c.Point, symbol, impLevel, StructuralType.NonStructural); }
                                    catch (Exception ex) { attempts.Add("imported-level-only: " + ex.Message); }
                                }
                                if (impInstance == null)
                                {
                                    try { impInstance = doc.Create.NewFamilyInstance(c.Point, symbol, StructuralType.NonStructural); }
                                    catch (Exception ex) { attempts.Add("imported-free-standing: " + ex.Message); }
                                }

                                if (impInstance == null)
                                {
                                    result.Errors++;
                                    var combined = $"[{placementType}, imported architecture] " + (attempts.Count > 0 ? string.Join(" | ", attempts) : "no applicable placement method found");
                                    result.ErrorMessages.Add(combined);
                                    result.ErrorByRowId[c.Id] = combined;
                                    continue;
                                }

                                // Both tiers used here place at the family's
                                // default rotation -- rotate explicitly to
                                // match the imported slab's own orientation,
                                // same idea as the native-wall path below.
                                try
                                {
                                    var dir = c.ImportedWallDirection ?? XYZ.BasisX;
                                    double angle = Math.Atan2(dir.Y, dir.X);
                                    if (Math.Abs(angle) > 1e-6)
                                    {
                                        var axis = Line.CreateBound(c.Point, c.Point + XYZ.BasisZ);
                                        ElementTransformUtils.RotateElement(doc, impInstance.Id, axis, angle);
                                    }
                                }
                                catch (Exception ex) { attempts.Add("imported-rotate: " + ex.Message); }

                                var impDimAttempts = new List<string>();
                                try { ApplyHoleDimensions(impInstance, impRun, c.ImportedWallThicknessFt, impDimAttempts); }
                                catch (Exception ex) { impDimAttempts.Add("dimensions: " + ex.Message); }
                                ApplyHoleHeight(impInstance, c.Point, impLevel, impDimAttempts);
                                InheritPropertiesFromExistingHole(doc, impInstance, req.HoleSymbolId, c.Point, c.LevelId, impDimAttempts);
                                if (impDimAttempts.Count > 0)
                                {
                                    result.DimensionWarnings++;
                                    if (result.FirstDimensionWarning == null)
                                        result.FirstDimensionWarning = string.Join(" | ", impDimAttempts);
                                }

                                LinkHoleToRunAndWall(doc, impInstance.UniqueId, impRun.UniqueId, importEl.UniqueId);
                                result.Placed++;
                                result.PlacedHoleByRowId[c.Id] = impInstance.Id;
                                continue;
                            }

                            var run  = doc.GetElement(c.ElementId);
                            var wall = doc.GetElement(c.WallId) as Wall;
                            if (run == null || wall == null || c.Point == null)
                            {
                                result.Skipped++;
                                continue;
                            }

                            // Reference direction for face-hosted placement
                            // (see case 1 below) -- deliberately the WALL's
                            // own direction along its length, not the run's.
                            // A run typically passes straight THROUGH a
                            // wall, i.e. roughly perpendicular to its face,
                            // which is a poor in-plane rotation reference
                            // for that face and could visually read as "the
                            // marker is oriented off the pipe" rather than
                            // sitting naturally in the wall. The wall's own
                            // direction is stable and correct regardless of
                            // which way the run happens to be crossing it.
                            var wallCurveForDir = (wall.Location as LocationCurve)?.Curve;
                            var direction = wallCurveForDir != null
                                ? (wallCurveForDir.GetEndPoint(1) - wallCurveForDir.GetEndPoint(0)).Normalize()
                                : XYZ.BasisX;

                            // Resolve a level up front -- several of the
                            // overloads below need one regardless of which
                            // placement type this turns out to actually be.
                            Level level = null;
                            try
                            {
                                var levelId = wall.LevelId != ElementId.InvalidElementId ? wall.LevelId : ResolveLevelId(doc, run);
                                level = doc.GetElement(levelId) as Level;
                            }
                            catch { }
                            if (level == null)
                            {
                                try { level = new FilteredElementCollector(doc).OfClass(typeof(Level)).Cast<Level>().FirstOrDefault(); }
                                catch { }
                            }

                            FamilyInstance instance = null;
                            bool placedWithExplicitDirection = false;

                            // Tried in order regardless of the family's
                            // reported FamilyPlacementType -- that enum's
                            // exact mapping to the right overload has been
                            // wrong twice already for this specific family,
                            // so runtime success/failure is more reliable
                            // ground truth here than the enum value is.

                            // 1) Face-hosted (WorkPlaneBased-style families)
                            // -- hosted BY THE WALL, at the exact collision
                            // point (c.Point), oriented via `direction`
                            // (the wall's own direction, set above) at
                            // creation time. The run itself is never passed
                            // as a host anywhere in this method; it's only
                            // ever used to compute where the crossing point
                            // is, upstream in ScanForCollisions.
                            if (instance == null)
                            {
                                try
                                {
                                    var faceRef = FindNearestFaceReference(wall, c.Point);
                                    if (faceRef != null)
                                    {
                                        var face = wall.GetGeometryObjectFromReference(faceRef) as Face;
                                        if (face != null)
                                        {
                                            instance = doc.Create.NewFamilyInstance(face, c.Point, direction, symbol);
                                            placedWithExplicitDirection = true;
                                        }
                                    }
                                }
                                catch (Exception ex) { attempts.Add("face-hosted: " + ex.Message); }
                            }

                            // 2) Wall-hosted with an explicit level (Door/
                            // Window-style families, e.g. OneLevelBasedHosted
                            // -- confirmed via the actual Revit error message
                            // that this family needs a level even with a host)
                            if (instance == null && level != null)
                            {
                                try { instance = doc.Create.NewFamilyInstance(c.Point, symbol, wall, level, StructuralType.NonStructural); }
                                catch (Exception ex) { attempts.Add("wall+level: " + ex.Message); }
                            }

                            // 3) Level only, no host (OneLevelBased, not hosted)
                            if (instance == null && level != null)
                            {
                                try { instance = doc.Create.NewFamilyInstance(c.Point, symbol, level, StructuralType.NonStructural); }
                                catch (Exception ex) { attempts.Add("level-only: " + ex.Message); }
                            }

                            // 4) Bare point placement -- genuinely free-
                            // standing families only; throws for any
                            // level-based family, which is the error seen
                            // twice now, so this is deliberately last.
                            if (instance == null)
                            {
                                try { instance = doc.Create.NewFamilyInstance(c.Point, symbol, StructuralType.NonStructural); }
                                catch (Exception ex) { attempts.Add("free-standing: " + ex.Message); }
                            }

                            if (instance == null)
                            {
                                result.Errors++;
                                var combined = $"[{placementType}] " + (attempts.Count > 0 ? string.Join(" | ", attempts) : "no applicable placement method found");
                                result.ErrorMessages.Add(combined);
                                result.ErrorByRowId[c.Id] = combined;
                                continue;
                            }

                            // Methods 2/3/4 place at the family's own default
                            // rotation, since none of those overloads accept
                            // a direction the way the face-hosted one does --
                            // rotate explicitly to match the wall afterward.
                            // Assumes the default (unrotated) orientation
                            // aligns with global X, the common Family Editor
                            // convention -- flag this assumption if the
                            // rotation still looks off after this.
                            if (!placedWithExplicitDirection)
                            {
                                try
                                {
                                    double angle = Math.Atan2(direction.Y, direction.X);
                                    if (Math.Abs(angle) > 1e-6)
                                    {
                                        var axis = Line.CreateBound(c.Point, c.Point + XYZ.BasisZ);
                                        ElementTransformUtils.RotateElement(doc, instance.Id, axis, angle);
                                    }
                                }
                                catch (Exception ex) { attempts.Add("rotate: " + ex.Message); }
                            }

                            // Length = the run's own cross-section dimension
                            // (cable tray width, or conduit diameter), Width
                            // = the wall's thickness -- per the family's own
                            // documented convention. Only the base dimension
                            // is set; any "extra per side" clearance is
                            // assumed to already be handled inside the
                            // family's own formula, per how this was
                            // described -- if it isn't, the visible result
                            // will be short by that amount on each side.
                            var dimAttempts = new List<string>();
                            try { ApplyHoleDimensions(instance, run, wall, dimAttempts); }
                            catch (Exception ex) { dimAttempts.Add("dimensions: " + ex.Message); }
                            ApplyHoleHeight(instance, c.Point, level, dimAttempts);
                            InheritPropertiesFromExistingHole(doc, instance, req.HoleSymbolId, c.Point, c.LevelId, dimAttempts);
                            if (dimAttempts.Count > 0)
                            {
                                result.DimensionWarnings++;
                                if (result.FirstDimensionWarning == null)
                                    result.FirstDimensionWarning = string.Join(" | ", dimAttempts);
                            }

                            LinkHoleToRunAndWall(doc, instance.UniqueId, run.UniqueId, wall.UniqueId);
                            result.Placed++;
                            result.PlacedHoleByRowId[c.Id] = instance.Id;
                        }
                        catch (Exception ex)
                        {
                            result.Errors++;
                            result.ErrorMessages.Add(ex.Message);
                            result.ErrorByRowId[c.Id] = ex.Message;
                        }
                    }

                    tx.Commit();
                }
                catch (Exception ex)
                {
                    if (tx.GetStatus() == TransactionStatus.Started) tx.RollBack();
                    result.Errors++;
                    result.ErrorMessages.Add(ex.Message);
                }
            }

            var summary = $"Placed {result.Placed} hole(s)";
            if (result.Skipped > 0) summary += $", {result.Skipped} skipped";
            if (result.Errors  > 0) summary += $", {result.Errors} errors: " + result.ErrorMessages.FirstOrDefault();
            if (result.DimensionWarnings > 0) summary += $", {result.DimensionWarnings} placed with a dimension parameter not found ({result.FirstDimensionWarning})";
            Report(summary);
            OnDone?.Invoke(result);
        }

        // Repositions holes whose run moved. Called via ExternalEvent from
        // CollisionCheckerWatcher -- DocumentChanged (where the move is
        // detected) is explicitly documented as read-only and cannot start
        // a transaction itself, so the actual move has to happen here,
        // once Revit returns to a valid API context.
        private void ExecuteMoveHoles(Document doc, CollisionCheckerRequest req)
        {
            if (req.HoleMoves == null || req.HoleMoves.Count == 0) return;

            using (var tx = new Transaction(doc, "ME-Tools: Update collision hole position"))
            {
                tx.Start();
                try
                {
                    foreach (var move in req.HoleMoves)
                    {
                        try
                        {
                            var run  = doc.GetElement(move.RunId);
                            var wall = doc.GetElement(move.WallUniqueId) as Wall;
                            var hole = doc.GetElement(move.HoleUniqueId);
                            if (run == null || wall == null || hole == null) continue;

                            var runCurve = (run.Location as LocationCurve)?.Curve;
                            if (runCurve == null) continue;

                            var newPoint = FindCrossingPoint(doc, wall, runCurve);
                            if (newPoint == null) continue;

                            if (hole.Location is LocationPoint lp)
                            {
                                var delta = newPoint - lp.Point;
                                if (delta.GetLength() > 1e-6) // skip a no-op move + regen if it's already there
                                    ElementTransformUtils.MoveElement(doc, hole.Id, delta);
                            }
                        }
                        catch { }
                    }
                    tx.Commit();
                }
                catch
                {
                    if (tx.GetStatus() == TransactionStatus.Started) tx.RollBack();
                }
            }
        }

        // Draws a red circle at every unresolved collision point, in EACH
        // collision's OWN physical level's plan view -- resolved via
        // FindPlanViewForLevel below, the same lookup "Go To" uses --
        // rather than whichever single view happened to be active when
        // Scan was clicked. That used to mean a Whole Model scan (which
        // routinely spans several levels) silently projected every
        // collision's mark onto the CURRENT view's plane regardless of
        // which level it actually belonged to: a real, visible circle,
        // just floating at the wrong level's Z, at X/Y coordinates that
        // meant nothing there -- and nothing at all drawn on the level the
        // collision was actually on unless the person happened to already
        // be looking at it when they pressed Scan.
        //
        // Confirmed via the actual Revit error message that a plain
        // button-click handler is not a valid context for starting a
        // transaction ("Starting a transaction from an external
        // application running outside of API context is not allowed") --
        // this used to run directly from the window's OnScanClicked, which
        // is exactly that invalid context. Routed through the ExternalEvent
        // instead, the same way every other write in this file already is.
        // Writes to Extensible Storage, so this needs a real Transaction
        // just like every other write in this file -- unlike hole
        // placement, there's no model geometry being created here, just a
        // manual acknowledgement being persisted.
        private void ExecuteMarkPlumbingSolved(Document doc, CollisionCheckerRequest req)
        {
            var result = new PlaceHolesResult { ResultAction = CollisionCheckerAction.MarkPlumbingSolved };
            if (doc == null || req.Collisions == null) { OnDone?.Invoke(result); return; }

            using (var tx = new Transaction(doc, "ME-Tools: Mark plumbing clashes solved"))
            {
                tx.Start();
                try
                {
                    foreach (var c in req.Collisions)
                    {
                        try
                        {
                            if (c.Kind != CollisionKind.PlumbingClash || c.IsSolved) continue;
                            var run = doc.GetElement(c.ElementId);
                            // Three genuinely different failure conditions,
                            // now reported separately instead of one vague
                            // combined message -- confirmed live that the
                            // plumbing link itself was still loaded and
                            // accessible when this fired for a real user,
                            // so the run-not-found case below is the most
                            // likely one in practice, but there was no way
                            // to tell from the old message alone.
                            if (run == null)
                            {
                                result.Errors++;
                                result.ErrorByRowId[c.Id] = "the conduit/cable tray for this row no longer exists in the model -- try rescanning";
                                continue;
                            }
                            if (string.IsNullOrEmpty(run.UniqueId))
                            {
                                result.Errors++;
                                result.ErrorByRowId[c.Id] = "the conduit/cable tray for this row has no UniqueId (unexpected) -- try rescanning";
                                continue;
                            }
                            if (string.IsNullOrEmpty(c.PlumbingElementUniqueId))
                            {
                                result.Errors++;
                                result.ErrorByRowId[c.Id] = "the plumbing element this row matched at scan time has no recorded ID -- try rescanning";
                                continue;
                            }
                            MarkPlumbingClashSolved(doc, $"{run.UniqueId}|{c.PlumbingElementUniqueId}");
                            result.Placed++;
                            result.SolvedRowIds.Add(c.Id);
                        }
                        catch (Exception ex)
                        {
                            result.Errors++;
                            result.ErrorByRowId[c.Id] = ex.Message;
                        }
                    }
                    tx.Commit();
                }
                catch (Exception ex)
                {
                    try { tx.RollBack(); } catch { }
                    result.Errors++;
                    result.ErrorMessages.Add(ex.Message);
                }
            }
            OnDone?.Invoke(result);
        }

        private void ExecuteMarkCollisions(Document doc, ElementId activeViewId, CollisionCheckerRequest req)
        {
            var result = new PlaceHolesResult { ResultAction = CollisionCheckerAction.MarkCollisions };
            if (doc == null || req.Collisions == null) { OnDone?.Invoke(result); return; }

            using (var tx = new Transaction(doc, "ME-Tools: Mark collisions"))
            {
                tx.Start();
                try
                {
                    if (req.OldMarkerIds != null && req.OldMarkerIds.Count > 0)
                    {
                        try { doc.Delete(req.OldMarkerIds); } catch { }
                    }

                    var red = new Autodesk.Revit.DB.Color(226, 42, 42);
                    var ogs = new OverrideGraphicSettings();
                    try { ogs.SetProjectionLineColor(red); ogs.SetProjectionLineWeight(7); } catch { }
                    double radiusFt = 250.0 / 304.8; // ~250mm radius -- visible regardless of view scale

                    var toMark = req.Collisions.Where(c => !c.IsResolved && c.Point != null).ToList();
                    result.MarksAttempted = toMark.Count;

                    // Grouped by the collision's own LevelId (resolved by
                    // physical Z-elevation upstream in ScanForCollisions --
                    // see lesson on Reference Level not being trustworthy
                    // for MEP elements), NOT by whatever view is active.
                    // activeViewId is only used as a TIE-BREAK inside
                    // FindPlanViewForLevel, for the case where the resolved
                    // level itself has more than one Floor Plan view (a
                    // real, confirmed case: one Floor Plan per discipline
                    // on the same level, e.g. an Electrical "EG" and a
                    // Mechanical/Heating coordination "H_EG" both tied to
                    // the same Level) -- it can never send a mark to the
                    // wrong LEVEL, only help pick between two views that
                    // are both already correct for this one.
                    foreach (var group in toMark.GroupBy(c => c.LevelId))
                    {
                        // A group's collisions can involve different runs,
                        // but on a real project they're overwhelmingly the
                        // same category/discipline (Cable Trays, Conduits)
                        // that would share the same visibility fate in any
                        // one view -- using the first as a representative
                        // for the visibility check is a reasonable, cheap
                        // stand-in for checking every one individually.
                        var representativeRunId = group.FirstOrDefault()?.ElementId;
                        var targetView = FindPlanViewForLevel(doc, group.Key, activeViewId, representativeRunId);

                        // No plan view exists for this level (or the level
                        // id never resolved) -- Detail Lines are a 2D,
                        // view-specific annotation, so there's no sensible
                        // view-agnostic fallback. Report it rather than
                        // silently dropping the count, same spirit as
                        // surfacing MarksFailed instead of swallowing it.
                        if (targetView == null || targetView is View3D)
                        {
                            result.MarksSkippedNoView += group.Count();
                            continue;
                        }

                        double? planeZ = GetViewPlaneZ(targetView);
                        XYZ xAxis = targetView.RightDirection;
                        XYZ yAxis = targetView.UpDirection;

                        foreach (var c in group)
                        {
                            try
                            {
                                var center = planeZ.HasValue
                                    ? new XYZ(c.Point.X, c.Point.Y, planeZ.Value)
                                    : c.Point;

                                // A circle, as two half-circle arcs (Revit
                                // detail curves can't be a single closed
                                // loop) on a plane centered exactly at the
                                // (projected) collision point.
                                var centeredPlane = Plane.CreateByOriginAndBasis(center, xAxis, yAxis);
                                var arc1 = Arc.Create(centeredPlane, radiusFt, 0, Math.PI);
                                var arc2 = Arc.Create(centeredPlane, radiusFt, Math.PI, 2 * Math.PI);
                                var dc1 = doc.Create.NewDetailCurve(targetView, arc1);
                                var dc2 = doc.Create.NewDetailCurve(targetView, arc2);
                                targetView.SetElementOverrides(dc1.Id, ogs);
                                targetView.SetElementOverrides(dc2.Id, ogs);

                                if (!result.MarkersByCollisionId.TryGetValue(c.Id, out var list))
                                {
                                    list = new List<ElementId>();
                                    result.MarkersByCollisionId[c.Id] = list;
                                }
                                list.Add(dc1.Id);
                                list.Add(dc2.Id);
                            }
                            catch (Exception ex)
                            {
                                result.MarksFailed++;
                                if (result.FirstMarkError == null) result.FirstMarkError = ex.Message;
                            }
                        }
                    }

                    tx.Commit();
                }
                catch (Exception ex)
                {
                    if (tx.GetStatus() == TransactionStatus.Started) tx.RollBack();
                    result.MarksFailed++;
                    if (result.FirstMarkError == null) result.FirstMarkError = "outer: " + ex.Message;
                }
            }

            OnDone?.Invoke(result);
        }

        // Prefers an actual Floor Plan view on the given level over other
        // plan-based view types (Ceiling Plan, Structural Plan, Area Plan)
        // that also happen to report the same GenLevel. Shared by "Go To"
        // (CollisionCheckerWindow) and mark-drawing (here) so both always
        // land on the exact same view for a given level -- a mark drawn
        // here is guaranteed visible from wherever "Go To" takes you.
        //
        // mustShowElementId is the load-bearing check, added after a real,
        // live case: a level can have more than one Floor Plan (one per
        // discipline -- e.g. an Electrical "EG" and a Mechanical/Heating
        // coordination "H_EG", both genuinely tied to the same Level), and
        // "same Level" alone does NOT mean "shows the run you're trying to
        // see" -- confirmed live that Cable Trays were entirely invisible
        // in the Mechanical view (discipline filtering hides Electrical-
        // only categories) while fully visible in the Electrical one on
        // the exact same Level. Candidates are filtered down to ones that
        // actually show this element FIRST; preferredViewId only breaks
        // ties AMONG those, so it can never win by being "already open"
        // if it wouldn't actually show anything there.
        public static View FindPlanViewForLevel(Document doc, ElementId levelId, ElementId preferredViewId = null, ElementId mustShowElementId = null)
        {
            if (doc == null || levelId == null || levelId == ElementId.InvalidElementId) return null;
            try
            {
                var candidates = new FilteredElementCollector(doc)
                    .OfClass(typeof(ViewPlan))
                    .Cast<ViewPlan>()
                    .Where(v => !v.IsTemplate && v.GenLevel != null && v.GenLevel.Id == levelId)
                    .ToList();

                var floorPlans = candidates.Where(v => v.ViewType == ViewType.FloorPlan).ToList();

                // Narrow to candidates that actually show the element, if
                // asked to check -- but only if that narrowing leaves at
                // least one option. An element genuinely hidden on every
                // Floor Plan for this level (e.g. a discipline filter or a
                // stray "Hide element" override) shouldn't make this
                // return null outright when there was a perfectly good
                // level-matching view to fall back to; it just means the
                // visibility preference can't be honored this time.
                if (mustShowElementId != null && mustShowElementId != ElementId.InvalidElementId)
                {
                    var visible = floorPlans.Where(v => IsElementVisibleInView(doc, v, mustShowElementId)).ToList();
                    if (visible.Count > 0) floorPlans = visible;
                }

                if (preferredViewId != null && preferredViewId != ElementId.InvalidElementId)
                {
                    // Only ever matched within floorPlans (the possibly
                    // visibility-filtered list), never falling back to
                    // the wider, unfiltered candidates -- confirmed as a
                    // real bug live: that fallback let a view excluded
                    // for NOT showing the element (H_EG, discipline-
                    // filtered away from Cable Trays) sneak right back in
                    // anyway, since it's still tied to the same level,
                    // completely undoing the visibility check above. The
                    // final line below already has its own safe fallback
                    // for when preferredViewId doesn't match anything
                    // here at all.
                    var preferred = floorPlans.FirstOrDefault(v => v.Id == preferredViewId);
                    if (preferred != null) return preferred;
                }

                return floorPlans.FirstOrDefault() ?? candidates.FirstOrDefault();
            }
            catch { return null; }
        }

        // See remarks above FindPlanViewForLevel. Confirmed against a real
        // project: a FilteredElementCollector scoped to a view is the
        // Revit-API-endorsed way to answer "would this element actually
        // show up here" -- it accounts for discipline filtering, category
        // visibility overrides, and per-element hide, all at once, rather
        // than trying to reason about each of those separately.
        private static bool IsElementVisibleInView(Document doc, View view, ElementId elementId)
        {
            if (doc == null || view == null || elementId == null || elementId == ElementId.InvalidElementId) return false;
            try
            {
                return new FilteredElementCollector(doc, view.Id)
                    .WhereElementIsNotElementType()
                    .ToElementIds()
                    .Contains(elementId);
            }
            catch { return false; }
        }

        // For the "3D" go-to button -- same visibility-check reasoning as
        // FindPlanViewForLevel above, since a custom 3D view someone built
        // for a specific purpose could just as easily hide a category via
        // its own V/G overrides as a plan view can via discipline
        // filtering. Prefers Revit's own auto-created default 3D view
        // (always named starting with "{3D", e.g. "{3D}" or
        // "{3D - username}") over a custom one, since a custom 3D view is
        // more likely to have been set up for some other, narrower
        // purpose.
        public static View3D FindDefault3DView(Document doc, ElementId mustShowElementId = null)
        {
            if (doc == null) return null;
            try
            {
                var candidates = new FilteredElementCollector(doc)
                    .OfClass(typeof(View3D))
                    .Cast<View3D>()
                    .Where(v => !v.IsTemplate)
                    .ToList();
                if (candidates.Count == 0) return null;

                if (mustShowElementId != null && mustShowElementId != ElementId.InvalidElementId)
                {
                    var visible = candidates.Where(v => IsElementVisibleInView(doc, v, mustShowElementId)).ToList();
                    if (visible.Count > 0) candidates = visible;
                }

                return candidates.FirstOrDefault(v => (v.Name ?? "").StartsWith("{3D", StringComparison.OrdinalIgnoreCase))
                    ?? candidates.FirstOrDefault();
            }
            catch { return null; }
        }

        // The Z-height detail curves must be drawn at for this view to
        // accept them, tried in order of reliability: a Floor/Ceiling
        // Plan's own associated level (always present for that view type,
        // unlike SketchPlane which may well be null), then SketchPlane
        // (covers Section/Elevation/Drafting views, which don't have a
        // GenLevel), then the view's own Origin as a last resort.
        private static double? GetViewPlaneZ(View view)
        {
            try { if (view is ViewPlan vp && vp.GenLevel != null) return vp.GenLevel.Elevation; }
            catch { }
            try
            {
                var plane = view.SketchPlane?.GetPlane();
                if (plane != null) return plane.Origin.Z;
            }
            catch { }
            try { return view.Origin.Z; }
            catch { }
            return null;
        }

        // The wall's side face (whichever shell layer) whose plane is
        // closest to the given point -- used to pick which face to host the
        // instance on when the family turns out to be WorkPlaneBased.
        private static Reference FindNearestFaceReference(Wall wall, XYZ point)
        {
            Reference best = null;
            double bestDist = double.MaxValue;
            foreach (var shell in new[] { ShellLayerType.Exterior, ShellLayerType.Interior })
            {
                try
                {
                    foreach (var faceRef in HostObjectUtils.GetSideFaces(wall, shell))
                    {
                        var face = wall.GetGeometryObjectFromReference(faceRef) as Face;
                        if (face == null) continue;
                        var proj = face.Project(point);
                        if (proj == null) continue;
                        if (proj.Distance < bestDist) { bestDist = proj.Distance; best = faceRef; }
                    }
                }
                catch { }
            }
            return best;
        }

        // Sets the placed hole's dimensional parameters -- exact names
        // confirmed directly from the family's own Properties panel:
        //   Tiefe            = how far the opening penetrates = the wall's
        //                      own thickness
        //   Trassenbreite    = the run's cross-section width (cable tray
        //                      width, or conduit diameter)
        //   Trassenhöhe      = the run's cross-section height (cable tray
        //                      height, or conduit diameter again for a
        //                      round conduit -- no separate width/height)
        //
        // Deliberately NOT touched: the X_Überstand_*_User / Z_Überstand_*_User
        // clearance parameters -- those are already set correctly on the
        // family itself and must stay exactly as configured there.
        private static void ApplyHoleDimensions(Element instance, Element run, Wall wall, List<string> attempts)
            => ApplyHoleDimensions(instance, run, wall.Width, attempts);

        // Shared by both the native-Wall path above and the imported-
        // architecture path in ExecutePlaceHoles -- takes the thickness as
        // a plain double instead of requiring a real Wall to read Width
        // from, since an ImportInstance has no Width property at all.
        private static void ApplyHoleDimensions(Element instance, Element run, double wallThicknessFt, List<string> attempts)
        {
            SetDoubleParam(instance, "Tiefe", wallThicknessFt, attempts);

            GetRunCrossDimensions(run, out var crossWidth, out var crossHeight);
            if (crossWidth.HasValue)  SetDoubleParam(instance, "Trassenbreite", crossWidth.Value, attempts);
            else attempts.Add("couldn't read the run's own width to set Trassenbreite from");
            if (crossHeight.HasValue) SetDoubleParam(instance, "Trassenhöhe", crossHeight.Value, attempts);
            else attempts.Add("couldn't read the run's own height to set Trassenhöhe from");
        }

        private static void SetDoubleParam(Element el, string name, double value, List<string> attempts)
        {
            try
            {
                var p = el.LookupParameter(name);
                if (p != null && !p.IsReadOnly && p.StorageType == StorageType.Double)
                    p.Set(value);
                else
                    attempts.Add($"no writable '{name}' parameter found on the hole family");
            }
            catch (Exception ex) { attempts.Add($"'{name}': {ex.Message}"); }
        }

        // Confirmed live against the real model: this family's insertion
        // point always ends up at Z=0 relative to its Base Level/Offset,
        // regardless of what XYZ point NewFamilyInstance was actually
        // given -- Location.Point.Z came back as exactly 0.00 on a placed
        // instance despite passing in the real 3D collision point. The
        // family's real vertical position instead comes from two
        // instance parameters, OKB_zu_Achse and CAx_Versatzhöhe_Bauteil
        // (both found holding the identical value, 3385mm, on a real
        // pre-existing hole placed by hand) -- neither of which this tool
        // was setting, so every hole it placed fell back to the family
        // TYPE's own generic default (2000mm) instead of the real
        // crossing height. Setting both (rather than guessing which one
        // actually drives the geometry) is a safe, low-cost way to not
        // depend on knowing the family's internal formula.
        private static void ApplyHoleHeight(Element instance, XYZ point, Level level, List<string> attempts)
        {
            if (level == null) { attempts.Add("no level resolved to compute the hole's height from"); return; }
            try
            {
                double heightAboveLevelFt = point.Z - level.Elevation;
                SetDoubleParam(instance, "OKB_zu_Achse", heightAboveLevelFt, attempts);
                SetDoubleParam(instance, "CAx_Versatzhöhe_Bauteil", heightAboveLevelFt, attempts);
            }
            catch (Exception ex) { attempts.Add("height: " + ex.Message); }
        }

        // Every parameter this tool computes itself, either directly
        // (dimensions, height) or because it's one of the protected
        // clearance overrides this tool must never touch -- excluded from
        // inheritance below so a template value can never overwrite what
        // this specific hole's own geometry actually needs. Also excludes
        // Mark/Comments, since those read as hole-specific notes ("checked
        // by X on date Y") rather than generic template metadata, and
        // blindly copying one hole's note onto every other hole would be
        // actively wrong, not just unhelpful.
        private static readonly HashSet<string> ExcludedFromInheritance = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Tiefe", "Trassenbreite", "Trassenhöhe", "OKB_zu_Achse", "CAx_Versatzhöhe_Bauteil",
            "X_Überstand", "Z_Überstand",
            "X_Überstand_1_User", "X_Überstand_2_User", "Z_Überstand_1_User", "Z_Überstand_2_User",
            "Mark", "Comments",
        };

        // Confirmed live: pre-existing holes placed by hand before this
        // tool existed carry a lot of generic metadata (trade
        // classification, manufacturer, fire-stop rating, etc.) that this
        // tool has no way to know on its own -- every hole it places
        // instead starts from the family TYPE's own generic defaults,
        // which can be simply wrong for the actual context (confirmed: a
        // fresh placement came out with CAx_Gewerk="A" while a real
        // nearby hole says "E" for Elektro). This copies every writable
        // parameter from the nearest EXISTING hole of the same family,
        // preferring one on the same Level first (a hole one floor up or
        // down could easily belong to a different trade/room/context
        // even if it happens to be close in plan) and falling back to a
        // global nearest-match only if none exist on this level yet.
        private static void InheritPropertiesFromExistingHole(Document doc, Element instance, ElementId holeSymbolId, XYZ point, ElementId levelId, List<string> attempts)
        {
            try
            {
                var familyId = (doc.GetElement(holeSymbolId) as FamilySymbol)?.Family?.Id;
                if (familyId == null || familyId == ElementId.InvalidElementId) return;

                var candidates = new List<FamilyInstance>();
                foreach (var el in new FilteredElementCollector(doc).OfClass(typeof(FamilyInstance)))
                {
                    try
                    {
                        if (el.Id == instance.Id) continue;
                        var fi = el as FamilyInstance;
                        if (fi?.Symbol?.Family?.Id == familyId) candidates.Add(fi);
                    }
                    catch { }
                }
                if (candidates.Count == 0) { attempts.Add("no existing hole of this family found to inherit properties from"); return; }

                var sameLevel = levelId != null && levelId != ElementId.InvalidElementId
                    ? candidates.Where(fi => fi.LevelId == levelId).ToList()
                    : new List<FamilyInstance>();
                var pool = sameLevel.Count > 0 ? sameLevel : candidates;

                FamilyInstance closest = null;
                double closestDistSq = double.MaxValue;
                foreach (var fi in pool)
                {
                    try
                    {
                        var loc = (fi.Location as LocationPoint)?.Point;
                        if (loc == null) continue;
                        double d = loc.DistanceTo(point);
                        double dSq = d * d;
                        if (dSq < closestDistSq) { closestDistSq = dSq; closest = fi; }
                    }
                    catch { }
                }
                if (closest == null) { attempts.Add("no existing hole with a resolvable location found to inherit properties from"); return; }

                foreach (Parameter srcParam in closest.Parameters)
                {
                    try
                    {
                        if (srcParam == null || srcParam.IsReadOnly || !srcParam.HasValue) continue;
                        var name = srcParam.Definition?.Name;
                        if (string.IsNullOrEmpty(name) || ExcludedFromInheritance.Contains(name)) continue;

                        var dstParam = instance.LookupParameter(name);
                        if (dstParam == null || dstParam.IsReadOnly || dstParam.StorageType != srcParam.StorageType) continue;

                        switch (dstParam.StorageType)
                        {
                            case StorageType.Double: dstParam.Set(srcParam.AsDouble()); break;
                            case StorageType.Integer: dstParam.Set(srcParam.AsInteger()); break;
                            case StorageType.String: dstParam.Set(srcParam.AsString() ?? ""); break;
                            case StorageType.ElementId: dstParam.Set(srcParam.AsElementId()); break;
                        }
                    }
                    catch { } // one bad parameter shouldn't abort the whole inheritance pass
                }
            }
            catch (Exception ex) { attempts.Add("inherit properties: " + ex.Message); }
        }

        // Cable tray width/height come from the confirmed BuiltInParameters
        // (RBS_CABLETRAY_WIDTH_PARAM / RBS_CABLETRAY_HEIGHT_PARAM). Conduit
        // diameter is read by name instead of a BuiltInParameter, since
        // that specific enum member wasn't independently confirmed and a
        // wrong enum name is a compile-time error, not a recoverable
        // runtime one -- LookupParameter by name degrades safely if none of
        // the candidates match. A round conduit has no separate width vs
        // height, so both come out equal to its diameter.
        private static void GetRunCrossDimensions(Element run, out double? width, out double? height)
        {
            width = null; height = null;
            try
            {
                if (run is CableTray tray)
                {
                    var wp = tray.get_Parameter(BuiltInParameter.RBS_CABLETRAY_WIDTH_PARAM);
                    if (wp != null && wp.HasValue) width = wp.AsDouble();
                    var hp = tray.get_Parameter(BuiltInParameter.RBS_CABLETRAY_HEIGHT_PARAM);
                    if (hp != null && hp.HasValue) height = hp.AsDouble();
                    return;
                }
                string[] candidates = { "Diameter", "Outside Diameter", "Außendurchmesser", "Durchmesser" };
                foreach (var name in candidates)
                {
                    var p = run.LookupParameter(name);
                    if (p != null && p.HasValue && p.StorageType == StorageType.Double)
                    {
                        width = p.AsDouble();
                        height = p.AsDouble();
                        return;
                    }
                }
            }
            catch { }
        }

        private void Report(string msg) => OnStatus?.Invoke(msg);

        // ═════════════════════════════════════════════════════════════════
        // Extensible Storage: hole UniqueId -> "runUniqueId|wallUniqueId", on
        // one DataStorage element per document. Not on the hole instances
        // individually -- see file header for why.
        //
        // Keyed by HOLE, not by run: a single run can cross more than one
        // wall (a corridor wall and a shaft wall, say), which means more
        // than one hole for the same run. Keying by run would mean the
        // second hole's write silently overwrites the first hole's link --
        // a real bug, caught before it shipped. Each hole always has
        // exactly one link, so keying by hole has no such collision.
        // ═════════════════════════════════════════════════════════════════
        private static readonly Guid SchemaGuid = new Guid("6C6E1A6B-6E62-4C0D-8C2B-2C9F2E7B7B10");
        private const string SchemaFieldName = "HoleToRunAndWall";
        private const string DataStorageName = "ME-Tools_CollisionHoleMap";

        private static Schema GetOrCreateSchema()
        {
            var schema = Schema.Lookup(SchemaGuid);
            if (schema != null) return schema;
            var builder = new SchemaBuilder(SchemaGuid);
            builder.SetSchemaName("METoolsCollisionHoleMap");
            builder.SetReadAccessLevel(AccessLevel.Public);
            builder.SetWriteAccessLevel(AccessLevel.Public);
            builder.AddMapField(SchemaFieldName, typeof(string), typeof(string));
            return builder.Finish();
        }

        private static DataStorage FindOrCreateDataStorage(Document doc)
        {
            var existing = new FilteredElementCollector(doc).OfClass(typeof(DataStorage))
                .Cast<DataStorage>()
                .FirstOrDefault(ds => ds.Name == DataStorageName);
            if (existing != null) return existing;
            var created = DataStorage.Create(doc);
            created.Name = DataStorageName;
            return created;
        }

        // Reads the whole map -- used by the watcher to prime its in-memory
        // caches once per document (both hole->link and the derived
        // run->holes reverse index), and by the window if it ever needs to
        // know which runs already have a hole across sessions. Value is
        // (RunUniqueId, WallUniqueId); entries that don't parse (shouldn't
        // happen, but a hand-edited or corrupted value shouldn't throw) are
        // skipped rather than failing the whole read.
        // Every instance of the SAME FAMILY as holeSymbolId, anywhere in
        // the document -- deliberately family-wide, not restricted to the
        // exact type currently selected, since a legacy/hand-placed hole
        // may well be a different type of the same family than what's
        // configured for new placements right now. Read-only, no
        // Transaction, safe to call from ScanForCollisions directly (no
        // ExternalEvent needed for a plain read).
        //
        // Returns each instance's BOUNDING BOX, not its raw Location
        // Point -- confirmed live against a real project that these two
        // can be nowhere near each other for this kind of family: a
        // "WD_Bezug_UKD_OKB" ("wall opening, referenced base-to-top")
        // style family often anchors its insertion point at its BASE
        // reference (Location.Point.Z came back as exactly 0.00 on a real
        // instance), while the actual opening geometry -- and the real
        // crossing point a run passes through -- sits much higher, near
        // the TOP of the family's own vertical reach (that same
        // instance's bounding box ran from Z=0.00 all the way to
        // Z=11.37ft). Matching against the raw insertion point would
        // systematically miss every hole of this shape, off by however
        // tall the family's own base-to-top span is -- several METERS,
        // nowhere close to the ~300mm tolerance this is meant to allow
        // for. The bounding box, by contrast, reflects the family's real
        // geometry regardless of where its own insertion point happens to
        // sit internally.
        internal static List<(ElementId Id, BoundingBoxXYZ BBox)> GetExistingHoleInstances(Document doc, ElementId holeSymbolId)
        {
            var result = new List<(ElementId, BoundingBoxXYZ)>();
            if (doc == null || holeSymbolId == null || holeSymbolId == ElementId.InvalidElementId) return result;
            try
            {
                var familyId = (doc.GetElement(holeSymbolId) as FamilySymbol)?.Family?.Id;
                if (familyId == null || familyId == ElementId.InvalidElementId) return result;

                foreach (var el in new FilteredElementCollector(doc).OfClass(typeof(FamilyInstance)))
                {
                    try
                    {
                        var fi = el as FamilyInstance;
                        if (fi?.Symbol?.Family?.Id != familyId) continue;
                        var bbox = fi.get_BoundingBox(null); // model space directly, same call already used for the wall/run quick-filter above -- no extra Transform needed
                        if (bbox != null) result.Add((fi.Id, bbox));
                    }
                    catch { }
                }
            }
            catch { }
            return result;
        }

        private static ElementId FindNearbyExistingHole(List<(ElementId Id, BoundingBoxXYZ BBox)> existingHoleInstances, XYZ point)
        {
            const double proximityToleranceFt = 300.0 / 304.8; // ~300mm, applied as padding on every side of each instance's own bounding box
            foreach (var (id, bbox) in existingHoleInstances)
            {
                try { if (IsPointNearBoundingBox(point, bbox, proximityToleranceFt)) return id; }
                catch { }
            }
            return null;
        }

        // Shared by the run-scoped fallback above and FindNearbyExistingHole
        // -- padding an element's own real bounding box, rather than
        // measuring straight-line distance to its Location Point, is what
        // actually accounts for a family whose insertion point doesn't sit
        // where its visible geometry does (see the remarks above
        // GetExistingHoleInstances for a real, confirmed example of this).
        private static bool IsPointNearBoundingBox(XYZ point, BoundingBoxXYZ bbox, double toleranceFt)
        {
            if (point == null || bbox == null) return false;
            return point.X >= bbox.Min.X - toleranceFt && point.X <= bbox.Max.X + toleranceFt
                && point.Y >= bbox.Min.Y - toleranceFt && point.Y <= bbox.Max.Y + toleranceFt
                && point.Z >= bbox.Min.Z - toleranceFt && point.Z <= bbox.Max.Z + toleranceFt;
        }

        public static Dictionary<string, (string RunUniqueId, string WallUniqueId)> ReadHoleLinkMap(Document doc)
        {
            var result = new Dictionary<string, (string RunUniqueId, string WallUniqueId)>();
            try
            {
                var ds = new FilteredElementCollector(doc).OfClass(typeof(DataStorage))
                    .Cast<DataStorage>().FirstOrDefault(d => d.Name == DataStorageName);
                if (ds == null) return result;
                var schema = Schema.Lookup(SchemaGuid);
                if (schema == null) return result;
                var entity = ds.GetEntity(schema);
                if (entity == null || !entity.IsValid()) return result;
                var map = entity.Get<IDictionary<string, string>>(schema.GetField(SchemaFieldName));
                if (map == null) return result;
                foreach (var kv in map)
                {
                    var parts = (kv.Value ?? "").Split('|');
                    if (parts.Length != 2) continue;
                    result[kv.Key] = (parts[0], parts[1]);
                }
            }
            catch { }
            return result;
        }

        // Must be called from inside an already-open transaction (this is
        // called from within ExecutePlaceHoles' own transaction above).
        private static void LinkHoleToRunAndWall(Document doc, string holeUniqueId, string runUniqueId, string wallUniqueId)
        {
            try
            {
                var schema = GetOrCreateSchema();
                var ds = FindOrCreateDataStorage(doc);
                var entity = ds.GetEntity(schema);
                Dictionary<string, string> map;
                if (entity != null && entity.IsValid())
                {
                    var existing = entity.Get<IDictionary<string, string>>(schema.GetField(SchemaFieldName));
                    map = existing != null ? new Dictionary<string, string>(existing) : new Dictionary<string, string>();
                }
                else
                {
                    entity = new Entity(schema);
                    map = new Dictionary<string, string>();
                }
                map[holeUniqueId] = $"{runUniqueId}|{wallUniqueId}";
                entity.Set(schema.GetField(SchemaFieldName), map);
                ds.SetEntity(entity);
            }
            catch { }
        }

        // ═════════════════════════════════════════════════════════════════
        // SOLVED PLUMBING CLASHES -- a separate schema/storage from the
        // hole-link one above, kept deliberately distinct rather than
        // reused: a plumbing clash has no host-document hole element to
        // link to (the pipe it clashes with lives inside a LINKED
        // document, and there's no "hole" at all for this kind of
        // finding -- see the remarks on CollisionKind.PlumbingClash), so
        // this tracks a plain manual "marked as solved" acknowledgement
        // instead. Keyed by "runUniqueId|pipeUniqueId" -- the pipe's
        // UniqueId is only unique WITHIN its own link document, not
        // globally, but this project only has one plumbing link in
        // practice and the run half of the key makes a cross-link
        // collision exceedingly unlikely even if that ever changes.
        // ═════════════════════════════════════════════════════════════════
        private static readonly Guid SolvedClashSchemaGuid = new Guid("8A2E4F19-3B7D-4E1A-9C5F-1D6E8B4A2F73");
        private const string SolvedClashFieldName = "SolvedPlumbingClashes";
        private const string SolvedClashDataStorageName = "ME-Tools_SolvedPlumbingClashes";

        private static Schema GetOrCreateSolvedClashSchema()
        {
            var schema = Schema.Lookup(SolvedClashSchemaGuid);
            if (schema != null) return schema;
            var builder = new SchemaBuilder(SolvedClashSchemaGuid);
            builder.SetSchemaName("METoolsSolvedPlumbingClashes");
            builder.SetReadAccessLevel(AccessLevel.Public);
            builder.SetWriteAccessLevel(AccessLevel.Public);
            builder.AddMapField(SolvedClashFieldName, typeof(string), typeof(string));
            return builder.Finish();
        }

        private static DataStorage FindOrCreateSolvedClashDataStorage(Document doc)
        {
            var existing = new FilteredElementCollector(doc).OfClass(typeof(DataStorage))
                .Cast<DataStorage>()
                .FirstOrDefault(ds => ds.Name == SolvedClashDataStorageName);
            if (existing != null) return existing;
            var created = DataStorage.Create(doc);
            created.Name = SolvedClashDataStorageName;
            return created;
        }

        // Read-only, no Transaction -- safe to call directly from
        // ScanForPlumbingClashes the same way ReadHoleLinkMap is called
        // directly from the wall-crossing scan.
        internal static HashSet<string> ReadSolvedPlumbingClashKeys(Document doc)
        {
            var result = new HashSet<string>();
            try
            {
                var ds = new FilteredElementCollector(doc).OfClass(typeof(DataStorage))
                    .Cast<DataStorage>().FirstOrDefault(d => d.Name == SolvedClashDataStorageName);
                if (ds == null) return result;
                var schema = Schema.Lookup(SolvedClashSchemaGuid);
                if (schema == null) return result;
                var entity = ds.GetEntity(schema);
                if (entity == null || !entity.IsValid()) return result;
                var map = entity.Get<IDictionary<string, string>>(schema.GetField(SolvedClashFieldName));
                if (map == null) return result;
                foreach (var key in map.Keys) result.Add(key);
            }
            catch { }
            return result;
        }

        // Must be called from inside an already-open transaction --
        // called from ExecuteMarkPlumbingSolved below.
        private static void MarkPlumbingClashSolved(Document doc, string combinedKey)
        {
            try
            {
                var schema = GetOrCreateSolvedClashSchema();
                var ds = FindOrCreateSolvedClashDataStorage(doc);
                var entity = ds.GetEntity(schema);
                Dictionary<string, string> map;
                if (entity != null && entity.IsValid())
                {
                    var existing = entity.Get<IDictionary<string, string>>(schema.GetField(SolvedClashFieldName));
                    map = existing != null ? new Dictionary<string, string>(existing) : new Dictionary<string, string>();
                }
                else
                {
                    entity = new Entity(schema);
                    map = new Dictionary<string, string>();
                }
                map[combinedKey] = "1";
                entity.Set(schema.GetField(SolvedClashFieldName), map);
                ds.SetEntity(entity);
            }
            catch { }
        }
    }
}

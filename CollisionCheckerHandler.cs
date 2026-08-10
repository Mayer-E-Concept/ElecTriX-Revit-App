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

        // Runs the two-phase detection described at the top of this file and
        // returns one CollisionInfo per point where a run's centerline
        // crosses a wall. scope applies to the runs being checked; every
        // wall in the whole model is always considered as a potential
        // obstacle regardless of scope, since a run in view/selection scope
        // can still be crossing a wall that itself isn't in that scope.
        public static List<CollisionInfo> ScanForCollisions(Document doc, UIDocument uiDoc, ScanScope scope)
        {
            var result = new List<CollisionInfo>();
            try
            {
                var runs  = GetScopedElements(doc, uiDoc, scope, RunCategories);
                var walls = new FilteredElementCollector(doc).OfClass(typeof(Wall)).WhereElementIsNotElementType().Cast<Wall>().ToList();
                if (runs.Count == 0 || walls.Count == 0) return result;

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

                            result.Add(new CollisionInfo
                            {
                                ElementId       = run.Id,
                                WallId          = wall.Id,
                                ElementCategory = run.Category?.Name ?? "",
                                ElementTypeName = TypeNameOf(doc, run),
                                WallTypeName    = TypeNameOf(doc, wall),
                                Point           = point,
                                LevelId         = ResolveLevelId(doc, run),
                                LevelName       = ResolveLevelName(doc, ResolveLevelId(doc, run)),
                            });
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

                            // Tried in order regardless of the family's
                            // reported FamilyPlacementType -- that enum's
                            // exact mapping to the right overload has been
                            // wrong twice already for this specific family,
                            // so runtime success/failure is more reliable
                            // ground truth here than the enum value is.

                            // 1) Face-hosted (WorkPlaneBased-style families)
                            // -- hosted BY THE WALL, at the exact collision
                            // point (c.Point). The run itself is never
                            // passed as a host anywhere in this method; it's
                            // only ever used to compute where the crossing
                            // point is, upstream in ScanForCollisions.
                            if (instance == null)
                            {
                                try
                                {
                                    var faceRef = FindNearestFaceReference(wall, c.Point);
                                    if (faceRef != null)
                                    {
                                        var face = wall.GetGeometryObjectFromReference(faceRef) as Face;
                                        if (face != null)
                                            instance = doc.Create.NewFamilyInstance(face, c.Point, direction, symbol);
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
    }
}

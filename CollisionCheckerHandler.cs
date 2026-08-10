// CollisionCheckerHandler.cs -- ME-Tools | Collision Checker (conduits/cable trays vs walls)
// Mayer E-Concept SRL
//
// Detection is two-phase, per the project's own established convention
// (verify Revit API behavior, don't guess -- see NOTES.md) and confirmed
// against Autodesk's own docs before writing this:
//   1. FAST pass: ElementIntersectsElementFilter (a "slow filter" per its
//      own doc page, but still far cheaper than per-pair face geometry)
//      combined with a BoundingBoxIntersectsFilter-style quick outline
//      check first, to find which conduit/wall PAIRS actually intersect.
//   2. PRECISE pass, only for confirmed pairs: Face.Intersect(Curve, out
//      IntersectionResultArray) against the wall's side faces, which
//      returns the exact 3D point(s) where the run's centerline crosses
//      the wall -- needed for the list, the red highlight, "go to", and
//      hole placement, none of which the fast filter alone can give.
//
// The link between a placed hole and the run it belongs to (so the hole
// can follow the run if it's later moved -- see CollisionCheckerWatcher)
// is stored via Extensible Storage on a single per-document DataStorage
// element, as a Map<string,string> of run UniqueId -> hole UniqueId. Not
// on the hole instances themselves individually, because that would need
// scanning every instance of a family whose category isn't known ahead of
// time; a DataStorage element is trivial to find and cheap to read/write
// regardless of what family the hole turns out to be.
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

                    ElementIntersectsElementFilter fastFilter;
                    try { fastFilter = new ElementIntersectsElementFilter(run); }
                    catch { continue; }

                    // Quick bounding-box pass first, over just the walls
                    // list (already small relative to a whole-model
                    // collector) -- cuts down how many walls actually reach
                    // the slow ElementIntersectsElementFilter check below.
                    var runBox = run.get_BoundingBox(null);
                    if (runBox == null) continue;
                    var outline = new Outline(runBox.Min, runBox.Max);

                    foreach (var wall in walls)
                    {
                        try
                        {
                            var wallBox = wall.get_BoundingBox(null);
                            if (wallBox == null) continue;
                            if (!outline.Intersects(new Outline(wallBox.Min, wallBox.Max), 0.01)) continue;
                            if (!fastFilter.PassesFilter(wall)) continue;

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
            if (points.Count == 0) return null;
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
                    var isFaceHosted  = placementType == FamilyPlacementType.WorkPlaneBased;

                    foreach (var c in req.Collisions)
                    {
                        try
                        {
                            var run  = doc.GetElement(c.ElementId);
                            var wall = doc.GetElement(c.WallId) as Wall;
                            if (run == null || wall == null || c.Point == null)
                            {
                                result.Skipped++;
                                continue;
                            }

                            var runCurve = (run.Location as LocationCurve)?.Curve;
                            var direction = runCurve != null
                                ? (runCurve.GetEndPoint(1) - runCurve.GetEndPoint(0)).Normalize()
                                : XYZ.BasisX;

                            FamilyInstance instance = null;

                            if (isFaceHosted)
                            {
                                var faceRef = FindNearestFaceReference(wall, c.Point);
                                if (faceRef != null)
                                {
                                    var face = wall.GetGeometryObjectFromReference(faceRef) as Face;
                                    if (face != null)
                                        instance = doc.Create.NewFamilyInstance(face, c.Point, direction, symbol);
                                }
                            }

                            if (instance == null) // WorkPlaneBased placement failed, or family isn't hosted at all
                                instance = doc.Create.NewFamilyInstance(c.Point, symbol, StructuralType.NonStructural);

                            if (instance == null)
                            {
                                result.Skipped++;
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

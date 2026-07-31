// ActivityLogWatcher.cs -- ME-Tools | Activity Log background tracker
// Mayer E-Concept SRL
//
// Runs for the lifetime of the Revit session, the same way CommentsWatcher
// does. Two events matter:
//   - DocumentOpened: does one initial scan of the tracked categories so
//     elements that already existed before this session started watching
//     can still be reported correctly if deleted later.
//   - DocumentChanged: fires after every transaction. Added/Modified
//     elements refresh the cache; Deleted elements are looked up in that
//     same cache, since by the time this event fires the element itself is
//     already gone -- doc.GetElement(deletedId) returns null.
//
// Scoped to the electrical/MEP categories ElecTriX actually works with
// (matches Circuit Tagger's category list, plus cable trays/conduit/wire),
// not every category in the model -- tracking every discipline's every edit
// on a shared central model would be both noisy and outside what this suite
// is for.
using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Events;
using Autodesk.Revit.UI;

namespace METools.ActivityLog
{
    public static class ActivityLogWatcher
    {
        private static readonly BuiltInCategory[] TrackedCategories =
        {
            BuiltInCategory.OST_ElectricalFixtures,
            BuiltInCategory.OST_LightingFixtures,
            BuiltInCategory.OST_LightingDevices,
            BuiltInCategory.OST_ElectricalEquipment,
            BuiltInCategory.OST_DataDevices,
            BuiltInCategory.OST_FireAlarmDevices,
            BuiltInCategory.OST_CommunicationDevices,
            BuiltInCategory.OST_SecurityDevices,
            BuiltInCategory.OST_CableTray,
            BuiltInCategory.OST_CableTrayFitting,
            BuiltInCategory.OST_Conduit,
            BuiltInCategory.OST_ConduitFitting,
            BuiltInCategory.OST_Wire,
        };

        // Same set as TrackedCategories, as an int HashSet -- built once, so
        // the per-element "is this a category I care about?" check in
        // Snapshot() is an O(1) lookup instead of a linear .Any() scan over
        // the array. That check runs for every added AND every modified
        // element in every transaction, so on a big edit it was 13 * N
        // comparisons per commit for no reason.
        private static readonly HashSet<int> _trackedCatIds =
            new HashSet<int>(System.Array.ConvertAll(TrackedCategories, c => (int)c));

        // Per-open-document element cache. Reference equality on Document is
        // fine here -- this only needs to be correct within the current
        // Revit session, never across restarts.
        private static readonly Dictionary<Document, Dictionary<long, ElementSnapshot>> _cache
            = new Dictionary<Document, Dictionary<long, ElementSnapshot>>();

        public static void Register(UIControlledApplication app)
        {
            app.ControlledApplication.DocumentOpened  += OnDocumentOpened;
            app.ControlledApplication.DocumentChanged += OnDocumentChanged;
            app.ControlledApplication.DocumentClosing += OnDocumentClosing;
        }

        // Without this, _cache below holds a live reference to every Document
        // ever opened this session, forever -- since Document is the
        // dictionary key, that pins the entire document object graph in
        // memory even long after the user has closed the project. On a
        // session that opens/closes several projects in a day, that's a real
        // and growing leak, not just a theoretical one.
        private static void OnDocumentClosing(object sender, DocumentClosingEventArgs e)
        {
            try { _cache.Remove(e.Document); } catch { }
        }

        private static void OnDocumentOpened(object sender, DocumentOpenedEventArgs e)
        {
            try
            {
                var doc = e.Document;
                if (doc == null || doc.IsFamilyDocument) return;
                PrimeCache(doc);
            }
            catch { }
        }

        private static void PrimeCache(Document doc)
        {
            // If the feature isn't configured, nothing is ever logged, so
            // there's no reason to scan the model to prime the delete-tracking
            // cache. Matches the same guard in OnDocumentChanged.
            var folder = METools.Comments.CommentsStorage.GetSharedFolder();
            if (string.IsNullOrWhiteSpace(folder)) return;

            var map = new Dictionary<long, ElementSnapshot>();
            foreach (var cat in TrackedCategories)
            {
                try
                {
                    foreach (var el in new FilteredElementCollector(doc)
                        .OfCategory(cat).WhereElementIsNotElementType())
                    {
                        var snap = Snapshot(doc, el);
                        if (snap != null) map[el.Id.IntegerValue] = snap;
                    }
                }
                catch { }
            }
            _cache[doc] = map;
        }

        private static void OnDocumentChanged(object sender, DocumentChangedEventArgs e)
        {
            try
            {
                var doc = e.GetDocument();
                if (doc == null || doc.IsFamilyDocument) return;
                if (METools.LicenseManager.IsTrialExpired) return; // silent gate, matches CommentsWatcher

                // Cheapest possible early-out first: if this transaction added,
                // modified and deleted nothing at all, there is nothing to do.
                var addedIds    = e.GetAddedElementIds();
                var modifiedIds = e.GetModifiedElementIds();
                var deletedIds  = e.GetDeletedElementIds();
                if (addedIds.Count == 0 && modifiedIds.Count == 0 && deletedIds.Count == 0) return;

                var folder = METools.Comments.CommentsStorage.GetSharedFolder();
                if (string.IsNullOrWhiteSpace(folder)) return; // feature not configured -- nothing to log to

                if (!_cache.TryGetValue(doc, out var map))
                {
                    map = new Dictionary<long, ElementSnapshot>();
                    _cache[doc] = map;
                }

                // Decide up front whether ANY of the changed elements could be
                // relevant, using only cheap checks -- added/modified elements
                // are gated by a live category lookup, deletions by whether the
                // id is in our cache at all. If nothing qualifies (the common
                // case on a shared model, where most commits are architectural/
                // structural), bail out BEFORE the more expensive project-id
                // read (Extensible Storage) and username/transaction-name work.
                bool anyRelevant = false;
                foreach (var id in addedIds)    { if (IsTrackedLive(doc, id)) { anyRelevant = true; break; } }
                if (!anyRelevant)
                    foreach (var id in modifiedIds) { if (IsTrackedLive(doc, id)) { anyRelevant = true; break; } }
                if (!anyRelevant)
                    foreach (var id in deletedIds)  { if (map.ContainsKey(id.IntegerValue)) { anyRelevant = true; break; } }
                if (!anyRelevant) return;

                var projectId = ActivityLogStorage.GetProjectId(doc);
                if (string.IsNullOrWhiteSpace(projectId)) return;

                string user = "";
                try { user = (sender as Autodesk.Revit.ApplicationServices.Application)?.Username ?? ""; } catch { }
                if (string.IsNullOrWhiteSpace(user))
                    try { user = Environment.UserName; } catch { }

                string txNames = "";
                try { txNames = string.Join(", ", e.GetTransactionNames().Where(n => !string.IsNullOrWhiteSpace(n))); }
                catch { }

                var now = DateTime.UtcNow;
                // Collected here (main thread, needs live Revit API access for
                // Snapshot/doc.GetElement) and written to the network file
                // afterward on a background thread -- see the Task.Run below
                // this method's three loops for why.
                var entriesToWrite = new List<ActivityLogEntry>();

                // Added: refresh the cache from the live element, log only
                // for elements in a tracked category. (addedIds/modifiedIds/
                // deletedIds were already fetched once above for the early-out
                // check -- reused here rather than re-querying the event.)
                foreach (var id in addedIds)
                {
                    Element el;
                    try { el = doc.GetElement(id); } catch { el = null; }
                    if (el == null) continue;

                    var snap = Snapshot(doc, el);
                    if (snap == null) continue; // not a tracked category

                    map[id.IntegerValue] = snap;

                    entriesToWrite.Add(new ActivityLogEntry
                    {
                        TimestampUtc     = now,
                        User             = user,
                        Action           = ActivityAction.Added,
                        Category         = snap.Category,
                        FamilyName       = snap.FamilyName,
                        TypeName         = snap.TypeName,
                        LevelName        = snap.LevelName,
                        LevelId          = snap.LevelId,
                        ElementId        = id.IntegerValue.ToString(),
                        TransactionNames = txNames,
                    });
                }

                // Modified: same refresh, different action label.
                foreach (var id in modifiedIds)
                {
                    Element el;
                    try { el = doc.GetElement(id); } catch { el = null; }
                    if (el == null) continue;

                    var snap = Snapshot(doc, el);
                    if (snap == null) continue;

                    map[id.IntegerValue] = snap;

                    entriesToWrite.Add(new ActivityLogEntry
                    {
                        TimestampUtc     = now,
                        User             = user,
                        Action           = ActivityAction.Modified,
                        Category         = snap.Category,
                        FamilyName       = snap.FamilyName,
                        TypeName         = snap.TypeName,
                        LevelName        = snap.LevelName,
                        LevelId          = snap.LevelId,
                        ElementId        = id.IntegerValue.ToString(),
                        TransactionNames = txNames,
                    });
                }

                // Deleted: element is already gone -- use whatever the cache
                // has from the last time it was seen as Added/Modified, or
                // from the initial PrimeCache scan.
                foreach (var id in deletedIds)
                {
                    if (!map.TryGetValue(id.IntegerValue, out var snap)) continue; // never tracked -- not our category, skip quietly
                    map.Remove(id.IntegerValue);

                    entriesToWrite.Add(new ActivityLogEntry
                    {
                        TimestampUtc     = now,
                        User             = user,
                        Action           = ActivityAction.Deleted,
                        Category         = snap.Category,
                        FamilyName       = snap.FamilyName,
                        TypeName         = snap.TypeName,
                        LevelName        = snap.LevelName,
                        LevelId          = snap.LevelId,
                        ElementId        = id.IntegerValue.ToString(),
                        TransactionNames = txNames,
                    });
                }

                // The actual network write happens off the main thread.
                // ActivityLogStorage.Append() retries up to 3 times with a
                // 100ms sleep on contention (normal on a shared central model
                // with several people committing tracked-category edits close
                // together), and each attempt itself waits on a network call
                // that can be slow to fail on a degraded connection -- doing
                // that synchronously here, on Revit's own main thread, is what
                // was actually freezing Revit for a beat on every relevant
                // commit. Same Task.Run pattern CommentsHandler already uses
                // for its own network I/O, just applied here too.
                //
                // Trade-off, stated plainly: if Revit closes in the brief
                // window before this finishes, that entry can be lost. For an
                // activity log (not model data) that's a clearly acceptable
                // trade against blocking the main thread on every edit.
                if (entriesToWrite.Count > 0)
                {
                    System.Threading.Tasks.Task.Run(() =>
                    {
                        foreach (var entry in entriesToWrite)
                        {
                            try { ActivityLogStorage.Append(projectId, entry); }
                            catch { }
                        }
                    });
                }
            }
            catch { }
        }

        // Cheap "should I care about this element?" check -- category only,
        // no family/type/level resolution. Used by the up-front relevance
        // gate so the expensive Snapshot() work only happens once an element
        // is already known to be in a tracked category.
        private static bool IsTrackedLive(Document doc, ElementId id)
        {
            try
            {
                var el = doc.GetElement(id);
                var catId = el?.Category?.Id?.IntegerValue;
                return catId != null && _trackedCatIds.Contains(catId.Value);
            }
            catch { return false; }
        }

        private static ElementSnapshot Snapshot(Document doc, Element el)
        {
            try
            {
                var catId = el.Category?.Id?.IntegerValue;
                if (catId == null) return null;
                bool tracked = _trackedCatIds.Contains(catId.Value);
                if (!tracked) return null;

                string family = "", type = "";
                if (el is FamilyInstance fi)
                {
                    try { family = fi.Symbol?.Family?.Name ?? ""; } catch { }
                    try { type = fi.Symbol?.Name ?? ""; } catch { }
                }
                else
                {
                    try { type = doc.GetElement(el.GetTypeId())?.Name ?? ""; } catch { }
                }

                var (levelId, levelName) = ResolveLevel(doc, el);

                return new ElementSnapshot
                {
                    Category   = el.Category?.Name ?? "",
                    FamilyName = family,
                    TypeName   = type,
                    LevelName  = levelName,
                    LevelId    = levelId,
                };
            }
            catch { return null; }
        }

        // The one piece of this file that has to be exactly right, per an
        // explicit ask: matches CurrentLevelId(FamilyInstance) in
        // FixLevelCommand.cs -- the tool whose entire job is level
        // correctness for these exact element types -- rather than the
        // generic Element.LevelId this file originally (and wrongly) relied
        // on alone. For these CAx electrical families, Element.LevelId is
        // frequently InvalidElementId even though the family instance has a
        // perfectly good, user-meaningful level via its Schedule Level
        // parameter (INSTANCE_SCHEDULE_ONLY_LEVEL_PARAM) -- the same
        // parameter Lamp Placer explicitly sets post-placement "for
        // scheduling correctness" and Fix Level exists specifically to
        // repair. Checking that parameter first, before falling back to
        // Element.LevelId, is what was missing and is why every entry
        // showed a blank level before this fix.
        //
        // Non-FamilyInstance tracked elements (cable tray, conduit, wire)
        // aren't affected by this -- they don't have a Schedule Level
        // parameter at all, and their own Element.LevelId is reliable, so
        // they go straight to the LevelId fallback.
        private static (string LevelId, string LevelName) ResolveLevel(Document doc, Element el)
        {
            ElementId levelId = ElementId.InvalidElementId;

            if (el is FamilyInstance fi)
            {
                try
                {
                    var p = fi.get_Parameter(BuiltInParameter.INSTANCE_SCHEDULE_ONLY_LEVEL_PARAM);
                    if (p != null)
                    {
                        var id = p.AsElementId();
                        if (id != null && id != ElementId.InvalidElementId) levelId = id;
                    }
                }
                catch { }
            }

            if (levelId == ElementId.InvalidElementId)
            {
                try { levelId = el.LevelId ?? ElementId.InvalidElementId; }
                catch { levelId = ElementId.InvalidElementId; }
            }

            if (levelId == ElementId.InvalidElementId) return ("", "");

            string name = "";
            try { name = (doc.GetElement(levelId) as Level)?.Name ?? ""; } catch { }
            if (string.IsNullOrEmpty(name)) return ("", ""); // levelId pointed at something that isn't actually a Level

            return (levelId.IntegerValue.ToString(), name);
        }
    }
}

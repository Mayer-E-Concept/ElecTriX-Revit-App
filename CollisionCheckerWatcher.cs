// CollisionCheckerWatcher.cs -- ME-Tools | Collision Checker background tracker
// Mayer E-Concept SRL
//
// Follows the exact same shape as ActivityLogWatcher.cs -- read the header
// comment there first if this file doesn't make sense on its own.
//
// The one thing that ISN'T like ActivityLogWatcher: DocumentChanged is
// explicitly documented by Autodesk as a read-only event ("This is a
// readonly event... To update the Revit database in response to changes
// in elements, use the IUpdater framework") -- calling new Transaction(...)
// inside it throws InvalidOperationException. So detection here stays
// read-only, and the actual hole move happens later, in a valid API
// context, via a session-long ExternalEvent -- the same mechanism every
// other write in this app already uses from modeless windows, just kept
// alive for the whole Revit session instead of only while a window is
// open, since a hole has to keep following its run even after the
// Collision Checker window itself is closed.
using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Events;
using Autodesk.Revit.UI;

namespace METools.CollisionChecker
{
    public static class CollisionCheckerWatcher
    {
        // Session-long ExternalEvent + handler, independent of whatever
        // handler/event the Collision Checker window itself creates and
        // disposes of per-open -- a hole must keep following its run even
        // while the window is closed.
        private static CollisionCheckerHandler _handler;
        private static ExternalEvent _moveEvent;

        // run UniqueId -> every hole that belongs to it, per open document.
        // Derived from the persisted Extensible Storage map (which is keyed
        // by hole, not run -- see CollisionCheckerHandler) by inverting it
        // once at prime time, so the DocumentChanged handler's per-element
        // check below is an O(1) dictionary lookup, not a rescan.
        private static readonly Dictionary<Document, Dictionary<string, List<(string HoleUniqueId, string WallUniqueId)>>> _cache
            = new Dictionary<Document, Dictionary<string, List<(string HoleUniqueId, string WallUniqueId)>>>();

        // Last scan results per document, so closing and reopening the
        // Collision Checker window (without closing the document itself)
        // doesn't lose what was already found -- session-level only, not
        // persisted to disk, since it's just a convenience cache of
        // something a fresh Scan can always reproduce. Shares this file's
        // existing DocumentClosing hook for cleanup rather than adding a
        // second one.
        private static readonly Dictionary<Document, (List<CollisionInfo> Collisions, DateTime ScannedAt)> _scanCache
            = new Dictionary<Document, (List<CollisionInfo> Collisions, DateTime ScannedAt)>();

        public static void SaveScanResults(Document doc, List<CollisionInfo> collisions)
        {
            try { _scanCache[doc] = (collisions, DateTime.Now); } catch { }
        }

        public static (List<CollisionInfo> Collisions, DateTime ScannedAt)? GetScanResults(Document doc)
        {
            try { if (doc != null && _scanCache.TryGetValue(doc, out var entry)) return entry; }
            catch { }
            return null;
        }

        // Call once from App.OnStartup -- this is a valid API context, so
        // the ExternalEvent can be created proactively here rather than
        // lazily on first use from a context that isn't valid (see
        // NOTES.md's "ExternalEvent must be created during a valid API
        // context" entry).
        public static void Register(UIControlledApplication app)
        {
            _handler   = new CollisionCheckerHandler();
            _moveEvent = ExternalEvent.Create(_handler);

            app.ControlledApplication.DocumentOpened  += OnDocumentOpened;
            app.ControlledApplication.DocumentChanged += OnDocumentChanged;
            app.ControlledApplication.DocumentClosing += OnDocumentClosing;
        }

        // Without this, _cache holds a live reference to every Document
        // ever opened this session, forever -- see the identical note in
        // ActivityLogWatcher.cs, which is exactly where this lesson came
        // from in the first place.
        private static void OnDocumentClosing(object sender, DocumentClosingEventArgs e)
        {
            try { _cache.Remove(e.Document); } catch { }
            try { _scanCache.Remove(e.Document); } catch { }
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

        // Called right after a hole is placed (from CollisionCheckerWindow),
        // so the brand new link is watched immediately -- otherwise it would
        // only be picked up on the next DocumentOpened, which won't happen
        // again this session for a document that's already open.
        public static void NotifyHoleLinked(Document doc, string holeUniqueId, string runUniqueId, string wallUniqueId)
        {
            try
            {
                if (!_cache.TryGetValue(doc, out var byRun))
                {
                    byRun = new Dictionary<string, List<(string HoleUniqueId, string WallUniqueId)>>();
                    _cache[doc] = byRun;
                }
                if (!byRun.TryGetValue(runUniqueId, out var list))
                {
                    list = new List<(string HoleUniqueId, string WallUniqueId)>();
                    byRun[runUniqueId] = list;
                }
                list.Add((holeUniqueId, wallUniqueId));
            }
            catch { }
        }

        private static void PrimeCache(Document doc)
        {
            var byHole = CollisionCheckerHandler.ReadHoleLinkMap(doc); // hole -> (run, wall)
            var byRun = new Dictionary<string, List<(string HoleUniqueId, string WallUniqueId)>>();
            foreach (var kv in byHole)
            {
                var holeUid = kv.Key;
                var (runUid, wallUid) = kv.Value;
                if (!byRun.TryGetValue(runUid, out var list))
                {
                    list = new List<(string HoleUniqueId, string WallUniqueId)>();
                    byRun[runUid] = list;
                }
                list.Add((holeUid, wallUid));
            }
            _cache[doc] = byRun;
        }

        private static void OnDocumentChanged(object sender, DocumentChangedEventArgs e)
        {
            try
            {
                var doc = e.GetDocument();
                if (doc == null || doc.IsFamilyDocument) return;
                if (METools.LicenseManager.IsTrialExpired) return; // matches other watchers' silent gate

                // Cheapest possible early-out: only Modified elements can be
                // a run that moved -- Added/Deleted don't apply here (a
                // deleted run's hole is left in place deliberately, same as
                // Revit leaves an opening in place if you delete a duct
                // that used to need it -- not this tool's job to also
                // delete the hole).
                var modifiedIds = e.GetModifiedElementIds();
                if (modifiedIds.Count == 0) return;

                if (!_cache.TryGetValue(doc, out var byRun) || byRun.Count == 0) return; // nothing tracked in this doc yet

                List<HoleMoveInfo> moves = null;
                foreach (var id in modifiedIds)
                {
                    Element el;
                    try { el = doc.GetElement(id); } catch { continue; }
                    if (el == null) continue;
                    string uid;
                    try { uid = el.UniqueId; } catch { continue; }
                    if (!byRun.TryGetValue(uid, out var holes)) continue; // not a tracked run -- the common case for almost every commit

                    moves = moves ?? new List<HoleMoveInfo>();
                    foreach (var (holeUid, wallUid) in holes)
                        moves.Add(new HoleMoveInfo { RunId = id, HoleUniqueId = holeUid, WallUniqueId = wallUid });
                }
                if (moves == null || moves.Count == 0) return;

                // Queue the actual move for the next valid API context --
                // see the file header for why this can't just happen here.
                _handler.Request = new CollisionCheckerRequest
                {
                    Action    = CollisionCheckerAction.MoveHoles,
                    HoleMoves = moves,
                };
                _moveEvent.Raise();
            }
            catch { }
        }
    }
}

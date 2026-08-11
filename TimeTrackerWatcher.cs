// TimeTrackerWatcher.cs -- ME-Tools | Time Tracker background tracker
// Mayer E-Concept SRL
//
// Runs for the lifetime of the Revit session, the same way ActivityLogWatcher
// does. Three things matter:
//   - DocumentOpened:  starts a session (in-memory state + a local marker
//                       file, in case this session never ends cleanly).
//   - Idling:          refreshes that marker's heartbeat every few minutes,
//                       so a crash only loses a few minutes of time instead
//                       of the whole session.
//   - DocumentClosing: ends the session cleanly -- computes the duration,
//                       appends it to the shared per-project log, deletes
//                       the local marker.
//
// Measurement is deliberately simple wall time (open -> close), not an
// idle-aware "were they actually working" heuristic -- see TimeSessionEntry.
// The one piece of real nuance this file handles is making that simple
// measurement crash-safe: RecoverStaleMarkers(), run once at Revit startup,
// finds any marker a *previous, crashed* Revit process on this machine left
// behind and finalizes it using its last heartbeat as the end time, flagged
// Recovered so the UI never presents an estimate as if it were a clean close.
using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Events;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Events;

namespace METools.TimeTracker
{
    public static class TimeTrackerWatcher
    {
        private const int MIN_SESSION_SECONDS = 30; // shorter than this is a quick open/glance/close, not real work
        private static readonly TimeSpan HEARTBEAT_INTERVAL = TimeSpan.FromMinutes(3);
        private static readonly TimeSpan STALE_THRESHOLD    = TimeSpan.FromMinutes(10); // older than this at startup => the owning process is gone, not just slow

        private class SessionState
        {
            public DateTime StartUtc;
            public DateTime LastHeartbeatUtc;
            public string   SessionId;
            public string   ProjectId;
            public string   User;
        }

        // Per-open-document session state. Reference equality on Document is
        // fine -- only needs to be correct within the current Revit session.
        // Removed the moment its DocumentClosing fires, so this never
        // accumulates the same kind of leak ActivityLogWatcher's own cache
        // had to be fixed for.
        private static readonly Dictionary<Document, SessionState> _sessions
            = new Dictionary<Document, SessionState>();

        // Read-only lookup, safe to call directly from UI code (no
        // ExternalEvent needed -- this only touches the in-memory
        // dictionary above, never the Revit API). Lets the window show a
        // "currently tracking" indicator for the open document instead of
        // looking inert until the first session actually finishes.
        public static DateTime? GetCurrentSessionStart(Document doc)
        {
            if (doc != null && _sessions.TryGetValue(doc, out var state)) return state.StartUtc;
            return null;
        }

        public static void Register(UIControlledApplication app)
        {
            app.ControlledApplication.DocumentOpened  += OnDocumentOpened;
            app.ControlledApplication.DocumentClosing += OnDocumentClosing;
            app.Idling += OnIdling;

            // One-time crash recovery for whatever THIS machine's previous
            // Revit process left behind. Off the main thread since it may
            // touch a shared network file; nothing in it needs live Revit
            // API access, so there's no "valid API context" concern here.
            System.Threading.Tasks.Task.Run(() => RecoverStaleMarkers());
        }

        private static void OnDocumentOpened(object sender, DocumentOpenedEventArgs e)
        {
            try
            {
                var doc = e.Document;
                if (doc == null || doc.IsFamilyDocument) return;
                if (METools.LicenseManager.IsTrialExpired) return; // silent gate, matches CommentsWatcher/ActivityLogWatcher

                var folder = METools.Comments.CommentsStorage.GetSharedFolder();
                if (string.IsNullOrWhiteSpace(folder)) return; // feature not configured -- nothing to log to yet

                // Read-only on purpose -- DocumentOpened already has an
                // implicit transaction of Revit's own open, and starting
                // another explicit one (GetProjectId's write path does,
                // on a never-before-stamped document) is prohibited there
                // too. If this comes back null, skip this session start
                // rather than risk silently minting an id that never
                // actually gets saved -- see TryGetCachedOrExistingProjectId.
                var projectId = TimeTrackerStorage.TryGetCachedOrExistingProjectId(doc);
                if (string.IsNullOrWhiteSpace(projectId)) return;

                string user = "";
                try { user = (sender as Autodesk.Revit.ApplicationServices.Application)?.Username ?? ""; } catch { }
                if (string.IsNullOrWhiteSpace(user))
                    try { user = Environment.UserName; } catch { }

                var now = DateTime.UtcNow;
                var state = new SessionState
                {
                    StartUtc         = now,
                    LastHeartbeatUtc = now,
                    SessionId        = Guid.NewGuid().ToString("N"),
                    ProjectId        = projectId,
                    User             = user,
                };
                _sessions[doc] = state;

                TimeTrackerStorage.WriteMarker(projectId, state.SessionId, new ActiveSessionMarker
                {
                    ProjectId        = projectId,
                    User             = user,
                    StartUtc         = now.ToString("O"),
                    LastHeartbeatUtc = now.ToString("O"),
                });
            }
            catch { }
        }

        private static void OnDocumentClosing(object sender, DocumentClosingEventArgs e)
        {
            try
            {
                var doc = e.Document;
                if (doc == null) return;
                if (!_sessions.TryGetValue(doc, out var state)) return; // never tracked -- folder wasn't configured when it opened, or a family doc

                _sessions.Remove(doc);

                // This session ended cleanly -- nothing left to recover, so
                // the local marker can go regardless of whether the duration
                // ends up long enough to log.
                TimeTrackerStorage.DeleteMarker(state.ProjectId, state.SessionId);

                var now = DateTime.UtcNow;
                var durationSeconds = (now - state.StartUtc).TotalSeconds;
                if (durationSeconds < MIN_SESSION_SECONDS) return;

                var entry = new TimeSessionEntry
                {
                    StartUtc  = state.StartUtc,
                    EndUtc    = now,
                    User      = state.User,
                    Recovered = false,
                };

                // Off the main thread -- same reasoning as ActivityLogWatcher's
                // Task.Run: this write can hit a slow/degraded network share,
                // and that must never freeze Revit on a project close.
                var projectId = state.ProjectId;
                System.Threading.Tasks.Task.Run(() =>
                {
                    try { TimeTrackerStorage.Append(projectId, entry); } catch { }
                });
            }
            catch { }
        }

        // Fires very frequently whenever Revit itself is idle -- used here
        // purely as a "some time has passed, is any heartbeat due" clock.
        // Cheapest possible early-out first: if nothing is being tracked
        // (typically because no shared folder is configured at all), every
        // single Idling tick returns immediately.
        private static void OnIdling(object sender, IdlingEventArgs e)
        {
            try
            {
                if (_sessions.Count == 0) return;

                var now = DateTime.UtcNow;
                // Copy first -- WriteMarker below never touches _sessions, but
                // this keeps the loop safe against future edits that might.
                foreach (var kvp in new List<KeyValuePair<Document, SessionState>>(_sessions))
                {
                    var state = kvp.Value;
                    if (now - state.LastHeartbeatUtc < HEARTBEAT_INTERVAL) continue;

                    state.LastHeartbeatUtc = now;
                    TimeTrackerStorage.WriteMarker(state.ProjectId, state.SessionId, new ActiveSessionMarker
                    {
                        ProjectId        = state.ProjectId,
                        User             = state.User,
                        StartUtc         = state.StartUtc.ToString("O"),
                        LastHeartbeatUtc = now.ToString("O"),
                    });
                }
            }
            catch { }
        }

        // Looks for markers left behind by a previous, crashed Revit process
        // on this machine (a clean close always deletes its own marker in
        // OnDocumentClosing, so anything still here is by definition either
        // stale or -- rarely -- a second Revit instance's still-live session).
        private static void RecoverStaleMarkers()
        {
            try
            {
                // Can't write a recovered entry anywhere useful yet -- leave
                // the markers in place so a later, successful attempt (once a
                // shared folder is configured) can still recover them, rather
                // than deleting evidence of lost time for nothing.
                if (string.IsNullOrWhiteSpace(METools.Comments.CommentsStorage.GetSharedFolder())) return;

                var now = DateTime.UtcNow;
                foreach (var (path, marker) in TimeTrackerStorage.ListAllMarkers())
                {
                    if (marker == null || string.IsNullOrWhiteSpace(marker.ProjectId))
                    { TimeTrackerStorage.DeleteMarkerFile(path); continue; }

                    if (!DateTime.TryParse(marker.StartUtc, null,
                            System.Globalization.DateTimeStyles.RoundtripKind, out var start))
                    { TimeTrackerStorage.DeleteMarkerFile(path); continue; }

                    if (!DateTime.TryParse(marker.LastHeartbeatUtc, null,
                            System.Globalization.DateTimeStyles.RoundtripKind, out var lastBeat))
                    { TimeTrackerStorage.DeleteMarkerFile(path); continue; }

                    // Younger than the threshold could genuinely be a second,
                    // still-running Revit instance's own live session -- leave
                    // it alone rather than risk stealing its in-progress time.
                    if (now - lastBeat < STALE_THRESHOLD) continue;

                    var duration = (lastBeat - start).TotalSeconds;
                    if (duration >= MIN_SESSION_SECONDS)
                    {
                        TimeTrackerStorage.Append(marker.ProjectId, new TimeSessionEntry
                        {
                            StartUtc  = start,
                            EndUtc    = lastBeat, // best available estimate: the last moment this machine confirmed the session was still open
                            User      = marker.User,
                            Recovered = true,
                        });
                    }
                    TimeTrackerStorage.DeleteMarkerFile(path);
                }
            }
            catch { }
        }
    }
}

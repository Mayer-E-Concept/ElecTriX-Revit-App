// TimeTrackerStorage.cs -- ME-Tools | Time Tracker shared storage
// Mayer E-Concept SRL
//
// Reuses METools.Comments.CommentsStorage for the same two things
// ActivityLogStorage already reuses it for:
//   - GetSharedFolder(): the network folder every team member points at.
//   - GetOrCreateProjectId(doc): a GUID stamped into the model via
//     Extensible Storage, so sessions stay matched to the right project
//     even if the .rvt file gets renamed or moved.
//
// Two separate stores here, on purpose:
//   1. The shared, per-project JSON-Lines log of FINISHED sessions -- exactly
//      ActivityLogStorage's own format/reasoning, reused directly.
//   2. A local (%APPDATA%), per-machine marker for whatever session is
//      currently IN PROGRESS on this machine -- see ActiveSessionMarker.
//      This one is never shared; it exists purely so a crash on this
//      machine doesn't erase this machine's own not-yet-finalized time.
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading;
using Autodesk.Revit.DB;

namespace METools.TimeTracker
{
    public static class TimeTrackerStorage
    {
        private static string GetFilePath(string projectId)
        {
            var folder = METools.Comments.CommentsStorage.GetSharedFolder();
            if (string.IsNullOrWhiteSpace(folder) || string.IsNullOrWhiteSpace(projectId)) return null;
            return Path.Combine(folder, $"METools_TimeTracker_{projectId}.jsonl");
        }

        // Same per-document session cache trick as ActivityLogStorage.GetProjectId
        // -- GetOrCreateProjectId() re-reads Extensible Storage every call, and
        // this is read at least once per DocumentOpened plus once per heartbeat,
        // so the dictionary lookup is worth it. Reference-keyed on Document, so
        // it's automatically irrelevant once that Document object is gone.
        private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Document, string> _projectIdCache
            = new System.Runtime.CompilerServices.ConditionalWeakTable<Document, string>();

        // Write-capable: mints and stamps a new id if this document has
        // never been stamped yet. Only safe from a context that actually
        // permits starting a Transaction -- used by ActivityLogCommand.Open,
        // which runs from an IExternalCommand.Execute() context.
        public static string GetProjectId(Document doc)
        {
            if (doc == null) return null;
            if (_projectIdCache.TryGetValue(doc, out var cached)) return cached;

            var id = METools.Comments.CommentsStorage.GetOrCreateProjectId(doc);
            if (!string.IsNullOrWhiteSpace(id))
            {
                try { _projectIdCache.Add(doc, id); } catch { }
            }
            return id;
        }

        // Read-only twin, for contexts that can't start a Transaction --
        // specifically TimeTrackerWatcher.OnDocumentOpened. DocumentOpened
        // already has an implicit transaction of Revit's own open for the
        // duration of the event, and starting another one explicitly (which
        // is exactly what GetOrCreateProjectId's Transaction.Start() does)
        // is documented as prohibited there too, not just in DocumentChanged
        // -- same InvalidOperationException risk, same silent-swallow, same
        // "freshly minted id never actually gets persisted" consequence on
        // a project that's never been stamped before. See the identical fix
        // on ActivityLogStorage.TryGetCachedOrExistingProjectId for the full
        // reasoning; this is the same fix for the same root cause.
        //
        // Returns null (never caches null) if this document hasn't been
        // stamped yet -- the caller should just skip that one action. The id
        // gets stamped for real the moment anyone opens Activity Log, Time
        // Tracker's own Command.Open, Comments, or a ViewActivated check.
        public static string TryGetCachedOrExistingProjectId(Document doc)
        {
            if (doc == null) return null;
            if (_projectIdCache.TryGetValue(doc, out var cached)) return cached;

            var id = METools.Comments.CommentsStorage.TryGetExistingProjectId(doc);
            if (!string.IsNullOrWhiteSpace(id))
            {
                try { _projectIdCache.Add(doc, id); } catch { }
            }
            return id; // null if not stamped yet -- deliberately not cached
        }

        // ── Shared, per-project session log ────────────────────────────────

        // Appends one finished session. Safe to call often -- failures (folder
        // not configured, momentarily locked file, network hiccup) are
        // swallowed by design: a missed log line should never interrupt
        // someone's actual modeling work, and this is always called from a
        // background thread already (see TimeTrackerWatcher).
        public static void Append(string projectId, TimeSessionEntry entry)
        {
            var path = GetFilePath(projectId);
            if (path == null) return;

            var line = new TimeTrackerFileLine
            {
                StartUtc  = entry.StartUtc.ToString("O"),
                EndUtc    = entry.EndUtc.ToString("O"),
                User      = entry.User,
                Recovered = entry.Recovered,
            };
            string json;
            try { json = JsonSerializer.Serialize(line); }
            catch { return; }

            for (int attempt = 0; attempt < 3; attempt++)
            {
                try
                {
                    var dir = Path.GetDirectoryName(path);
                    if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                    File.AppendAllText(path, json + Environment.NewLine);
                    return;
                }
                catch (IOException) { Thread.Sleep(100); } // another teammate's write in flight
                catch { return; } // any other failure: not worth retrying, never worth surfacing mid-session
            }
        }

        // Loads every readable session for this project. Malformed individual
        // lines are skipped, not treated as a reason to discard the rest of
        // the log -- same reasoning as ActivityLogStorage.LoadAll.
        public static List<TimeSessionEntry> LoadAll(string projectId, out string warning)
        {
            warning = null;
            var result = new List<TimeSessionEntry>();
            var path = GetFilePath(projectId);
            if (path == null || !File.Exists(path)) return result;

            string[] lines;
            try { lines = File.ReadAllLines(path); }
            catch (Exception ex)
            {
                warning = "Time Tracker file could not be read (" + ex.Message + ").";
                return result;
            }

            int skipped = 0;
            foreach (var raw in lines)
            {
                if (string.IsNullOrWhiteSpace(raw)) continue;
                try
                {
                    var l = JsonSerializer.Deserialize<TimeTrackerFileLine>(raw);
                    if (l == null) { skipped++; continue; }
                    if (!DateTime.TryParse(l.StartUtc, null, System.Globalization.DateTimeStyles.RoundtripKind, out var start))
                    { skipped++; continue; }
                    if (!DateTime.TryParse(l.EndUtc, null, System.Globalization.DateTimeStyles.RoundtripKind, out var end))
                    { skipped++; continue; }

                    result.Add(new TimeSessionEntry
                    {
                        StartUtc  = start,
                        EndUtc    = end,
                        User      = l.User ?? "",
                        Recovered = l.Recovered,
                    });
                }
                catch { skipped++; }
            }

            if (skipped > 0)
                warning = $"{skipped} session line(s) could not be read and were skipped.";

            return result;
        }

        // ── Local, per-machine "session in progress" marker ─────────────────

        private static string MarkersDir =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "METools", "TimeTracker");

        private static string MarkerPath(string projectId, string sessionId) =>
            Path.Combine(MarkersDir, $"active_{projectId}_{sessionId}.json");

        internal static void WriteMarker(string projectId, string sessionId, ActiveSessionMarker marker)
        {
            try
            {
                Directory.CreateDirectory(MarkersDir);
                var json = JsonSerializer.Serialize(marker);
                File.WriteAllText(MarkerPath(projectId, sessionId), json);
            }
            catch { } // best-effort -- a missed heartbeat write just narrows the crash-recovery window slightly
        }

        public static void DeleteMarker(string projectId, string sessionId)
        {
            try
            {
                var path = MarkerPath(projectId, sessionId);
                if (File.Exists(path)) File.Delete(path);
            }
            catch { }
        }

        // All marker files currently sitting in the local folder, regardless
        // of project. Used once at Revit startup to look for sessions a
        // previous, crashed Revit process never got to finalize. Returns the
        // marker plus its own file path so the caller can delete it once
        // handled.
        internal static List<(string Path, ActiveSessionMarker Marker)> ListAllMarkers()
        {
            var result = new List<(string, ActiveSessionMarker)>();
            try
            {
                if (!Directory.Exists(MarkersDir)) return result;
                foreach (var file in Directory.GetFiles(MarkersDir, "active_*.json"))
                {
                    try
                    {
                        var json = File.ReadAllText(file);
                        var marker = JsonSerializer.Deserialize<ActiveSessionMarker>(json);
                        if (marker != null) result.Add((file, marker));
                    }
                    catch { } // one unreadable marker shouldn't block recovering the rest
                }
            }
            catch { }
            return result;
        }

        public static void DeleteMarkerFile(string path)
        {
            try { if (File.Exists(path)) File.Delete(path); } catch { }
        }
    }
}

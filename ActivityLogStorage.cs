// ActivityLogStorage.cs -- ME-Tools | Activity Log shared storage
// Mayer E-Concept SRL
//
// Reuses METools.Comments.CommentsStorage for two things Comments already
// solved well, so this doesn't need its own copy or its own settings prompt:
//   - GetSharedFolder(): the network folder every team member points at.
//   - GetOrCreateProjectId(doc): a GUID stamped into the model via
//     Extensible Storage, so log entries stay matched to the right project
//     even if the .rvt file gets renamed or moved.
//
// Format is JSON Lines (one JSON object per line), not a single JSON array,
// specifically because this file is write-heavy (every Added/Modified/
// Deleted element from every teammate, every session) and read-rarely (only
// when someone is actually investigating). Appending a line is cheap and
// doesn't require reading the whole file first; a single corrupted line
// (e.g. two people's writes interleaving at exactly the same instant) is
// just skipped on read instead of invalidating the entire log the way one
// bad byte would in a single JSON array.
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading;
using Autodesk.Revit.DB;

namespace METools.ActivityLog
{
    public static class ActivityLogStorage
    {
        private static string GetFilePath(string projectId)
        {
            var folder = METools.Comments.CommentsStorage.GetSharedFolder();
            if (string.IsNullOrWhiteSpace(folder) || string.IsNullOrWhiteSpace(projectId)) return null;
            return Path.Combine(folder, $"METools_ActivityLog_{projectId}.jsonl");
        }

        public static string GetProjectId(Document doc) => METools.Comments.CommentsStorage.GetOrCreateProjectId(doc);

        // Appends one entry. Safe to call often -- failures (folder not
        // configured, momentarily locked file, network hiccup) are swallowed
        // by design: a missed log line should never interrupt someone's
        // actual modeling work.
        public static void Append(string projectId, ActivityLogEntry entry)
        {
            var path = GetFilePath(projectId);
            if (path == null) return;

            var line = new ActivityLogFileLine
            {
                TimestampUtc     = entry.TimestampUtc.ToString("O"),
                User             = entry.User,
                Action           = entry.Action.ToString(),
                Category         = entry.Category,
                FamilyName       = entry.FamilyName,
                TypeName         = entry.TypeName,
                LevelName        = entry.LevelName,
                LevelId          = entry.LevelId,
                ElementId        = entry.ElementId,
                TransactionNames = entry.TransactionNames,
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
                catch { return; } // any other failure: not worth retrying, and never worth surfacing to the modeler
            }
        }

        // Loads every readable entry for this project. Malformed individual
        // lines are skipped, not treated as a reason to discard the rest of
        // the log.
        public static List<ActivityLogEntry> LoadAll(string projectId, out string warning)
        {
            warning = null;
            var result = new List<ActivityLogEntry>();
            var path = GetFilePath(projectId);
            if (path == null || !File.Exists(path)) return result;

            string[] lines;
            try { lines = File.ReadAllLines(path); }
            catch (Exception ex)
            {
                warning = "Activity log file could not be read (" + ex.Message + ").";
                return result;
            }

            int skipped = 0;
            foreach (var raw in lines)
            {
                if (string.IsNullOrWhiteSpace(raw)) continue;
                try
                {
                    var l = JsonSerializer.Deserialize<ActivityLogFileLine>(raw);
                    if (l == null) { skipped++; continue; }
                    if (!Enum.TryParse<ActivityAction>(l.Action, out var action)) { skipped++; continue; }
                    if (!DateTime.TryParse(l.TimestampUtc, null,
                            System.Globalization.DateTimeStyles.RoundtripKind, out var ts))
                    { skipped++; continue; }

                    result.Add(new ActivityLogEntry
                    {
                        TimestampUtc     = ts,
                        User             = l.User ?? "",
                        Action           = action,
                        Category         = l.Category ?? "",
                        FamilyName       = l.FamilyName ?? "",
                        TypeName         = l.TypeName ?? "",
                        LevelName        = l.LevelName ?? "",
                        LevelId          = l.LevelId ?? "",
                        ElementId        = l.ElementId ?? "",
                        TransactionNames = l.TransactionNames ?? "",
                    });
                }
                catch { skipped++; }
            }

            if (skipped > 0)
                warning = $"{skipped} log line(s) could not be read and were skipped.";

            return result;
        }
    }
}

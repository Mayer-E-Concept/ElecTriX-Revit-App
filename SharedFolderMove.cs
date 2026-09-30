// SharedFolderMove.cs -- ME-Tools | Following a moved shared folder
// Mayer E-Concept SRL
//
// The shared folder (Comments, Tasks, Activity log, Time tracker, project
// registry...) is configured once per PC in %APPDATA%\METools\comments-settings.json.
// When the office moves it, the old folder keeps only a small marker file,
// _FOLDER_MOVED.json = { "NewPath": "\\Database\...\08_Aplicatii\METools_Comments" }.
//
//   * At Revit start (before any watcher touches the folder) the configured
//     path is checked; if it carries the marker, the new path is saved into
//     this PC's settings and a message says so once Revit has started.
//   * Files an older, not yet updated add-in (or MailBridge / Nexus) wrote into
//     the old folder after the move are merged into the new one: new files
//     are moved over, lists (tasks, comments) are merged by Id, activity logs
//     (.jsonl) are appended.
//   * GetSharedFolder() also follows the marker at runtime, so a move during
//     a session, or a settings file that couldn't be written, still works.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using Autodesk.Revit.UI;

namespace METools
{
    public static class SharedFolderMove
    {
        public const string MarkerName = "_FOLDER_MOVED.json";

        private class Marker
        {
            public string NewPath { get; set; } = "";
        }

        private static readonly Dictionary<string, (string Resolved, DateTime At)> _cache =
            new Dictionary<string, (string, DateTime)>(StringComparer.OrdinalIgnoreCase);
        private static readonly object _lock = new object();

        // Where the folder is now: follows markers (at most 3 hops, in case it moved twice).
        public static string Resolve(string folder)
        {
            var current = folder;
            for (int hop = 0; hop < 3 && !string.IsNullOrWhiteSpace(current); hop++)
            {
                string next = null;
                try
                {
                    var marker = Path.Combine(current, MarkerName);
                    if (File.Exists(marker))
                        next = JsonSerializer.Deserialize<Marker>(File.ReadAllText(marker))?.NewPath;
                }
                catch { /* unreadable marker or share offline -- stay where we are */ }
                if (string.IsNullOrWhiteSpace(next) || string.Equals(next, current, StringComparison.OrdinalIgnoreCase)) break;
                current = next;
            }
            return current;
        }

        // Same, cached for a minute -- GetSharedFolder() runs on every model change.
        public static string ResolveCached(string folder)
        {
            if (string.IsNullOrWhiteSpace(folder)) return folder;
            lock (_lock)
            {
                if (_cache.TryGetValue(folder, out var hit) && (DateTime.UtcNow - hit.At).TotalSeconds < 60)
                    return hit.Resolved;
            }
            var resolved = Resolve(folder);
            lock (_lock) { _cache[folder] = (resolved, DateTime.UtcNow); }
            return resolved;
        }

        // From App.OnStartup, before the watchers register.
        public static void CheckAtStartup(UIControlledApplication app)
        {
            string configured, resolved;
            int merged = 0;
            try
            {
                configured = METools.Comments.CommentsStorage.GetConfiguredSharedFolder();
                if (string.IsNullOrWhiteSpace(configured)) return;
                resolved = Resolve(configured);
                if (string.Equals(configured, resolved, StringComparison.OrdinalIgnoreCase)) return;
                METools.Comments.CommentsStorage.SetSharedFolder(resolved);
                merged = MergeLateFiles(configured, resolved);
                Log($"Shared folder moved: {configured} -> {resolved}, {merged} late file(s) merged");
            }
            catch (Exception ex)
            {
                Log("Folder move check failed: " + ex.Message);
                return;
            }

            app.ControlledApplication.ApplicationInitialized += (s, e) =>
            {
                try
                {
                    var td = new TaskDialog("ME-Tools")
                    {
                        MainInstruction = S._("folder_moved.title"),
                        MainContent = string.Format(S._("folder_moved.body_fmt"), configured, resolved) +
                                      (merged > 0 ? "\n\n" + string.Format(S._("folder_moved.merged_fmt"), merged) : ""),
                        CommonButtons = TaskDialogCommonButtons.Ok,
                    };
                    td.Show();
                }
                catch { /* the switch itself already happened */ }
            };
        }

        // Moves/merges whatever sits in the old folder besides the marker.
        public static int MergeLateFiles(string oldDir, string newDir)
        {
            int count = 0;
            if (!Directory.Exists(oldDir) || !Directory.Exists(newDir)) return 0;
            foreach (var file in Directory.GetFiles(oldDir))
            {
                var name = Path.GetFileName(file);
                if (name.Equals(MarkerName, StringComparison.OrdinalIgnoreCase) || name.StartsWith("_README", StringComparison.OrdinalIgnoreCase)) continue;
                var target = Path.Combine(newDir, name);
                try
                {
                    if (!File.Exists(target)) File.Move(file, target);
                    else if (name.EndsWith(".jsonl", StringComparison.OrdinalIgnoreCase))
                    {
                        File.AppendAllText(target, File.ReadAllText(file));
                        File.Delete(file);
                    }
                    else if (name.EndsWith(".json", StringComparison.OrdinalIgnoreCase) && MergeJsonLists(file, target)) File.Delete(file);
                    else continue; // unknown kind that exists in both places -- leave the old copy alone
                    count++;
                }
                catch (Exception ex)
                {
                    Log($"Could not merge {name}: {ex.Message}");
                }
            }
            return count;
        }

        // Union of the record lists in both files by Id; the new folder's version wins on the same Id.
        private static bool MergeJsonLists(string fromFile, string intoFile)
        {
            var from = JsonNode.Parse(File.ReadAllText(fromFile));
            var into = JsonNode.Parse(File.ReadAllText(intoFile));
            var a = ListOf(from);
            var b = ListOf(into);
            if (a == null || b == null) return false;
            var ids = new HashSet<string>(b.Select(IdOf).Where(x => x != null), StringComparer.OrdinalIgnoreCase);
            foreach (var item in a.ToList())
            {
                var id = IdOf(item);
                if (id == null || ids.Contains(id)) continue;
                b.Add(item?.DeepClone());
                ids.Add(id);
            }
            File.WriteAllText(intoFile, into.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
            return true;
        }

        private static JsonArray ListOf(JsonNode node) =>
            node as JsonArray ?? (node as JsonObject)?.Select(kv => kv.Value).OfType<JsonArray>().FirstOrDefault();

        private static string IdOf(JsonNode item)
        {
            if (!(item is JsonObject o)) return null;
            var v = o["Id"] ?? o["id"] ?? o["ProjectId"];
            return v?.ToString();
        }

        private static void Log(string line)
        {
            try
            {
                var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "METools");
                Directory.CreateDirectory(dir);
                File.AppendAllText(Path.Combine(dir, "folder-move.log"), $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {line}{Environment.NewLine}");
            }
            catch { }
        }
    }
}

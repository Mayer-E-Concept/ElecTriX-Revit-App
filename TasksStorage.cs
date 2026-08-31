// TasksStorage.cs -- ME-Tools | Project Tasks shared storage
// Mayer E-Concept SRL
//
// Reuses CommentsStorage's shared folder setting and per-project GUID
// identity as-is -- Stefan doesn't configure a second path for Tasks, and
// a project's task file always sits right next to that project's
// comments file. Mail bridge writes into the exact same file format from
// a separate standalone service -- see TasksModels.cs's header.
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading;
using METools.Comments;

namespace METools.Tasks
{
    public enum ClaimResult { Success, AlreadyClaimed, NotFound, StorageError }

    public static class TasksStorage
    {
        private static string GetFilePath(string projectId)
        {
            var folder = CommentsStorage.GetSharedFolder();
            if (string.IsNullOrWhiteSpace(folder) || string.IsNullOrWhiteSpace(projectId)) return null;
            return Path.Combine(folder, $"METools_Tasks_{projectId}.json");
        }

        private class TasksFileWrapper
        {
            public List<ProjectTask> Tasks { get; set; } = new List<ProjectTask>();
        }

        // Same two-case distinction as CommentsStorage.TryReadRaw: a
        // missing/empty file means "nothing yet"; a file that exists but
        // won't parse is left completely alone so a network hiccup can't
        // wipe out tasks the mail bridge (or another machine) already
        // wrote.
        private static bool TryReadRaw(string path, out List<ProjectTask> list, out string parseError)
        {
            list = new List<ProjectTask>();
            parseError = null;
            if (path == null || !File.Exists(path)) return true;

            for (int attempt = 0; attempt < 3; attempt++)
            {
                try
                {
                    var json = File.ReadAllText(path);
                    if (string.IsNullOrWhiteSpace(json)) return true;
                    var file = JsonSerializer.Deserialize<TasksFileWrapper>(json);
                    list = file?.Tasks ?? new List<ProjectTask>();
                    return true;
                }
                catch (IOException)
                {
                    Thread.Sleep(150); // likely the mail bridge or another machine writing right now
                }
                catch (Exception ex)
                {
                    parseError = ex.Message;
                    return false;
                }
            }

            parseError = "File was locked/busy after several attempts.";
            return false;
        }

        public static List<ProjectTask> LoadAll(string projectId) => LoadAll(projectId, out _);

        public static List<ProjectTask> LoadAll(string projectId, out string warning)
        {
            warning = null;
            var path = GetFilePath(projectId);
            if (!TryReadRaw(path, out var list, out string parseError))
                warning = $"Tasks file could not be read ({parseError}). Showing none for now -- " +
                          "existing data on the shared drive has not been touched.";
            return list;
        }

        public static bool Mutate(string projectId, Action<List<ProjectTask>> mutation, out string error)
        {
            error = "";
            var folder = CommentsStorage.GetSharedFolder();
            if (string.IsNullOrWhiteSpace(folder))
            {
                error = "No shared folder configured yet (set it up in Comments settings).";
                return false;
            }
            if (string.IsNullOrWhiteSpace(projectId))
            {
                error = "Could not identify this project.";
                return false;
            }

            try { Directory.CreateDirectory(folder); }
            catch (Exception ex) { error = "Shared folder not reachable: " + ex.Message; return false; }

            var path = GetFilePath(projectId);

            for (int attempt = 0; attempt < 5; attempt++)
            {
                try
                {
                    if (!TryReadRaw(path, out var list, out string parseError))
                    {
                        error = $"Shared tasks file appears corrupted ({parseError}). Nothing was changed -- " +
                                "check the file on the shared drive directly before trying again.";
                        return false;
                    }

                    mutation(list);

                    var json = JsonSerializer.Serialize(new TasksFileWrapper { Tasks = list },
                        new JsonSerializerOptions { WriteIndented = true });
                    File.WriteAllText(path, json);
                    return true;
                }
                catch (IOException)
                {
                    Thread.Sleep(200);
                }
                catch (Exception ex)
                {
                    error = ex.Message;
                    return false;
                }
            }

            error = "Shared tasks file was busy after several attempts -- try again.";
            return false;
        }

        // Reloads fresh (same as any Mutate call) right before deciding,
        // so two people clicking "assign to me" within the same write
        // cycle can't both silently win -- whichever write actually lands
        // last is what's on disk, and this returns AlreadyClaimed for
        // whichever caller loses that race. Like Comments, this still
        // relies on plain file writes rather than a real database
        // transaction, so a true dead-heat (both reads landing before
        // either write) remains a narrow, low-stakes edge case -- the
        // same trade-off Comments already accepts, not a new one.
        public static ClaimResult TryClaim(string projectId, string taskId, string currentUser, out string claimedBy, out string error)
        {
            string resultClaimedBy = null;
            bool found = false;
            bool alreadyClaimed = false;

            bool ok = Mutate(projectId, list =>
            {
                var task = list.Find(t => t.Id == taskId);
                if (task == null) return;
                found = true;

                if (!string.IsNullOrWhiteSpace(task.AssignedTo))
                {
                    alreadyClaimed = true;
                    resultClaimedBy = task.AssignedTo;
                    return;
                }

                task.AssignedTo = currentUser;
                task.AssignedAtUtc = DateTime.UtcNow;
                task.Status = "assigned";
                resultClaimedBy = currentUser;
            }, out error);

            if (!ok) { claimedBy = null; return ClaimResult.StorageError; }
            if (!found) { claimedBy = null; return ClaimResult.NotFound; }

            claimedBy = resultClaimedBy;
            return alreadyClaimed ? ClaimResult.AlreadyClaimed : ClaimResult.Success;
        }

        public static bool MarkDone(string projectId, string taskId, out string error)
        {
            bool found = false;
            bool ok = Mutate(projectId, list =>
            {
                var task = list.Find(t => t.Id == taskId);
                if (task == null) return;
                found = true;
                task.Status = "done";
            }, out error);

            if (ok && !found) error = "Task not found -- it may have been removed since this list was loaded.";
            return ok && found;
        }
    }
}

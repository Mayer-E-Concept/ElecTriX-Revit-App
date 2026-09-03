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
    public enum ClaimResult { Success, AlreadyClaimed, NotYours, NotFound, StorageError }

    public static class TasksStorage
    {
        // Same normalization scheme as MailBridge's ProjectRegistry.Resolve
        // -- case, underscores, hyphens, and extra spaces all collapsed to
        // single spaces -- kept in sync by hand across the two separate
        // .NET projects, same as the model shapes themselves. Public and
        // shared here so TasksWindow and TasksHandler use the exact same
        // logic rather than each keeping their own copy.
        public static string Normalize(string s) =>
            System.Text.RegularExpressions.Regex.Replace((s ?? "").ToLowerInvariant(), @"[_\-\s]+", " ").Trim();

        private const string TasksFilePrefix = "METools_Tasks_";
        private const string TasksFileSuffix = ".json";
        private const string RegistryFileName = "METools_ProjectRegistry.json";

        private static string GetFilePath(string projectId)
        {
            var folder = CommentsStorage.GetSharedFolder();
            if (string.IsNullOrWhiteSpace(folder) || string.IsNullOrWhiteSpace(projectId)) return null;
            return Path.Combine(folder, $"{TasksFilePrefix}{projectId}{TasksFileSuffix}");
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

        // Scans the shared folder for every project's task file at once --
        // each ProjectTask already carries its own ProjectId (set when the
        // task was created), so there's no need to parse it back out of
        // the filename. One unreadable file is skipped and reported, not
        // allowed to hide every other project's tasks.
        public static List<ProjectTask> LoadAllAcrossProjects(out string warning)
        {
            warning = null;
            var result = new List<ProjectTask>();

            var folder = CommentsStorage.GetSharedFolder();
            if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
                return result;

            string[] files;
            try
            {
                files = Directory.GetFiles(folder, $"{TasksFilePrefix}*{TasksFileSuffix}");
            }
            catch (Exception ex)
            {
                warning = $"Could not list tasks files in the shared folder: {ex.Message}";
                return result;
            }

            var errors = new List<string>();
            foreach (var path in files)
            {
                if (!TryReadRaw(path, out var list, out string parseError))
                {
                    errors.Add($"{Path.GetFileName(path)} ({parseError})");
                    continue;
                }
                result.AddRange(list);
            }

            if (errors.Count > 0)
                warning = "Some task files could not be read and were skipped: " + string.Join("; ", errors);

            return result;
        }

        // Read-only history note: this used to be read-only from the Revit
        // side, with the mail bridge as sole writer. RegisterProject below
        // changes that -- both sides can write here now, using the same
        // read-modify-write-with-retry safety the task files already rely
        // on, since coordinating multiple writers through a plain JSON
        // file is already an accepted pattern in this codebase.
        //
        // A missing or unreadable registry just means tasks show their
        // raw project id instead of a friendly name, and move-suggestions
        // never trigger -- never fatal.
        public static List<ProjectRegistryEntry> LoadProjectRegistry()
        {
            var result = new List<ProjectRegistryEntry>();
            var folder = CommentsStorage.GetSharedFolder();
            if (string.IsNullOrWhiteSpace(folder)) return result;

            var path = Path.Combine(folder, RegistryFileName);
            if (!File.Exists(path)) return result;

            try
            {
                var json = File.ReadAllText(path);
                var entries = JsonSerializer.Deserialize<List<ProjectRegistryEntry>>(json);
                if (entries != null) result = entries;
            }
            catch
            {
                // Ignore -- see method comment.
            }

            return result;
        }

        public static Dictionary<string, string> LoadProjectDisplayNames()
        {
            var result = new Dictionary<string, string>();
            foreach (var entry in LoadProjectRegistry())
            {
                if (!string.IsNullOrWhiteSpace(entry.ProjectId) && !string.IsNullOrWhiteSpace(entry.DisplayName))
                    result[entry.ProjectId] = entry.DisplayName;
            }
            return result;
        }

        // Adds or updates one project's registry entry -- if this
        // projectId is already registered, its DisplayName/Keywords are
        // refreshed rather than duplicated. A corrupted existing file is
        // left untouched and reported, never silently replaced with an
        // empty one -- the same rule Mutate() already follows for task
        // files.
        public static bool RegisterProject(string projectId, string displayName, List<string> keywords,
            List<string> customerDomains, out string error)
        {
            error = "";
            var folder = CommentsStorage.GetSharedFolder();
            if (string.IsNullOrWhiteSpace(folder))
            {
                error = "No shared folder configured yet (set it up in Comments settings).";
                return false;
            }

            try { Directory.CreateDirectory(folder); }
            catch (Exception ex) { error = "Shared folder not reachable: " + ex.Message; return false; }

            var path = Path.Combine(folder, RegistryFileName);

            for (int attempt = 0; attempt < 5; attempt++)
            {
                try
                {
                    List<ProjectRegistryEntry> list;
                    if (!File.Exists(path))
                    {
                        list = new List<ProjectRegistryEntry>();
                    }
                    else
                    {
                        var existingJson = File.ReadAllText(path);
                        if (string.IsNullOrWhiteSpace(existingJson))
                        {
                            list = new List<ProjectRegistryEntry>();
                        }
                        else
                        {
                            try
                            {
                                list = JsonSerializer.Deserialize<List<ProjectRegistryEntry>>(existingJson)
                                       ?? new List<ProjectRegistryEntry>();
                            }
                            catch (JsonException ex)
                            {
                                error = $"Registry file appears corrupted ({ex.Message}). Nothing was changed.";
                                return false;
                            }
                        }
                    }

                    var existing = list.Find(e => e.ProjectId == projectId);
                    if (existing != null)
                    {
                        existing.DisplayName = displayName;
                        existing.Keywords = keywords;
                        if (customerDomains != null) existing.CustomerDomains = customerDomains;
                    }
                    else
                    {
                        list.Add(new ProjectRegistryEntry
                        {
                            ProjectId = projectId,
                            DisplayName = displayName,
                            Keywords = keywords,
                            CustomerDomains = customerDomains ?? new List<string>(),
                        });
                    }

                    var outJson = JsonSerializer.Serialize(list, new JsonSerializerOptions { WriteIndented = true });
                    File.WriteAllText(path, outJson);
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

            error = "Registry file was busy after several attempts -- try again.";
            return false;
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

        // The inverse of TryClaim -- only the person currently holding a
        // task can let it go, so a stray click can't un-assign someone
        // else's work.
        public static ClaimResult Release(string projectId, string taskId, string currentUser, out string error)
        {
            bool found = false;
            bool notMine = false;

            bool ok = Mutate(projectId, list =>
            {
                var task = list.Find(t => t.Id == taskId);
                if (task == null) return;
                found = true;

                if (!string.Equals(task.AssignedTo, currentUser, StringComparison.OrdinalIgnoreCase))
                {
                    notMine = true;
                    return;
                }

                task.AssignedTo = null;
                task.AssignedAtUtc = null;
                task.Status = "unassigned";
            }, out error);

            if (!ok) return ClaimResult.StorageError;
            if (!found) return ClaimResult.NotFound;
            if (notMine) return ClaimResult.NotYours;
            return ClaimResult.Success;
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

        public static bool DeleteTask(string projectId, string taskId, out string error)
        {
            bool found = false;
            bool ok = Mutate(projectId, list => { found = list.RemoveAll(t => t.Id == taskId) > 0; }, out error);

            if (ok && !found) error = "Task not found -- it may have already been removed.";
            return ok && found;
        }

        // Moves one task from one project's file to another's -- the
        // suggestion flow's confirm button (see TasksWindow.
        // FindSuggestedProject) is what calls this today, always moving
        // out of "unassigned", but nothing here assumes that.
        //
        // Order matters for safety: the destination is written FIRST. If
        // the source removal that follows then fails for any reason, the
        // task exists in both files -- a visible, recoverable duplicate --
        // rather than disappearing from both.
        public static bool MoveTaskToProject(string fromProjectId, string toProjectId, string taskId, out string error)
        {
            error = "";
            if (string.Equals(fromProjectId, toProjectId, StringComparison.OrdinalIgnoreCase))
            {
                error = "Task is already filed under that project.";
                return false;
            }

            var sourceList = LoadAll(fromProjectId, out var loadWarning);
            var task = sourceList.Find(t => t.Id == taskId);
            if (task == null)
            {
                error = string.IsNullOrWhiteSpace(loadWarning)
                    ? "Task not found in the source project's file."
                    : $"Could not read the source project's file: {loadWarning}";
                return false;
            }

            var addedOk = Mutate(toProjectId, list =>
            {
                if (list.Exists(t => t.Id == taskId)) return; // already moved -- a retried click, not an error
                list.Add(CloneWithNewProject(task, toProjectId));
            }, out error);
            if (!addedOk) return false;

            var removedOk = Mutate(fromProjectId, list => list.RemoveAll(t => t.Id == taskId), out var removeError);
            if (!removedOk)
            {
                error = $"Moved, but could not remove the original from '{fromProjectId}': {removeError}. " +
                        "It may show up twice until that's cleaned up by hand.";
                return false;
            }

            return true;
        }

        private static ProjectTask CloneWithNewProject(ProjectTask source, string newProjectId) => new ProjectTask
        {
            Id = source.Id,
            ProjectId = newProjectId,
            Source = source.Source,
            SenderName = source.SenderName,
            SenderEmail = source.SenderEmail,
            ReceivedAtUtc = source.ReceivedAtUtc,
            SourceLanguage = source.SourceLanguage,
            OriginalSubject = source.OriginalSubject,
            OriginalBody = source.OriginalBody,
            TranslatedSubject = source.TranslatedSubject,
            TranslatedBody = source.TranslatedBody,
            Summary = source.Summary,
            ClassificationReason = source.ClassificationReason,
            Category = source.Category,
            Urgency = source.Urgency,
            ProjectGuessConfidence = source.ProjectGuessConfidence,
            ProjectGuessRaw = source.ProjectGuessRaw,
            RoutingMethod = "manual-confirm", // this project came from a human confirming the AI's guess, not domain/keyword auto-match
            AttachmentPaths = source.AttachmentPaths,
            BlockedAttachments = source.BlockedAttachments,
            Status = source.Status,
            AssignedTo = source.AssignedTo,
            AssignedAtUtc = source.AssignedAtUtc,
            SourceMessageId = source.SourceMessageId,
            ReferencedElementId = source.ReferencedElementId,
            ReferencedSummary = source.ReferencedSummary,
        };
    }
}

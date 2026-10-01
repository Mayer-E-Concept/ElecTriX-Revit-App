// TaskAssignments.cs -- ME-Tools | Workboard request hand-overs
// Mayer E-Concept SRL
//
// Same two shared files Nexus uses (Nexus: Tools/Requests.cs), next to the
// task files in the shared METools folder -- the code is duplicated on
// purpose (Nexus is a separate app), a fix in one belongs in the other.
//
//  METools_Users.json -- which Revit user name belongs to which Windows
//    login. Written here every time Revit runs, so Nexus (which only
//    knows the Windows login when started on its own) knows who "me" is.
//
//  METools_TaskAssignments.json -- handing a request to someone else asks
//    them first: "pending" until they accept (then the request is theirs)
//    or decline (it stays as it was, the asker is told). In its own file
//    rather than on the task, because MailBridge rewrites the task files.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using METools.Comments;

namespace METools.Tasks
{
    public class UserEntry
    {
        public string WindowsLogin { get; set; } = "";
        public string RevitUser { get; set; } = "";
        public DateTime LastSeenUtc { get; set; }
        [JsonExtensionData] public Dictionary<string, JsonElement> Extra { get; set; }
    }

    public class UsersFile
    {
        public List<UserEntry> Users { get; set; } = new List<UserEntry>();
        [JsonExtensionData] public Dictionary<string, JsonElement> Extra { get; set; }
    }

    public class AssignmentRequest
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N");
        public string ProjectId { get; set; } = "";
        public string TaskId { get; set; } = "";
        public string Subject { get; set; } = "";
        public string To { get; set; } = "";
        public string By { get; set; } = "";
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
        public string State { get; set; } = "pending"; // pending | accepted | declined | cancelled
        public string Reason { get; set; } = "";
        public DateTime? DecidedUtc { get; set; }
        public bool AskerSeen { get; set; }
        [JsonExtensionData] public Dictionary<string, JsonElement> Extra { get; set; }
    }

    public class AssignmentsFile
    {
        public List<AssignmentRequest> Items { get; set; } = new List<AssignmentRequest>();
        [JsonExtensionData] public Dictionary<string, JsonElement> Extra { get; set; }
    }

    public static class TaskAssignments
    {
        private static string Folder => CommentsStorage.GetSharedFolder();
        private static string UsersPath => Path.Combine(Folder ?? "", "METools_Users.json");
        private static string FilePath => Path.Combine(Folder ?? "", "METools_TaskAssignments.json");
        private const string NoFolder = "No shared folder configured yet (set it up in Comments settings).";

        private static bool Same(string a, string b) =>
            !string.IsNullOrWhiteSpace(a) && string.Equals(a.Trim(), (b ?? "").Trim(), StringComparison.OrdinalIgnoreCase);

        public static void RecordUser(string revitUser)
        {
            if (string.IsNullOrWhiteSpace(revitUser) || string.IsNullOrWhiteSpace(Folder)) return;
            var login = Environment.UserName;
            SharedJsonStorage.Mutate<UsersFile>(Folder, UsersPath, f =>
            {
                var e = f.Users.FirstOrDefault(u => Same(u.WindowsLogin, login) && Same(u.RevitUser, revitUser));
                if (e == null) f.Users.Add(e = new UserEntry { WindowsLogin = login, RevitUser = revitUser });
                e.LastSeenUtc = DateTime.UtcNow;
            }, NoFolder, out _);
        }

        public static List<AssignmentRequest> Load()
        {
            if (string.IsNullOrWhiteSpace(Folder)) return new List<AssignmentRequest>();
            SharedJsonStorage.TryReadRaw<AssignmentsFile>(FilePath, out var file, out _);
            return file?.Items ?? new List<AssignmentRequest>();
        }

        private static bool Mutate(Action<List<AssignmentRequest>> change, out string error) =>
            SharedJsonStorage.Mutate<AssignmentsFile>(Folder, FilePath, f =>
            {
                change(f.Items);
                var cutoff = DateTime.UtcNow.AddDays(-60);
                f.Items = f.Items.Where(a => a.State == "pending" || (a.DecidedUtc ?? a.CreatedUtc) > cutoff).Skip(Math.Max(0, f.Items.Count - 1000)).ToList();
            }, NoFolder, out error);

        public static Dictionary<string, AssignmentRequest> PendingByTask() =>
            Load().Where(a => a.State == "pending").GroupBy(a => a.TaskId).ToDictionary(g => g.Key, g => g.Last());

        public static List<AssignmentRequest> PendingFor(string me) =>
            Load().Where(a => a.State == "pending" && Same(a.To, me)).OrderBy(a => a.CreatedUtc).ToList();

        public static List<AssignmentRequest> OutcomesFor(string me) =>
            Load().Where(a => (a.State == "accepted" || a.State == "declined") && !a.AskerSeen && Same(a.By, me)).ToList();

        public static bool Accept(AssignmentRequest a, out string error)
        {
            bool accepted = false;
            if (!Mutate(items =>
            {
                var x = items.FirstOrDefault(i => i.Id == a.Id);
                if (x == null || x.State != "pending") return;
                x.State = "accepted"; x.DecidedUtc = DateTime.UtcNow; accepted = true;
            }, out error)) return false;
            if (!accepted) { error = S._("assignpopup.already_handled"); return false; }
            return TasksStorage.Mutate(a.ProjectId, list =>
            {
                var t = list.Find(x => x.Id == a.TaskId);
                if (t == null) return;
                t.AssignedTo = a.To;
                t.AssignedBy = a.By;
                t.AssignedAtUtc = DateTime.UtcNow;
                if (t.Status != "done") t.Status = "assigned";
            }, out error);
        }

        public static bool Decline(AssignmentRequest a, string reason, out string error)
        {
            bool declined = false;
            var ok = Mutate(items =>
            {
                var x = items.FirstOrDefault(i => i.Id == a.Id);
                if (x == null || x.State != "pending") return;
                x.State = "declined"; x.Reason = reason ?? ""; x.DecidedUtc = DateTime.UtcNow; declined = true;
            }, out error);
            if (ok && !declined) { error = S._("assignpopup.already_handled"); return false; }
            return ok;
        }

        public static void MarkSeen(IEnumerable<string> ids)
        {
            var set = new HashSet<string>(ids);
            if (set.Count == 0) return;
            Mutate(items => { foreach (var a in items.Where(a => set.Contains(a.Id))) a.AskerSeen = true; }, out _);
        }
    }
}

// TasksModels.cs -- ME-Tools | Project Tasks
// Mayer E-Concept SRL
//
// ProjectTask's first block of properties must stay byte-for-byte
// identical (same names, same types) to METools.MailBridge/Models/MailTask.cs
// -- that's a separate standalone service writing into the exact same
// METools_Tasks_{projectId}.json files this add-in reads. Status is a
// plain string ("unassigned" | "assigned" | "done"), not a C# enum like
// Comments' CommentStatus -- deliberately, since the mail bridge is a
// separate .NET project with no knowledge of any enum defined here, and
// a string keeps that cross-process contract simple. The mail bridge
// identifies a message by its IMAP UID (SourceMessageId) -- it uses plain
// IMAP rather than Microsoft Graph, so this is provider-agnostic, not a
// Graph message id.
using System;
using System.Collections.Generic;

namespace METools.Tasks
{
    public class ProjectTask
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N");
        public string ProjectId { get; set; } = "";
        public string Source { get; set; } = "email"; // "email" | "manual"
        public string SenderName { get; set; } = "";
        public string SenderEmail { get; set; } = "";
        public DateTime ReceivedAtUtc { get; set; }
        public string SourceLanguage { get; set; } = "";
        public string OriginalSubject { get; set; } = "";
        public string OriginalBody { get; set; } = "";
        public string TranslatedSubject { get; set; } = "";
        public string TranslatedBody { get; set; } = "";
        public string Summary { get; set; } = "";
        public string ClassificationReason { get; set; } = ""; // one sentence -- why the AI chose this category
        public string Category { get; set; } = "other";
        public string Urgency { get; set; } = "low";
        public double ProjectGuessConfidence { get; set; }
        public string ProjectGuessRaw { get; set; } = "";
        public string RoutingMethod { get; set; } = "unassigned";
        public List<string> AttachmentPaths { get; set; } = new List<string>();
        public List<string> BlockedAttachments { get; set; } = new List<string>();
        public string Status { get; set; } = "unassigned"; // unassigned | assigned | done
        public string AssignedTo { get; set; }
        public DateTime? AssignedAtUtc { get; set; }
        public string SourceMessageId { get; set; } = "";

        // Revit-side-only fields -- see file header.
        public string ReferencedElementId { get; set; } = "";
        public string ReferencedSummary { get; set; } = "";
    }

    // Root object of the shared JSON file -- wrapping the list, not a bare
    // array, matches CommentsFile's own reasoning: room to add file-level
    // fields later without breaking already-deployed files.
    public class TasksFile
    {
        public List<ProjectTask> Tasks { get; set; } = new List<ProjectTask>();
    }

    // One registry entry, read-only on this side -- MailBridge owns
    // writing METools_ProjectRegistry.json; this add-in only reads
    // DisplayName out of it to label tasks in the cross-project view.
    public class ProjectRegistryEntry
    {
        public string ProjectId { get; set; } = "";
        public string DisplayName { get; set; } = "";
        public List<string> CustomerDomains { get; set; } = new List<string>();
        public List<string> Keywords { get; set; } = new List<string>();
    }

    // Counts across every project's task file combined -- always computed
    // from the full set, independent of whichever tab/filter the window
    // is currently showing.
    public class TaskStats
    {
        public int Total { get; set; }
        public int Unassigned { get; set; }
        public int InProgress { get; set; } // assigned, not done, any user
        public int Done { get; set; }
    }

    // Every action from the modeless TasksWindow -- including ones that
    // never touch the Revit document, like Claim or MarkDone -- goes
    // through this single enum/request pair and TasksHandler's
    // ExternalEvent, mirroring CommentsAction/CommentsRequest exactly.
    public enum TasksAction { Refresh, Claim, Release, MarkDone, GoToElement, AttachElement, RegisterCurrentProject, MoveToProject, Delete }

    public class TasksRequest
    {
        public TasksAction Action { get; set; }
        public string TaskId { get; set; } = "";

        // Which project's METools_Tasks_{id}.json file to mutate --
        // required for Claim/Release/MarkDone/AttachElement now that the
        // window shows tasks from every project at once, not just
        // whichever one it happened to be opened from.
        public string TaskProjectId { get; set; } = "";

        // Destination project for MoveToProject -- the suggestion flow's
        // confirm button (see TasksWindow.FindSuggestedProject) is the
        // only thing that sets this today.
        public string TargetProjectId { get; set; } = "";

        public string CurrentUser { get; set; } = "";
        public string ReferencedElementId { get; set; } = "";
        public string ReferencedSummary { get; set; } = "";
    }
}

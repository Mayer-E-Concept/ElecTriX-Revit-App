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
// a string keeps that cross-process contract simple.
//
// ReferencedElementId/ReferencedSummary name-match ProjectComment's own
// fields in CommentsModels.cs on purpose, for the same "Go There" idea --
// a task can optionally be pinned to one specific element, same as a
// comment can. The mail bridge never sets these (a customer email has no
// Revit context), which is fine: unknown JSON properties are simply
// ignored on deserialize by whichever side doesn't declare them.
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
        public string GraphMessageId { get; set; } = "";

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

    // Every action from the modeless TasksWindow -- including ones that
    // never touch the Revit document, like Claim or MarkDone -- goes
    // through this single enum/request pair and TasksHandler's
    // ExternalEvent, mirroring CommentsAction/CommentsRequest exactly.
    public enum TasksAction { Refresh, Claim, MarkDone, GoToElement, AttachElement }

    public class TasksRequest
    {
        public TasksAction Action { get; set; }
        public string TaskId { get; set; } = "";
        public string CurrentUser { get; set; } = "";
        public string ReferencedElementId { get; set; } = "";
        public string ReferencedSummary { get; set; } = "";
    }
}

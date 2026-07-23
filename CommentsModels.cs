// CommentsModels.cs -- ME-Tools | Project Comments
// Mayer E-Concept SRL
using System;
using System.Collections.Generic;

namespace METools.Comments
{
    public enum CommentStatus { Open, Done, Ignored }

    // One comment. LevelName (not a raw ElementId) is deliberately the sole
    // location reference -- it's what actually travels safely through a JSON
    // file shared between different machines/documents, matches the same
    // string-based philosophy CAx_Trassenbezugsebene already uses elsewhere
    // in this project, and is human-readable in the shared file if anyone
    // ever needs to look at it directly.
    //
    // ScopeBoxName is stored alongside it because Level names alone can be
    // genuinely ambiguous -- confirmed via live model inspection that two
    // different building sections (e.g. Haus 1 / Haus 2) can each have their
    // own separate "Obergeschoss 1" floor plan view, both reporting the exact
    // same Associated Level name, distinguished only by which Scope Box each
    // view is assigned. Without this, "Go There" could jump to the wrong
    // building section's matching-named level.
    public class ProjectComment
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N");
        public string Author { get; set; } = "";
        public string LevelName { get; set; } = "";
        public string ScopeBoxName { get; set; } = "";
        public string Text { get; set; } = "";
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
        public CommentStatus Status { get; set; } = CommentStatus.Open;
        public string ResolvedBy { get; set; } = "";
        public DateTime? ResolvedUtc { get; set; }

        // Optional: a specific element this comment points at (e.g. "fix
        // these lamps" on a crowded plan). Unlike LevelName/ScopeBoxName,
        // this genuinely can't survive the element being deleted -- Go To
        // Item degrades to a clear "that element no longer exists" message
        // in that case, same as Activity Log's level navigation does.
        public string ReferencedElementId { get; set; } = "";
        public string ReferencedSummary { get; set; } = ""; // e.g. "Lighting Fixtures - 18W Ceiling Lamp", shown even if the element is later deleted

        // Optional: who this comment is for. Informational only -- everyone
        // on the team still sees the comment either way, this just makes
        // clear whose job it is. Free text (typed username), not a fixed
        // user directory, since there isn't one to validate against.
        public string AssignedTo { get; set; } = "";
    }

    // Root object of the shared JSON file -- wrapping the list (rather than
    // serializing a bare array) leaves room to add file-level fields later
    // (e.g. a format version) without breaking already-deployed files.
    public class CommentsFile
    {
        public List<ProjectComment> Comments { get; set; } = new List<ProjectComment>();
    }

    public enum CommentsAction { Refresh, Add, SetStatus, Delete, JumpToLevel, GoToElement, SetAssignedTo }

    public class CommentsRequest
    {
        public CommentsAction Action { get; set; }
        public string LevelName { get; set; } = "";
        public string ScopeBoxName { get; set; } = "";
        public string Text { get; set; } = "";
        public string CommentId { get; set; } = "";
        public CommentStatus NewStatus { get; set; }
        public string ReferencedElementId { get; set; } = "";
        public string ReferencedSummary { get; set; } = "";
        public string AssignedTo { get; set; } = "";
    }
}

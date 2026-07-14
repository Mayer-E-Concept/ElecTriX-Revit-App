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
    public class ProjectComment
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N");
        public string Author { get; set; } = "";
        public string LevelName { get; set; } = "";
        public string Text { get; set; } = "";
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
        public CommentStatus Status { get; set; } = CommentStatus.Open;
        public string ResolvedBy { get; set; } = "";
        public DateTime? ResolvedUtc { get; set; }
    }

    // Root object of the shared JSON file -- wrapping the list (rather than
    // serializing a bare array) leaves room to add file-level fields later
    // (e.g. a format version) without breaking already-deployed files.
    public class CommentsFile
    {
        public List<ProjectComment> Comments { get; set; } = new List<ProjectComment>();
    }

    public enum CommentsAction { Refresh, Add, SetStatus, JumpToLevel }

    public class CommentsRequest
    {
        public CommentsAction Action { get; set; }
        public string LevelName { get; set; } = "";
        public string Text { get; set; } = "";
        public string CommentId { get; set; } = "";
        public CommentStatus NewStatus { get; set; }
    }
}

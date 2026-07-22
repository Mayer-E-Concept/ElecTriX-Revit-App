// ActivityLogModels.cs -- ME-Tools | Activity Log
// Mayer E-Concept SRL
using System;

namespace METools.ActivityLog
{
    public enum ActivityAction { Added, Modified, Deleted }

    public class ActivityLogEntry
    {
        public DateTime TimestampUtc     { get; set; }
        public string   User             { get; set; } = "";
        public ActivityAction Action     { get; set; }
        public string   Category        { get; set; } = "";
        public string   FamilyName      { get; set; } = "";
        public string   TypeName        { get; set; } = "";
        public string   LevelName       { get; set; } = "";
        public string   LevelId         { get; set; } = ""; // ElementId as string, "" if no level resolved -- used by the "Go to Level" button
        public string   ElementId       { get; set; } = "";
        public string   TransactionNames{ get; set; } = ""; // e.g. "Delete", "ME-Tools: Circuit Tagger"

        // Local-time convenience for display; storage always keeps UTC.
        public DateTime TimestampLocal => TimestampUtc.ToLocalTime();
    }

    // One lightweight snapshot per tracked element, refreshed on every
    // Added/Modified event, so a later Deleted event (which arrives with the
    // element already gone from the model) can still report what it WAS.
    public class ElementSnapshot
    {
        public string Category   { get; set; } = "";
        public string FamilyName { get; set; } = "";
        public string TypeName   { get; set; } = "";
        public string LevelName  { get; set; } = "";
        public string LevelId    { get; set; } = "";
    }

    internal class ActivityLogFileLine
    {
        public string TimestampUtc { get; set; }
        public string User { get; set; }
        public string Action { get; set; }
        public string Category { get; set; }
        public string FamilyName { get; set; }
        public string TypeName { get; set; }
        public string LevelName { get; set; }
        public string LevelId { get; set; }
        public string ElementId { get; set; }
        public string TransactionNames { get; set; }
    }
}

// DuplicateElementModels.cs -- ME-Tools | Collision Checker: Duplicate Devices
// Mayer E-Concept SRL
using System.Collections.Generic;

namespace METools.CollisionChecker
{
    public class DuplicateElementInfo
    {
        public long ElementId { get; set; }
        public string UniqueId { get; set; } = "";
    }

    // One bucket of elements that all share the same category, family,
    // type, level, and (rounded) location -- the exact fingerprint an
    // accidental double-paste leaves behind. KeepElementId is the survivor
    // (lowest id -- see DuplicateElementDetector); DuplicateInstances is
    // everything else in the bucket, i.e. exactly what would be deleted.
    public class DuplicateGroup
    {
        public string CategoryName { get; set; } = "";
        public string FamilyName { get; set; } = "";
        public string TypeName { get; set; } = "";
        public string LevelName { get; set; } = "";
        public string LocationSummary { get; set; } = "";
        public long KeepElementId { get; set; }
        public List<DuplicateElementInfo> DuplicateInstances { get; set; } = new List<DuplicateElementInfo>();
    }

    public enum DuplicateCheckAction { Scan, DeleteDuplicates, GoToGroup }

    public class DuplicateCheckRequest
    {
        public DuplicateCheckAction Action { get; set; }

        // For DeleteDuplicates: exactly the groups from the most recent
        // Scan, re-supplied rather than re-derived from a fresh scan --
        // what gets deleted is guaranteed to be exactly what was shown and
        // confirmed on screen, not whatever a second scan happens to find.
        public List<DuplicateGroup> GroupsToDelete { get; set; } = new List<DuplicateGroup>();

        // For GoToGroup: the one group to select and zoom to -- every
        // element in it (the kept copy and all the extras), since they're
        // all sitting at the same point by definition. Seeing the whole
        // stack highlighted is the point, not just one of them.
        public DuplicateGroup TargetGroup { get; set; }
    }

    public class DuplicateScanResult
    {
        public List<DuplicateGroup> Groups { get; set; } = new List<DuplicateGroup>();
        public int TotalExtraElements { get; set; }
        public string Error { get; set; }
    }

    public class DuplicateDeleteResult
    {
        public int Deleted { get; set; }
        public string Error { get; set; }
    }
}

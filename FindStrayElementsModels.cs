// FindStrayElementsModels.cs -- ME-Tools | Find Stray Elements
// Mayer E-Concept SRL
using Autodesk.Revit.DB;

namespace METools
{
    public enum FindStrayAction { None, Scan, GoTo, Prune }

    public class StrayElementInfo
    {
        public ElementId Id;
        public ElementId ViewId;   // InvalidElementId = model-space, not tied to one view
        public string    ViewName;
        public string    Category;
        public string    TypeName;
        public double    DistanceFt;     // from that view's own median center
        public double    NormalSpreadFt; // that view's own median distance, for context in the message
        public XYZ       Center;
    }

    public class FindStrayElementsRequest
    {
        public FindStrayAction Action;
        public bool WholeModel;

        // -- GoTo only --
        public ElementId TargetViewId;
        public ElementId TargetElementId;

        // -- Prune only -- verifies a previously-cached result set (see
        // FindStrayElementsCommand's static cache) still exists, so a
        // reopened window can show "still there" results immediately
        // without a full rescan, while anything the person already fixed
        // (most commonly: deleted) silently drops off the list rather than
        // sitting there stale.
        public System.Collections.Generic.List<StrayElementInfo> ToPrune;
    }
}

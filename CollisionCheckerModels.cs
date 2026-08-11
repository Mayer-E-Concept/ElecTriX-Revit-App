// CollisionCheckerModels.cs -- ME-Tools | Collision Checker (conduits/cable trays vs walls)
// Mayer E-Concept SRL
using System;
using System.Collections.Generic;

namespace METools.CollisionChecker
{
    public enum ScanScope { WholeModel, ActiveView, CurrentSelection }

    // One place where a conduit or cable tray run passes through a wall.
    // A single run can cross several walls (a corridor wall + a shaft wall,
    // say) -- each crossing is its own CollisionInfo, not grouped under the
    // run, since each needs its own hole and its own "go to" location.
    public class CollisionInfo
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N"); // stable row key for the UI list, independent of Revit IDs
        public Autodesk.Revit.DB.ElementId ElementId { get; set; }     // the conduit/cable tray
        public Autodesk.Revit.DB.ElementId WallId    { get; set; }
        public string ElementCategory { get; set; } = "";              // "Conduits" or "Cable Trays"
        public string ElementTypeName { get; set; } = "";
        public string WallTypeName    { get; set; } = "";
        public string LevelName       { get; set; } = "";
        public Autodesk.Revit.DB.ElementId LevelId { get; set; } = Autodesk.Revit.DB.ElementId.InvalidElementId;
        public Autodesk.Revit.DB.XYZ Point { get; set; }               // exact intersection point, model coordinates

        // Filled in once a hole has been placed for this row -- lets the
        // list show "done" instead of a Place button, and is how the
        // live-follow watcher knows which hole belongs to which run.
        public Autodesk.Revit.DB.ElementId HoleInstanceId { get; set; } = Autodesk.Revit.DB.ElementId.InvalidElementId;
        public bool HasHole => HoleInstanceId != null && HoleInstanceId != Autodesk.Revit.DB.ElementId.InvalidElementId;
    }

    public enum CollisionCheckerAction { None, PlaceHoles, MoveHoles, MarkCollisions }

    public class CollisionCheckerRequest
    {
        public CollisionCheckerAction Action { get; set; } = CollisionCheckerAction.None;
        public List<CollisionInfo> Collisions { get; set; } = new List<CollisionInfo>();
        public Autodesk.Revit.DB.ElementId HoleSymbolId { get; set; } = Autodesk.Revit.DB.ElementId.InvalidElementId;

        // Used only by MoveHoles -- the watcher fills this in with whatever
        // holes need repositioning after a tracked run moved. RunId is a
        // live ElementId (resolved already, in the same document session
        // the watcher is running in); Hole/Wall are UniqueIds since those
        // are what's actually persisted in Extensible Storage.
        public List<HoleMoveInfo> HoleMoves { get; set; } = new List<HoleMoveInfo>();

        // Used only by MarkCollisions -- detail-curve marker elements from
        // a previous Scan that need deleting before drawing new ones.
        // Deleting them also needs a transaction, hence going through this
        // request rather than the window deleting them directly.
        public List<Autodesk.Revit.DB.ElementId> OldMarkerIds { get; set; } = new List<Autodesk.Revit.DB.ElementId>();
    }

    // One hole that needs to be repositioned because the run it belongs to
    // moved. Filled in by CollisionCheckerWatcher (read-only detection),
    // consumed by CollisionCheckerHandler.ExecuteMoveHoles (the actual
    // move, which needs a valid API context DocumentChanged can't provide).
    public class HoleMoveInfo
    {
        public Autodesk.Revit.DB.ElementId RunId { get; set; }
        public string HoleUniqueId { get; set; } = "";
        public string WallUniqueId { get; set; } = "";
    }

    public class PlaceHolesResult
    {
        // Which action produced this result -- PlaceHoles and MarkCollisions
        // now share this one result type and the same OnDone callback, so
        // the window needs to know which handler to run on it.
        public CollisionCheckerAction ResultAction { get; set; } = CollisionCheckerAction.PlaceHoles;

        public int Placed  { get; set; }
        public int Skipped { get; set; }
        public int Errors  { get; set; }
        public List<string> ErrorMessages { get; set; } = new List<string>();
        // Row id -> the hole instance that got placed for it, so the window
        // can update HasHole on the matching rows without re-scanning.
        public Dictionary<string, Autodesk.Revit.DB.ElementId> PlacedHoleByRowId { get; set; } = new Dictionary<string, Autodesk.Revit.DB.ElementId>();
        // Row id -> the specific exception message for THAT row, so the
        // result list can show exactly why each failed row failed, not
        // just a total count.
        public Dictionary<string, string> ErrorByRowId { get; set; } = new Dictionary<string, string>();
        // A hole was placed successfully but a Length/Width-like parameter
        // wasn't found on it by any of the guessed names -- not a failure
        // (the hole IS there), so it's tracked separately from Errors
        // rather than making a successful row look like it failed.
        public int DimensionWarnings { get; set; }
        public string FirstDimensionWarning { get; set; }

        // -- MarkCollisions-specific fields --
        public int MarksAttempted { get; set; }
        public int MarksFailed { get; set; }
        public string FirstMarkError { get; set; }
        // Collisions whose own level has no Floor Plan (or other plan)
        // view to draw into -- e.g. a level nobody ever created a plan
        // view for. Tracked separately from MarksFailed since nothing
        // actually went wrong; there's just nowhere to put the mark.
        public int MarksSkippedNoView { get; set; }
        // Row id -> the detail-curve element IDs drawn for it, so the
        // window can track them for later removal (RemoveMarkerFor) the
        // same way it did when it drew them directly.
        public Dictionary<string, List<Autodesk.Revit.DB.ElementId>> MarkersByCollisionId { get; set; } = new Dictionary<string, List<Autodesk.Revit.DB.ElementId>>();
    }

    // One loaded family/type the user can pick as the hole marker.
    public class HoleSymbolOption
    {
        public Autodesk.Revit.DB.ElementId SymbolId { get; set; }
        public string FamilyName { get; set; } = "";
        public string TypeName   { get; set; } = "";
        public string DisplayName =>
            string.IsNullOrEmpty(TypeName) || string.Equals(TypeName, FamilyName, StringComparison.OrdinalIgnoreCase)
                ? FamilyName : $"{FamilyName} : {TypeName}";
        public override string ToString() => DisplayName;
    }
}

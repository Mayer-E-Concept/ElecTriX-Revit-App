// CollisionCheckerModels.cs -- ME-Tools | Collision Checker (conduits/cable trays vs walls)
// Mayer E-Concept SRL
using System;
using System.Collections.Generic;

namespace METools.CollisionChecker
{
    public enum ScanScope { WholeModel, ActiveView, CurrentSelection }

    // WallCrossing is the original case this whole file was built around --
    // a run passing through a wall, resolved by placing a hole. The other
    // two kinds are a genuinely different kind of finding: two routed
    // systems (a cable tray/conduit and, respectively, a pipe or a
    // structural beam/column/foundation) physically overlapping in open
    // space, not a run meeting a fixed obstacle. There's no equivalent
    // "place a hole" resolution for that -- the fix is always a human
    // decision to reroute one system or the other -- so PlumbingClash and
    // StructuralClash rows are informational (Go To only) rather than
    // actionable the way WallCrossing rows are. Both share the exact same
    // detection algorithm (see ScanForLinkedClashes) and the same
    // Mark-as-Solved storage; only which categories get searched for in the
    // chosen link differs.
    public enum CollisionKind { WallCrossing, PlumbingClash, StructuralClash }

    // One place where a conduit or cable tray run passes through a wall.
    // A single run can cross several walls (a corridor wall + a shaft wall,
    // say) -- each crossing is its own CollisionInfo, not grouped under the
    // run, since each needs its own hole and its own "go to" location.
    public class CollisionInfo
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N"); // stable row key for the UI list, independent of Revit IDs
        public CollisionKind Kind { get; set; } = CollisionKind.WallCrossing;
        public Autodesk.Revit.DB.ElementId ElementId { get; set; }     // the conduit/cable tray
        public Autodesk.Revit.DB.ElementId WallId    { get; set; }
        public string ElementCategory { get; set; } = "";              // "Conduits" or "Cable Trays"
        public string ElementTypeName { get; set; } = "";
        public string WallTypeName    { get; set; } = "";
        public string LevelName       { get; set; } = "";
        public Autodesk.Revit.DB.ElementId LevelId { get; set; } = Autodesk.Revit.DB.ElementId.InvalidElementId;
        public Autodesk.Revit.DB.XYZ Point { get; set; }               // exact intersection point, model coordinates

        // Only meaningful when Kind == PlumbingClash or StructuralClash. The
        // linked element (pipe/fitting, or beam/column/foundation) is a
        // DirectShape or plain element living inside the LINK's own
        // document (same situation as walls in an IFC-linked architecture
        // model -- see FindWallLikeElementsInLink), so there's no host-
        // document ElementId for it the way WallId is for a real Wall; this
        // is a human-readable description (category + a rough size if one
        // could be read) captured at scan time instead. Field name kept as
        // "Plumbing..." even though it's shared with StructuralClash rows --
        // renaming would ripple through every call site for no functional
        // gain; the comment here is the source of truth on what it's
        // actually used for.
        public string PlumbingElementDescription { get; set; } = "";

        // Filled in once a hole has been placed for this row -- lets the
        // list show "done" instead of a Place button, and is how the
        // live-follow watcher knows which hole belongs to which run.
        public Autodesk.Revit.DB.ElementId HoleInstanceId { get; set; } = Autodesk.Revit.DB.ElementId.InvalidElementId;
        public bool HasHole => HoleInstanceId != null && HoleInstanceId != Autodesk.Revit.DB.ElementId.InvalidElementId;

        // Only meaningful when Kind == PlumbingClash or StructuralClash --
        // there's no "hole" to place for two overlapping routed
        // systems/structural elements, so this is a plain manual
        // acknowledgement instead ("I've looked at this, someone rerouted
        // something, it's handled") rather than something this tool can
        // verify against the model the way HasHole can. Persisted keyed by
        // (run UniqueId, linked element UniqueId) -- see
        // CollisionCheckerHandler's solved-clash schema -- and reloaded at
        // the start of every scan. The same schema/storage is shared across
        // both clash kinds since UniqueIds are already globally unique, so
        // there's no risk of a pipe's key colliding with a beam's.
        public bool IsSolved { get; set; } = false;

        // What the filter dropdown and the row's own checkbox actually
        // care about: "is there nothing left to do here", regardless of
        // whether that's because a hole got placed or because a clash got
        // manually marked solved.
        public bool IsResolved => HasHole || IsSolved;

        // Only meaningful when Kind == PlumbingClash or StructuralClash.
        // Combined with ElementId (the run) to form the persisted
        // solved-clash key -- see the remarks above IsSolved.
        public string PlumbingElementUniqueId { get; set; } = "";

        // True when WallId points at an ImportInstance (an imported CAD/IFC
        // file) or a RevitLinkInstance (a linked model) rather than a real
        // host-document Wall -- see CollisionCheckerHandler.
        // FindWallLikeSolidsInImport / FindWallLikeElementsInLink. The
        // crossing point/level resolve the same way regardless of which;
        // only hole placement treats this differently, since there's no
        // real host-document Wall to host a face-hosted family on in
        // either case (see ExecutePlaceHoles).
        public bool IsExternalGeometry { get; set; } = false;

        // Only meaningful when IsExternalGeometry is true -- captured at
        // scan time (from the detected face pair) so hole placement doesn't
        // need to re-parse the import's geometry to recover the thickness/
        // orientation a real Wall would otherwise supply directly.
        public double ImportedWallThicknessFt { get; set; } = 0;
        public Autodesk.Revit.DB.XYZ ImportedWallDirection { get; set; } = null;
    }

    public enum CollisionCheckerAction { None, PlaceHoles, MoveHoles, MarkCollisions, MarkClashSolved, Frame3D }

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
        // Row ids successfully marked solved -- MarkClashSolved's own
        // equivalent of PlacedHoleByRowId, so the window can flip IsSolved
        // on the matching rows without re-scanning.
        public List<string> SolvedRowIds { get; set; } = new List<string>();

        // Frame3D's own result fields -- setting the section box is a
        // document change, so it has to happen in the handler (this
        // result carries back which view/element the window then needs
        // to switch to and select, since THAT part isn't a document
        // change and belongs back on the window side).
        public Autodesk.Revit.DB.ElementId Frame3DViewId { get; set; } = Autodesk.Revit.DB.ElementId.InvalidElementId;
        public Autodesk.Revit.DB.ElementId Frame3DElementId { get; set; } = Autodesk.Revit.DB.ElementId.InvalidElementId;
        public bool Frame3DSucceeded { get; set; } = false;
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

    // One entry per ImportInstance in the project, for the "which import is
    // the architecture" picker -- Name is whatever Revit shows for that
    // import (typically the source file name, e.g. "ARC_OG1.dwg").
    // One entry per candidate architecture source in the project -- either
    // an ImportInstance (imported CAD/IFC file) or a RevitLinkInstance (a
    // linked model, checked live against a real project: often the more
    // common case for a genuine .ifc, since Revit's own IFC linker
    // converts it into a real Document you can query, whereas an imported
    // .ifc has no per-entity structure left at all). IsLink says which, so
    // ScanForCollisions knows which of FindWallLikeSolidsInImport /
    // FindWallLikeElementsInLink to use.
    public class ArchitectureSourceOption
    {
        public Autodesk.Revit.DB.ElementId InstanceId { get; set; }
        public string Name { get; set; } = "";
        public bool IsLink { get; set; } = false;
        public override string ToString() => Name;
    }

    // Same shape as ArchitectureSourceOption, kept as its own type rather
    // than reused directly -- plumbing clash detection only ever looks at
    // linked models (there's no equivalent "imported CAD file" case for
    // it the way ImportInstance is for architecture), and keeping the two
    // pickers as distinct types means a plumbing link can never
    // accidentally get passed into the architecture-crossing code path or
    // vice versa.
    public class PlumbingSourceOption
    {
        public Autodesk.Revit.DB.ElementId InstanceId { get; set; }
        public string Name { get; set; } = "";
        public override string ToString() => Name;
    }

    // Same idea again, for structural clash detection (beams, columns,
    // foundations against a linked structural model) -- kept distinct from
    // PlumbingSourceOption for the same reason PlumbingSourceOption is kept
    // distinct from ArchitectureSourceOption: a structural link picked here
    // can never accidentally end up passed into the plumbing or
    // architecture code paths.
    public class StructuralSourceOption
    {
        public Autodesk.Revit.DB.ElementId InstanceId { get; set; }
        public string Name { get; set; } = "";
        public override string ToString() => Name;
    }
}

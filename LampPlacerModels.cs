// LampPlacerModels.cs — ME-Tools | Lamp Placer
// Mayer E-Concept SRL
using System.Collections.Generic;
using Autodesk.Revit.DB;

namespace METools.LampPlacer
{
    public class LampFamilyInfo
    {
        public string    FamilyName { get; set; } = "";
        public string    TypeName   { get; set; } = "";
        public ElementId SymbolId   { get; set; }
        public FamilyPlacementType Placement { get; set; } = FamilyPlacementType.Invalid;
        public string    Group      { get; set; } = "Lighting";   // "Lighting" or "Fire Alarm"
    }

    public class LevelInfo
    {
        public ElementId Id        { get; set; } = ElementId.InvalidElementId;
        public string    Name      { get; set; } = "";
        public double    Elevation { get; set; } = 0.0;   // in internal units (feet)
    }

    public enum DistributionMode { AreaBased, ManualGrid, Line }
    public enum PlacementSurface { Face, WorkPlane }
    public enum LineMode          { BySpacing, ByCount }
    public enum LineRotation      { AlongLine, Perpendicular }
    public enum RotationMode      { Auto, Deg0, Deg90 }
    public enum DimensionMode     { None, Auto, Custom }  // None=off, Auto=automatic, Custom=user picks points
    public enum LampAction        { PlaceSingle, PlaceMulti, Redistribute, RefreshRoom, PlaceLine, PlaceGrid, UpdatePreset, ApplyPresetToMatchingRooms }

    public class LampConfig
    {
        public string           FamilyName   { get; set; } = "";
        public string           TypeName     { get; set; } = "";
        public DistributionMode Distribution { get; set; } = DistributionMode.AreaBased;
        public double           SqmPerLamp   { get; set; } = 12.0;
        public int              ManualRows   { get; set; } = 2;
        public int              ManualCols   { get; set; } = 2;

        // Line mode
        public LineMode         LineMode     { get; set; } = LineMode.BySpacing;
        public double           LineSpacing  { get; set; } = 2000.0;  // mm between lamps
        public int              LineCount    { get; set; } = 4;       // number of lamps on line
        public LineRotation     LineRotation { get; set; } = LineRotation.AlongLine;

        public double           WallMargin   { get; set; } = 1500.0;
        public RotationMode     Rotation     { get; set; } = RotationMode.Deg0;
        public DimensionMode    Dimensions   { get; set; } = DimensionMode.Auto;
        public PlacementSurface Surface      { get; set; } = PlacementSurface.WorkPlane;
        public double           OverlapThreshold { get; set; } = 300;   // mm, min gap to existing fixtures
        public double           UKDOffset    { get; set; } = 0.0;

        // Off by default -- matches the existing, unchanged behavior
        // (Line-mode rotation always comes from Rotation/CalcAngle, the
        // same fixed plan-orientation angle every other distribution mode
        // uses, regardless of the actual guide line's own direction --
        // confirmed as a real, pre-existing gap: PlaceAlongLine already
        // computes each point's real tangent along the guide line for
        // POSITIONING, but never used it for ROTATION). Only affects Line
        // distribution mode -- Area-based and Manual Grid are untouched by
        // this flag either way, since there's no "line placed on" for them.
        // When on, PlaceAlongLine uses that same already-computed tangent
        // as each lamp's rotation instead of the fixed angle -- no separate
        // pick step, no extra state to keep in sync: it's exactly the guide
        // line already selected as part of running Line-mode placement.
        public bool              OrientToLine { get; set; } = false;

        // Reference level — reliable fallback when no slab face is found at the UKD
        public ElementId        FallbackLevelId { get; set; } = ElementId.InvalidElementId;
    }

    // A reusable room-type recipe for area-based placement: a named set of families + counts.
    public class LampPresetEntry
    {
        public string FamilyName { get; set; } = "";
        public string TypeName   { get; set; } = "";
        public int    Count      { get; set; } = 1;
    }

    public class LampPreset
    {
        public string                Name    { get; set; } = "";
        public List<LampPresetEntry> Entries { get; set; } = new List<LampPresetEntry>();
    }

    public class LampRequest
    {
        public LampAction  Action   { get; set; } = LampAction.PlaceSingle;
        public LampConfig  Config   { get; set; } = new LampConfig();
        public ElementId   SymbolId { get; set; } = ElementId.InvalidElementId;
        public string      PresetName { get; set; } = "";
        public string      RoomNameFilter { get; set; } = ""; // for ApplyPresetToMatchingRooms -- substring match, case-insensitive
    }
}

// LampPlacerModels.cs — ME-Tools | Lamp Placer
// Mayer E-Concept SRL
using Autodesk.Revit.DB;

namespace METools.LampPlacer
{
    public class LampFamilyInfo
    {
        public string    FamilyName { get; set; } = "";
        public string    TypeName   { get; set; } = "";
        public ElementId SymbolId   { get; set; }
    }

    public class LevelInfo
    {
        public ElementId Id        { get; set; } = ElementId.InvalidElementId;
        public string    Name      { get; set; } = "";
        public double    Elevation { get; set; } = 0.0;   // in internal units (feet)
    }

    public enum DistributionMode { AreaBased, ManualGrid, Line }
    public enum LineMode          { BySpacing, ByCount }
    public enum LineRotation      { AlongLine, Perpendicular }
    public enum RotationMode      { Auto, Deg0, Deg90 }
    public enum LampAction        { PlaceSingle, PlaceMulti, Redistribute, RefreshRoom, PlaceLine }

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
        public RotationMode     Rotation     { get; set; } = RotationMode.Auto;
        public double           UKDOffset    { get; set; } = 0.0;

        // Reference level — reliable fallback when no slab face is found at the UKD
        public ElementId        FallbackLevelId { get; set; } = ElementId.InvalidElementId;
    }

    public class LampRequest
    {
        public LampAction  Action   { get; set; } = LampAction.PlaceSingle;
        public LampConfig  Config   { get; set; } = new LampConfig();
        public ElementId   SymbolId { get; set; } = ElementId.InvalidElementId;
    }
}

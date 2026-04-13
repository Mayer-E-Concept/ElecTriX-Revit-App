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

    public enum DistributionMode { AreaBased, ManualGrid }
    public enum RotationMode     { Auto, Deg0, Deg90 }

    public enum LineOrientation
    {
        AlongLine,    // Lamp length parallel to line direction
        Perpendicular // Lamp length perpendicular to line direction
    }

    public enum LampAction
    {
        PlaceSingle,
        PlaceMulti,
        RefreshRoom,
        RefreshMulti,
        RotateRoom,
        PlaceLine
    }

    public class LampConfig
    {
        // Room placement
        public string           FamilyName   { get; set; } = "";
        public string           TypeName     { get; set; } = "";
        public DistributionMode Distribution { get; set; } = DistributionMode.AreaBased;
        public double           SqmPerLamp   { get; set; } = 12.0;
        public int              ManualRows   { get; set; } = 2;
        public int              ManualCols   { get; set; } = 2;
        public double           WallMargin   { get; set; } = 1500.0;
        public RotationMode     Rotation     { get; set; } = RotationMode.Auto;
        public double           UKDOffset    { get; set; } = 0.0;

        // Line placement — count is the only user input.
        // Lamp length read automatically from family symbol.
        // Axis spacing = lineLength / count (each lamp in its own segment).
        // Margin at start/end = axis/2 (lamp centered in first/last segment).
        public LineOrientation LineOrientation { get; set; } = LineOrientation.AlongLine;
        public int             LineCount       { get; set; } = 4;
    }

    public class LampRequest
    {
        public LampAction  Action   { get; set; } = LampAction.PlaceSingle;
        public LampConfig  Config   { get; set; } = new LampConfig();
        public ElementId   SymbolId { get; set; } = ElementId.InvalidElementId;
    }
}

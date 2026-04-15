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
    public enum LineMode        { BySpacing, ByCount }
    public enum LineOrientation { AlongLine, Perpendicular }

    public enum LampAction
    {
        PlaceSingle,
        PlaceMulti,
        Redistribute,
        RefreshRoom,
        RefreshMulti,
        PlaceOnLine
    }

    public class LampConfig
    {
        public string           FamilyName   { get; set; } = "";
        public string           TypeName     { get; set; } = "";
        public DistributionMode Distribution { get; set; } = DistributionMode.AreaBased;
        public double           SqmPerLamp   { get; set; } = 12.0;
        public int              ManualRows   { get; set; } = 2;
        public int              ManualCols   { get; set; } = 2;
        public double           WallMargin   { get; set; } = 1500.0;
        public RotationMode     Rotation     { get; set; } = RotationMode.Auto;
        public double           UKDOffset    { get; set; } = 0.0;
        public double           LineSpacing     { get; set; } = 2000.0;
        public int              LineCount       { get; set; } = 4;
        public LineMode         LineMode        { get; set; } = LineMode.BySpacing;
        public LineOrientation  LineOrientation { get; set; } = LineOrientation.AlongLine;
    }

    public class LampRequest
    {
        public LampAction  Action   { get; set; } = LampAction.PlaceSingle;
        public LampConfig  Config   { get; set; } = new LampConfig();
        public ElementId   SymbolId { get; set; } = ElementId.InvalidElementId;
    }
}

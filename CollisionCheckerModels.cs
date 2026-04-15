// CollisionCheckerModels.cs — ME-Tools | Clash Detector  v5
// Mayer E-Concept SRL
using System.Collections.Generic;
using Autodesk.Revit.DB;

namespace METools.ClashDetector
{
    public enum ClashAction
    {
        RunCheck,
        MarkInPlan,
        ClearPlanMarkers,
        PlaceFamily,
        SyncFamilies,
        NavigatePlan,
        NavigateTo3D,
    }

    public class ClashResult
    {
        public int Index { get; set; }

        // MEP element
        public ElementId MepElementId    { get; set; }
        public string    MepCategory     { get; set; }
        public string    MepDescription  { get; set; }
        public double    MepWidthFt      { get; set; }   // Revit internal feet
        public double    MepHeightFt     { get; set; }

        // Obstacle
        public ElementId ObstacleElementId   { get; set; }
        public string    ObstacleCategory    { get; set; }
        public string    ObstacleDescription { get; set; }
        public double    ObstacleDepthFt     { get; set; }

        // Source
        public bool      IsLinked       { get; set; }
        public string    LinkName       { get; set; }
        public ElementId LinkInstanceId { get; set; }

        // Geometry (Revit internal feet)
        public XYZ    IntersectionCenter { get; set; }
        public XYZ    OverlapMin         { get; set; }   // exact bounding-box overlap zone
        public XYZ    OverlapMax         { get; set; }
        public double FloorLevelElevFt   { get; set; }

        // Status
        public bool      IsSelected     { get; set; } = true;
        public bool      HasPlanMarker  { get; set; }
        public bool      FamilyPlaced   { get; set; }
        public ElementId PlacedFamilyId { get; set; }

        // IDs of created FilledRegion elements (for clean deletion)
        public List<long> MarkerIds { get; set; } = new List<long>();
    }

    public class PenFamilyInfo
    {
        public string    FamilyName { get; set; }
        public string    TypeName   { get; set; }
        public ElementId SymbolId   { get; set; }
        public ElementId FamilyId   { get; set; }
        public bool      IsCax      { get; set; }
        public bool      IsIfc      { get; set; }

        public override string ToString()
        {
            string prefix = IsCax ? "★ " : IsIfc ? "◆ " : "";
            return $"{prefix}{FamilyName} : {TypeName}";
        }
    }

    /// <summary>
    /// Overstand / protrusion settings for the Auxalia CAx family.
    /// All values in mm (converted to feet in the handler before passing to Revit API).
    /// </summary>
    public class CaxSettings
    {
        public double X_Ueberstand_mm { get; set; } = 50.0;
        public double Z_Ueberstand_mm { get; set; } = 100.0;
        public double Vorzug_mm       { get; set; } = 0.0;
        public double Nachzug_mm      { get; set; } = 0.0;
    }

    public class ClashRequest
    {
        public ClashAction Action { get; set; }

        // RunCheck filters
        public bool CheckCableTrays { get; set; } = true;
        public bool CheckConduits   { get; set; } = true;
        public bool CheckDucts      { get; set; } = true;
        public bool CheckPipes      { get; set; } = true;
        public bool CheckWalls      { get; set; } = true;
        public bool CheckFloors     { get; set; } = true;
        public bool CheckStructural { get; set; } = true;
        public bool CheckCurrentDoc { get; set; } = true;
        public bool CheckLinkedDocs { get; set; } = true;

        // Mark / Clear
        public List<ClashResult> ResultsToMark { get; set; }

        // PlaceFamily
        public List<ClashResult> ResultsToPlace { get; set; }
        public ElementId         SymbolId       { get; set; } = ElementId.InvalidElementId;
        public CaxSettings       CaxSettings    { get; set; } = new CaxSettings();

        // Navigate
        public ClashResult TargetResult { get; set; }
    }
}

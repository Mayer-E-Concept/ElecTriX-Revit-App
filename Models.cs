// Models.cs — ME-Tools | Family Placer Types
// Mayer E-Concept SRL
using System.Collections.Generic;
using Autodesk.Revit.DB;

namespace METools.FamilyPlacer
{
    public class FamilySlot
    {
        public string FamilyName   { get; set; } = "";
        public string TypeName     { get; set; } = "";
        public double Height       { get; set; } = 85.0;
        public int    OffsetX { get; set; } = 0;
        public int    OffsetY { get; set; } = 0;
        public Dictionary<string,string> ParamOverrides { get; set; } = new Dictionary<string,string>();
    }

    public class FamilyParamInfo
    {
        public string Name         { get; set; } = ""; // Revit parameter key (German)
        public string Label        { get; set; } = ""; // English display label
        public string Kind         { get; set; } = ""; // "length" | "int" | "double" | "yesno"
        public string DefaultValue { get; set; } = ""; // display string (mm for length)
        public bool   Inline       { get; set; } = false; // handled inline (Niveau -> height field); not shown in popup
    }
    public class PlacerTemplate
    {
        public string           Name        { get; set; } = "";
        public string           Orientation { get; set; } = "Vertical";
        public List<FamilySlot> Slots       { get; set; } = new List<FamilySlot>();
    }
    public class FamilyTypeInfo
    {
        public string    FamilyName    { get; set; } = "";
        public string    TypeName      { get; set; } = "";
        public ElementId SymbolId      { get; set; }
        public string    CategoryGroup { get; set; } = "";
    }
    public enum HandlerAction { PlaceSingle, PlaceMulti }
    public class PlacerRequest
    {
        public HandlerAction    Action      { get; set; } = HandlerAction.PlaceSingle;
        public List<FamilySlot> Slots       { get; set; } = new List<FamilySlot>();
        public string           Orientation { get; set; } = "Vertical";
        public ElementId        LevelId     { get; set; } = ElementId.InvalidElementId;
    }
    public class LevelInfo
    {
        public string    Name      { get; set; } = "";
        public ElementId Id        { get; set; }
        public double    Elevation { get; set; }
    }
}

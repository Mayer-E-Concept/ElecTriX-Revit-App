// CircuitTaggerModels.cs -- ME-Tools | Circuit Tagger
// Mayer E-Concept SRL
using System.Collections.Generic;

namespace METools.FamilyPlacer
{
    public class TaggedElementInfo
    {
        public Autodesk.Revit.DB.ElementId ElementId { get; set; }
        public string CategoryName  { get; set; }
        public string FamilyName    { get; set; }
        public string RoomName      { get; set; }
        public string CircuitLabel  { get; set; }
    }

    public class CircuitStatRow
    {
        public string CircuitBase       { get; set; } // e.g. "1F1" (without sub-index)
        public string CircuitLabel      { get; set; } // e.g. "1F1" or "1F1_1"
        public string Vorsicherung      { get; set; }
        public string FI                { get; set; }
        public string Stromkreis        { get; set; }
        public string Beleuchtungskreis { get; set; }
        public string Apartment         { get; set; }
        public string Building          { get; set; }
        public int    CountSockets      { get; set; }
        public int    CountLamps        { get; set; }
        public int    CountSwitches     { get; set; }
        public int    CountOther        { get; set; }
        public int    Total             { get { return CountSockets + CountLamps + CountSwitches + CountOther; } }
    }

    public class ExportRow
    {
        public string Building          { get; set; }
        public string Apartment         { get; set; }
        public string CircuitLabel      { get; set; }
        public string Vorsicherung      { get; set; }
        public string FI                { get; set; }
        public string Stromkreis        { get; set; }
        public string Beleuchtungskreis { get; set; }
        public string Category          { get; set; }
        public int    CategoryId        { get; set; } // BuiltInCategory int ID -- locale-independent
        public string FamilyName        { get; set; }
        public string Room              { get; set; }
        public string ElementId         { get; set; }
    }

    public enum CircuitTaggerAction
    {
        None,
        WriteParamsAndPlaceTags,
        ReadApartmentValues,
        LoadParamsFromSelection,
        ClearCircuitData,
    }

    public class CircuitTaggerRequest
    {
        public CircuitTaggerAction               Action           { get; set; } = CircuitTaggerAction.None;
        public List<Autodesk.Revit.DB.ElementId> ElementIds       { get; set; } = new List<Autodesk.Revit.DB.ElementId>();
        public string Vorsicherung        { get; set; } = "";
        public string FI                  { get; set; } = "";
        public string Stromkreis          { get; set; } = "";
        public string SubIndex            { get; set; } = "";
        public string Beleuchtungskreis   { get; set; } = "";
        public string Apartment           { get; set; } = "";
        public string Building            { get; set; } = "";
        public string SubLabel            { get; set; } = "";
        public string CircuitLabelToClear { get; set; } = ""; // used by ClearCircuitData action
    }
}

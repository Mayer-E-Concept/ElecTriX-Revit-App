// CircuitTaggerModels.cs -- ME-Tools | Circuit Tagger
// Mayer E-Concept SRL
using System;
using System.Collections.Generic;

namespace METools.FamilyPlacer
{
    // One selectable entry in the Tag Family picker on the Tag tab -- one
    // per FamilySymbol (family + type) currently loaded under the
    // Multi-Category Tags category. Lets the user switch which tag gets
    // placed (e.g. a lamp/socket tag vs. a fire alarm tag) without leaving
    // Circuit Tagger.
    public class TagFamilyOption
    {
        public Autodesk.Revit.DB.ElementId SymbolId   { get; set; }
        public string                      FamilyName { get; set; } = "";
        public string                      TypeName   { get; set; } = "";

        // Most Multi-Category Tag families only have one type, in which
        // case showing the type name too is just noise ("MyTag : MyTag").
        public string DisplayName =>
            string.IsNullOrEmpty(TypeName) ||
            string.Equals(TypeName, FamilyName, StringComparison.OrdinalIgnoreCase)
                ? FamilyName
                : $"{FamilyName} : {TypeName}";
    }

    public class TaggedElementInfo
    {
        public Autodesk.Revit.DB.ElementId ElementId { get; set; }
        public string CategoryName  { get; set; }
        public int    CategoryId    { get; set; } // BuiltInCategory int ID -- locale-independent, see CatShort
        public string FamilyName    { get; set; }
        public string RoomName      { get; set; }
        public string CircuitLabel  { get; set; }
    }

    // One element found by FindUntagged -- in a taggable category, but its
    // circuit tag (Stromkreis Tag) is empty. Deliberately minimal (no circuit
    // fields at all, since by definition none are set) -- just enough to find
    // and select the element for review.
    public class UntaggedElementInfo
    {
        public Autodesk.Revit.DB.ElementId ElementId { get; set; }
        public string CategoryName { get; set; } = "";
        public string FamilyName   { get; set; } = "";
        public string LevelName    { get; set; } = "";
        public string RoomName     { get; set; } = "";
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

        // The actual tagged elements in this circuit -- used by the Stats
        // tab's expandable per-level breakdown. Not exported/serialized
        // anywhere, just carried alongside the aggregate counts for display.
        public List<ExportRow> Elements { get; set; } = new List<ExportRow>();
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
        public string LevelName         { get; set; } // "" if none could be resolved
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
        // Which tag family/type to place, from the Tag tab's live picker.
        // InvalidElementId means "no explicit pick" -- ResolveTagSymbol then
        // falls back to the original hardcoded default family by name.
        public Autodesk.Revit.DB.ElementId TagSymbolId { get; set; } = Autodesk.Revit.DB.ElementId.InvalidElementId;
        public string TagFamilyDisplayName { get; set; } = "";
        // Used by ClearCircuitData action -- a list (not a single label) so
        // multiple selected circuits can be cleared in one Revit-thread round
        // trip, one transaction, and one stats refresh, instead of the user
        // having to click Clear + confirm a dialog once per circuit.
        public List<string> CircuitLabelsToClear { get; set; } = new List<string>();
    }
}

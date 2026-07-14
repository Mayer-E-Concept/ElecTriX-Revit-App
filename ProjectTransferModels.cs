// ProjectTransferModels.cs — ME-Tools | Project Transfer
// Mayer E-Concept SRL
using System.Collections.Generic;
using Autodesk.Revit.DB;

namespace METools.ProjectTransfer
{
    public enum TransferCategory { Filters, Views, Sheets, Schedules }

    // One item the user can pick to copy: a filter, a drafting view/legend,
    // a sheet, or a schedule from the SOURCE (active) document.
    public class TransferItem
    {
        public ElementId        Id          { get; set; } = ElementId.InvalidElementId;
        public string           Name        { get; set; } = "";
        public TransferCategory Category    { get; set; }
        public string           SubInfo     { get; set; } = "";    // e.g. view type, field count, category count
        public bool             Warning     { get; set; }         // e.g. sheet holds views tied to this project's levels
        public string           WarningText { get; set; } = "";
        // Auto-detected naming prefix within this category (e.g. "A", "E", "S" for
        // discipline-coded filters like "A_Brandschutz_AUS") — "" if nothing recurs
        // often enough in this category to call it a group.
        public string           SubGroup    { get; set; } = "";
    }

    // One Revit document currently open in this session, offered as a copy target.
    public class OpenDocInfo
    {
        public string Title    { get; set; } = "";
        public bool   IsActive { get; set; }
    }

    public enum TransferAction { RefreshSource, ListTargets, OpenTargetFile, Copy }

    public class TransferRequest
    {
        public TransferAction  Action         { get; set; } = TransferAction.RefreshSource;
        public string          TargetTitle    { get; set; } = "";   // matches OpenDocInfo.Title
        public string          TargetFilePath { get; set; } = "";   // for OpenTargetFile
        public List<ElementId> ItemIds        { get; set; } = new List<ElementId>();
    }

    public class TransferResult
    {
        public int          Requested { get; set; }
        public int          Copied    { get; set; }
        public List<string> Lines     { get; set; } = new List<string>();
    }
}

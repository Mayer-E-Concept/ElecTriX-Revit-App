// BatchParamsModels.cs -- ME-Tools | Batch Params (Renumber + Bulk Edit)
// Mayer E-Concept SRL
//
// Inspired by DiRoots' ReOrdering (renumber instance parameters with a
// prefix/number/suffix, manually or along a detail line) and OneParameter
// (bulk add-prefix/add-suffix/find-replace/clear across many elements at
// once) -- combined here into one tool with two tabs sharing the same
// element-filtering step, generic across any category/parameter rather
// than scoped to electrical categories specifically.
using System;
using System.Collections.Generic;

namespace METools.BatchParams
{
    public enum ElementScope { CurrentSelection, ActiveView, WholeModel }

    // One checkable category row in the filter list, with a live count so
    // it's obvious how many elements each checkbox actually adds.
    public class CategoryOption
    {
        public Autodesk.Revit.DB.ElementId CategoryId { get; set; }
        public string Name  { get; set; } = "";
        public int    Count { get; set; }
        public string DisplayName => $"{Name} ({Count})";
    }

    // One selectable parameter, gathered as the union of writable String-
    // storage parameters found across the currently-matched elements.
    // Scoped to String storage deliberately (see BatchParamsHandler) -- it
    // covers virtually every real "numbering"/"label" field in Revit
    // (Mark, room/sheet/detail Number, Comments, shared text parameters
    // like this project's own Stromkreis Tag) and sidesteps a large class
    // of storage-type conversion bugs that a fully-generic numeric handler
    // would otherwise need to cover.
    public class ParamOption
    {
        public string Name       { get; set; } = "";
        public bool   IsInstance { get; set; } = true;
        public string DisplayName => IsInstance ? Name : $"{Name} (Type)";
        public override string ToString() => DisplayName;
    }

    public enum RenumberOrderMode { Manual, Path }

    public class RenumberConfig
    {
        public string ParameterName { get; set; } = "";
        public string Prefix        { get; set; } = "";
        public string Suffix        { get; set; } = "";
        public int    StartNumber   { get; set; } = 1;
        public int    Step          { get; set; } = 1;
        // Zero-pad the number to at least this many digits (0 = no padding).
        public int    Padding       { get; set; } = 0;
        public RenumberOrderMode OrderMode { get; set; } = RenumberOrderMode.Manual;
    }

    public enum BulkEditAction { AddPrefix, AddSuffix, FindReplace, SetValue, ClearValue }

    public class BulkEditConfig
    {
        public string ParameterName { get; set; } = "";
        public bool   IsInstance    { get; set; } = true;
        public BulkEditAction Action { get; set; } = BulkEditAction.AddPrefix;
        public string PrefixText    { get; set; } = "";
        public string SuffixText    { get; set; } = "";
        public string FindText      { get; set; } = "";
        public string ReplaceText   { get; set; } = "";
        public string SetText       { get; set; } = "";
        // Only touch elements whose current value contains this
        // (case-insensitive). Empty = no filter, touch every matched element.
        // Mirrors OneParameter's "filter by parameter value" behavior.
        public string ValueFilter   { get; set; } = "";
    }

    public enum BatchParamsAction { None, ApplyRenumber, ApplyBulkEdit }

    public class BatchParamsRequest
    {
        public BatchParamsAction Action { get; set; } = BatchParamsAction.None;

        // The exact elements to write to, and in what order (order only
        // matters for ApplyRenumber -- index 0 gets StartNumber, etc).
        public List<Autodesk.Revit.DB.ElementId> OrderedElementIds { get; set; } = new List<Autodesk.Revit.DB.ElementId>();

        public RenumberConfig Renumber { get; set; } = new RenumberConfig();
        public BulkEditConfig BulkEdit { get; set; } = new BulkEditConfig();

        // When true, the handler runs the exact same logic (including
        // attempting every write, so read-only/missing-parameter detection
        // is accurate) inside a transaction that gets rolled back instead of
        // committed -- a preview of exactly what Apply would do, using the
        // same code path so it can't drift from the real thing.
        public bool DryRun { get; set; } = false;
    }

    public enum ChangeStatus { Updated, Skipped, Error }

    // One element's before/after -- the same record powers both the preview
    // (before you commit) and the after-the-fact detail list (which elements
    // got skipped/errored and why), since both come from the same dry-run-or-
    // real pass over the elements.
    public class ElementChangeInfo
    {
        public Autodesk.Revit.DB.ElementId ElementId { get; set; }
        public string ElementLabel { get; set; } = "";
        public string OldValue     { get; set; } = "";
        public string NewValue     { get; set; } = "";
        public ChangeStatus Status { get; set; } = ChangeStatus.Updated;
        public string Reason       { get; set; } = "";
    }

    public class ApplyResult
    {
        public int Updated { get; set; }
        public int Skipped { get; set; }
        public int Errors  { get; set; }
        public List<string> ErrorMessages { get; set; } = new List<string>();

        // Which action produced this result and whether it was a dry run --
        // lets one shared UI panel/handler distinguish "here's what WOULD
        // happen, confirm?" from "here's what DID happen, and why anything
        // was skipped" without separate plumbing for each.
        public BatchParamsAction WhichAction { get; set; } = BatchParamsAction.None;
        public bool WasDryRun { get; set; } = false;
        public List<ElementChangeInfo> Changes { get; set; } = new List<ElementChangeInfo>();
    }
}

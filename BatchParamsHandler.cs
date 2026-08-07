// BatchParamsHandler.cs -- ME-Tools | Batch Params (Renumber + Bulk Edit)
// Mayer E-Concept SRL
using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace METools.BatchParams
{
    public class BatchParamsHandler : IExternalEventHandler
    {
        public BatchParamsRequest  Request  { get; set; } = new BatchParamsRequest();
        public Action<string>      OnStatus { get; set; }
        public Action<ApplyResult> OnDone   { get; set; }

        public string GetName() => "ME-Tools Batch Params";

        public void Execute(UIApplication app)
        {
            var req = Request;
            var doc = app.ActiveUIDocument?.Document;
            if (doc == null || req == null || req.Action == BatchParamsAction.None) return;

            switch (req.Action)
            {
                case BatchParamsAction.ApplyRenumber: ExecuteRenumber(doc, req); break;
                case BatchParamsAction.ApplyBulkEdit: ExecuteBulkEdit(doc, req); break;
            }
        }

        // -- Renumber: prefix + zero-padded counter + suffix, in whatever
        // order the caller already resolved (manual pick order, or Path
        // mode's curve-projection order) --------------------------------
        private void ExecuteRenumber(Document doc, BatchParamsRequest req)
        {
            var cfg = req.Renumber ?? new RenumberConfig();
            var ids = req.OrderedElementIds ?? new List<ElementId>();
            var result = new ApplyResult { WhichAction = BatchParamsAction.ApplyRenumber, WasDryRun = req.DryRun };

            if (ids.Count == 0) { Report("No elements to renumber."); OnDone?.Invoke(result); return; }
            if (string.IsNullOrEmpty(cfg.ParameterName)) { Report("No parameter selected."); OnDone?.Invoke(result); return; }

            using (var tx = new Transaction(doc, "ME-Tools: Batch Renumber"))
            {
                tx.Start();
                int n = cfg.StartNumber;
                foreach (var id in ids)
                {
                    string label = "";
                    string newVal = "";
                    try
                    {
                        var el = doc.GetElement(id);
                        string numStr = cfg.Padding > 0 ? n.ToString().PadLeft(cfg.Padding, '0') : n.ToString();
                        newVal = (cfg.Prefix ?? "") + numStr + (cfg.Suffix ?? "");
                        label  = ElementLabel(el);

                        if (el == null)
                        {
                            result.Skipped++;
                            result.Changes.Add(new ElementChangeInfo { ElementId = id, ElementLabel = "(deleted)", NewValue = newVal, Status = ChangeStatus.Skipped, Reason = "element no longer exists" });
                        }
                        else
                        {
                            var p = el.LookupParameter(cfg.ParameterName);
                            if (p == null || p.IsReadOnly || p.StorageType != StorageType.String)
                            {
                                result.Skipped++;
                                string reason = p == null ? "parameter not found on this element"
                                              : p.IsReadOnly ? "parameter is read-only"
                                              : "parameter is not a text parameter";
                                result.Changes.Add(new ElementChangeInfo { ElementId = id, ElementLabel = label, NewValue = newVal, Status = ChangeStatus.Skipped, Reason = reason });
                            }
                            else
                            {
                                string oldVal = p.AsString() ?? "";
                                p.Set(newVal);
                                result.Updated++;
                                result.Changes.Add(new ElementChangeInfo { ElementId = id, ElementLabel = label, OldValue = oldVal, NewValue = newVal, Status = ChangeStatus.Updated });
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        result.Errors++;
                        result.ErrorMessages.Add(ex.Message);
                        result.Changes.Add(new ElementChangeInfo { ElementId = id, ElementLabel = label, NewValue = newVal, Status = ChangeStatus.Error, Reason = ex.Message });
                    }
                    n += cfg.Step;
                }
                if (tx.GetStatus() == TransactionStatus.Started)
                {
                    if (req.DryRun) tx.RollBack(); else tx.Commit();
                }
            }

            var verb = req.DryRun ? "Would renumber" : "Renumbered";
            var summary = $"{verb} {result.Updated} element(s)";
            if (result.Skipped > 0) summary += $", {result.Skipped} skipped";
            if (result.Errors  > 0) summary += $", {result.Errors} errors: " + result.ErrorMessages.FirstOrDefault();
            if (req.DryRun) summary += " -- review, then Confirm to apply.";
            Report(summary);
            OnDone?.Invoke(result);
        }

        // -- Bulk Edit: add prefix/suffix, find & replace, set, or clear --
        private void ExecuteBulkEdit(Document doc, BatchParamsRequest req)
        {
            var cfg = req.BulkEdit ?? new BulkEditConfig();
            var ids = req.OrderedElementIds ?? new List<ElementId>();
            var result = new ApplyResult { WhichAction = BatchParamsAction.ApplyBulkEdit, WasDryRun = req.DryRun };

            if (ids.Count == 0) { Report("No elements matched."); OnDone?.Invoke(result); return; }
            if (string.IsNullOrEmpty(cfg.ParameterName)) { Report("No parameter selected."); OnDone?.Invoke(result); return; }

            using (var tx = new Transaction(doc, "ME-Tools: Batch Bulk Edit"))
            {
                tx.Start();
                foreach (var id in ids)
                {
                    string label = "";
                    try
                    {
                        var el = doc.GetElement(id);
                        if (el == null)
                        {
                            result.Skipped++;
                            result.Changes.Add(new ElementChangeInfo { ElementId = id, ElementLabel = "(deleted)", Status = ChangeStatus.Skipped, Reason = "element no longer exists" });
                            continue;
                        }
                        label = ElementLabel(el);
                        var target = cfg.IsInstance ? el : ResolveTypeElement(doc, el);
                        if (target == null)
                        {
                            result.Skipped++;
                            result.Changes.Add(new ElementChangeInfo { ElementId = id, ElementLabel = label, Status = ChangeStatus.Skipped, Reason = "no type element found" });
                            continue;
                        }

                        var p = target.LookupParameter(cfg.ParameterName);
                        if (p == null || p.IsReadOnly || p.StorageType != StorageType.String)
                        {
                            result.Skipped++;
                            string reason = p == null ? "parameter not found on this element"
                                          : p.IsReadOnly ? "parameter is read-only"
                                          : "parameter is not a text parameter";
                            result.Changes.Add(new ElementChangeInfo { ElementId = id, ElementLabel = label, Status = ChangeStatus.Skipped, Reason = reason });
                            continue;
                        }

                        string current = p.AsString() ?? "";
                        if (!string.IsNullOrEmpty(cfg.ValueFilter) &&
                            current.IndexOf(cfg.ValueFilter, StringComparison.OrdinalIgnoreCase) < 0)
                        {
                            result.Skipped++;
                            result.Changes.Add(new ElementChangeInfo { ElementId = id, ElementLabel = label, OldValue = current, Status = ChangeStatus.Skipped, Reason = "current value doesn't match the filter" });
                            continue;
                        }

                        string next;
                        switch (cfg.Action)
                        {
                            case BulkEditAction.AddPrefix:  next = (cfg.PrefixText ?? "") + current; break;
                            case BulkEditAction.AddSuffix:  next = current + (cfg.SuffixText ?? ""); break;
                            case BulkEditAction.FindReplace:
                                next = string.IsNullOrEmpty(cfg.FindText)
                                    ? current
                                    : current.Replace(cfg.FindText, cfg.ReplaceText ?? "");
                                break;
                            case BulkEditAction.SetValue:   next = cfg.SetText ?? ""; break;
                            case BulkEditAction.ClearValue: next = ""; break;
                            default: next = current; break;
                        }

                        if (string.Equals(next, current, StringComparison.Ordinal))
                        {
                            result.Skipped++;
                            result.Changes.Add(new ElementChangeInfo { ElementId = id, ElementLabel = label, OldValue = current, NewValue = next, Status = ChangeStatus.Skipped, Reason = "no change" });
                            continue;
                        }
                        p.Set(next);
                        result.Updated++;
                        result.Changes.Add(new ElementChangeInfo { ElementId = id, ElementLabel = label, OldValue = current, NewValue = next, Status = ChangeStatus.Updated });
                    }
                    catch (Exception ex)
                    {
                        result.Errors++;
                        result.ErrorMessages.Add(ex.Message);
                        result.Changes.Add(new ElementChangeInfo { ElementId = id, ElementLabel = label, Status = ChangeStatus.Error, Reason = ex.Message });
                    }
                }
                if (tx.GetStatus() == TransactionStatus.Started)
                {
                    if (req.DryRun) tx.RollBack(); else tx.Commit();
                }
            }

            var verb = req.DryRun ? "Would update" : "Updated";
            var summary = $"{verb} {result.Updated} element(s)";
            if (result.Skipped > 0) summary += $", {result.Skipped} skipped";
            if (result.Errors  > 0) summary += $", {result.Errors} errors: " + result.ErrorMessages.FirstOrDefault();
            if (req.DryRun) summary += " -- review, then Confirm to apply.";
            Report(summary);
            OnDone?.Invoke(result);
        }

        private static string ElementLabel(Element el)
        {
            if (el == null) return "(deleted)";
            try { return $"{el.Category?.Name ?? "Element"} #{el.Id.Value}"; }
            catch { return "Element"; }
        }

        private void Report(string msg) => OnStatus?.Invoke(msg);

        private static Element ResolveTypeElement(Document doc, Element el)
        {
            try
            {
                var typeId = el?.GetTypeId();
                if (typeId == null || typeId == ElementId.InvalidElementId) return null;
                return doc.GetElement(typeId);
            }
            catch { return null; }
        }

        // ═════════════════════════════════════════════════════════════════
        // READ-ONLY HELPERS -- called directly from the window, same
        // convention as CircuitTaggerHandler.GetAvailableTagFamilies /
        // FindTagSymbol: no ExternalEvent round trip needed for plain reads.
        // ═════════════════════════════════════════════════════════════════

        // Every non-type element in the given scope. ActiveView and
        // WholeModel both exclude element types via
        // WhereElementIsNotElementType(); CurrentSelection is exactly
        // whatever's already selected in Revit.
        public static List<Element> CollectByScope(Document doc, UIDocument uiDoc, ElementScope scope)
        {
            try
            {
                switch (scope)
                {
                    case ElementScope.CurrentSelection:
                        return uiDoc.Selection.GetElementIds()
                            .Select(id => doc.GetElement(id))
                            .Where(e => e != null)
                            .ToList();
                    case ElementScope.ActiveView:
                        var view = uiDoc.ActiveView;
                        if (view == null) return new List<Element>();
                        return new FilteredElementCollector(doc, view.Id)
                            .WhereElementIsNotElementType()
                            .ToList();
                    case ElementScope.WholeModel:
                    default:
                        return new FilteredElementCollector(doc)
                            .WhereElementIsNotElementType()
                            .ToList();
                }
            }
            catch { return new List<Element>(); }
        }

        // Distinct categories present among the given elements, with
        // counts, sorted by name -- populates the filter checklist.
        public static List<CategoryOption> ListCategories(IEnumerable<Element> elements)
        {
            try
            {
                return elements
                    .Where(e => e.Category != null)
                    .GroupBy(e => e.Category.Id)
                    .Select(g => new CategoryOption
                    {
                        CategoryId = g.Key,
                        Name       = g.First().Category.Name,
                        Count      = g.Count(),
                    })
                    .OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase)
                    .ToList();
            }
            catch { return new List<CategoryOption>(); }
        }

        // Elements from the scanned set whose category is one of the
        // checked ones.
        public static List<Element> FilterByCategories(IEnumerable<Element> elements, IEnumerable<ElementId> categoryIds)
        {
            var wanted = new HashSet<ElementId>(categoryIds ?? Enumerable.Empty<ElementId>());
            return elements.Where(e => e.Category != null && wanted.Contains(e.Category.Id)).ToList();
        }

        // Union of writable String-storage parameters (instance and type)
        // across the matched elements. Capped at a sample of the set for
        // performance -- the goal here is just to discover which parameter
        // NAMES exist across this kind of selection, not to touch every
        // element yet, so scanning a representative sample is enough even
        // on a whole-model scope with thousands of elements.
        public static List<ParamOption> GetParameterOptions(Document doc, IEnumerable<Element> elements)
        {
            var seenInstance = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var seenType     = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var result       = new List<ParamOption>();

            foreach (var el in elements.Take(300))
            {
                try
                {
                    foreach (Parameter p in el.GetOrderedParameters())
                    {
                        if (p.IsReadOnly || p.StorageType != StorageType.String) continue;
                        var name = p.Definition?.Name;
                        if (string.IsNullOrEmpty(name) || !seenInstance.Add(name)) continue;
                        result.Add(new ParamOption { Name = name, IsInstance = true });
                    }
                }
                catch { }

                try
                {
                    var typeEl = ResolveTypeElement(doc, el);
                    if (typeEl == null) continue;
                    foreach (Parameter p in typeEl.GetOrderedParameters())
                    {
                        if (p.IsReadOnly || p.StorageType != StorageType.String) continue;
                        var name = p.Definition?.Name;
                        if (string.IsNullOrEmpty(name) || !seenType.Add(name)) continue;
                        result.Add(new ParamOption { Name = name, IsInstance = false });
                    }
                }
                catch { }
            }

            return result
                .OrderBy(o => !o.IsInstance)
                .ThenBy(o => o.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        // Orders elements by where their center point projects onto a
        // picked detail line's curve -- this is the "Path" renumber mode.
        // Deliberately simpler than DiRoots' separate Crossing/Vertex modes
        // (which hit-test actual geometric crossings against the line):
        // projecting each element's center onto the curve and sorting by
        // the resulting curve parameter gives the same practical result --
        // "number these in the order the line passes them" -- without
        // needing exact intersection geometry, and it works uniformly for
        // any element with a location point or a bounding box, not just
        // ones the line happens to touch precisely.
        public static List<ElementId> OrderByPath(Document doc, IEnumerable<ElementId> elementIds, Curve curve)
        {
            var withT = new List<(ElementId Id, double T)>();
            foreach (var id in elementIds)
            {
                try
                {
                    var el = doc.GetElement(id);
                    var center = GetElementCenter(el);
                    if (center == null) continue;
                    var proj = curve.Project(center);
                    if (proj == null) continue;
                    withT.Add((id, proj.Parameter));
                }
                catch { }
            }
            return withT.OrderBy(x => x.T).Select(x => x.Id).ToList();
        }

        internal static XYZ GetElementCenter(Element el)
        {
            try
            {
                if (el?.Location is LocationPoint lp) return lp.Point;
                var bb = el?.get_BoundingBox(null);
                if (bb != null) return (bb.Min + bb.Max) * 0.5;
            }
            catch { }
            return null;
        }
    }
}

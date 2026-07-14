// ProjectTransferHandler.cs — ME-Tools | Project Transfer
// Mayer E-Concept SRL
//
// Copies Filters, Views (Drafting/Legend only), Sheets and Schedules from the
// active (source) project into another project that is either already open
// in this Revit session, or opened from disk on demand.
//
// Scope note: only Drafting Views and Legends are offered under "Views" —
// Plan/Section/Elevation/3D views are tied to THIS project's own Levels and
// Grids, so copying them into an unrelated project rarely produces a usable
// result (this mirrors Revit's own native "Insert Views from File", which has
// the same limitation). Sheets are copied together with whatever views/
// schedules are placed on them (Revit brings those along automatically), but
// a sheet holding plan/section/3D views inherits that same limitation.
using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace METools.ProjectTransfer
{
    public class ProjectTransferHandler : IExternalEventHandler
    {
        public TransferRequest             Request        { get; set; } = new TransferRequest();
        public Action<List<TransferItem>>  OnSourceLoaded { get; set; }
        public Action<List<OpenDocInfo>>   OnTargetsLoaded{ get; set; }
        public Action<TransferResult>      OnCopyDone     { get; set; }
        public Action<string>              OnStatus       { get; set; }

        public void Execute(UIApplication app)
        {
            try
            {
                switch (Request.Action)
                {
                    case TransferAction.RefreshSource: RefreshSource(app); break;
                    case TransferAction.ListTargets:    ListTargets(app);  break;
                    case TransferAction.OpenTargetFile: OpenTargetFile(app); break;
                    case TransferAction.Copy:           Copy(app);         break;
                }
            }
            catch (Exception ex)
            {
                OnStatus?.Invoke("Error: " + ex.Message);
            }
        }

        // ── Source (active document) inventory ─────────────────────────────
        private void RefreshSource(UIApplication app)
        {
            var doc = app.ActiveUIDocument?.Document;
            if (doc == null) { OnStatus?.Invoke("No active document."); return; }

            var items = new List<TransferItem>();

            try
            {
                items.AddRange(new FilteredElementCollector(doc)
                    .OfClass(typeof(ParameterFilterElement))
                    .Cast<ParameterFilterElement>()
                    .OrderBy(f => f.Name)
                    .Select(f => new TransferItem
                    {
                        Id = f.Id, Name = f.Name, Category = TransferCategory.Filters,
                        SubInfo = Plural(f.GetCategories()?.Count ?? 0, "category", "categories"),
                    }));
            }
            catch { }

            try
            {
                items.AddRange(new FilteredElementCollector(doc)
                    .OfClass(typeof(View))
                    .Cast<View>()
                    .Where(v => !v.IsTemplate && (v.ViewType == ViewType.DraftingView || v.ViewType == ViewType.Legend))
                    .OrderBy(v => v.Name)
                    .Select(v => new TransferItem
                    {
                        Id = v.Id, Name = v.Name, Category = TransferCategory.Views,
                        SubInfo = v.ViewType == ViewType.DraftingView ? "Drafting View" : "Legend",
                    }));
            }
            catch { }

            try
            {
                items.AddRange(new FilteredElementCollector(doc)
                    .OfClass(typeof(ViewSheet))
                    .Cast<ViewSheet>()
                    .OrderBy(s => s.SheetNumber)
                    .Select(s => BuildSheetItem(doc, s)));
            }
            catch { }

            try
            {
                items.AddRange(new FilteredElementCollector(doc)
                    .OfClass(typeof(ViewSchedule))
                    .Cast<ViewSchedule>()
                    .Where(vs => !vs.IsTemplate && !vs.IsTitleblockRevisionSchedule && !vs.Name.StartsWith("<"))
                    .OrderBy(vs => vs.Name)
                    .Select(vs => new TransferItem
                    {
                        Id = vs.Id, Name = vs.Name, Category = TransferCategory.Schedules,
                        SubInfo = Plural(vs.Definition?.GetFieldOrder()?.Count ?? 0, "field", "fields"),
                    }));
            }
            catch { }

            AssignSubGroups(items);
            OnSourceLoaded?.Invoke(items);
        }

        // Auto-detects a naming prefix within each category (e.g. discipline codes
        // like "A_", "E_", "S_" on filters: "A_Brandschutz_AUS" -> group "A").
        // Nothing is hardcoded to any one office's convention — a prefix only
        // becomes a group if it actually recurs often enough within its category.
        private static void AssignSubGroups(List<TransferItem> items)
        {
            const int MinGroupSize = 3;
            foreach (var group in items.GroupBy(i => i.Category))
            {
                var counts = new Dictionary<string, int>();
                var prefixOf = new Dictionary<TransferItem, string>();
                foreach (var item in group)
                {
                    var name = item.Name ?? "";
                    int idx = name.IndexOf('_');
                    string prefix = (idx > 0 && idx <= 6) ? name.Substring(0, idx) : "";
                    prefixOf[item] = prefix;
                    if (!string.IsNullOrEmpty(prefix))
                        counts[prefix] = counts.GetValueOrDefault(prefix) + 1;
                }
                foreach (var item in group)
                {
                    var p = prefixOf[item];
                    item.SubGroup = (!string.IsNullOrEmpty(p) && counts.TryGetValue(p, out var c) && c >= MinGroupSize)
                        ? p : "";
                }
            }
        }

        private TransferItem BuildSheetItem(Document doc, ViewSheet s)
        {
            var placedViews = new List<View>();
            try
            {
                placedViews = s.GetAllPlacedViews()
                    .Select(id => doc.GetElement(id) as View)
                    .Where(v => v != null)
                    .ToList();
            }
            catch { }

            bool hasModelViews = placedViews.Any(v =>
                v.ViewType != ViewType.DraftingView &&
                v.ViewType != ViewType.Legend &&
                v.ViewType != ViewType.Schedule);

            return new TransferItem
            {
                Id       = s.Id,
                Name     = $"{s.SheetNumber} — {s.Name}",
                Category = TransferCategory.Sheets,
                SubInfo  = Plural(placedViews.Count, "view", "views") + " on sheet",
                Warning     = hasModelViews,
                WarningText = hasModelViews
                    ? "Contains a plan/section/elevation/3D view tied to this project's levels & grids — may not transfer cleanly."
                    : "",
            };
        }

        private static string Plural(int n, string singular, string plural)
            => $"{n} {(n == 1 ? singular : plural)}";

        // ── Target document discovery ───────────────────────────────────────
        private void ListTargets(UIApplication app)
        {
            var list = new List<OpenDocInfo>();
            try
            {
                var activeTitle = app.ActiveUIDocument?.Document?.Title;
                foreach (Document d in app.Application.Documents)
                {
                    if (d == null || d.IsLinked) continue;
                    list.Add(new OpenDocInfo { Title = d.Title, IsActive = d.Title == activeTitle });
                }
            }
            catch { }
            OnTargetsLoaded?.Invoke(list.OrderBy(d => d.IsActive).ThenBy(d => d.Title).ToList());
        }

        private void OpenTargetFile(UIApplication app)
        {
            var path = Request.TargetFilePath;
            if (string.IsNullOrWhiteSpace(path)) { OnStatus?.Invoke("No file selected."); return; }

            try
            {
                // Already open? Use that instance instead of trying to reopen it
                // (Revit refuses to open a model that's already open in this session).
                foreach (Document d in app.Application.Documents)
                {
                    if (d != null && !d.IsLinked &&
                        string.Equals(d.PathName, path, StringComparison.OrdinalIgnoreCase))
                    {
                        OnStatus?.Invoke($"\"{d.Title}\" is already open — pick it from the list.");
                        ListTargets(app);
                        return;
                    }
                }

                var opened = app.Application.OpenDocumentFile(path);
                if (opened == null) { OnStatus?.Invoke("Could not open that file."); return; }
                OnStatus?.Invoke($"Opened \"{opened.Title}\".");
            }
            catch (Exception ex)
            {
                OnStatus?.Invoke("Could not open file: " + ex.Message);
            }
            ListTargets(app);
        }

        // ── Copy ─────────────────────────────────────────────────────────────
        private class KeepDestinationTypesHandler : IDuplicateTypeNamesHandler
        {
            public DuplicateTypeAction OnDuplicateTypeNamesFound(DuplicateTypeNamesHandlerArgs args)
                => DuplicateTypeAction.UseDestinationTypes;
        }

        private void Copy(UIApplication app)
        {
            var sourceDoc = app.ActiveUIDocument?.Document;
            if (sourceDoc == null) { OnStatus?.Invoke("No active document."); return; }

            Document targetDoc = null;
            foreach (Document d in app.Application.Documents)
                if (d != null && !d.IsLinked && d.Title == Request.TargetTitle) { targetDoc = d; break; }

            if (targetDoc == null) { OnStatus?.Invoke("Pick or open a target project first."); return; }
            if (targetDoc.Title == sourceDoc.Title)
            { OnStatus?.Invoke("Source and target must be different projects."); return; }

            var ids = Request.ItemIds ?? new List<ElementId>();
            if (ids.Count == 0) { OnStatus?.Invoke("Nothing selected to copy."); return; }

            // Bucket by actual element type so one bad category doesn't take the
            // rest down with it — each gets its own SubTransaction.
            var buckets = new (string Label, List<ElementId> Ids)[]
            {
                ("Filters",   new List<ElementId>()),
                ("Views",     new List<ElementId>()),
                ("Sheets",    new List<ElementId>()),
                ("Schedules", new List<ElementId>()),
            };
            foreach (var id in ids)
            {
                var el = sourceDoc.GetElement(id);
                if (el is ParameterFilterElement) buckets[0].Ids.Add(id);
                else if (el is ViewSheet)          buckets[2].Ids.Add(id);
                else if (el is ViewSchedule)        buckets[3].Ids.Add(id);
                else if (el is View)                buckets[1].Ids.Add(id);
            }

            var result = new TransferResult { Requested = ids.Count };

            using (var tx = new Transaction(targetDoc, "ME-Tools: Project Transfer"))
            {
                tx.Start();
                foreach (var (label, bucketIds) in buckets)
                {
                    if (bucketIds.Count == 0) continue;
                    using (var sub = new SubTransaction(targetDoc))
                    {
                        sub.Start();
                        try
                        {
                            var opts = new CopyPasteOptions();
                            opts.SetDuplicateTypeNamesHandler(new KeepDestinationTypesHandler());
                            ElementTransformUtils.CopyElements(sourceDoc, bucketIds, targetDoc, Transform.Identity, opts);
                            sub.Commit();
                            result.Copied += bucketIds.Count;
                            result.Lines.Add($"{label}: {Plural(bucketIds.Count, "item", "items")} copied");
                        }
                        catch (Exception ex)
                        {
                            if (sub.HasStarted() && !sub.HasEnded()) sub.RollBack();
                            result.Lines.Add($"{label}: failed — {ex.Message}");
                        }
                    }
                }
                tx.Commit();
            }

            OnCopyDone?.Invoke(result);
        }

        public string GetName() => "ME-Tools Project Transfer";
    }
}

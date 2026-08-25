// FindStrayElementsHandler.cs -- ME-Tools | Find Stray Elements
// Mayer E-Concept SRL
//
// BUG FIXED HERE: this tool used to be a modal (ShowDialog) window doing
// all its Revit API access directly, on the reasoning that a modal dialog
// inherits valid API context from whatever launched it. That's true, but
// it has a real cost that was reported live: a modal dialog blocks
// interaction with the rest of Revit entirely while it's open, which
// defeats a lot of the point of a "Go To and then look around" tool.
// Converted to the standard modeless (.Show()) + ExternalEvent pattern
// used everywhere else in this app instead -- all the actual Revit API
// work (scanning, go-to, pruning) lives here now, not in the Window.
using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace METools
{
    public class FindStrayElementsHandler : IExternalEventHandler
    {
        public FindStrayElementsRequest Request { get; set; } = new FindStrayElementsRequest();
        public Action<List<StrayElementInfo>, int, string> OnScanDone { get; set; } // results, viewsScanned, statusMessage
        public Action<List<StrayElementInfo>> OnPruneDone { get; set; }
        public Action<string> OnStatus { get; set; }

        // Same reasoning as originally written: these legitimately span
        // huge, deliberate distances by design (a grid or level is
        // *supposed* to run the length of a building) and would otherwise
        // single-handedly wreck the median for every real piece of content
        // sharing the same view -- they're the frame of reference a view is
        // drawn against, not stray content sitting inside it.
        private static readonly BuiltInCategory[] ExcludedCategories =
        {
            BuiltInCategory.OST_Levels, BuiltInCategory.OST_Grids,
            BuiltInCategory.OST_VolumeOfInterest, BuiltInCategory.OST_Cameras,
            BuiltInCategory.OST_Viewports, BuiltInCategory.OST_Sheets,
            BuiltInCategory.OST_Views, BuiltInCategory.OST_Elev,
            BuiltInCategory.OST_Sections, BuiltInCategory.OST_Matchline,
        };

        // 50x / 500ft, both comfortably conservative: today's real
        // confirmed case was on the order of a MILLION times the normal
        // spread, so anything genuinely worth flagging clears this by a
        // huge margin, while ordinary content practically never will.
        private const double OutlierMultiplier = 50.0;
        private const double AbsoluteFloorFt   = 500.0;

        public string GetName() => "Find Stray Elements";

        public void Execute(UIApplication app)
        {
            var req = Request;
            if (req == null || req.Action == FindStrayAction.None) return;
            var doc = app.ActiveUIDocument?.Document;
            var uiDoc = app.ActiveUIDocument;
            if (doc == null || uiDoc == null) { OnStatus?.Invoke("No active document."); return; }

            if (req.Action == FindStrayAction.Scan) ExecuteScan(doc, uiDoc, req);
            else if (req.Action == FindStrayAction.GoTo) ExecuteGoTo(doc, uiDoc, req);
            else if (req.Action == FindStrayAction.Prune) ExecutePrune(doc, req);
        }

        // ── Scan ──────────────────────────────────────────────────────────
        private void ExecuteScan(Document doc, UIDocument uiDoc, FindStrayElementsRequest req)
        {
            var results = new List<StrayElementInfo>();
            List<View> viewsToScan;
            try
            {
                if (req.WholeModel)
                {
                    viewsToScan = new FilteredElementCollector(doc)
                        .OfClass(typeof(View))
                        .Cast<View>()
                        .Where(v => !v.IsTemplate && v.ViewType != ViewType.Schedule
                                 && v.ViewType != ViewType.Internal && v.ViewType != ViewType.SystemBrowser
                                 && v.ViewType != ViewType.ProjectBrowser && v.ViewType != ViewType.Undefined)
                        .ToList();
                }
                else
                {
                    var av = uiDoc.ActiveView;
                    viewsToScan = av != null ? new List<View> { av } : new List<View>();
                }
            }
            catch (Exception ex) { OnStatus?.Invoke("Scan failed: " + ex.Message); return; }

            if (viewsToScan.Count == 0) { OnScanDone?.Invoke(results, 0, "no_views"); return; }

            int viewsScanned = 0;
            foreach (var view in viewsToScan)
            {
                try { ScanOneView(doc, view, results); viewsScanned++; }
                catch { } // one bad view (e.g. a view type that doesn't support the collector used) shouldn't stop the rest
            }

            OnScanDone?.Invoke(results, viewsScanned, null);
        }

        // Analyzes exactly one view's own content in isolation -- a
        // building-wide site view and a tightly-cropped detail view have
        // completely different "normal" scales, so mixing them into one
        // global analysis would either miss real outliers in the detail
        // view or false-flag ordinary content in the site view. Every view
        // is judged only against itself.
        private void ScanOneView(Document doc, View view, List<StrayElementInfo> results)
        {
            var excludedIds = new HashSet<long>();
            foreach (var bic in ExcludedCategories)
            {
                try { excludedIds.Add((long)bic); } catch { }
            }

            var entries = new List<(Element El, XYZ Center)>();
            List<Element> elems;
            try
            {
                elems = new FilteredElementCollector(doc, view.Id)
                    .WhereElementIsNotElementType()
                    .ToElements()
                    .ToList();
            }
            catch { return; }

            foreach (var el in elems)
            {
                try
                {
                    var catId = el.Category?.Id?.Value;
                    if (catId != null && excludedIds.Contains(catId.Value)) continue;

                    BoundingBoxXYZ bbox;
                    try { bbox = el.get_BoundingBox(view) ?? el.get_BoundingBox(null); }
                    catch { bbox = null; }
                    if (bbox == null) continue;

                    var center = (bbox.Min + bbox.Max) * 0.5;
                    entries.Add((el, center));
                }
                catch { }
            }

            // Nothing meaningful to compare with fewer than a handful of
            // elements -- a "typical spread" computed from 1-2 points
            // isn't a real statistic, and would risk flagging perfectly
            // ordinary content just because there's almost nothing to
            // compare it against.
            if (entries.Count < 5) return;

            double medianX = Median(entries.Select(e => e.Center.X));
            double medianY = Median(entries.Select(e => e.Center.Y));
            double medianZ = Median(entries.Select(e => e.Center.Z));
            var medianCenter = new XYZ(medianX, medianY, medianZ);

            var distances = entries.Select(e => e.Center.DistanceTo(medianCenter)).ToList();
            double medianDistance = Median(distances);
            double threshold = Math.Max(medianDistance * OutlierMultiplier, AbsoluteFloorFt);

            for (int i = 0; i < entries.Count; i++)
            {
                double dist = distances[i];
                if (dist <= threshold) continue;

                var el = entries[i].El;
                ElementId ownerViewId;
                try { ownerViewId = el.OwnerViewId; } catch { ownerViewId = ElementId.InvalidElementId; }

                results.Add(new StrayElementInfo
                {
                    Id             = el.Id,
                    ViewId         = ownerViewId != ElementId.InvalidElementId ? ownerViewId : view.Id,
                    ViewName       = view.Name,
                    Category       = el.Category?.Name ?? "",
                    TypeName       = TypeNameOf(doc, el),
                    DistanceFt     = dist,
                    NormalSpreadFt = medianDistance,
                    Center         = entries[i].Center,
                });
            }
        }

        private static double Median(IEnumerable<double> values)
        {
            var list = values.OrderBy(v => v).ToList();
            if (list.Count == 0) return 0;
            int mid = list.Count / 2;
            return list.Count % 2 == 0 ? (list[mid - 1] + list[mid]) / 2.0 : list[mid];
        }

        private static string TypeNameOf(Document doc, Element el)
        {
            try
            {
                var typeId = el.GetTypeId();
                if (typeId != null && typeId != ElementId.InvalidElementId)
                    return (doc.GetElement(typeId) as ElementType)?.Name ?? "";
            }
            catch { }
            return "";
        }

        // ── Prune -- verifies a cached result set from a previous session
        // still exists, dropping anything that's since been deleted (by far
        // the most common way someone "solves" one of these -- confirmed
        // directly this session, every real stray-element case found and
        // fixed today was fixed by deleting it). A full re-verification of
        // "is it still actually an outlier" would cost about the same as a
        // full rescan of that view, so this deliberately only checks plain
        // existence -- cheap, and covers the case that actually happens in
        // practice. Anything moved-but-not-deleted needs an explicit Scan
        // to notice, same as any other change since the last scan would.
        // ──────────────────────────────────────────────────────────────────
        private void ExecutePrune(Document doc, FindStrayElementsRequest req)
        {
            var survivors = new List<StrayElementInfo>();
            if (req.ToPrune != null)
            {
                foreach (var r in req.ToPrune)
                {
                    try { if (doc.GetElement(r.Id) != null) survivors.Add(r); }
                    catch { }
                }
            }
            OnPruneDone?.Invoke(survivors);
        }

        // ── Go To -- same pattern already proven in Collision Checker and
        // Settings/Imported Objects this session: switch the active view,
        // select the element, then zoom directly to its own bounding box
        // via UIView.ZoomAndCenterRectangle rather than ShowElements --
        // confirmed via research that ShowElements performs an internal
        // "search every view for one that shows this" that can fail with
        // Revit's own native "No good view could be found" dialog, which
        // ZoomAndCenterRectangle's direct "point this view's camera here"
        // approach never triggers. ──────────────────────────────────────
        private void ExecuteGoTo(Document doc, UIDocument uiDoc, FindStrayElementsRequest req)
        {
            try
            {
                var targetView = doc.GetElement(req.TargetViewId) as View;
                if (targetView != null && targetView.Id != uiDoc.ActiveView?.Id)
                    uiDoc.ActiveView = targetView;
                uiDoc.Selection.SetElementIds(new List<ElementId> { req.TargetElementId });
                try { uiDoc.RefreshActiveView(); } catch { }

                var effectiveView = targetView ?? uiDoc.ActiveView;
                BoundingBoxXYZ bbox = null;
                try { bbox = doc.GetElement(req.TargetElementId)?.get_BoundingBox(effectiveView); } catch { }
                if (bbox != null)
                {
                    double padX = Math.Max((bbox.Max.X - bbox.Min.X) * 0.5, 2.0 / 304.8);
                    double padY = Math.Max((bbox.Max.Y - bbox.Min.Y) * 0.5, 2.0 / 304.8);
                    var min = new XYZ(bbox.Min.X - padX, bbox.Min.Y - padY, bbox.Min.Z);
                    var max = new XYZ(bbox.Max.X + padX, bbox.Max.Y + padY, bbox.Max.Z);
                    var openUiView = uiDoc.GetOpenUIViews().FirstOrDefault(uv => uv.ViewId == uiDoc.ActiveView?.Id);
                    openUiView?.ZoomAndCenterRectangle(min, max);
                }
            }
            catch { }
        }
    }
}

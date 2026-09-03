// DuplicateElementHandler.cs -- ME-Tools | Collision Checker: Duplicate Devices
// Mayer E-Concept SRL
//
// A dedicated ExternalEvent handler, separate from the existing
// CollisionCheckerHandler entirely -- this feature has nothing to do with
// conduit/cable-tray-vs-wall clash detection, and keeping it fully
// decoupled means adding it can't risk anything in that much larger,
// already-proven handler.
using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace METools.CollisionChecker
{
    public class DuplicateElementHandler : IExternalEventHandler
    {
        public DuplicateCheckRequest Request { get; set; }
        public Action<DuplicateScanResult> OnScanComplete { get; set; }
        public Action<DuplicateDeleteResult> OnDeleteComplete { get; set; }

        public void Execute(UIApplication app)
        {
            var request = Request;
            if (request == null) return;

            var doc = app.ActiveUIDocument?.Document;

            switch (request.Action)
            {
                case DuplicateCheckAction.Scan:
                    OnScanComplete?.Invoke(DuplicateElementDetector.Scan(doc));
                    break;

                case DuplicateCheckAction.DeleteDuplicates:
                    OnDeleteComplete?.Invoke(DuplicateElementDetector.Delete(doc, request.GroupsToDelete));
                    break;

                case DuplicateCheckAction.GoToGroup:
                    GoToGroup(app, request.TargetGroup);
                    break;
            }
        }

        // Selects and zooms to every element in the group -- the kept
        // copy and all the extras -- since seeing the whole stack sitting
        // on top of each other is the actual point, not just confirming
        // one instance exists. Best-effort: an id that no longer resolves
        // (already deleted, or stale from an older scan) is silently
        // skipped rather than shown as an error, since this is a
        // read-only navigation action, not a destructive one.
        private static void GoToGroup(UIApplication app, DuplicateGroup group)
        {
            var uidoc = app.ActiveUIDocument;
            if (uidoc == null || group == null) return;

            var doc = uidoc.Document;
            var allIds = new List<long> { group.KeepElementId };
            allIds.AddRange(group.DuplicateInstances.Select(d => d.ElementId));

            var elements = new List<Element>();
            foreach (var id in allIds.Distinct())
            {
                try
                {
                    var el = doc.GetElement(new ElementId(id));
                    if (el != null) elements.Add(el);
                }
                catch { /* stale id -- skip */ }
            }

            if (elements.Count == 0) return;

            // ShowElements has no overload taking a List<Element> -- only
            // a single Element, or a collection of ElementId. Converting
            // once and reusing it for both calls, rather than the mismatch
            // that just failed to build.
            var ids = elements.Select(e => e.Id).ToList();
            uidoc.Selection.SetElementIds(ids);
            uidoc.ShowElements(ids);
        }

        public string GetName() => "METools Duplicate Element Handler";
    }
}

// DuplicateElementHandler.cs -- ME-Tools | Collision Checker: Duplicate Devices
// Mayer E-Concept SRL
//
// A dedicated ExternalEvent handler, separate from the existing
// CollisionCheckerHandler entirely -- this feature has nothing to do with
// conduit/cable-tray-vs-wall clash detection, and keeping it fully
// decoupled means adding it can't risk anything in that much larger,
// already-proven handler.
using System;
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
            }
        }

        public string GetName() => "METools Duplicate Element Handler";
    }
}

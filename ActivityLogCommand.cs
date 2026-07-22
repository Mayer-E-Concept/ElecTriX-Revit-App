// ActivityLogCommand.cs -- ME-Tools | Activity Log
// Mayer E-Concept SRL
using Autodesk.Revit.Attributes;
using Autodesk.Revit.UI;
using System;
using System.Linq;

namespace METools.ActivityLog
{
    public class ActivityLogRefreshHandler : IExternalEventHandler
    {
        public Action<System.Collections.Generic.List<ActivityLogEntry>, string> OnResult;

        public void Execute(UIApplication app)
        {
            try
            {
                var doc = app.ActiveUIDocument?.Document;
                if (doc == null) return;
                var projectId = ActivityLogStorage.GetProjectId(doc);
                var entries = ActivityLogStorage.LoadAll(projectId, out string warning);
                OnResult?.Invoke(entries, warning);
            }
            catch { }
        }

        public string GetName() => "ME-Tools Activity Log Refresh";
    }

    // Jumps the active view to a floor plan on the requested level. A
    // separate handler/ExternalEvent from Refresh, since these are two
    // distinct simple actions rather than one shared request/action union.
    public class ActivityLogNavigateHandler : IExternalEventHandler
    {
        public string TargetLevelId; // set just before Raise()
        public Action<bool, string> OnDone; // (success, message-if-any)

        public void Execute(UIApplication app)
        {
            try
            {
                var uidoc = app.ActiveUIDocument;
                var doc = uidoc?.Document;
                if (doc == null) { OnDone?.Invoke(false, "No active document."); return; }

                if (string.IsNullOrWhiteSpace(TargetLevelId) || !int.TryParse(TargetLevelId, out int idInt))
                { OnDone?.Invoke(false, "No level recorded for this entry."); return; }

                var levelId = new Autodesk.Revit.DB.ElementId(idInt);
                var level = doc.GetElement(levelId) as Autodesk.Revit.DB.Level;
                if (level == null)
                { OnDone?.Invoke(false, "That level no longer exists in this project."); return; }

                // Prefer an actual Floor Plan view for this level; fall back
                // to any non-template plan view associated with it (Ceiling
                // Plan, etc.) if that's all there is.
                var plans = new Autodesk.Revit.DB.FilteredElementCollector(doc)
                    .OfClass(typeof(Autodesk.Revit.DB.ViewPlan))
                    .Cast<Autodesk.Revit.DB.ViewPlan>()
                    .Where(v => !v.IsTemplate && v.GenLevel != null && v.GenLevel.Id == levelId)
                    .ToList();

                var target = plans.FirstOrDefault(v => v.ViewType == Autodesk.Revit.DB.ViewType.FloorPlan)
                             ?? plans.FirstOrDefault();

                if (target == null)
                { OnDone?.Invoke(false, $"No plan view found for level '{level.Name}'."); return; }

                uidoc.ActiveView = target;
                OnDone?.Invoke(true, null);
            }
            catch (Exception ex) { OnDone?.Invoke(false, ex.Message); }
        }

        public string GetName() => "ME-Tools Activity Log Go To Level";
    }

    [Transaction(TransactionMode.Manual)]
    public class ActivityLogCommand : IExternalCommand
    {
        private static ActivityLogWindow _window;

        public Result Execute(ExternalCommandData commandData, ref string message, Autodesk.Revit.DB.ElementSet elements)
        {
            Open(commandData.Application);
            return Result.Succeeded;
        }

        public static void Open(UIApplication uiApp)
        {
            if (!METools.LicenseManager.CheckAccessOrExplain()) return;

            var doc = uiApp.ActiveUIDocument?.Document;
            if (doc == null) return;

            if (_window != null && _window.IsVisible)
            { _window.Activate(); _window.Focus(); return; }

            AppSwitcher.Ensure();
            MeToolsWindowBase.RevitHandle = uiApp.MainWindowHandle;

            var projectId = ActivityLogStorage.GetProjectId(doc);
            var entries   = ActivityLogStorage.LoadAll(projectId, out string warning);
            var handler   = new ActivityLogRefreshHandler();
            var evt       = ExternalEvent.Create(handler);
            var navHandler = new ActivityLogNavigateHandler();
            var navEvt      = ExternalEvent.Create(navHandler);

            _window = new ActivityLogWindow(entries, warning, evt, handler, navEvt, navHandler);
            _window.Closed += (s, e) => _window = null;
            _window.Show();
        }
    }
}

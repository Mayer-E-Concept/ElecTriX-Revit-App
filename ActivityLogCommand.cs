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
            var entries = new System.Collections.Generic.List<ActivityLogEntry>();
            string warning = null;
            try
            {
                var doc = app.ActiveUIDocument?.Document;
                if (doc == null)
                {
                    warning = S._("activitylog.no_document");
                }
                else
                {
                    var projectId = ActivityLogStorage.GetProjectId(doc);
                    entries = ActivityLogStorage.LoadAll(projectId, out warning);
                }
            }
            catch (Exception ex)
            {
                warning = string.Format(S._("activitylog.refresh_failed"), ex.Message);
            }
            // Always invoke, success or failure -- otherwise the window has no
            // way to know the refresh finished and stays on "Refreshing..." forever.
            OnResult?.Invoke(entries, warning);
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

                var levelId = new Autodesk.Revit.DB.ElementId((long)idInt);
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

            // Time Tracker -- same shared-folder idea as Activity Log, now
            // shown as extra tabs in this same window rather than a separate
            // tool. See TimeTrackerHandler.cs.
            var ttProjectId = METools.TimeTracker.TimeTrackerStorage.GetProjectId(doc);
            var ttEntries   = METools.TimeTracker.TimeTrackerStorage.LoadAll(ttProjectId, out string ttWarning);
            var ttHandler   = new METools.TimeTracker.TimeTrackerRefreshHandler();
            var ttEvt       = ExternalEvent.Create(ttHandler);
            string currentUser = "";
            try { currentUser = uiApp.Application?.Username ?? ""; } catch { }
            if (string.IsNullOrWhiteSpace(currentUser))
                try { currentUser = Environment.UserName; } catch { }
            // A live "currently tracking" indicator for the document that's
            // open right now -- otherwise My Sessions looks inert the first
            // time anyone opens it, since nothing shows up as a *finished*
            // session until a tracked document actually closes.
            var liveSessionStartUtc = METools.TimeTracker.TimeTrackerWatcher.GetCurrentSessionStart(doc);

            _window = new ActivityLogWindow(entries, warning, evt, handler, navEvt, navHandler,
                                             ttEntries, ttWarning, ttEvt, ttHandler, currentUser, liveSessionStartUtc);
            _window.Closed += (s, e) => _window = null;
            _window.Show();
        }
    }
}

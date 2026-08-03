// TimeTrackerCommand.cs -- ME-Tools | Time Tracker
// Mayer E-Concept SRL
using Autodesk.Revit.Attributes;
using Autodesk.Revit.UI;
using System;

namespace METools.TimeTracker
{
    public class TimeTrackerRefreshHandler : IExternalEventHandler
    {
        public Action<System.Collections.Generic.List<TimeSessionEntry>, string> OnResult;

        public void Execute(UIApplication app)
        {
            var entries = new System.Collections.Generic.List<TimeSessionEntry>();
            string warning = null;
            try
            {
                var doc = app.ActiveUIDocument?.Document;
                if (doc == null)
                {
                    warning = S._("timetracker.no_document");
                }
                else
                {
                    var projectId = TimeTrackerStorage.GetProjectId(doc);
                    entries = TimeTrackerStorage.LoadAll(projectId, out warning);
                }
            }
            catch (Exception ex)
            {
                warning = string.Format(S._("timetracker.refresh_failed"), ex.Message);
            }
            // Always invoke, success or failure -- otherwise the window has no
            // way to know the refresh finished and stays on "Refreshing..." forever.
            OnResult?.Invoke(entries, warning);
        }

        public string GetName() => "ME-Tools Time Tracker Refresh";
    }

    [Transaction(TransactionMode.Manual)]
    public class TimeTrackerCommand : IExternalCommand
    {
        private static TimeTrackerWindow _window;

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

            var projectId = TimeTrackerStorage.GetProjectId(doc);
            var entries   = TimeTrackerStorage.LoadAll(projectId, out string warning);

            string currentUser = "";
            try { currentUser = uiApp.Application?.Username ?? ""; } catch { }
            if (string.IsNullOrWhiteSpace(currentUser))
                try { currentUser = Environment.UserName; } catch { }

            var handler = new TimeTrackerRefreshHandler();
            var evt     = ExternalEvent.Create(handler);

            _window = new TimeTrackerWindow(entries, warning, currentUser, evt, handler);
            _window.Closed += (s, e) => _window = null;
            _window.Show();
        }
    }
}

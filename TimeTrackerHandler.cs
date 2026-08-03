// TimeTrackerHandler.cs -- ME-Tools | Time Tracker
// Mayer E-Concept SRL
//
// Just the refresh ExternalEventHandler -- Time Tracker no longer has its
// own command/window. It's presented as two tabs ("Team Totals" / "My
// Sessions") inside ActivityLogWindow, opened via ActivityLogCommand, since
// both tools are the same underlying idea: per-user, per-project history
// over a shared network folder. The background tracking itself
// (TimeTrackerWatcher/TimeTrackerStorage) is unchanged by this -- only the
// UI entry point moved.
using Autodesk.Revit.UI;
using System;

namespace METools.TimeTracker
{
    public class TimeTrackerRefreshHandler : IExternalEventHandler
    {
        public Action<System.Collections.Generic.List<TimeSessionEntry>, string, DateTime?> OnResult;

        public void Execute(UIApplication app)
        {
            var entries = new System.Collections.Generic.List<TimeSessionEntry>();
            string warning = null;
            DateTime? liveStart = null;
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
                    liveStart = TimeTrackerWatcher.GetCurrentSessionStart(doc);
                }
            }
            catch (Exception ex)
            {
                warning = string.Format(S._("timetracker.refresh_failed"), ex.Message);
            }
            // Always invoke, success or failure -- otherwise the window has no
            // way to know the refresh finished and stays on "Refreshing..." forever.
            OnResult?.Invoke(entries, warning, liveStart);
        }

        public string GetName() => "ME-Tools Time Tracker Refresh";
    }
}

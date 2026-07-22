// AppSwitcher.cs -- ME-Tools | switch between apps from the window title
// Mayer E-Concept SRL
using System.Collections.Generic;
using Autodesk.Revit.UI;

namespace METools
{
    // Runs the target app's open routine inside a valid Revit API context.
    public class AppSwitchHandler : IExternalEventHandler
    {
        public string Target;

        public void Execute(UIApplication app)
        {
            try
            {
                if (Target == "LampPlacer")
                    METools.LampPlacer.LampPlacerCommand.Open(app);
                else if (Target == "FamilyPlacer")
                    METools.FamilyPlacer.FamilyPlacerCommand.Open(app);
                else if (Target == "LevelManager")
                    METools.LevelManager.LevelManagerCommand.Open(app);
                else if (Target == "ProjectTransfer")
                    METools.ProjectTransfer.ProjectTransferCommand.Open(app);
                else if (Target == "CircuitTagger")
                    METools.FamilyPlacer.CircuitTaggerCommand.Open(app);
                else if (Target == "FamilyBrowser")
                    METools.FamilyBrowserCommand.Open(app);
                else if (Target == "FixLevel")
                    TryOpen("METools.FixLevelCommand", app);
                else if (Target == "Statistics")
                    TryOpen("METools.StatisticsCommand", app);
                else if (Target == "Comments")
                    METools.Comments.CommentsCommand.Open(app);
                else if (Target == "ProjectHealthCheck")
                    TryOpen("METools.ProjectHealthCheckCommand", app);
                else if (Target == "ActivityLog")
                    METools.ActivityLog.ActivityLogCommand.Open(app);
            }
            catch { }
        }

        private static void TryOpen(string typeName, UIApplication app)
        {
            try
            {
                var t  = System.Type.GetType($"{typeName}, METools");
                if (t == null)
                {
                    // Try all loaded assemblies
                    foreach (var asm in System.AppDomain.CurrentDomain.GetAssemblies())
                    {
                        t = asm.GetType(typeName);
                        if (t != null) break;
                    }
                }
                if (t == null) return;
                var m = t.GetMethod("Open", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
                m?.Invoke(null, new object[] { app });
            }
            catch { }
        }

        public string GetName() => "ME-Tools App Switch";
    }

    public static class AppSwitcher
    {
        // Registry of switchable apps: Key (internal) -> Label (shown in the menu)
        public static readonly List<(string Key, string Label)> Apps =
            new List<(string, string)>
            {
                ("FamilyPlacer",  "Family Placer"),
                ("FamilyBrowser", "Family Browser"),
                ("LampPlacer",    "Lamp Placer"),
                ("LevelManager",  "Level Manager"),
                ("ProjectTransfer", "Project Transfer"),
                ("FixLevel",      "Fix Level"),
                ("CircuitTagger", "Circuit Tagger"),
                ("Statistics",    "Statistics"),
                ("Comments",      "Comments"),
                ("ProjectHealthCheck", "Project Health Check"),
                ("ActivityLog",   "Activity Log"),
            };

        private static AppSwitchHandler _handler;
        private static ExternalEvent    _event;

        // Must be called from a valid API context (command Execute / Open).
        public static void Ensure()
        {
            if (_event == null)
            {
                _handler = new AppSwitchHandler();
                _event   = ExternalEvent.Create(_handler);
            }
        }

        public static void SwitchTo(string key)
        {
            if (_event == null || _handler == null) return;
            _handler.Target = key;
            _event.Raise();
        }
    }
}

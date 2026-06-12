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
                ("FamilyPlacer", "Family Placer"),
                ("LampPlacer",   "Lamp Placer"),
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

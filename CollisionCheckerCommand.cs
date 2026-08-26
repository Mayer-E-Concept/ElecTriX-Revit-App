// CollisionCheckerCommand.cs -- ME-Tools | Collision Checker (conduits/cable trays vs walls)
// Mayer E-Concept SRL
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using System;

namespace METools.CollisionChecker
{
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class CollisionCheckerCommand : IExternalCommand
    {
        private static CollisionCheckerWindow _window;

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try { Open(commandData.Application); return Result.Succeeded; }
            catch (Exception ex) { message = ex.Message; return Result.Failed; }
        }

        public static void Open(UIApplication uiApp)
        {
            if (!METools.LicenseManager.CheckFullAccessOrExplain()) return;

            if (_window != null && _window.IsVisible)
            {
                _window.Activate(); _window.Focus(); return;
            }

            METools.AppSwitcher.Ensure();
            METools.MeToolsWindowBase.RevitHandle = uiApp.MainWindowHandle;

            // This window's own handler/event, for its interactive Place
            // Holes button -- separate from CollisionCheckerWatcher's own
            // session-long handler/event, which keeps running in the
            // background even after this window is closed.
            var handler  = new CollisionCheckerHandler();
            var extEvent = ExternalEvent.Create(handler);

            _window = new CollisionCheckerWindow(uiApp, extEvent, handler);
            _window.Closed += (s, e) => { _window = null; };
            _window.Show();
        }
    }
}

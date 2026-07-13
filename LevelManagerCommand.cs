// LevelManagerCommand.cs — ME-Tools | Level Manager
// Mayer E-Concept SRL
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace METools.LevelManager
{
    [Transaction(TransactionMode.Manual)]
    public class LevelManagerCommand : IExternalCommand
    {
        private static LevelManagerWindow _window;

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            Open(commandData.Application);
            return Result.Succeeded;
        }

        public static void Open(UIApplication uiApp)
        {
            if (!METools.LicenseManager.CheckAccessOrExplain()) return;

            if (_window != null && _window.IsVisible)
            { _window.Activate(); _window.Focus(); return; }

            AppSwitcher.Ensure();
            MeToolsWindowBase.RevitHandle = uiApp.MainWindowHandle;

            var handler  = new LevelManagerHandler();
            var extEvent = ExternalEvent.Create(handler);

            _window = new LevelManagerWindow(extEvent, handler);
            _window.Closed += (s, e) => _window = null;
            _window.Show();

            // Populate immediately on open.
            handler.Request = new LevelManagerRequest { Action = LevelManagerAction.Refresh };
            extEvent.Raise();
        }
    }
}

// DuplicateFamilyCommand.cs -- ME-Tools | Duplicate Family Finder
// Mayer E-Concept SRL
//
// Not directly registered on the ribbon -- launched from the Diagnostics
// hub only. Same "reuse the existing window if it's still open" pattern as
// Find Stray Elements / Project Health Check.
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace METools
{
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class DuplicateFamilyCommand : IExternalCommand
    {
        private static DuplicateFamilyWindow _window;

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            Open(commandData.Application);
            return Result.Succeeded;
        }

        public static void Open(UIApplication uiApp)
        {
            if (!LicenseManager.CheckFullAccessOrExplain()) return;

            if (_window != null && _window.IsVisible)
            { _window.Activate(); _window.Focus(); return; }

            MeToolsWindowBase.RevitHandle = uiApp.MainWindowHandle;

            var handler = new DuplicateFamilyHandler();
            var evt     = ExternalEvent.Create(handler);

            _window = new DuplicateFamilyWindow(uiApp, evt, handler);
            _window.Closed += (s, e) => _window = null;
            _window.Show();
        }
    }
}

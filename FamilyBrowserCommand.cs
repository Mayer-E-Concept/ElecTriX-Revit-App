// FamilyBrowserCommand.cs -- ME-Tools | Family Browser
// Mayer E-Concept SRL
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace METools
{
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class FamilyBrowserCommand : IExternalCommand
    {
        private static FamilyBrowserWindow _window;

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            Open(commandData.Application);
            return Result.Succeeded;
        }

        public static void Open(UIApplication uiApp)
        {
            if (_window != null && _window.IsVisible)
            {
                _window.Activate();
                _window.Focus();
                return;
            }

            AppSwitcher.Ensure();
            MeToolsWindowBase.RevitHandle = uiApp.MainWindowHandle;

            _window = new FamilyBrowserWindow(uiApp);
            _window.Closed += (s, e) => _window = null;
            _window.Show();
        }
    }
}

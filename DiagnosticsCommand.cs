// DiagnosticsCommand.cs -- ME-Tools | Diagnostics hub
// Mayer E-Concept SRL
//
// BUG FIXED HERE: this used to be modal (ShowDialog), which meant Revit's
// own canvas was completely blocked while the hub -- or anything launched
// from it -- was open. Converted to modeless (.Show()), matching every
// other tool in this app. DiagnosticsWindow itself never touches Revit's
// API directly (it only opens other windows/commands, each of which
// handles its own API access needs), so this conversion needed no
// ExternalEvent plumbing of its own.
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace METools
{
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class DiagnosticsCommand : IExternalCommand
    {
        private static DiagnosticsWindow _window;

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            Open(commandData.Application);
            return Result.Succeeded;
        }

        public static void Open(UIApplication uiApp)
        {
            if (!LicenseManager.CheckAccessOrExplain()) return;
            MeToolsWindowBase.RevitHandle = uiApp.MainWindowHandle;

            if (_window != null && _window.IsVisible)
            { _window.Activate(); _window.Focus(); return; }

            _window = new DiagnosticsWindow(uiApp);
            _window.Closed += (s, e) => _window = null;
            _window.Show();
        }
    }
}

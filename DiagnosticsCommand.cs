// DiagnosticsCommand.cs -- ME-Tools | Diagnostics hub
// Mayer E-Concept SRL
//
// Modeless (.Show()), so Revit's own canvas stays interactive while this
// hub -- or anything launched from it -- is open, unlike the original
// modal (ShowDialog) design. DiagnosticsWindow's own tiles route through
// DiagnosticsHandler's ExternalEvent rather than calling their target
// tools directly -- see that file for why a modeless window's click
// handlers can't safely do that on their own.
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

            var handler = new DiagnosticsHandler();
            var evt     = ExternalEvent.Create(handler);

            _window = new DiagnosticsWindow(uiApp, evt, handler);
            _window.Closed += (s, e) => _window = null;
            _window.Show();
        }
    }
}

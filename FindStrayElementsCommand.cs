// FindStrayElementsCommand.cs -- ME-Tools | Find Stray Elements
// Mayer E-Concept SRL
//
// Not directly registered on the ribbon -- launched from the Diagnostics
// hub instead (see DiagnosticsWindow). Kept as a real IExternalCommand
// with a clean, reusable static Open() anyway, mirroring
// ProjectHealthCheckCommand's own pattern, in case a direct ribbon entry
// is ever wanted later too.
//
// Modeless now (see FindStrayElementsHandler for why), matching
// ProjectHealthCheckCommand's own "reuse the existing window if it's
// still open" pattern rather than opening a second copy.
using System.Collections.Generic;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace METools
{
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class FindStrayElementsCommand : IExternalCommand
    {
        private static FindStrayElementsWindow _window;

        // Session-lifetime cache, not written to disk -- see the
        // constructor comment in FindStrayElementsWindow for why that's
        // the right amount of persistence here (confirmed real gap:
        // closing and reopening this tool used to lose every result).
        public static List<StrayElementInfo> CachedResults { get; set; }

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            Open(commandData.Application);
            return Result.Succeeded;
        }

        public static void Open(UIApplication uiApp)
        {
            if (!LicenseManager.CheckAccessOrExplain()) return;

            if (_window != null && _window.IsVisible)
            { _window.Activate(); _window.Focus(); return; }

            MeToolsWindowBase.RevitHandle = uiApp.MainWindowHandle;

            var handler = new FindStrayElementsHandler();
            var evt     = ExternalEvent.Create(handler);

            _window = new FindStrayElementsWindow(uiApp, evt, handler);
            _window.Closed += (s, e) => _window = null;
            _window.Show();
        }
    }
}

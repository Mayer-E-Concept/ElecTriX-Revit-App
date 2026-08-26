// BatchParamsCommand.cs -- ME-Tools | Batch Params (Renumber + Bulk Edit)
// Mayer E-Concept SRL
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using System;

namespace METools.BatchParams
{
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class BatchParamsCommand : IExternalCommand
    {
        private static BatchParamsWindow _window;

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

            var handler  = new BatchParamsHandler();
            var extEvent = ExternalEvent.Create(handler);

            _window = new BatchParamsWindow(uiApp, extEvent, handler);
            _window.Closed += (s, e) => { _window = null; };
            _window.Show();
        }
    }
}

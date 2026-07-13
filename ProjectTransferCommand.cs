// ProjectTransferCommand.cs — ME-Tools | Project Transfer
// Mayer E-Concept SRL
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace METools.ProjectTransfer
{
    [Transaction(TransactionMode.Manual)]
    public class ProjectTransferCommand : IExternalCommand
    {
        private static ProjectTransferWindow _window;

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

            var handler  = new ProjectTransferHandler();
            var extEvent = ExternalEvent.Create(handler);

            _window = new ProjectTransferWindow(extEvent, handler);
            _window.Closed += (s, e) => _window = null;
            _window.Show();

            handler.Request = new TransferRequest { Action = TransferAction.RefreshSource };
            extEvent.Raise();
        }
    }
}

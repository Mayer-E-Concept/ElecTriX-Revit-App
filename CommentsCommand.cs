// CommentsCommand.cs -- ME-Tools | Project Comments
// Mayer E-Concept SRL
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace METools.Comments
{
    [Transaction(TransactionMode.Manual)]
    public class CommentsCommand : IExternalCommand
    {
        private static CommentsWindow _window;

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

            var handler  = new CommentsHandler();
            var extEvent = ExternalEvent.Create(handler);

            _window = new CommentsWindow(extEvent, handler, uiApp);
            _window.Closed += (s, e) => _window = null;
            _window.Show();

            handler.Request = new CommentsRequest { Action = CommentsAction.Refresh };
            extEvent.Raise();
        }
    }
}

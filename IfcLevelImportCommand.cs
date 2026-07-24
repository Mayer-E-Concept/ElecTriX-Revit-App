// IfcLevelImportCommand.cs -- ME-Tools | IFC Level Importer
// Mayer E-Concept SRL
// Parsing the IFC file is plain file I/O (see IfcLiteReader) and needs no
// Revit API access, so it happens right here, synchronously, before the
// window is even shown -- only the actual Level creation later needs the
// ExternalEvent/transaction machinery.
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Microsoft.Win32;

namespace METools.IfcImport
{
    [Transaction(TransactionMode.Manual)]
    public class IfcLevelImportCommand : IExternalCommand
    {
        private static IfcLevelImportWindow _window;

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

            var dlg = new OpenFileDialog
            {
                Title = "Select an IFC file",
                Filter = "IFC files (*.ifc)|*.ifc|All files (*.*)|*.*",
                CheckFileExists = true,
            };
            if (dlg.ShowDialog() != true) return;

            var parseResult = IfcLiteReader.Parse(dlg.FileName);
            if (!parseResult.Success)
            {
                TaskDialog.Show("ME-Tools -- IFC Level Importer",
                    parseResult.FatalError ?? "Could not read this file for an unknown reason.");
                return;
            }

            AppSwitcher.Ensure();
            MeToolsWindowBase.RevitHandle = uiApp.MainWindowHandle;

            var handler  = new IfcLevelImportHandler();
            var extEvent = ExternalEvent.Create(handler);

            _window = new IfcLevelImportWindow(parseResult, dlg.FileName, extEvent, handler, uiApp);
            _window.Closed += (s, e) => _window = null;
            _window.Show();
        }
    }
}

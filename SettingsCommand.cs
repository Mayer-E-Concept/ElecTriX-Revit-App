// SettingsCommand.cs — ME-Tools
// Mayer E-Concept SRL
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace METools
{
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class SettingsCommand : IExternalCommand
    {
        // Accessible by SettingsWindow to call Revit API during ShowDialog
        public static PushButton    RibbonButton    { get; set; }
        public static UIApplication CurrentApp      { get; private set; }
        public static Document      CurrentDocument => CurrentApp?.ActiveUIDocument?.Document;

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            CurrentApp = commandData.Application;
            MeToolsWindowBase.RevitHandle = CurrentApp.MainWindowHandle;

            var win = new SettingsWindow();
            win.ShowDialog();

            CurrentApp = null;

            if (RibbonButton != null)
                RibbonButton.LongDescription =
                    $"Settings — Mayer E-Concept SRL\n\n" +
                    "Appearance · Language · License · Worksets\n\n" +
                    $"License status: {LicenseManager.StatusText}";

            return Result.Succeeded;
        }
    }
}

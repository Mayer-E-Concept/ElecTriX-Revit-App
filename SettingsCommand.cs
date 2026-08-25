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

        // BUG FIXED HERE: CurrentApp's setter is deliberately private --
        // this is the one legitimate, controlled way for something outside
        // this class (specifically DiagnosticsWindow, opening Imported
        // Objects directly via SettingsWindow's own constructor rather
        // than through SettingsCommand.Execute()) to set it, without
        // opening the property up to being set from just anywhere.
        public static void SetCurrentAppForDirectLaunch(UIApplication app) => CurrentApp = app;

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
                    "Appearance · Language · License · Worksets · Imported Objects\n\n" +
                    $"License status: {LicenseManager.StatusText}";

            return Result.Succeeded;
        }
    }
}

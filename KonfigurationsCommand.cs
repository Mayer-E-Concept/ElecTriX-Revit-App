// KonfigurationsCommand.cs — ME-Tools | Circuit Configuration
// Mayer E-Concept SRL
using System;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using METools.Licensing;

namespace METools.FamilyPlacer
{
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class KonfigurationsCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            // ── License check ────────────────────────────────────────────────
            if (!LicenseCheck.Verify(commandData.Application.MainWindowHandle))
                return Result.Cancelled;

            try
            {
                var doc    = commandData.Application.ActiveUIDocument.Document;
                var fenster = new KonfigFensterWindow(doc);
                bool? result = fenster.ShowDialog();

                if (result == true && fenster.GespeichertErfolgreich)
                {
                    TaskDialog.Show("ME-Tools", "Configuration saved.\n\nNow run Module 2 (Create Circuits).");
                    return Result.Succeeded;
                }
                return Result.Cancelled;
            }
            catch (Exception ex)
            {
                message = ex.Message;
                return Result.Failed;
            }
        }
    }
}

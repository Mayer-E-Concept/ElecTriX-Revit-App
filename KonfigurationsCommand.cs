using System;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace METools.FamilyPlacer
{
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class KonfigurationsCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            if (!METools.LicenseManager.CheckAccessOrExplain()) return Result.Cancelled;

            try
            {
                var doc    = commandData.Application.ActiveUIDocument.Document;
                var fenster = new KonfigFensterWindow(doc);
                bool? result = fenster.ShowDialog();

                if (result == true && fenster.GespeichertErfolgreich)
                {
                    TaskDialog.Show("ME-Tools", "Konfiguration gespeichert.\n\nJetzt Modul 2 (Stromkreise erstellen) ausführen.");
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

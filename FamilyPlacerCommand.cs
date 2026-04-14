// FamilyPlacerCommand.cs — ME-Tools | Family Placer
// Mayer E-Concept SRL
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using System.Collections.Generic;
using METools.Licensing;

namespace METools.FamilyPlacer
{
    [Transaction(TransactionMode.Manual)]
    public class FamilyPlacerCommand : IExternalCommand
    {
        private static FamilyPlacerWindow _window;

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            if (!LicenseCheck.Verify(commandData.Application.MainWindowHandle))
                return Result.Cancelled;

            var uiApp = commandData.Application;
            var uidoc = uiApp.ActiveUIDocument;
            var doc   = uidoc.Document;

            if (_window != null && _window.IsVisible)
            { _window.Activate(); _window.Focus(); return Result.Succeeded; }

            var families = FamilyLoader.LoadFromDocument(doc);
            var levels   = FamilyLoader.LoadLevels(doc);

            using (var tx = new Transaction(doc, "ME-Tools: Activate Symbols"))
            {
                tx.Start();
                foreach (var f in families)
                {
                    if (f.SymbolId != null && f.SymbolId != ElementId.InvalidElementId
                        && doc.GetElement(f.SymbolId) is FamilySymbol sym && !sym.IsActive)
                        sym.Activate();
                }
                tx.Commit();
            }

            ElementId defaultLevelId = ElementId.InvalidElementId;
            try { if (uidoc.ActiveView is ViewPlan vp) defaultLevelId = vp.GenLevel?.Id ?? ElementId.InvalidElementId; }
            catch { }

            var handler  = new FamilyPlacerHandler { AllFamilies = families };
            var extEvent = ExternalEvent.Create(handler);

            _window = new FamilyPlacerWindow(extEvent, handler, families, levels, defaultLevelId);
            _window.Closed += (s, e) => _window = null;
            _window.Show();

            return Result.Succeeded;
        }
    }
}

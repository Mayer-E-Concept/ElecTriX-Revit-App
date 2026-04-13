// FamilyPlacerCommand.cs — ME-Tools | Family Placer
// Mayer E-Concept SRL
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using System.Collections.Generic;

namespace METools.FamilyPlacer
{
    [Transaction(TransactionMode.Manual)]
    public class FamilyPlacerCommand : IExternalCommand
    {
        // Singleton window — keeps window alive between command calls
        private static FamilyPlacerWindow _window;

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var uiApp = commandData.Application;
            var uidoc = uiApp.ActiveUIDocument;
            var doc   = uidoc.Document;

            // If window already open, bring to front
            if (_window != null && _window.IsVisible)
            {
                _window.Activate();
                _window.Focus();
                return Result.Succeeded;
            }

            // Load families + levels from document
            var families = FamilyLoader.LoadFromDocument(doc);
            var levels   = FamilyLoader.LoadLevels(doc);

            // ★ Activate ALL symbols now — so placement calls need NO transaction
            // (transactions right before PromptForFamilyInstancePlacement reset workplane state)
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

            // Get active view level as default
            ElementId defaultLevelId = ElementId.InvalidElementId;
            try
            {
                if (uidoc.ActiveView is ViewPlan vp)
                    defaultLevelId = vp.GenLevel?.Id ?? ElementId.InvalidElementId;
            }
            catch { }

            // Create handler + external event
            var handler  = new FamilyPlacerHandler { AllFamilies = families };
            var extEvent = ExternalEvent.Create(handler);

            // Create and show modeless window
            _window = new FamilyPlacerWindow(extEvent, handler, families, levels, defaultLevelId);
            _window.Closed += (s, e) => _window = null;
            _window.Show();

            return Result.Succeeded;
        }
    }
}

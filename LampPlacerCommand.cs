// LampPlacer/LampPlacerCommand.cs — ME-Tools | Lamp Placer
// Mayer E-Concept SRL
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using System.Collections.Generic;
using System.Linq;
using METools.Licensing;

namespace METools.LampPlacer
{
    [Transaction(TransactionMode.Manual)]
    public class LampPlacerCommand : IExternalCommand
    {
        private static LampPlacerWindow _window;

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            // ── License check ────────────────────────────────────────────────
            if (!LicenseCheck.Verify(commandData.Application.MainWindowHandle))
                return Result.Cancelled;

            var uidoc = commandData.Application.ActiveUIDocument;
            var doc   = uidoc.Document;

            if (_window != null && _window.IsVisible)
            { _window.Activate(); _window.Focus(); return Result.Succeeded; }

            var families = LoadLightingFamilies(doc);

            var handler  = new LampPlacerHandler();
            var extEvent = ExternalEvent.Create(handler);

            _window = new LampPlacerWindow(extEvent, handler, families);
            _window.Closed += (s, e) => _window = null;
            _window.Show();

            return Result.Succeeded;
        }

        private List<LampFamilyInfo> LoadLightingFamilies(Document doc)
        {
            var result = new List<LampFamilyInfo>();
            var cats   = new[]
            {
                BuiltInCategory.OST_LightingFixtures,
                BuiltInCategory.OST_LightingDevices,
            };

            foreach (var cat in cats)
            {
                try
                {
                    var symbols = new FilteredElementCollector(doc)
                        .OfClass(typeof(FamilySymbol))
                        .OfCategory(cat)
                        .Cast<FamilySymbol>();

                    foreach (var sym in symbols)
                        result.Add(new LampFamilyInfo
                        {
                            FamilyName = sym.Family?.Name ?? "",
                            TypeName   = sym.Name ?? "Default",
                            SymbolId   = sym.Id,
                        });
                }
                catch { }
            }

            return result
                .GroupBy(f => f.FamilyName + "|" + f.TypeName)
                .Select(g => g.First())
                .OrderBy(f => f.FamilyName)
                .ThenBy(f => f.TypeName)
                .ToList();
        }
    }
}

// LampPlacer/LampPlacerCommand.cs — ME-Tools | Lamp Placer
// Mayer E-Concept SRL
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using System.Collections.Generic;
using System.Linq;

namespace METools.LampPlacer
{
    [Transaction(TransactionMode.Manual)]
    public class LampPlacerCommand : IExternalCommand
    {
        private static LampPlacerWindow _window;

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var uidoc = commandData.Application.ActiveUIDocument;
            var doc   = uidoc.Document;

            if (_window != null && _window.IsVisible)
            { _window.Activate(); _window.Focus(); return Result.Succeeded; }

            // Load lighting families
            var families = LoadLightingFamilies(doc);

            // Load levels (sorted low → high)
            var levels = LoadLevels(doc);

            // Get active view level as default reference level
            ElementId defaultLevelId = ElementId.InvalidElementId;
            try
            {
                if (uidoc.ActiveView is ViewPlan vp)
                    defaultLevelId = vp.GenLevel?.Id ?? ElementId.InvalidElementId;
            }
            catch { }

            var handler  = new LampPlacerHandler();
            var extEvent = ExternalEvent.Create(handler);

            _window = new LampPlacerWindow(extEvent, handler, families, levels, defaultLevelId);
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

        private List<LevelInfo> LoadLevels(Document doc)
        {
            try
            {
                return new FilteredElementCollector(doc)
                    .OfClass(typeof(Level))
                    .Cast<Level>()
                    .Select(l => new LevelInfo
                    {
                        Id        = l.Id,
                        Name      = l.Name ?? "",
                        Elevation = l.Elevation,
                    })
                    .OrderBy(l => l.Elevation)
                    .ToList();
            }
            catch { return new List<LevelInfo>(); }
        }
    }
}

// CollisionCheckerCommand.cs — ME-Tools | Clash Detector  v6
// Mayer E-Concept SRL
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using System.Collections.Generic;
using System.Linq;

namespace METools.ClashDetector
{
    [Transaction(TransactionMode.Manual)]
    public class ClashDetectorCommand : IExternalCommand
    {
        private static ClashDetectorWindow _window;

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            if (_window != null && _window.IsVisible)
            { _window.Activate(); _window.Focus(); return Result.Succeeded; }

            var doc      = commandData.Application.ActiveUIDocument.Document;
            var fams     = LoadOpeningFamilies(doc);
            var handler  = new ClashDetectorHandler();
            var extEvent = ExternalEvent.Create(handler);

            _window = new ClashDetectorWindow(extEvent, handler, fams);
            _window.Closed += (s, e) => _window = null;
            _window.Show();
            return Result.Succeeded;
        }

        // Strict: only CAx (Auxalia) OR IFC ProvisionForVoid — nothing else
        private List<PenFamilyInfo> LoadOpeningFamilies(Document doc)
        {
            var cats = new[]
            {
                BuiltInCategory.OST_GenericModel,
                BuiltInCategory.OST_MechanicalEquipment,
                BuiltInCategory.OST_SpecialityEquipment,
                BuiltInCategory.OST_DuctAccessory,
                BuiltInCategory.OST_ElectricalFixtures,
            };

            var result = new List<PenFamilyInfo>();
            foreach (var cat in cats)
            {
                try
                {
                    foreach (var sym in new FilteredElementCollector(doc)
                        .OfClass(typeof(FamilySymbol)).OfCategory(cat)
                        .Cast<FamilySymbol>())
                    {
                        bool isCax = IsCax(sym);
                        bool isIfc = IsIfc(sym);
                        if (!isCax && !isIfc) continue;
                        result.Add(new PenFamilyInfo
                        {
                            FamilyName = sym.Family?.Name ?? "",
                            TypeName   = sym.Name ?? "Default",
                            SymbolId   = sym.Id,
                            FamilyId   = sym.Family?.Id ?? ElementId.InvalidElementId,
                            IsCax = isCax, IsIfc = isIfc,
                        });
                    }
                }
                catch { }
            }

            return result
                .GroupBy(f => f.FamilyName + "|" + f.TypeName).Select(g => g.First())
                .OrderByDescending(f => f.IsCax).ThenByDescending(f => f.IsIfc)
                .ThenBy(f => f.FamilyName).ThenBy(f => f.TypeName).ToList();
        }

        // Auxalia CAx: at least 3 of 5 characteristic tray params must exist
        private static bool IsCax(FamilySymbol sym)
        {
            var caxParams = new[]
            { "Trassenhöhe","Trassenbreite","OKB_zu_Achse","X_Überstand_1_User","Z_Überstand_1_User" };
            return caxParams.Count(p => sym.LookupParameter(p) != null) >= 3;
        }

        // IFC ProvisionForVoid: check dedicated param or IFC export type
        private static bool IsIfc(FamilySymbol sym)
        {
            try
            {
                var p1 = sym.LookupParameter("IFC_setze_ProvisionForVoid");
                if (p1 != null && p1.AsInteger() == 1) return true;
                foreach (var n in new[] { "IfcExportType","IFCExportType","Vordefinierter IFC-Typ" })
                {
                    var p = sym.LookupParameter(n);
                    if (p == null) continue;
                    string v = (p.AsString() ?? p.AsValueString() ?? "").ToUpperInvariant();
                    if (v.Contains("PROVISIONFORVOID")) return true;
                }
            }
            catch { }
            return false;
        }
    }
}

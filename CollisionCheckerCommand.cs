// CollisionCheckerCommand.cs — ME-Tools | Clash Detector
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
            var fams     = LoadFamilies(doc);
            var handler  = new ClashDetectorHandler();
            var extEvent = ExternalEvent.Create(handler);

            _window = new ClashDetectorWindow(extEvent, handler, fams);
            _window.Closed += (s, e) => _window = null;
            _window.Show();

            return Result.Succeeded;
        }

        // Load only opening / Provision-for-Void relevant families
        private List<PenFamilyInfo> LoadFamilies(Document doc)
        {
            var cats = new[]
            {
                BuiltInCategory.OST_GenericModel,
                BuiltInCategory.OST_MechanicalEquipment,
                BuiltInCategory.OST_SpecialityEquipment,
                BuiltInCategory.OST_DuctAccessory,
            };

            var result = new List<PenFamilyInfo>();
            foreach (var cat in cats)
            {
                try
                {
                    foreach (var sym in new FilteredElementCollector(doc)
                        .OfClass(typeof(FamilySymbol))
                        .OfCategory(cat)
                        .Cast<FamilySymbol>())
                    {
                        bool isCax = IsCax(sym);
                        bool isIfc = IsIfc(sym);
                        string fn  = sym.Family?.Name ?? "";
                        bool relevant = isCax || isIfc
                            || fn.Contains("Durchbruch") || fn.Contains("Aussparung")
                            || fn.Contains("Opening")    || fn.Contains("Void")
                            || fn.Contains("Provision")  || fn.Contains("AUSSP");
                        if (!relevant) continue;

                        result.Add(new PenFamilyInfo
                        {
                            FamilyName = fn,
                            TypeName   = sym.Name ?? "Default",
                            SymbolId   = sym.Id,
                            FamilyId   = sym.Family?.Id ?? ElementId.InvalidElementId,
                            IsCax      = isCax,
                            IsIfc      = isIfc,
                        });
                    }
                }
                catch { }
            }

            return result
                .GroupBy(f => f.FamilyName + "|" + f.TypeName)
                .Select(g => g.First())
                .OrderByDescending(f => f.IsCax)
                .ThenByDescending(f => f.IsIfc)
                .ThenBy(f => f.FamilyName)
                .ThenBy(f => f.TypeName)
                .ToList();
        }

        // Auxalia CAx family — detected by characteristic parameter names
        private static bool IsCax(FamilySymbol sym)
        {
            string fn = sym.Family?.Name ?? "";
            if (fn.Contains("AUSSP") || fn.Contains("CAx") || fn.Contains("Auxalia")) return true;
            var markers = new[] { "Trassenhöhe", "Trassenbreite", "OKB_zu_Achse", "X_Überstand_1_User" };
            return markers.Any(p => sym.LookupParameter(p) != null);
        }

        // IFC ProvisionForVoid — detected by IFC export parameter
        private static bool IsIfc(FamilySymbol sym)
        {
            try
            {
                foreach (var n in new[] { "IFC_setze_ProvisionForVoid", "IFCExportType", "IfcExportType" })
                {
                    var p = sym.LookupParameter(n);
                    if (p == null) continue;
                    string v = p.AsString() ?? p.AsValueString() ?? "";
                    if (v.Contains("PROVISION") || v.Contains("Provision")) return true;
                }
                string fn = sym.Family?.Name ?? "";
                return fn.Contains("ProvisionForVoid") || fn.Contains("Provision")
                    || fn.Contains("VOID") || fn.Contains("Void");
            }
            catch { return false; }
        }
    }
}

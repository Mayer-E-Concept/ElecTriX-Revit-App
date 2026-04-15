// FixLevelCommand.cs — ME-Tools | Fix Level
// Setzt Bauteillistenebene aller sichtbaren Elemente auf die Ebene der aktiven Ansicht
// Mayer E-Concept SRL
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using System.Collections.Generic;
using System.Linq;

namespace METools
{
    [Transaction(TransactionMode.Manual)]
    public class FixLevelCommand : IExternalCommand
    {
        // Electrical categories to fix
        private static readonly BuiltInCategory[] _cats =
        {
            BuiltInCategory.OST_ElectricalEquipment,
            BuiltInCategory.OST_ElectricalFixtures,
            BuiltInCategory.OST_LightingFixtures,
            BuiltInCategory.OST_LightingDevices,
            BuiltInCategory.OST_DataDevices,
            BuiltInCategory.OST_FireAlarmDevices,
            BuiltInCategory.OST_SecurityDevices,
            BuiltInCategory.OST_TelephoneDevices,
            BuiltInCategory.OST_CommunicationDevices,
            BuiltInCategory.OST_NurseCallDevices,
            BuiltInCategory.OST_MechanicalEquipment,
        };

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var uidoc = commandData.Application.ActiveUIDocument;
            var doc   = uidoc.Document;
            var view  = uidoc.ActiveView;

            // Get current view level
            Level viewLevel = null;
            try { if (view is ViewPlan vp) viewLevel = vp.GenLevel; } catch { }

            if (viewLevel == null)
            {
                TaskDialog.Show("ME-Tools – Fix Level",
                    "Aktive Ansicht hat keine zugeordnete Ebene.\nBitte eine Grundriss-Ansicht öffnen.");
                return Result.Cancelled;
            }

            // Collect all electrical family instances visible in this view
            var allElements = new List<Element>();
            foreach (var cat in _cats)
            {
                try
                {
                    var elems = new FilteredElementCollector(doc, view.Id)
                        .OfCategory(cat)
                        .OfClass(typeof(FamilyInstance))
                        .ToElements();
                    allElements.AddRange(elems);
                }
                catch { }
            }

            if (!allElements.Any())
            {
                TaskDialog.Show("ME-Tools – Fix Level",
                    $"Keine elektrischen Elemente in Ansicht '{view.Name}' gefunden.");
                return Result.Cancelled;
            }

            int fixedCount = 0;

            using (var tx = new Transaction(doc, "ME-Tools: Fix Level"))
            {
                tx.Start();
                foreach (var elem in allElements)
                {
                    try
                    {
                        var p = elem.get_Parameter(BuiltInParameter.INSTANCE_SCHEDULE_ONLY_LEVEL_PARAM);
                        if (p != null && !p.IsReadOnly)
                        { p.Set(viewLevel.Id); fixedCount++; continue; }
                    }
                    catch { }
                    try
                    {
                        var p = elem.get_Parameter(BuiltInParameter.INSTANCE_REFERENCE_LEVEL_PARAM);
                        if (p != null && !p.IsReadOnly)
                        { p.Set(viewLevel.Id); fixedCount++; }
                    }
                    catch { }
                }
                tx.Commit();
            }

            TaskDialog.Show("ME-Tools – Fix Level",
                $"✓ Ebene '{viewLevel.Name}' zugewiesen\n{fixedCount} von {allElements.Count} Elementen aktualisiert.");

            return Result.Succeeded;
        }
    }
}

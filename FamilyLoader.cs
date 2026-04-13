// FamilyLoader.cs — ME-Tools | Family Placer
// Mayer E-Concept SRL
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;

namespace METools.FamilyPlacer
{
    public static class FamilyLoader
    {
        // All relevant electrical categories with display group name
        private static readonly (BuiltInCategory Cat, string Group)[] _categories =
        {
            (BuiltInCategory.OST_ElectricalEquipment,   "Elektrische Geräte"),
            (BuiltInCategory.OST_ElectricalFixtures,    "Elektrische Ausstattung"),
            (BuiltInCategory.OST_LightingFixtures,      "Lampen"),
            (BuiltInCategory.OST_LightingDevices,       "Lichtschalter"),
            (BuiltInCategory.OST_DataDevices,           "Daten"),
            (BuiltInCategory.OST_FireAlarmDevices,      "Brandmelder"),
            (BuiltInCategory.OST_SecurityDevices,       "Sicherheit"),
            (BuiltInCategory.OST_TelephoneDevices,      "Telefon"),
            (BuiltInCategory.OST_CommunicationDevices,  "Kommunikation / AV"),
            (BuiltInCategory.OST_NurseCallDevices,      "Pflegenotruf"),
            (BuiltInCategory.OST_MechanicalEquipment,   "Mechanische Geräte"),
        };

        /// <summary>
        /// Loads all matching FamilySymbols from the document.
        /// Each entry has a CategoryGroup for UI grouping.
        /// </summary>
        public static List<FamilyTypeInfo> LoadFromDocument(Document doc)
        {
            var result = new List<FamilyTypeInfo>();

            foreach (var (cat, group) in _categories)
            {
                try
                {
                    var symbols = new FilteredElementCollector(doc)
                        .OfClass(typeof(FamilySymbol))
                        .OfCategory(cat)
                        .Cast<FamilySymbol>();

                    foreach (var sym in symbols)
                    {
                        try
                        {
                            result.Add(new FamilyTypeInfo
                            {
                                FamilyName     = sym.Family?.Name ?? "Unknown",
                                TypeName       = sym.Name ?? "Default",
                                SymbolId       = sym.Id,
                                CategoryGroup  = group,
                            });
                        }
                        catch { }
                    }
                }
                catch { }
            }

            return result
                .OrderBy(f => f.CategoryGroup)
                .ThenBy(f => f.FamilyName)
                .ThenBy(f => f.TypeName)
                .ToList();
        }

        public static List<string> GetFamilyNames(List<FamilyTypeInfo> all)
            => all.Select(f => f.FamilyName).Distinct().OrderBy(n => n).ToList();

        public static List<string> GetTypeNames(List<FamilyTypeInfo> all, string familyName)
            => all.Where(f => f.FamilyName == familyName)
                  .Select(f => f.TypeName)
                  .OrderBy(n => n).ToList();

        public static ElementId FindSymbolId(List<FamilyTypeInfo> all, string familyName, string typeName)
        {
            if (string.IsNullOrEmpty(familyName)) return ElementId.InvalidElementId;

            // Try exact match first
            var exact = all.FirstOrDefault(f => f.FamilyName == familyName && f.TypeName == typeName);
            if (exact != null) return exact.SymbolId;

            // Fallback: first type of this family (handles empty TypeName from old templates)
            var fallback = all.FirstOrDefault(f => f.FamilyName == familyName);
            return fallback?.SymbolId ?? ElementId.InvalidElementId;
        }

        /// <summary>Loads all levels sorted by elevation.</summary>
        public static List<LevelInfo> LoadLevels(Document doc)
        {
            try
            {
                return new FilteredElementCollector(doc)
                    .OfClass(typeof(Level)).Cast<Level>()
                    .OrderBy(l => l.Elevation)
                    .Select(l => new LevelInfo
                    {
                        Name      = l.Name,
                        Id        = l.Id,
                        Elevation = UnitUtils.ConvertFromInternalUnits(l.Elevation, UnitTypeId.Meters),
                    })
                    .ToList();
            }
            catch { return new List<LevelInfo>(); }
        }
    }
}

// FamilyHeightScanner.cs -- ME-Tools | Family Placer
// Mayer E-Concept SRL -- derives each family's default mounting height (Niveau)
// from its PLACED INSTANCES. No EditFamily, so no family-edit warning dialogs.
// Project-agnostic: nothing hardcoded; reflects the heights actually used in the model.
using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;

namespace METools.FamilyPlacer
{
    public class FamilyHeightEntry
    {
        public string  Family    { get; set; } = "";
        public string  Group     { get; set; } = "";
        public double? DefaultMm { get; set; }   // most-common Niveau among placed instances
    }

    public static class FamilyHeightScanner
    {
        // One pass over placed instances of the Family Placer family set, tallying Niveau per family.
        public static List<FamilyHeightEntry> Scan(Document doc)
        {
            var result = new List<FamilyHeightEntry>();
            if (doc == null) return result;

            var all      = FamilyLoader.LoadFromDocument(doc);
            var groupOf  = new Dictionary<string, string>(StringComparer.Ordinal);
            var famOfSym = new Dictionary<ElementId, string>();
            foreach (var f in all)
            {
                if (string.IsNullOrEmpty(f.FamilyName)) continue;
                if (!groupOf.ContainsKey(f.FamilyName)) groupOf[f.FamilyName] = f.CategoryGroup;
                if (f.SymbolId != null && f.SymbolId != ElementId.InvalidElementId)
                    famOfSym[f.SymbolId] = f.FamilyName;
            }
            if (famOfSym.Count == 0) return result;

            var tally = new Dictionary<string, Dictionary<long, int>>(StringComparer.Ordinal);
            try
            {
                var collector = new FilteredElementCollector(doc)
                    .OfClass(typeof(FamilyInstance))
                    .WhereElementIsNotElementType();
                foreach (var e in collector)
                {
                    try
                    {
                        if (!(e is FamilyInstance fi) || fi.Symbol == null) continue;
                        if (!famOfSym.TryGetValue(fi.Symbol.Id, out string fam)) continue;
                        var p = fi.LookupParameter("Niveau");
                        if (p == null || p.StorageType != StorageType.Double || !p.HasValue) continue;
                        long key = (long)Math.Round(
                            UnitUtils.ConvertFromInternalUnits(p.AsDouble(), UnitTypeId.Millimeters));
                        if (!tally.TryGetValue(fam, out var d)) { d = new Dictionary<long, int>(); tally[fam] = d; }
                        d[key] = d.TryGetValue(key, out int c) ? c + 1 : 1;
                    }
                    catch { }
                }
            }
            catch { }

            foreach (var kv in tally)
            {
                long best = kv.Value.OrderByDescending(x => x.Value).First().Key;
                result.Add(new FamilyHeightEntry
                {
                    Family    = kv.Key,
                    Group     = groupOf.TryGetValue(kv.Key, out var g) ? g : "",
                    DefaultMm = best,
                });
            }

            return result.OrderBy(e => e.Group).ThenBy(e => e.Family).ToList();
        }

        // Most-common Niveau (mm) among placed instances of a single family, or null.
        public static double? MostCommonNiveauMm(Document doc, string familyName)
        {
            if (doc == null || string.IsNullOrEmpty(familyName)) return null;
            try
            {
                Family fam = null;
                foreach (var el in new FilteredElementCollector(doc).OfClass(typeof(Family)))
                    if (el is Family ff && ff.Name == familyName) { fam = ff; break; }
                if (fam == null) return null;

                var symIds = new HashSet<ElementId>(fam.GetFamilySymbolIds());
                if (symIds.Count == 0) return null;

                var collector = new FilteredElementCollector(doc)
                    .OfClass(typeof(FamilyInstance))
                    .WhereElementIsNotElementType();
                ElementId catId = fam.FamilyCategory?.Id;
                if (catId != null && catId != ElementId.InvalidElementId)
                    collector = collector.OfCategoryId(catId);

                var counts = new Dictionary<long, int>();
                foreach (var e in collector)
                {
                    try
                    {
                        if (!(e is FamilyInstance fi) || fi.Symbol == null) continue;
                        if (!symIds.Contains(fi.Symbol.Id)) continue;
                        var p = fi.LookupParameter("Niveau");
                        if (p == null || p.StorageType != StorageType.Double || !p.HasValue) continue;
                        long key = (long)Math.Round(
                            UnitUtils.ConvertFromInternalUnits(p.AsDouble(), UnitTypeId.Millimeters));
                        counts[key] = counts.TryGetValue(key, out int c) ? c + 1 : 1;
                    }
                    catch { }
                }
                if (counts.Count == 0) return null;
                return counts.OrderByDescending(x => x.Value).First().Key;
            }
            catch { return null; }
        }
    }
}

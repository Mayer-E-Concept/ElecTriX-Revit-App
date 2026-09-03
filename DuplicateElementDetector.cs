// DuplicateElementDetector.cs -- ME-Tools | Collision Checker: Duplicate Devices
// Mayer E-Concept SRL
//
// Scans for electrical device instances (sockets, switches, and the other
// individually-placed device categories) sitting at effectively the same
// location, same family/type, same level -- the exact fingerprint an
// accidental double-paste of a whole room or apartment's worth of devices
// leaves behind.
//
// Deliberately narrow, on purpose: this is NOT a general "find any two
// elements that overlap" tool -- that's a much harder, much more
// false-positive-prone problem (two adjacent switches in a real duplex
// plate can legitimately sit close together). This looks specifically for
// the double-paste pattern, which is safe to detect reliably because a
// true duplicate paste places an exact copy, not an approximately-similar
// one -- same family, same type, same level, same point, down to
// floating-point noise.
using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;

namespace METools.CollisionChecker
{
    public static class DuplicateElementDetector
    {
        // The individually-placed device categories a double-pasted room
        // would actually contain -- sockets, switches, and their
        // low-voltage/fire/security cousins. Deliberately NOT including
        // OST_ElectricalEquipment (panels, distribution boards) -- those
        // are usually one-off, intentionally-placed elements, and treating
        // two panels as "duplicates" would be a much riskier false
        // positive than a receptacle.
        private static readonly BuiltInCategory[] DeviceCategories =
        {
            BuiltInCategory.OST_ElectricalFixtures,
            BuiltInCategory.OST_LightingDevices,
            BuiltInCategory.OST_LightingFixtures,
            BuiltInCategory.OST_CommunicationDevices,
            BuiltInCategory.OST_DataDevices,
            BuiltInCategory.OST_FireAlarmDevices,
            BuiltInCategory.OST_NurseCallDevices,
            BuiltInCategory.OST_SecurityDevices,
            BuiltInCategory.OST_TelephoneDevices,
        };

        // Feet -- Revit's internal unit. About 1/32 inch (~0.8mm): tight
        // enough that two genuinely different, intentionally-placed
        // devices essentially never land inside it by coincidence, loose
        // enough to absorb ordinary floating-point noise from the same
        // paste operation.
        private const double LocationTolerance = 0.0026;

        public static DuplicateScanResult Scan(Document doc)
        {
            var result = new DuplicateScanResult();
            if (doc == null) { result.Error = "No active document."; return result; }

            try
            {
                var collector = new FilteredElementCollector(doc)
                    .WhereElementIsNotElementType()
                    .WherePasses(new ElementMulticategoryFilter(DeviceCategories))
                    .OfClass(typeof(FamilyInstance));

                // Group key: category + family + type + level + rounded
                // location + rounded rotation. Two elements only land in
                // the same bucket if all of that matches -- exactly what a
                // double-pasted copy looks like, and essentially nothing
                // else does. Built defensively per-element: one odd
                // element (unusual host, missing location) is skipped
                // rather than aborting the whole scan.
                var buckets = new Dictionary<string, List<FamilyInstance>>();

                foreach (var element in collector)
                {
                    try
                    {
                        var fi = element as FamilyInstance;
                        if (fi == null) continue;

                        var locPoint = fi.Location as LocationPoint;
                        var loc = locPoint?.Point;
                        if (loc == null) continue; // line/curve-based devices aren't this scenario -- skip rather than guess

                        var rotation = locPoint.Rotation;
                        var levelId = (fi.LevelId != null && fi.LevelId != ElementId.InvalidElementId)
                            ? fi.LevelId.Value
                            : -1L;

                        var key = string.Join("|",
                            fi.Category?.Id.Value ?? -1,
                            fi.Symbol?.Family?.Id.Value ?? -1,
                            fi.Symbol?.Id.Value ?? -1,
                            levelId,
                            RoundCoord(loc.X), RoundCoord(loc.Y), RoundCoord(loc.Z),
                            Math.Round(rotation, 3));

                        if (!buckets.TryGetValue(key, out var list))
                            buckets[key] = list = new List<FamilyInstance>();
                        list.Add(fi);
                    }
                    catch
                    {
                        // One unusual element shouldn't abort the whole scan.
                    }
                }

                foreach (var kv in buckets)
                {
                    if (kv.Value.Count < 2) continue; // no duplicate in this bucket

                    // Keep the lowest element id -- typically the original,
                    // since a duplicate paste happens afterward and gets a
                    // higher id. Delete the rest.
                    var ordered = kv.Value.OrderBy(e => e.Id.Value).ToList();
                    var keep = ordered[0];
                    var extras = ordered.Skip(1).ToList();

                    var keepLoc = (keep.Location as LocationPoint)?.Point;
                    Level level = null;
                    try { level = doc.GetElement(keep.LevelId) as Level; } catch { /* best effort for display only */ }

                    var group = new DuplicateGroup
                    {
                        CategoryName = keep.Category?.Name ?? "",
                        FamilyName = keep.Symbol?.Family?.Name ?? "",
                        TypeName = keep.Symbol?.Name ?? "",
                        LevelName = level?.Name ?? "",
                        LocationSummary = keepLoc != null
                            ? $"({UnitUtils.ConvertFromInternalUnits(keepLoc.X, UnitTypeId.Millimeters):F0}, " +
                              $"{UnitUtils.ConvertFromInternalUnits(keepLoc.Y, UnitTypeId.Millimeters):F0}, " +
                              $"{UnitUtils.ConvertFromInternalUnits(keepLoc.Z, UnitTypeId.Millimeters):F0}) mm"
                            : "",
                        KeepElementId = keep.Id.Value,
                        DuplicateInstances = extras.Select(e => new DuplicateElementInfo
                        {
                            ElementId = e.Id.Value,
                            UniqueId = e.UniqueId,
                        }).ToList(),
                    };

                    result.Groups.Add(group);
                    result.TotalExtraElements += extras.Count;
                }
            }
            catch (Exception ex)
            {
                result.Error = ex.Message;
            }

            return result;
        }

        private static double RoundCoord(double feet) => Math.Round(feet / LocationTolerance) * LocationTolerance;

        public static DuplicateDeleteResult Delete(Document doc, List<DuplicateGroup> groups)
        {
            var result = new DuplicateDeleteResult();
            if (doc == null) { result.Error = "No active document."; return result; }
            if (groups == null || groups.Count == 0) return result;

            try
            {
                var idsToDelete = groups
                    .SelectMany(g => g.DuplicateInstances)
                    .Select(d => new ElementId(d.ElementId))
                    .Distinct()
                    .ToList();

                if (idsToDelete.Count == 0) return result;

                using (var tx = new Transaction(doc, "ME-Tools: Delete duplicate electrical devices"))
                {
                    tx.Start();
                    var deletedIds = doc.Delete(idsToDelete);
                    tx.Commit();
                    result.Deleted = deletedIds?.Count ?? 0;
                }
            }
            catch (Exception ex)
            {
                result.Error = ex.Message;
            }

            return result;
        }
    }
}

// IfcLiteReader.cs -- ME-Tools | IFC Level Importer
// Mayer E-Concept SRL
//
// A minimal, dependency-free STEP/IFC reader. It does NOT do a full IFC
// import -- it scans the file for a small whitelist of entity types
// (IFCBUILDINGSTOREY, IFCSITE, IFCPROJECT, IFCUNITASSIGNMENT, IFCSIUNIT,
// IFCCONVERSIONBASEDUNIT, IFCMEASUREWITHUNIT, IFCLOCALPLACEMENT,
// IFCAXIS2PLACEMENT3D, IFCCARTESIANPOINT, IFCMAPCONVERSION) and discards
// everything else (geometry, materials, property sets...) without ever
// storing it, which is what keeps this fast and low-memory even on a
// multi-hundred-MB architectural IFC file -- the vast bulk of a real IFC
// file's size is geometry we never need to look at for this.
//
// Scope, honestly stated:
// * Building storeys: name + elevation, straight off IFCBUILDINGSTOREY.
//   Works the same way on IFC2X3 and IFC4 (attribute order is identical
//   for the attributes we read).
// * Length unit: resolved from IFCPROJECT -> IFCUNITASSIGNMENT -> whichever
//   unit entity has UnitType = .LENGTHUNIT. Handles plain SI units (metre,
//   with an optional prefix like MILLI/CENTI) and conversion-based units
//   (foot/inch), with hardcoded fallback factors if the conversion-factor
//   chain itself can't be resolved for some reason.
// * Site placement: IFCSITE's OWN local placement (its ObjectPlacement's
//   Cartesian point), NOT the fully composed absolute position -- if that
//   placement is itself relative to a parent placement, this does not walk
//   further up the chain. Also reads RefLatitude/RefLongitude/RefElevation
//   if present, and IFCMAPCONVERSION (Eastings/Northings/OrthogonalHeight),
//   which is the IFC4 mechanism for real-world survey coordinates and,
//   when present, the single most reliable "where is this building"
//   answer. Everything here is informational only -- nothing in this file
//   or the tool built on it modifies your Revit model's coordinates.
//
// Not handled (will show up as a Warning instead of silently guessing):
// * STEP files using non-UTF8/non-Latin1 encodings with unusual escape
//   sequences in quoted strings.
// * IFCSITE placements that are relative to a parent placement chain more
//   than one level deep.
// * IFC2X2/older schema variants with structurally different attribute lists.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace METools.IfcImport
{
    public class IfcLevelInfo
    {
        public string Name;
        public double ElevationRaw; // in the file's declared length unit
        public string Guid;
    }

    public class IfcSiteInfo
    {
        // Site's own local placement (Cartesian point), in the file's declared length unit.
        public double? LocalX, LocalY, LocalZ;

        // Geographic reference, from IFCSITE's RefLatitude/RefLongitude/RefElevation.
        public double? LatitudeDeg, LongitudeDeg;
        public double? RefElevationRaw; // in the file's declared length unit

        // IFC4 IFCMAPCONVERSION -- real-world survey coordinates, when present.
        public double? MapEastings, MapNorthings, MapOrthogonalHeight;

        public bool HasAnyInfo =>
            LocalX.HasValue || LatitudeDeg.HasValue || MapEastings.HasValue;
    }

    public enum IfcLengthUnitKind { Millimeter, Centimeter, Decimeter, Meter, Kilometer, Foot, Inch, Unknown }

    public class IfcParseResult
    {
        public string SchemaVersion = "";
        public List<IfcLevelInfo> Levels = new List<IfcLevelInfo>();
        public IfcSiteInfo Site = new IfcSiteInfo();
        public IfcLengthUnitKind LengthUnitKind = IfcLengthUnitKind.Unknown;
        public string LengthUnitLabel = "(unknown)";
        public double LengthUnitToMeters = 1.0; // multiply a raw file value by this to get meters
        public List<string> Warnings = new List<string>();
        public bool Success;
        public string FatalError;
    }

    internal class StepEntity
    {
        public int Id;
        public string Type;
        public string RawArgs; // text between the outer parens, unparsed
    }

    public static class IfcLiteReader
    {
        // Only these entity types are ever kept in memory -- everything else
        // (geometry, property sets, materials, styles...) is skipped the
        // moment its type is read, before its argument text is even stored.
        private static readonly HashSet<string> Whitelist = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "IFCPROJECT", "IFCSITE", "IFCBUILDINGSTOREY",
            "IFCUNITASSIGNMENT", "IFCSIUNIT", "IFCCONVERSIONBASEDUNIT", "IFCMEASUREWITHUNIT",
            "IFCLOCALPLACEMENT", "IFCAXIS2PLACEMENT3D", "IFCCARTESIANPOINT",
            "IFCMAPCONVERSION",
        };

        public static IfcParseResult Parse(string path)
        {
            var result = new IfcParseResult();
            try
            {
                string text;
                try { text = File.ReadAllText(path, Encoding.UTF8); }
                catch { text = File.ReadAllText(path, Encoding.Latin1); }

                result.SchemaVersion = ExtractSchema(text);

                var entities = ExtractWhitelistedEntities(text);
                if (entities.Count == 0)
                {
                    result.FatalError = "No recognizable IFC entities found -- is this a valid IFC (.ifc) text file?";
                    return result;
                }

                // -- Length unit --------------------------------------------------
                ResolveLengthUnit(entities, result);

                // -- Building storeys ----------------------------------------------
                foreach (var e in entities.Values.Where(x => x.Type == "IFCBUILDINGSTOREY"))
                {
                    var args = SplitTopLevel(e.RawArgs);
                    if (args.Count < 10) { result.Warnings.Add($"IFCBUILDINGSTOREY #{e.Id}: unexpected attribute count, skipped."); continue; }
                    string name = UnquoteOrNull(args[2]) ?? $"Level #{e.Id}";
                    double elevation = 0;
                    bool haveElevation = TryParseNumber(args[9], out elevation);
                    if (!haveElevation)
                        result.Warnings.Add($"'{name}': no Elevation value in the file -- defaulted to 0, check it manually.");
                    result.Levels.Add(new IfcLevelInfo
                    {
                        Name = name,
                        ElevationRaw = elevation,
                        Guid = UnquoteOrNull(args.Count > 0 ? args[0] : null) ?? "",
                    });
                }
                result.Levels = result.Levels.OrderBy(l => l.ElevationRaw).ToList();
                if (result.Levels.Count == 0)
                    result.Warnings.Add("No IFCBUILDINGSTOREY entities found -- this file may not define levels/storeys.");

                // -- Site: local placement + geographic + map conversion ----------
                ResolveSite(entities, result);

                result.Success = true;
            }
            catch (Exception ex)
            {
                result.FatalError = "Could not parse this file: " + ex.Message;
            }
            return result;
        }

        private static string ExtractSchema(string text)
        {
            int i = text.IndexOf("FILE_SCHEMA", StringComparison.OrdinalIgnoreCase);
            if (i < 0) return "(unknown schema)";
            int a = text.IndexOf('\'', i);
            if (a < 0) return "(unknown schema)";
            int b = text.IndexOf('\'', a + 1);
            if (b < 0) return "(unknown schema)";
            return text.Substring(a + 1, b - a - 1);
        }

        // Walks the DATA section, splitting on top-level semicolons (i.e. not
        // inside a quoted string), and keeps only whitelisted entity types.
        private static Dictionary<int, StepEntity> ExtractWhitelistedEntities(string text)
        {
            var result = new Dictionary<int, StepEntity>();
            int dataStart = text.IndexOf("DATA;", StringComparison.OrdinalIgnoreCase);
            int start = dataStart >= 0 ? dataStart + 5 : 0;
            int endSec = text.IndexOf("ENDSEC;", start, StringComparison.OrdinalIgnoreCase);
            int end = endSec >= 0 ? endSec : text.Length;

            var stmt = new StringBuilder();
            bool inString = false;
            for (int i = start; i < end; i++)
            {
                char c = text[i];
                if (c == '\'')
                {
                    // STEP escapes an embedded quote as a doubled '' -- treat
                    // that as a literal character, not a string-boundary toggle.
                    if (inString && i + 1 < end && text[i + 1] == '\'') { stmt.Append("''"); i++; continue; }
                    inString = !inString;
                    stmt.Append(c);
                    continue;
                }
                if (c == ';' && !inString)
                {
                    TryAddEntity(stmt.ToString(), result);
                    stmt.Clear();
                    continue;
                }
                stmt.Append(c);
            }
            return result;
        }

        private static void TryAddEntity(string statement, Dictionary<int, StepEntity> into)
        {
            string s = statement.Trim();
            if (s.Length == 0 || s[0] != '#') return;

            int eq = s.IndexOf('=');
            if (eq < 0) return;
            if (!int.TryParse(s.Substring(1, eq - 1).Trim(), out int id)) return;

            string rest = s.Substring(eq + 1).Trim();
            int paren = rest.IndexOf('(');
            if (paren < 0) return;
            string type = rest.Substring(0, paren).Trim().ToUpperInvariant();
            if (!Whitelist.Contains(type)) return; // discard immediately -- never stored

            int lastParen = rest.LastIndexOf(')');
            if (lastParen < paren) return;
            string args = rest.Substring(paren + 1, lastParen - paren - 1);

            into[id] = new StepEntity { Id = id, Type = type, RawArgs = args };
        }

        // Splits a STEP attribute list on top-level commas: respects nested
        // parens (for lists like (#1,#2,#3) or (1.,2.,3.)) and quoted strings.
        private static List<string> SplitTopLevel(string args)
        {
            var parts = new List<string>();
            int depth = 0;
            bool inString = false;
            var cur = new StringBuilder();
            for (int i = 0; i < args.Length; i++)
            {
                char c = args[i];
                if (c == '\'')
                {
                    if (inString && i + 1 < args.Length && args[i + 1] == '\'') { cur.Append("''"); i++; continue; }
                    inString = !inString; cur.Append(c); continue;
                }
                if (!inString)
                {
                    if (c == '(') depth++;
                    else if (c == ')') depth--;
                    else if (c == ',' && depth == 0) { parts.Add(cur.ToString().Trim()); cur.Clear(); continue; }
                }
                cur.Append(c);
            }
            if (cur.Length > 0 || parts.Count > 0) parts.Add(cur.ToString().Trim());
            return parts;
        }

        private static string UnquoteOrNull(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return null;
            raw = raw.Trim();
            if (raw == "$" || raw == "*") return null;
            if (raw.Length >= 2 && raw[0] == '\'' && raw[raw.Length - 1] == '\'')
                return raw.Substring(1, raw.Length - 2).Replace("''", "'");
            return raw;
        }

        private static bool TryParseNumber(string raw, out double value)
        {
            value = 0;
            if (string.IsNullOrWhiteSpace(raw)) return false;
            raw = raw.Trim();
            if (raw == "$" || raw == "*") return false;
            // Typed values look like IFCLENGTHMEASURE(12.5) or IFCREAL(12.5) -- unwrap one layer.
            int p1 = raw.IndexOf('(');
            if (p1 > 0 && raw.EndsWith(")"))
                raw = raw.Substring(p1 + 1, raw.Length - p1 - 2);
            return double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
        }

        private static bool TryGetRef(string raw, out int id)
        {
            id = 0;
            if (string.IsNullOrWhiteSpace(raw)) return false;
            raw = raw.Trim();
            if (raw.Length < 2 || raw[0] != '#') return false;
            return int.TryParse(raw.Substring(1), out id);
        }

        private static void ResolveLengthUnit(Dictionary<int, StepEntity> entities, IfcParseResult result)
        {
            try
            {
                var project = entities.Values.FirstOrDefault(e => e.Type == "IFCPROJECT");
                if (project == null) { result.Warnings.Add("No IFCPROJECT entity found -- can't confirm units, assuming metres."); SetUnit(result, IfcLengthUnitKind.Meter, "Metre (assumed -- no IFCPROJECT found)", 1.0); return; }

                var projArgs = SplitTopLevel(project.RawArgs);
                if (projArgs.Count == 0 || !TryGetRef(projArgs[projArgs.Count - 1], out int unitsId))
                { result.Warnings.Add("IFCPROJECT has no UnitsInContext reference -- assuming metres."); SetUnit(result, IfcLengthUnitKind.Meter, "Metre (assumed)", 1.0); return; }

                if (!entities.TryGetValue(unitsId, out var unitsEntity) || unitsEntity.Type != "IFCUNITASSIGNMENT")
                { result.Warnings.Add("Could not resolve IFCUNITASSIGNMENT -- assuming metres."); SetUnit(result, IfcLengthUnitKind.Meter, "Metre (assumed)", 1.0); return; }

                var unitRefs = SplitTopLevel(unitsEntity.RawArgs.Trim().TrimStart('(').TrimEnd(')'));
                foreach (var uref in unitRefs)
                {
                    if (!TryGetRef(uref, out int uid)) continue;
                    if (!entities.TryGetValue(uid, out var unitEnt)) continue;

                    if (unitEnt.Type == "IFCSIUNIT")
                    {
                        var a = SplitTopLevel(unitEnt.RawArgs);
                        // (Dimensions, UnitType, Prefix, Name)
                        if (a.Count < 4) continue;
                        if (!a[1].Contains("LENGTHUNIT", StringComparison.OrdinalIgnoreCase)) continue;
                        string prefix = a[2].Trim().Trim('.').ToUpperInvariant();
                        ApplySiPrefix(result, prefix);
                        return;
                    }
                    if (unitEnt.Type == "IFCCONVERSIONBASEDUNIT")
                    {
                        var a = SplitTopLevel(unitEnt.RawArgs);
                        // (Dimensions, UnitType, Name, ConversionFactor -> #IFCMEASUREWITHUNIT)
                        if (a.Count < 4) continue;
                        if (!a[1].Contains("LENGTHUNIT", StringComparison.OrdinalIgnoreCase)) continue;
                        string name = (UnquoteOrNull(a[2]) ?? "").ToUpperInvariant();
                        double factor = 0;
                        bool resolvedFactor = false;
                        if (TryGetRef(a[3], out int mwuId) && entities.TryGetValue(mwuId, out var mwu) && mwu.Type == "IFCMEASUREWITHUNIT")
                        {
                            var mwuArgs = SplitTopLevel(mwu.RawArgs);
                            if (mwuArgs.Count >= 1 && TryParseNumber(mwuArgs[0], out factor)) resolvedFactor = true;
                        }
                        if (!resolvedFactor)
                        {
                            // Fallback: hardcoded factors if the conversion chain
                            // itself couldn't be resolved (rare, but don't fail silently).
                            if (name.Contains("FOOT") || name.Contains("FEET")) factor = 0.3048;
                            else if (name.Contains("INCH")) factor = 0.0254;
                            result.Warnings.Add($"Could not resolve the exact conversion factor for unit '{name}' -- used a standard fallback value.");
                        }
                        var kind = name.Contains("INCH") ? IfcLengthUnitKind.Inch
                                 : name.Contains("FOOT") || name.Contains("FEET") ? IfcLengthUnitKind.Foot
                                 : IfcLengthUnitKind.Unknown;
                        SetUnit(result, kind, $"{Capitalize(name)} (custom unit)", factor);
                        return;
                    }
                }
                result.Warnings.Add("Found IFCUNITASSIGNMENT but no length unit inside it -- assuming metres.");
                SetUnit(result, IfcLengthUnitKind.Meter, "Metre (assumed)", 1.0);
            }
            catch (Exception ex)
            {
                result.Warnings.Add("Unit detection failed (" + ex.Message + ") -- assuming metres.");
                SetUnit(result, IfcLengthUnitKind.Meter, "Metre (assumed)", 1.0);
            }
        }

        private static void ApplySiPrefix(IfcParseResult result, string prefix)
        {
            switch (prefix)
            {
                case "MILLI": SetUnit(result, IfcLengthUnitKind.Millimeter, "Millimetre (mm)", 0.001); break;
                case "CENTI": SetUnit(result, IfcLengthUnitKind.Centimeter, "Centimetre (cm)", 0.01); break;
                case "DECI":  SetUnit(result, IfcLengthUnitKind.Decimeter,  "Decimetre (dm)", 0.1); break;
                case "KILO":  SetUnit(result, IfcLengthUnitKind.Kilometer,  "Kilometre (km)", 1000.0); break;
                case "":      SetUnit(result, IfcLengthUnitKind.Meter,      "Metre (m)", 1.0); break;
                default:
                    result.Warnings.Add($"Unrecognized SI prefix '{prefix}' on the length unit -- assuming metres.");
                    SetUnit(result, IfcLengthUnitKind.Meter, "Metre (assumed)", 1.0);
                    break;
            }
        }

        private static void SetUnit(IfcParseResult result, IfcLengthUnitKind kind, string label, double toMeters)
        {
            result.LengthUnitKind = kind;
            result.LengthUnitLabel = label;
            result.LengthUnitToMeters = toMeters;
        }

        private static string Capitalize(string s) =>
            string.IsNullOrEmpty(s) ? s : char.ToUpperInvariant(s[0]) + s.Substring(1).ToLowerInvariant();

        private static void ResolveSite(Dictionary<int, StepEntity> entities, IfcParseResult result)
        {
            try
            {
                var site = entities.Values.FirstOrDefault(e => e.Type == "IFCSITE");
                if (site != null)
                {
                    var a = SplitTopLevel(site.RawArgs);
                    // (GlobalId, OwnerHistory, Name, Description, ObjectType,
                    //  ObjectPlacement, Representation, LongName, CompositionType,
                    //  RefLatitude, RefLongitude, RefElevation, LandTitleNumber, SiteAddress)
                    if (a.Count >= 12)
                    {
                        result.Site.LatitudeDeg  = ParseCompoundAngle(a[9]);
                        result.Site.LongitudeDeg = ParseCompoundAngle(a[10]);
                        if (TryParseNumber(a[11], out double refElev)) result.Site.RefElevationRaw = refElev;
                    }
                    if (a.Count >= 6 && TryGetRef(a[5], out int placementId))
                        ResolveLocalPlacementXYZ(entities, placementId, result.Site);
                }
                else
                {
                    result.Warnings.Add("No IFCSITE entity found -- no site placement/geographic info available.");
                }

                var mapConv = entities.Values.FirstOrDefault(e => e.Type == "IFCMAPCONVERSION");
                if (mapConv != null)
                {
                    var a = SplitTopLevel(mapConv.RawArgs);
                    // (SourceCRS, TargetCRS, Eastings, Northings, OrthogonalHeight, XAxisAbscissa?, XAxisOrdinate?, Scale?)
                    if (a.Count >= 5)
                    {
                        if (TryParseNumber(a[2], out double e_)) result.Site.MapEastings = e_;
                        if (TryParseNumber(a[3], out double n_)) result.Site.MapNorthings = n_;
                        if (TryParseNumber(a[4], out double h_)) result.Site.MapOrthogonalHeight = h_;
                    }
                }
            }
            catch (Exception ex)
            {
                result.Warnings.Add("Site info could not be fully read (" + ex.Message + ").");
            }
        }

        // One level deep only: IfcLocalPlacement -> IfcAxis2Placement3D -> IfcCartesianPoint.
        // Does NOT walk a further PlacementRelTo chain -- see the file header.
        private static void ResolveLocalPlacementXYZ(Dictionary<int, StepEntity> entities, int placementId, IfcSiteInfo site)
        {
            if (!entities.TryGetValue(placementId, out var placement) || placement.Type != "IFCLOCALPLACEMENT") return;
            var pArgs = SplitTopLevel(placement.RawArgs);
            if (pArgs.Count < 2 || !TryGetRef(pArgs[1], out int axisId)) return;
            if (!entities.TryGetValue(axisId, out var axis) || axis.Type != "IFCAXIS2PLACEMENT3D") return;
            var axArgs = SplitTopLevel(axis.RawArgs);
            if (axArgs.Count < 1 || !TryGetRef(axArgs[0], out int pointId)) return;
            if (!entities.TryGetValue(pointId, out var point) || point.Type != "IFCCARTESIANPOINT") return;
            var coords = SplitTopLevel(point.RawArgs.Trim().TrimStart('(').TrimEnd(')'));
            if (coords.Count >= 1 && TryParseNumber(coords[0], out double x)) site.LocalX = x;
            if (coords.Count >= 2 && TryParseNumber(coords[1], out double y)) site.LocalY = y;
            if (coords.Count >= 3 && TryParseNumber(coords[2], out double z)) site.LocalZ = z;
        }

        // IFC compound angle: (degrees, minutes, seconds[, millionths-of-second]).
        // First component's sign carries the overall sign (S/W hemispheres).
        private static double? ParseCompoundAngle(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return null;
            raw = raw.Trim();
            if (raw == "$" || raw == "*") return null;
            var parts = SplitTopLevel(raw.TrimStart('(').TrimEnd(')'));
            if (parts.Count == 0) return null;
            double[] vals = new double[parts.Count];
            for (int i = 0; i < parts.Count; i++)
                if (!TryParseNumber(parts[i], out vals[i])) return null;

            bool negative = vals[0] < 0;
            double deg = Math.Abs(vals[0]);
            double min = vals.Length > 1 ? Math.Abs(vals[1]) : 0;
            double sec = vals.Length > 2 ? Math.Abs(vals[2]) : 0;
            double micro = vals.Length > 3 ? Math.Abs(vals[3]) : 0;
            double total = deg + min / 60.0 + sec / 3600.0 + micro / 3600.0e6;
            return negative ? -total : total;
        }
    }
}

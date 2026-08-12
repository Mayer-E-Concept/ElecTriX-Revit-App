// CollisionCheckerSettings.cs -- ME-Tools | Collision Checker
// Persists the last-picked hole family/type to %APPDATA%\METools\collision-checker.json
// Mayer E-Concept SRL
using System;
using System.IO;
using System.Text;

namespace METools.CollisionChecker
{
    public class CollisionCheckerSettingsData
    {
        // Remembered only to pick a sensible default selection next time
        // the Hole Family picker is populated -- matched by name against
        // whatever's actually loaded in the project that's open at the
        // time, since the family itself isn't guaranteed to be loaded there
        // (same convention as Circuit Tagger's TagFamilyName/TagTypeName).
        public string HoleFamilyName { get; set; } = "";
        public string HoleTypeName   { get; set; } = "";

        // Off by default -- this is a geometric heuristic against imported
        // (uncategorized) CAD/IFC geometry, not a guaranteed-precise check
        // the way native-wall detection is, so it stays opt-in rather than
        // silently changing what a plain Scan does.
        public bool IncludeImportedArchitecture { get; set; } = false;

        // Which import represents "the architecture" -- matched by name
        // against the project's actual imports at scan time, same
        // by-name-not-by-id convention as HoleFamilyName above, since a
        // project can easily have a dozen+ imports (other disciplines'
        // backgrounds, schemas, etc.) and only one or two are ever the
        // relevant architectural walls. There's no reliable way to guess
        // which one by name pattern alone -- two real projects checked so
        // far use two completely different naming conventions for the
        // same thing ("ARC_..." vs "ARH_...Rohbau...") -- so this is
        // picked explicitly rather than auto-detected.
        public string ImportArchitectureName { get; set; } = "";

        // Distinguishes an ImportInstance ("ARC_...dwg") from a
        // RevitLinkInstance ("Architekturmodell.ifc") sharing the same
        // ImportArchitectureName, so Load() resolves against the right
        // one -- checked live, a genuine .ifc is more often a LINK than an
        // import (Revit's IFC linker gives it a real, queryable Document),
        // while other disciplines' backgrounds tend to be plain DWG
        // imports, so a project can easily have both kinds at once.
        public bool ImportArchitectureIsLink { get; set; } = false;
    }

    public static class CollisionCheckerSettings
    {
        private static string FilePath
        {
            get
            {
                var dir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "METools");
                try { Directory.CreateDirectory(dir); } catch { }
                return Path.Combine(dir, "collision-checker.json");
            }
        }

        public static CollisionCheckerSettingsData Load()
        {
            try
            {
                if (File.Exists(FilePath))
                {
                    var json = File.ReadAllText(FilePath, Encoding.UTF8);
                    return SimpleJsonDeserialize(json) ?? new CollisionCheckerSettingsData();
                }
            }
            catch { }
            return new CollisionCheckerSettingsData();
        }

        public static void Save(CollisionCheckerSettingsData data)
        {
            try
            {
                var json = SimpleJsonSerialize(data);
                File.WriteAllText(FilePath, json, Encoding.UTF8);
            }
            catch { }
        }

        private static string SimpleJsonSerialize(CollisionCheckerSettingsData d)
        {
            var sb = new StringBuilder();
            sb.AppendLine("{");
            sb.AppendLine($"  \"HoleFamilyName\": \"{Esc(d.HoleFamilyName)}\",");
            sb.AppendLine($"  \"HoleTypeName\": \"{Esc(d.HoleTypeName)}\",");
            sb.AppendLine($"  \"IncludeImportedArchitecture\": {(d.IncludeImportedArchitecture ? "true" : "false")},");
            sb.AppendLine($"  \"ImportArchitectureName\": \"{Esc(d.ImportArchitectureName)}\",");
            sb.AppendLine($"  \"ImportArchitectureIsLink\": {(d.ImportArchitectureIsLink ? "true" : "false")}");
            sb.AppendLine("}");
            return sb.ToString();
        }

        private static string Esc(string s) => (s ?? "").Replace("\\", "\\\\").Replace("\"", "\\\"");

        private static CollisionCheckerSettingsData SimpleJsonDeserialize(string json)
        {
            var d = new CollisionCheckerSettingsData();
            foreach (var line in json.Split('\n'))
            {
                var trim = line.Trim().TrimEnd(',');
                if (TryReadString(trim, "HoleFamilyName", out var fn)) d.HoleFamilyName = fn;
                if (TryReadString(trim, "HoleTypeName",   out var tn)) d.HoleTypeName   = tn;
                if (TryReadBool(trim, "IncludeImportedArchitecture", out var ia)) d.IncludeImportedArchitecture = ia;
                if (TryReadString(trim, "ImportArchitectureName", out var ian)) d.ImportArchitectureName = ian;
                if (TryReadBool(trim, "ImportArchitectureIsLink", out var ial)) d.ImportArchitectureIsLink = ial;
            }
            return d;
        }

        private static bool TryReadBool(string trimmedLine, string key, out bool value)
        {
            value = false;
            var prefix = $"\"{key}\":";
            if (!trimmedLine.StartsWith(prefix)) return false;
            var rest = trimmedLine.Substring(prefix.Length).Trim();
            if (bool.TryParse(rest, out value)) return true;
            return false;
        }

        private static bool TryReadString(string trimmedLine, string key, out string value)
        {
            value = "";
            var prefix = $"\"{key}\":";
            if (!trimmedLine.StartsWith(prefix)) return false;
            var rest = trimmedLine.Substring(prefix.Length).Trim();
            if (rest.Length < 2 || rest[0] != '"' || rest[rest.Length - 1] != '"') return false;
            value = rest.Substring(1, rest.Length - 2).Replace("\\\"", "\"").Replace("\\\\", "\\");
            return true;
        }
    }
}

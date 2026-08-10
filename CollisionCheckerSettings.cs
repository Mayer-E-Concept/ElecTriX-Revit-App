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
            sb.AppendLine($"  \"HoleTypeName\": \"{Esc(d.HoleTypeName)}\"");
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
            }
            return d;
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

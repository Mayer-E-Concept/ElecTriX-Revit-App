// FamilyHeightStore.cs -- ME-Tools | Family Placer default-height overrides
// Mayer E-Concept SRL -- per-user override store (%APPDATA%\METools\family_heights.json)
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;

namespace METools
{
    // Stores user overrides for per-family default mounting heights (Niveau), in mm.
    // The project and its families are never modified -- Family Placer simply prefers
    // an override over the family's own stored default when one exists.
    public static class FamilyHeightStore
    {
        private static readonly object _lock = new object();
        private static Dictionary<string, double> _cache;

        private static string ConfigPath
        {
            get
            {
                string dir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "METools");
                return Path.Combine(dir, "family_heights.json");
            }
        }

        private static Dictionary<string, double> Map
        {
            get
            {
                if (_cache == null) _cache = Load();
                return _cache;
            }
        }

        // Override height (mm) for a family, or null if none is stored.
        public static double? Get(string familyName)
        {
            if (string.IsNullOrEmpty(familyName)) return null;
            lock (_lock)
            {
                return Map.TryGetValue(familyName, out double v) ? (double?)v : null;
            }
        }

        // Snapshot copy of all overrides.
        public static IReadOnlyDictionary<string, double> All()
        {
            lock (_lock) { return new Dictionary<string, double>(Map); }
        }

        // Replaces the whole override set and persists it.
        public static void SaveAll(Dictionary<string, double> overrides)
        {
            lock (_lock)
            {
                _cache = new Dictionary<string, double>(overrides ?? new Dictionary<string, double>());
                Write(_cache);
            }
        }

        private static Dictionary<string, double> Load()
        {
            var map = new Dictionary<string, double>(StringComparer.Ordinal);
            try
            {
                string path = ConfigPath;
                if (!File.Exists(path)) return map;
                string json = File.ReadAllText(path, Encoding.UTF8);
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("heights", out var obj) &&
                    obj.ValueKind == JsonValueKind.Object)
                {
                    foreach (var prop in obj.EnumerateObject())
                    {
                        if (prop.Value.ValueKind == JsonValueKind.Number &&
                            prop.Value.TryGetDouble(out double v))
                            map[prop.Name] = v;
                    }
                }
            }
            catch { }
            return map;
        }

        private static void Write(Dictionary<string, double> map)
        {
            try
            {
                string path = ConfigPath;
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                using var stream = File.Open(path, FileMode.Create, FileAccess.Write);
                using var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true });
                writer.WriteStartObject();
                writer.WriteStartObject("heights");
                foreach (var kv in map) writer.WriteNumber(kv.Key, kv.Value);
                writer.WriteEndObject();
                writer.WriteEndObject();
            }
            catch { }
        }
    }
}

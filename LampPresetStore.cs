// LampPresetStore.cs -- ME-Tools | Lamp Placer
// Mayer E-Concept SRL -- per-user room presets for area-based placement.
// Stored at %APPDATA%\METools\lamp_presets.json. Project files are never touched.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace METools.LampPlacer
{
    public static class LampPresetStore
    {
        private static readonly object _lock = new object();
        private static List<LampPreset> _cache;

        private static string ConfigPath
        {
            get
            {
                string dir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "METools");
                return Path.Combine(dir, "lamp_presets.json");
            }
        }

        // Snapshot copy of all presets (ordered by name).
        public static List<LampPreset> All()
        {
            lock (_lock)
            {
                if (_cache == null) _cache = Load();
                return _cache.Select(Clone).OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase).ToList();
            }
        }

        public static LampPreset Get(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            lock (_lock)
            {
                if (_cache == null) _cache = Load();
                var hit = _cache.FirstOrDefault(p =>
                    string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));
                return hit != null ? Clone(hit) : null;
            }
        }

        // Adds or replaces a preset by name, then persists.
        public static void Save(LampPreset preset)
        {
            if (preset == null || string.IsNullOrWhiteSpace(preset.Name)) return;
            lock (_lock)
            {
                if (_cache == null) _cache = Load();
                _cache.RemoveAll(p => string.Equals(p.Name, preset.Name, StringComparison.OrdinalIgnoreCase));
                _cache.Add(Clone(preset));
                Write(_cache);
            }
        }

        public static void Delete(string name)
        {
            if (string.IsNullOrEmpty(name)) return;
            lock (_lock)
            {
                if (_cache == null) _cache = Load();
                _cache.RemoveAll(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));
                Write(_cache);
            }
        }

        private static LampPreset Clone(LampPreset p)
        {
            return new LampPreset
            {
                Name    = p.Name,
                Entries = p.Entries == null ? new List<LampPresetEntry>()
                    : p.Entries.Select(e => new LampPresetEntry
                    {
                        FamilyName = e.FamilyName,
                        TypeName   = e.TypeName,
                        Count      = e.Count,
                    }).ToList(),
            };
        }

        private static List<LampPreset> Load()
        {
            var list = new List<LampPreset>();
            try
            {
                string path = ConfigPath;
                if (!File.Exists(path)) return list;
                string json = File.ReadAllText(path, Encoding.UTF8);
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("presets", out var arr) &&
                    arr.ValueKind == JsonValueKind.Array)
                {
                    foreach (var pe in arr.EnumerateArray())
                    {
                        var preset = new LampPreset
                        {
                            Name = pe.TryGetProperty("name", out var nm) && nm.ValueKind == JsonValueKind.String
                                ? nm.GetString() : "",
                        };
                        if (string.IsNullOrWhiteSpace(preset.Name)) continue;
                        if (pe.TryGetProperty("entries", out var en) && en.ValueKind == JsonValueKind.Array)
                        {
                            foreach (var ee in en.EnumerateArray())
                            {
                                var entry = new LampPresetEntry
                                {
                                    FamilyName = ee.TryGetProperty("family", out var f) && f.ValueKind == JsonValueKind.String ? f.GetString() : "",
                                    TypeName   = ee.TryGetProperty("type",   out var t) && t.ValueKind == JsonValueKind.String ? t.GetString() : "",
                                    Count      = ee.TryGetProperty("count",  out var c) && c.ValueKind == JsonValueKind.Number && c.TryGetInt32(out int ci) ? ci : 1,
                                };
                                if (!string.IsNullOrEmpty(entry.FamilyName)) preset.Entries.Add(entry);
                            }
                        }
                        list.Add(preset);
                    }
                }
            }
            catch { }
            return list;
        }

        private static void Write(List<LampPreset> presets)
        {
            try
            {
                string path = ConfigPath;
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                using var stream = File.Open(path, FileMode.Create, FileAccess.Write);
                using var w = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true });
                w.WriteStartObject();
                w.WriteStartArray("presets");
                foreach (var p in presets)
                {
                    w.WriteStartObject();
                    w.WriteString("name", p.Name ?? "");
                    w.WriteStartArray("entries");
                    foreach (var e in (p.Entries ?? new List<LampPresetEntry>()))
                    {
                        w.WriteStartObject();
                        w.WriteString("family", e.FamilyName ?? "");
                        w.WriteString("type",   e.TypeName ?? "");
                        w.WriteNumber("count",  e.Count);
                        w.WriteEndObject();
                    }
                    w.WriteEndArray();
                    w.WriteEndObject();
                }
                w.WriteEndArray();
                w.WriteEndObject();
            }
            catch { }
        }
    }
}

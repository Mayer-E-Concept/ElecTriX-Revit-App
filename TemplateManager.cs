// TemplateManager.cs — ME-Tools | Family Placer
// Mayer E-Concept SRL
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace METools.FamilyPlacer
{
    public static class TemplateManager
    {
        private static readonly string _path = System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Mayer E-Concept SRL", "ME-Tools", "templates.json");

        public static List<PlacerTemplate> Load()
        {
            try
            {
                if (!File.Exists(_path)) return new List<PlacerTemplate>();
                var json = File.ReadAllText(_path);
                return JsonSerializer.Deserialize<List<PlacerTemplate>>(json)
                       ?? new List<PlacerTemplate>();
            }
            catch { return new List<PlacerTemplate>(); }
        }

        public static void Save(List<PlacerTemplate> templates)
        {
            try
            {
                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(_path));
                var opts = new JsonSerializerOptions { WriteIndented = true };
                File.WriteAllText(_path, JsonSerializer.Serialize(templates, opts));
            }
            catch { }
        }
    }
}

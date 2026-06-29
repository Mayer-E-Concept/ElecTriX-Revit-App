// TemplateManager.cs -- ME-Tools | Family Placer
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

        // Seeds factory default templates if none exist yet.
        // Family/Type names verified directly from the Revit model via Nonica.
        // OffsetY: sequential integer -> 2DY_Versatzfaktor (0=first slot, 1=next, etc).
        // OffsetX -1/+1 = side-by-side pair; 0 = single column.
        // Note: "Change switch " has a trailing space - matches the model exactly.
        public static void SeedDefaults()
        {
            if (Load().Count > 0) return;

            var d = new List<PlacerTemplate>
            {
                // 1: Intercom + Temp sensor + Change switch + Single socket (stacked)
                new PlacerTemplate { Name = "1 - Intercom / Temp / Wechsel / Steckdose", Orientation = "Vertical", Slots = new List<FamilySlot>
                {
                    new FamilySlot { FamilyName = "_E_CAx W intercom system",          TypeName = "intercom system",        Height = 1300, OffsetX = 0, OffsetY = 0 },
                    new FamilySlot { FamilyName = "_E_CAx Temperature sensor MEP",     TypeName = "Temperature sensor MEP", Height = 1200, OffsetX = 0, OffsetY = 1 },
                    new FamilySlot { FamilyName = "_E_CAx Wechselschalter",            TypeName = "Change switch ",         Height = 1050, OffsetX = 0, OffsetY = 2 },
                    new FamilySlot { FamilyName = "_E_CAx Steckdose",                  TypeName = "Socket",                 Height = 300,  OffsetX = 0, OffsetY = 3 },
                }},
                // 2: Change switch + Double socket (stacked)
                new PlacerTemplate { Name = "2 - Wechselschalter + Doppelsteckdose", Orientation = "Vertical", Slots = new List<FamilySlot>
                {
                    new FamilySlot { FamilyName = "_E_CAx Wechselschalter",  TypeName = "Change switch ", Height = 1050, OffsetX = 0, OffsetY = 0 },
                    new FamilySlot { FamilyName = "_E_CAx Steckdose 2-FACH", TypeName = "Steckdose",     Height = 300,  OffsetX = 0, OffsetY = 1 },
                }},
                // 3: Temp sensor + Simple switch + Switch 2poles + Single socket (stacked)
                new PlacerTemplate { Name = "3 - Temp / Schalter / 2-polig / Steckdose", Orientation = "Vertical", Slots = new List<FamilySlot>
                {
                    new FamilySlot { FamilyName = "_E_CAx Temperature sensor MEP", TypeName = "Temperature sensor MEP", Height = 1200, OffsetX = 0, OffsetY = 0 },
                    new FamilySlot { FamilyName = "_E_CAx Schalter",               TypeName = "switch",                 Height = 1050, OffsetX = 0, OffsetY = 1 },
                    new FamilySlot { FamilyName = "_E_CAx Schalter",               TypeName = "switch 2poles",          Height = 1050, OffsetX = 0, OffsetY = 2 },
                    new FamilySlot { FamilyName = "_E_CAx Steckdose",              TypeName = "Socket",                 Height = 300,  OffsetX = 0, OffsetY = 3 },
                }},
                // 4: Temp sensor + Simple switch stacked, then Change switch + Single socket side-by-side
                new PlacerTemplate { Name = "4 - Temp / Schalter / Wechsel+Dose SbS", Orientation = "Vertical", Slots = new List<FamilySlot>
                {
                    new FamilySlot { FamilyName = "_E_CAx Temperature sensor MEP", TypeName = "Temperature sensor MEP", Height = 1200, OffsetX = 0,  OffsetY = 0 },
                    new FamilySlot { FamilyName = "_E_CAx Schalter",               TypeName = "switch",                 Height = 1050, OffsetX = 0,  OffsetY = 1 },
                    new FamilySlot { FamilyName = "_E_CAx Wechselschalter",        TypeName = "Change switch ",         Height = 1050, OffsetX = -1, OffsetY = 2 },
                    new FamilySlot { FamilyName = "_E_CAx Steckdose",              TypeName = "Socket",                 Height = 300,  OffsetX = 1,  OffsetY = 2 },
                }},
                // 5: Anschluss 400V centered on top, Steckdose schaltbar + Single socket side-by-side below
                new PlacerTemplate { Name = "5 - Schaltbare Dose + Steckdose + Anschluss 400V", Orientation = "Vertical", Slots = new List<FamilySlot>
                {
                    new FamilySlot { FamilyName = "_E_CAx Herdanschluss",              TypeName = "Anschluss 400V",      Height = 400, OffsetX = 0,  OffsetY = 0 },
                    new FamilySlot { FamilyName = "_E_CAx Steckdose 1-fach schaltbar", TypeName = "Steckdose schaltbar", Height = 300, OffsetX = -1, OffsetY = 1 },
                    new FamilySlot { FamilyName = "_E_CAx Steckdose",                  TypeName = "Socket",              Height = 300, OffsetX = 1,  OffsetY = 1 },
                }},
                // 6: Triple socket + EDV Dose Netzwerk (stacked)
                new PlacerTemplate { Name = "6 - Dreifachsteckdose + EDV", Orientation = "Vertical", Slots = new List<FamilySlot>
                {
                    new FamilySlot { FamilyName = "_E_CAx Steckdose 3-FACH", TypeName = "Steckdose", Height = 300, OffsetX = 0, OffsetY = 0 },
                    new FamilySlot { FamilyName = "_E_CAx EDV Dose",          TypeName = "Netzwerk",  Height = 300, OffsetX = 0, OffsetY = 1 },
                }},
            };
            Save(d);
        }
    }
}

// IfcLevelImportHandler.cs -- ME-Tools | IFC Level Importer
// Mayer E-Concept SRL
// Creates Revit Levels from selected IFCBUILDINGSTOREY entries. Runs in a
// single transaction for the whole batch. Read-only IFC parsing happens
// separately, in IfcLiteReader (no Revit API needed there); only this part
// needs the ExternalEvent/transaction machinery.
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using System;
using System.Collections.Generic;
using System.Linq;

namespace METools.IfcImport
{
    public class IfcLevelImportHandler : IExternalEventHandler
    {
        public IfcLevelImportRequest Request { get; set; } = new IfcLevelImportRequest();
        public Action<IfcLevelImportResultInfo> OnDone { get; set; }
        public Action<string> OnError { get; set; }

        public string GetName() => "ME-Tools IFC Level Import";

        public void Execute(UIApplication app)
        {
            var doc = app.ActiveUIDocument?.Document;
            if (doc == null) { OnError?.Invoke("No active document."); return; }
            var req = Request;
            if (req == null || req.LevelsToCreate.Count == 0) { OnError?.Invoke("No levels selected."); return; }

            var result = new IfcLevelImportResultInfo();
            try
            {
                var existingNames = new HashSet<string>(
                    new FilteredElementCollector(doc).OfClass(typeof(Level)).Cast<Level>()
                        .Select(l => l.Name), StringComparer.OrdinalIgnoreCase);

                using (var tx = new Transaction(doc, "ME-Tools: Import IFC Levels"))
                {
                    tx.Start();
                    foreach (var lvl in req.LevelsToCreate)
                    {
                        string name = string.IsNullOrWhiteSpace(lvl.Name) ? "Level" : lvl.Name.Trim();
                        if (existingNames.Contains(name))
                        {
                            result.Skipped++;
                            result.SkippedNames.Add(name);
                            continue;
                        }

                        double meters = lvl.ElevationRaw * req.LengthUnitToMeters;
                        double feet = UnitUtils.ConvertToInternalUnits(meters, UnitTypeId.Meters);

                        try
                        {
                            var newLevel = Level.Create(doc, feet);
                            if (newLevel == null) { result.Skipped++; result.SkippedNames.Add(name + " (creation failed)"); continue; }
                            try { newLevel.Name = name; }
                            catch { /* name collision surfaced after all -- keep the auto-generated name rather than fail the whole batch */ }
                            existingNames.Add(name);
                            result.Created++;
                        }
                        catch (Exception exLevel)
                        {
                            result.Skipped++;
                            result.SkippedNames.Add($"{name} ({exLevel.Message})");
                        }
                    }
                    if (tx.GetStatus() == TransactionStatus.Started) tx.Commit();
                }
            }
            catch (Exception ex)
            {
                OnError?.Invoke("Import failed: " + ex.Message);
                return;
            }

            OnDone?.Invoke(result);
        }
    }
}

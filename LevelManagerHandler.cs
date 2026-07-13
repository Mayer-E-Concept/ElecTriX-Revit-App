// LevelManagerHandler.cs — ME-Tools | Level Manager
// Mayer E-Concept SRL
using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace METools.LevelManager
{
    public class LevelManagerHandler : IExternalEventHandler
    {
        public LevelManagerRequest       Request  { get; set; } = new LevelManagerRequest();
        public Action<List<LevelRow>>    OnLoaded { get; set; }
        public Action<string>            OnStatus { get; set; }

        public void Execute(UIApplication app)
        {
            var doc = app.ActiveUIDocument?.Document;
            if (doc == null) { OnStatus?.Invoke("No active document."); return; }

            try
            {
                if (Request.Action == LevelManagerAction.AddLevel)
                    AddLevel(doc);

                // AddLevel falls through to Refresh so the list (and any new
                // group/zone discovered from the new name) is always current.
                Refresh(doc);
            }
            catch (Exception ex)
            {
                OnStatus?.Invoke("Error: " + ex.Message);
            }
        }

        private void Refresh(Document doc)
        {
            var rows = new FilteredElementCollector(doc)
                .OfClass(typeof(Level))
                .Cast<Level>()
                .Select(l => new LevelRow
                {
                    Id          = l.Id,
                    Name        = l.Name,
                    ElevationFt = l.Elevation,
                    ElevationM  = UnitUtils.ConvertFromInternalUnits(l.Elevation, UnitTypeId.Meters),
                })
                .OrderBy(r => r.ElevationFt)
                .ToList();

            LevelNameParser.AssignGroups(rows);
            OnLoaded?.Invoke(rows);
        }

        private void AddLevel(Document doc)
        {
            var name = (Request.NewName ?? "").Trim();
            if (string.IsNullOrEmpty(name))
            { OnStatus?.Invoke("Enter a name for the new level."); return; }

            // Reject an exact duplicate name up front — Revit's own exception
            // message for this is generic, so we give a clearer one first.
            bool nameTaken = new FilteredElementCollector(doc)
                .OfClass(typeof(Level)).Cast<Level>()
                .Any(l => string.Equals(l.Name, name, StringComparison.OrdinalIgnoreCase));
            if (nameTaken)
            { OnStatus?.Invoke($"A level named \"{name}\" already exists."); return; }

            double elevationFt = UnitUtils.ConvertToInternalUnits(Request.NewElevationM, UnitTypeId.Meters);

            using (var tx = new Transaction(doc, "ME-Tools: Add Level"))
            {
                tx.Start();
                try
                {
                    var level = Level.Create(doc, elevationFt);
                    if (level == null)
                    {
                        tx.RollBack();
                        OnStatus?.Invoke("Revit could not create the level.");
                        return;
                    }
                    level.Name = name;
                    tx.Commit();
                    OnStatus?.Invoke($"✓ Level \"{name}\" created at {Request.NewElevationM:0.###} m.");
                }
                catch (Exception ex)
                {
                    if (tx.HasStarted() && !tx.HasEnded()) tx.RollBack();
                    OnStatus?.Invoke("Could not create level: " + ex.Message);
                }
            }
        }

        public string GetName() => "ME-Tools Level Manager";
    }
}

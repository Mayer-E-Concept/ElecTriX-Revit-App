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
            var uiDoc = app.ActiveUIDocument;
            var doc = uiDoc?.Document;
            if (doc == null) { OnStatus?.Invoke(S._("levelmanager.no_active_document")); return; }

            try
            {
                switch (Request.Action)
                {
                    case LevelManagerAction.AddLevel:            AddLevel(doc); break;
                    case LevelManagerAction.ToggleBuildingStory: ToggleBuildingStory(doc); break;
                    case LevelManagerAction.DeleteLevel:         DeleteLevel(doc); break;
                    case LevelManagerAction.CreateFloorPlan:     CreateFloorPlan(doc); break;
                    case LevelManagerAction.CreateCeilingPlan:   CreateCeilingPlan(doc); break;
                    case LevelManagerAction.NavigateToLevel:     NavigateToLevel(uiDoc); break;
                    case LevelManagerAction.CreateMissingFloorPlans: CreateMissingFloorPlans(doc); break;
                }

                // Every action above (except pure navigation) changes the list
                // in some way, and Refresh is cheap -- simplest to just always
                // re-read it so the displayed list is never stale.
                Refresh(doc);
            }
            catch (Exception ex)
            {
                OnStatus?.Invoke(string.Format(S._("levelmanager.error"), ex.Message));
            }
        }

        private void Refresh(Document doc)
        {
            var rows = new FilteredElementCollector(doc)
                .OfClass(typeof(Level))
                .Cast<Level>()
                .Select(l =>
                {
                    var (isMonitoring, monitoredLinkName) = ReadMonitoring(doc, l);
                    return new LevelRow
                    {
                        Id          = l.Id,
                        Name        = l.Name,
                        ElevationFt = l.Elevation,
                        ElevationM  = UnitUtils.ConvertFromInternalUnits(l.Elevation, UnitTypeId.Meters),

                        LevelTypeName     = ReadLevelTypeName(doc, l),
                        ElevationBaseText = ReadElevationBaseText(doc, l),
                        IsBuildingStory   = ReadBuildingStory(l),
                        IsMonitoringLink  = isMonitoring,
                        MonitoredLinkName = monitoredLinkName,
                    };
                })
                .OrderBy(r => r.ElevationFt)
                .ToList();

            LevelNameParser.AssignGroups(rows);
            OnLoaded?.Invoke(rows);
        }

        // -- Detail-panel field readers -- each defensive on its own, since
        // none of these should ever prevent the row itself from showing. --
        private static string ReadLevelTypeName(Document doc, Level l)
        {
            try { return (doc.GetElement(l.GetTypeId()) as ElementType)?.Name ?? ""; }
            catch { return ""; }
        }

        // Elevation Base ("Project Base Point" vs "Survey Point") is a Level
        // TYPE parameter (LEVEL_RELATIVE_BASE_TYPE), read via AsValueString()
        // so the exact underlying int-to-option mapping never has to be
        // guessed at -- Revit hands back the same display string it shows
        // in the UI.
        private static string ReadElevationBaseText(Document doc, Level l)
        {
            try
            {
                var lt = doc.GetElement(l.GetTypeId());
                var p = lt?.get_Parameter(BuiltInParameter.LEVEL_RELATIVE_BASE_TYPE);
                return p?.AsValueString() ?? "";
            }
            catch { return ""; }
        }

        // Name-based lookup rather than a BuiltInParameter enum value --
        // "Building Story" is a well-documented, stable Revit parameter name
        // (confirmed unchanged since Revit 2013), but its exact BuiltInParameter
        // enum id wasn't confirmed, so this avoids guessing at that specifically.
        private static bool ReadBuildingStory(Level l)
        {
            try { return l.LookupParameter("Building Story")?.AsInteger() == 1; }
            catch { return false; }
        }

        // Copy/Monitor status. IsMonitoringLinkElement() and
        // GetMonitoredLinkElementIds() are confirmed, stable Element methods
        // (documented since Revit 2011). GetMonitoredLinkElementIds() returns
        // the monitored *link instance's* id, not the specific corresponding
        // element inside that link -- that finer-grained mapping is
        // confirmed (via an Autodesk API team answer) to not be exposed by
        // the public API at all, so this deliberately doesn't attempt to
        // show which element within the link is the counterpart, only which
        // link is being monitored.
        private static (bool IsMonitoring, string LinkName) ReadMonitoring(Document doc, Level l)
        {
            try
            {
                if (!l.IsMonitoringLinkElement()) return (false, "");

                var linkIds = l.GetMonitoredLinkElementIds();
                if (linkIds == null || linkIds.Count == 0) return (true, "");

                var linkInst = doc.GetElement(linkIds.First()) as RevitLinkInstance;
                string name = linkInst?.Name ?? "";
                // RevitLinkInstance.Name is often "File.rvt : 1 : location <Not Shared>" --
                // trim to just the file/type portion for a clean display name.
                int sepIdx = name.IndexOf(" : ", StringComparison.Ordinal);
                if (sepIdx > 0) name = name.Substring(0, sepIdx);

                return (true, name);
            }
            catch { return (false, ""); }
        }

        private void AddLevel(Document doc)
        {
            var name = (Request.NewName ?? "").Trim();
            if (string.IsNullOrEmpty(name))
            { OnStatus?.Invoke(S._("levelmanager.enter_level_name")); return; }

            // Reject an exact duplicate name up front — Revit's own exception
            // message for this is generic, so we give a clearer one first.
            bool nameTaken = new FilteredElementCollector(doc)
                .OfClass(typeof(Level)).Cast<Level>()
                .Any(l => string.Equals(l.Name, name, StringComparison.OrdinalIgnoreCase));
            if (nameTaken)
            { OnStatus?.Invoke(string.Format(S._("levelmanager.name_taken"), name)); return; }

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
                        OnStatus?.Invoke(S._("levelmanager.create_failed_generic"));
                        return;
                    }
                    level.Name = name;
                    tx.Commit();
                    OnStatus?.Invoke(string.Format(S._("levelmanager.level_created"), name, Request.NewElevationM.ToString("0.###")));
                }
                catch (Exception ex)
                {
                    if (tx.HasStarted() && !tx.HasEnded()) tx.RollBack();
                    OnStatus?.Invoke(string.Format(S._("levelmanager.create_failed"), ex.Message));
                }
            }
        }

        private void ToggleBuildingStory(Document doc)
        {
            var level = doc.GetElement(Request.TargetLevelId) as Level;
            if (level == null) return;

            using (var tx = new Transaction(doc, "ME-Tools: Toggle Building Story"))
            {
                tx.Start();
                try
                {
                    var p = level.LookupParameter("Building Story");
                    if (p != null && !p.IsReadOnly) p.Set(Request.NewBuildingStoryValue ? 1 : 0);
                    tx.Commit();
                }
                catch (Exception ex)
                {
                    if (tx.HasStarted() && !tx.HasEnded()) tx.RollBack();
                    OnStatus?.Invoke(string.Format(S._("levelmanager.toggle_failed"), ex.Message));
                }
            }
        }

        private void DeleteLevel(Document doc)
        {
            var level = doc.GetElement(Request.TargetLevelId) as Level;
            if (level == null) return;
            string name = level.Name;

            using (var tx = new Transaction(doc, "ME-Tools: Delete Level"))
            {
                tx.Start();
                try
                {
                    doc.Delete(Request.TargetLevelId);
                    tx.Commit();
                    OnStatus?.Invoke(string.Format(S._("levelmanager.level_deleted"), name));
                }
                catch (Exception ex)
                {
                    if (tx.HasStarted() && !tx.HasEnded()) tx.RollBack();
                    OnStatus?.Invoke(string.Format(S._("levelmanager.delete_failed"), ex.Message));
                }
            }
        }

        private void CreateFloorPlan(Document doc)
        {
            var level = doc.GetElement(Request.TargetLevelId) as Level;
            if (level == null) return;

            try
            {
                var vft = new FilteredElementCollector(doc)
                    .OfClass(typeof(ViewFamilyType))
                    .Cast<ViewFamilyType>()
                    .FirstOrDefault(v => v.ViewFamily == ViewFamily.FloorPlan);
                if (vft == null) { OnStatus?.Invoke(S._("levelmanager.no_floor_plan_type")); return; }

                using (var tx = new Transaction(doc, "ME-Tools: Create Floor Plan"))
                {
                    tx.Start();
                    ViewPlan.Create(doc, vft.Id, Request.TargetLevelId);
                    tx.Commit();
                }
                OnStatus?.Invoke(string.Format(S._("levelmanager.floor_plan_created"), level.Name));
            }
            catch (Exception ex)
            {
                OnStatus?.Invoke(string.Format(S._("levelmanager.floor_plan_failed"), ex.Message));
            }
        }

        // Finds every level that doesn't already have a floor plan and
        // creates one for each, in a single transaction. Levels are matched
        // to an existing plan the same way NavigateToLevel does (by
        // ViewPlan.GenLevel.Id), so a level that already has one is
        // correctly skipped rather than getting a duplicate.
        private void CreateMissingFloorPlans(Document doc)
        {
            try
            {
                var vft = new FilteredElementCollector(doc)
                    .OfClass(typeof(ViewFamilyType))
                    .Cast<ViewFamilyType>()
                    .FirstOrDefault(v => v.ViewFamily == ViewFamily.FloorPlan);
                if (vft == null) { OnStatus?.Invoke(S._("levelmanager.no_floor_plan_type")); return; }

                var levelsWithPlans = new HashSet<ElementId>(
                    new FilteredElementCollector(doc).OfClass(typeof(ViewPlan)).Cast<ViewPlan>()
                        .Where(v => !v.IsTemplate && v.ViewType == ViewType.FloorPlan && v.GenLevel != null)
                        .Select(v => v.GenLevel.Id));

                var missingLevels = new FilteredElementCollector(doc)
                    .OfClass(typeof(Level)).Cast<Level>()
                    .Where(l => !levelsWithPlans.Contains(l.Id))
                    .OrderBy(l => l.Elevation)
                    .ToList();

                if (missingLevels.Count == 0)
                { OnStatus?.Invoke(S._("levelmanager.all_have_floor_plans")); return; }

                int created = 0;
                var failed = new List<string>();
                using (var tx = new Transaction(doc, "ME-Tools: Create Missing Floor Plans"))
                {
                    tx.Start();
                    foreach (var lvl in missingLevels)
                    {
                        try { ViewPlan.Create(doc, vft.Id, lvl.Id); created++; }
                        catch { failed.Add(lvl.Name); }
                    }
                    tx.Commit();
                }

                string msg = string.Format(S._("levelmanager.bulk_floor_plans_created"), created);
                if (failed.Count > 0)
                    msg += string.Format(S._("levelmanager.bulk_floor_plans_failed"), failed.Count, string.Join(", ", failed));
                OnStatus?.Invoke(msg);
            }
            catch (Exception ex)
            {
                OnStatus?.Invoke(string.Format(S._("levelmanager.floor_plan_failed"), ex.Message));
            }
        }

        private void CreateCeilingPlan(Document doc)
        {
            var level = doc.GetElement(Request.TargetLevelId) as Level;
            if (level == null) return;

            try
            {
                var vft = new FilteredElementCollector(doc)
                    .OfClass(typeof(ViewFamilyType))
                    .Cast<ViewFamilyType>()
                    .FirstOrDefault(v => v.ViewFamily == ViewFamily.CeilingPlan);
                if (vft == null) { OnStatus?.Invoke(S._("levelmanager.no_ceiling_plan_type")); return; }

                using (var tx = new Transaction(doc, "ME-Tools: Create Ceiling Plan"))
                {
                    tx.Start();
                    ViewPlan.Create(doc, vft.Id, Request.TargetLevelId);
                    tx.Commit();
                }
                OnStatus?.Invoke(string.Format(S._("levelmanager.ceiling_plan_created"), level.Name));
            }
            catch (Exception ex)
            {
                OnStatus?.Invoke(string.Format(S._("levelmanager.ceiling_plan_failed"), ex.Message));
            }
        }

        // Switches the active view to an existing floor plan for the target
        // level, if one already exists (does not create one -- that's what
        // Create Floor Plan is for).
        private void NavigateToLevel(UIDocument uiDoc)
        {
            var doc = uiDoc?.Document;
            var level = doc?.GetElement(Request.TargetLevelId) as Level;
            if (doc == null || level == null) return;

            try
            {
                var plan = new FilteredElementCollector(doc)
                    .OfClass(typeof(ViewPlan)).Cast<ViewPlan>()
                    .FirstOrDefault(v => !v.IsTemplate && v.ViewType == ViewType.FloorPlan
                        && v.GenLevel != null && v.GenLevel.Id == Request.TargetLevelId);

                if (plan == null)
                { OnStatus?.Invoke(S._("levelmanager.navigate_failed")); return; }

                uiDoc.ActiveView = plan;
                OnStatus?.Invoke(string.Format(S._("levelmanager.navigated"), level.Name));
            }
            catch (Exception ex)
            {
                OnStatus?.Invoke(string.Format(S._("levelmanager.navigate_error"), ex.Message));
            }
        }

        public string GetName() => "ME-Tools Level Manager";
    }
}

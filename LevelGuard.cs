// LevelGuard.cs -- ME-Tools
// Mayer E-Concept SRL -- "are you on the right level?" confirmation before placing.
// Project-agnostic: no hardcoded level names; matching is elevation-based.
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace METools
{
    public enum LampLevelDecision
    {
        Ok,              // reference matches the expected ceiling -> place normally
        Cancel,          // user declined the warning -> abort
        ProceedKeep,     // mismatch accepted, reference is above the floor -> place normally
        ProceedFloorRef, // mismatch accepted, reference is the floor itself -> ask for wall offset
    }

    public static class LevelGuard
    {
        // The level of the active plan view, or null (e.g. 3D / section view).
        public static Level ActiveLevel(UIDocument uidoc)
        {
            try { return (uidoc?.ActiveView as ViewPlan)?.GenLevel; }
            catch { return null; }
        }

        // Family Placer: warn if the tool's selected placement level differs from the
        // level of the view the user is actually working in. Returns true to proceed.
        public static bool ConfirmPlacementLevel(UIDocument uidoc, Document doc, ElementId selectedLevelId)
        {
            try
            {
                var active = ActiveLevel(uidoc);
                if (active == null) return true;                 // not a plan view -> can't compare
                var sel = Resolve(doc, selectedLevelId);
                if (sel == null) return true;                    // nothing selected -> don't nag
                if (sel.Id == active.Id) return true;            // match

                return Ask(
                    "Level check",
                    "You are working on level \"" + active.Name + "\", " +
                    "but the tool is set to place on \"" + sel.Name + "\".\n\n" +
                    "Place anyway?");
            }
            catch { return true; }
        }

        // Lamp Placer: the selected level is the ceiling / reference level. The natural ceiling
        // for the storey you are on is the nearest level above it. Returns a decision: Ok to place
        // normally, Cancel to abort, or ProceedFloorRef when the user kept a floor level as the
        // reference (wall-mounted lamps -> the handler then asks for an offset from host).
        public static LampLevelDecision CheckLampReference(UIDocument uidoc, Document doc, ElementId referenceLevelId)
        {
            try
            {
                var active = ActiveLevel(uidoc);
                if (active == null) return LampLevelDecision.Ok;
                var sel = Resolve(doc, referenceLevelId);
                if (sel == null) return LampLevelDecision.Ok;        // no explicit reference -> handler falls back

                var expected = NearestAbove(doc, active) ?? active;
                if (sel.Id == expected.Id) return LampLevelDecision.Ok;

                bool yes = Ask(
                    "Reference level check",
                    "You are working on level \"" + active.Name + "\".\n" +
                    "Its ceiling / reference level looks like \"" + expected.Name + "\", " +
                    "but the tool is set to \"" + sel.Name + "\".\n\n" +
                    "Place anyway?");
                if (!yes) return LampLevelDecision.Cancel;

                // Reference at or below the current floor -> wall-mounted scenario (offset from floor).
                return (sel.Elevation <= active.Elevation + 1e-6)
                    ? LampLevelDecision.ProceedFloorRef
                    : LampLevelDecision.ProceedKeep;
            }
            catch { return LampLevelDecision.Ok; }
        }

        private static Level Resolve(Document doc, ElementId id)
        {
            try
            {
                return (id != null && id != ElementId.InvalidElementId)
                    ? doc.GetElement(id) as Level : null;
            }
            catch { return null; }
        }

        private static Level NearestAbove(Document doc, Level baseLvl)
        {
            try
            {
                return new FilteredElementCollector(doc).OfClass(typeof(Level)).Cast<Level>()
                    .Where(l => l.Elevation > baseLvl.Elevation + 1e-6)
                    .OrderBy(l => l.Elevation)
                    .FirstOrDefault();
            }
            catch { return null; }
        }

        private static bool Ask(string title, string msg)
        {
            try
            {
                var td = new TaskDialog(title)
                {
                    MainInstruction = "Are you on the right level?",
                    MainContent     = msg,
                    CommonButtons   = TaskDialogCommonButtons.Yes | TaskDialogCommonButtons.No,
                    DefaultButton   = TaskDialogResult.No,
                };
                return td.Show() == TaskDialogResult.Yes;
            }
            catch { return true; }
        }
    }
}

// AutoRoomSeparation/AutoRoomSeparationCommand.cs — ME-Tools | Auto Room Separation
// Mayer E-Concept SRL
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace METools.AutoRoomSeparation
{
    [Transaction(TransactionMode.Manual)]
    public class AutoRoomSeparationCommand : IExternalCommand
    {
        public Result Execute(
            ExternalCommandData commandData,
            ref string message,
            ElementSet elements)
        {
            var uidoc = commandData.Application.ActiveUIDocument;
            var doc   = uidoc.Document;

            // ── Validate active view ────────────────────────────────────────
            var view = uidoc.ActiveView;
            if (view == null)
            {
                TaskDialog.Show("Auto Room Separation", "No active view found.");
                return Result.Cancelled;
            }

            if (view.ViewType != ViewType.FloorPlan &&
                view.ViewType != ViewType.CeilingPlan &&
                view.ViewType != ViewType.AreaPlan)
            {
                TaskDialog.Show("Auto Room Separation",
                    "Please activate a Floor Plan, Ceiling Plan, or Area Plan view before running this command.\n\n" +
                    $"Current view type: {view.ViewType}");
                return Result.Cancelled;
            }

            // ── Get the associated level ────────────────────────────────────
            Level level = null;
            try
            {
                if (view is ViewPlan vp)
                    level = vp.GenLevel;
            }
            catch { }

            if (level == null)
            {
                TaskDialog.Show("Auto Room Separation",
                    "Could not determine the level of the active view.\n" +
                    "Please open a Floor Plan associated with a level.");
                return Result.Cancelled;
            }

            // ── Show settings dialog ────────────────────────────────────────
            var window = new AutoRoomSeparationWindow();
            window.ShowDialog();

            if (!window.Confirmed)
                return Result.Succeeded;

            var settings = window.Settings;

            // ── Extract curves ──────────────────────────────────────────────
            var curves = CurveExtractor.Extract(doc, view, settings,
                out var exStats);

            if (curves.Count == 0)
            {
                TaskDialog.Show("Auto Room Separation",
                    "No usable geometry curves were found in the selected sources.\n\n" +
                    "Make sure that:\n" +
                    "• A DWG file is linked or imported in this view, or\n" +
                    "• DirectShape / IFC elements are present, or\n" +
                    "• Native Walls exist in the model.");
                return Result.Succeeded;
            }

            // ── Find closed loops ───────────────────────────────────────────
            var loops = LoopFinder.FindLoops(curves, settings, out var loopStats);

            if (loops.Count == 0)
            {
                var td = new TaskDialog("Auto Room Separation")
                {
                    MainContent =
                        $"No closed room areas were found.\n\n" +
                        $"Curves extracted:  {exStats.AfterProjection}\n" +
                        $"Open / degenerate: {loopStats.OpenOrDegenerate}\n" +
                        $"Too small (<{settings.MinAreaSqM} m²): {loopStats.FilteredTooSmall}\n" +
                        $"Too large (>{settings.MaxAreaSqM} m²): {loopStats.FilteredTooLarge}\n\n" +
                        "Tip: lower the minimum area or check that wall lines form closed polygons.",
                };
                td.Show();
                return Result.Succeeded;
            }

            // ── Write room separation lines ─────────────────────────────────
            var writeResult = RoomSeparationWriter.Write(doc, view, level, loops);

            if (!writeResult.Success)
            {
                TaskDialog.Show("Auto Room Separation",
                    $"An error occurred while writing boundary lines:\n{writeResult.FatalError}");
                return Result.Failed;
            }

            // ── Show result summary ─────────────────────────────────────────
            var summary = new TaskDialog("Auto Room Separation — Done")
            {
                MainContent =
                    $"{writeResult.Created} Room Separation Lines created " +
                    $"from {loops.Count} closed rooms.\n\n" +
                    $"Skipped (already existed): {writeResult.Skipped}\n" +
                    $"Loops filtered (too small/large/open): {loopStats.TotalFiltered}\n\n" +
                    "Next step: Architecture → Room  or  Analyze → Place Spaces Automatically.",
            };
            summary.Show();

            return Result.Succeeded;
        }
    }
}

// AutoRoomSeparation/RoomSeparationWriter.cs — ME-Tools | Auto Room Separation
// Mayer E-Concept SRL
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using System;
using System.Collections.Generic;
using System.Linq;

namespace METools.AutoRoomSeparation
{
    /// <summary>
    /// Writes room separation boundary lines to the Revit document.
    /// All curves are elevated to the active view's level elevation.
    /// Duplicate lines (already present in the model) are skipped.
    /// </summary>
    public static class RoomSeparationWriter
    {
        // Tolerance for checking if a line already exists (in feet, ≈ 20 mm)
        private const double EXIST_TOL = 0.066;

        // ── Public API ─────────────────────────────────────────────────────

        public static WriteResult Write(
            Document doc,
            View activeView,
            Level level,
            List<List<Curve>> loops)
        {
            var result = new WriteResult();

            if (loops == null || loops.Count == 0)
                return result;

            // Build a SketchPlane at the level's elevation
            double elevation = level.Elevation;

            // Collect existing room separation curves (to avoid duplicates)
            var existingCurves = GetExistingBoundaryCurves(doc, activeView);

            using (var trans = new Transaction(doc, "Auto Room Separation Lines"))
            {
                // Configure failure handling to ignore warnings about overlapping lines
                var failOptions = trans.GetFailureHandlingOptions();
                failOptions.SetFailuresPreprocessor(new SilentFailurePreprocessor());
                trans.SetFailureHandlingOptions(failOptions);

                trans.Start();
                try
                {
                    var plane      = Plane.CreateByNormalAndOrigin(XYZ.BasisZ, new XYZ(0, 0, elevation));
                    var sketchPlane = SketchPlane.Create(doc, plane);

                    foreach (var loop in loops)
                    {
                        foreach (var curve in loop)
                        {
                            try
                            {
                                // Elevate curve to the level's Z
                                var elevated = ElevateCurve(curve, elevation);
                                if (elevated == null) continue;

                                // Skip if a near-identical line already exists
                                if (IsDuplicate(elevated, existingCurves))
                                {
                                    result.Skipped++;
                                    continue;
                                }

                                var arr = new CurveArray();
                                arr.Append(elevated);
                                doc.Create.NewRoomBoundaryLines(sketchPlane, arr, activeView);
                                result.Created++;
                            }
                            catch { result.Errors++; }
                        }
                    }

                    trans.Commit();
                }
                catch (Exception ex)
                {
                    trans.RollBack();
                    result.FatalError = ex.Message;
                }
            }

            return result;
        }

        // ── Helpers ────────────────────────────────────────────────────────

        private static Curve ElevateCurve(Curve c, double z)
        {
            try
            {
                var p0 = c.GetEndPoint(0);
                var p1 = c.GetEndPoint(1);
                var e0 = new XYZ(p0.X, p0.Y, z);
                var e1 = new XYZ(p1.X, p1.Y, z);

                if (e0.DistanceTo(e1) < 1e-6) return null;

                if (c is Arc arc)
                {
                    var pm  = arc.Evaluate(0.5, true);
                    var em  = new XYZ(pm.X, pm.Y, z);
                    try { return Arc.Create(e0, e1, em); }
                    catch { return Line.CreateBound(e0, e1); }
                }

                return Line.CreateBound(e0, e1);
            }
            catch { return null; }
        }

        private static List<Curve> GetExistingBoundaryCurves(Document doc, View view)
        {
            var result = new List<Curve>();
            try
            {
                // Room separation lines are ModelCurves in category OST_RoomSeparationLines
                var collector = new FilteredElementCollector(doc, view.Id)
                    .OfClass(typeof(ModelCurve))
                    .Cast<ModelCurve>()
                    .Where(mc =>
                        mc.Category?.Id?.IntegerValue ==
                        (int)BuiltInCategory.OST_RoomSeparationLines);

                foreach (var mc in collector)
                {
                    try { result.Add(mc.GeometryCurve); } catch { }
                }
            }
            catch { }
            return result;
        }

        private static bool IsDuplicate(Curve newCurve, List<Curve> existing)
        {
            var np0 = newCurve.GetEndPoint(0);
            var np1 = newCurve.GetEndPoint(1);

            foreach (var ex in existing)
            {
                try
                {
                    var ep0 = ex.GetEndPoint(0);
                    var ep1 = ex.GetEndPoint(1);

                    // Check both directions (undirected comparison)
                    bool match1 = np0.DistanceTo(ep0) < EXIST_TOL && np1.DistanceTo(ep1) < EXIST_TOL;
                    bool match2 = np0.DistanceTo(ep1) < EXIST_TOL && np1.DistanceTo(ep0) < EXIST_TOL;

                    if (match1 || match2) return true;
                }
                catch { }
            }
            return false;
        }

        // ── Silent failure preprocessor ─────────────────────────────────────

        private class SilentFailurePreprocessor : IFailuresPreprocessor
        {
            public FailureProcessingResult PreprocessFailures(FailuresAccessor fa)
            {
                // Delete all warnings (non-fatal), let errors propagate
                var warnings = fa.GetFailureMessages()
                    .Where(m => m.GetSeverity() == FailureSeverity.Warning)
                    .ToList();

                foreach (var w in warnings)
                    fa.DeleteWarning(w);

                return FailureProcessingResult.Continue;
            }
        }
    }

    // ── Write result ────────────────────────────────────────────────────────

    public class WriteResult
    {
        public int    Created    { get; set; }
        public int    Skipped    { get; set; }
        public int    Errors     { get; set; }
        public string FatalError { get; set; }

        public bool Success => string.IsNullOrEmpty(FatalError);
    }
}

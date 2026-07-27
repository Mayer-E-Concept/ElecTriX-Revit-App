// CircuitDuplicateHandler.cs -- ME-Tools | Circuit Tagger duplicate-apartment reassign
// Mayer E-Concept SRL
//
// Two jobs, one transaction: (1) reassign CAx_Building/CAx_Apartment on the
// duplicated elements, and (2) place a circuit tag for each of them in the
// active view. (2) matters because Revit tags are view-specific annotation
// elements -- Copy/Paste (especially Paste Aligned to a different level)
// duplicates the host elements and their parameter values just fine, but the
// original tags stay behind in the source view. Without this, a pasted
// apartment shows correctly in Circuit Tagger's Stats (parameters travelled)
// but has no visible tags next to it (annotations didn't).
//
// Tag placement reuses CircuitTaggerHandler's own helpers (FindTagSymbol,
// GetElementCenter, GetFacingDirection, GetDirectionKey) rather than a
// second copy of that logic -- one intentional simplification versus the
// original "Apply & Tag" flow: this places each tag individually rather than
// grouping multiple elements sharing a wall position into a stacked column.
// That grouping mattered for a fresh multi-element tagging pass; here the
// elements were already laid out (they're a duplicate of an existing,
// already-tagged apartment), so simple per-element placement plus the same
// bounding-box-based alignment is enough.
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using METools.FamilyPlacer;
using System;
using System.Collections.Generic;
using System.Linq;

namespace METools.CircuitDuplicate
{
    public class CircuitDuplicateHandler : IExternalEventHandler
    {
        public ReassignRequest Request { get; set; }
        public Action<ReassignResult> OnDone { get; set; }

        public string GetName() => "ME-Tools Circuit Duplicate Reassign";

        public void Execute(UIApplication app)
        {
            var uiDoc = app.ActiveUIDocument;
            var doc = uiDoc?.Document;
            var req = Request;
            if (doc == null || req == null || req.ElementIds.Count == 0) return;

            var result = new ReassignResult();
            try
            {
                using (var tx = new Transaction(doc, "ME-Tools: Reassign House/Apartment + Tag"))
                {
                    tx.Start();

                    // -- 1) Reassign House/Apartment ------------------------------
                    foreach (var id in req.ElementIds)
                    {
                        Element el;
                        try { el = doc.GetElement(id); } catch { continue; }
                        if (el == null) continue;

                        bool changed = false;
                        try
                        {
                            var pB = el.LookupParameter(CircuitTaggerHandler.PARAM_BUILDING);
                            if (pB != null && !pB.IsReadOnly) { pB.Set(req.NewBuilding); changed = true; }
                        }
                        catch { }
                        try
                        {
                            var pA = el.LookupParameter(CircuitTaggerHandler.PARAM_APARTMENT);
                            if (pA != null && !pA.IsReadOnly) { pA.Set(req.NewApartment); changed = true; }
                        }
                        catch { }
                        if (changed) result.Updated++;
                    }

                    // -- 2) Place missing tags in the active view -----------------
                    var view = uiDoc.ActiveView;
                    var tagSymbol = CircuitTaggerHandler.FindTagSymbol(doc);
                    if (tagSymbol != null && (view is ViewPlan || view is ViewSection))
                    {
                        if (!tagSymbol.IsActive)
                        {
                            try { tagSymbol.Activate(); doc.Regenerate(); }
                            catch { tagSymbol = null; }
                        }
                    }
                    else
                    {
                        tagSymbol = null; // can't tag here -- report why below
                    }

                    if (tagSymbol != null)
                    {
                        var existingTagsByElement = new HashSet<ElementId>();
                        try
                        {
                            foreach (var tag in new FilteredElementCollector(doc, view.Id)
                                .OfClass(typeof(IndependentTag)).Cast<IndependentTag>())
                            {
                                try { foreach (var linkEl in tag.GetTaggedElementIds()) existingTagsByElement.Add(linkEl.HostElementId); }
                                catch { }
                            }
                        }
                        catch { }

                        double gapFt  = 50.0 / 304.8;  // matches CircuitTagStyle's default GapMm
                        double offYFt = 0.0;

                        foreach (var id in req.ElementIds)
                        {
                            if (existingTagsByElement.Contains(id)) continue; // already tagged here -- skip
                            try
                            {
                                var el = doc.GetElement(id);
                                if (el == null) continue;

                                var center = CircuitTaggerHandler.GetElementCenter(el);
                                var bb     = el.get_BoundingBox(view) ?? el.get_BoundingBox(null);
                                if (center == null) continue;

                                XYZ facing = CircuitTaggerHandler.GetFacingDirection(el as FamilyInstance);
                                string dir = CircuitTaggerHandler.GetDirectionKey(facing);
                                bool isNS  = dir == "N" || dir == "S";
                                var orient = isNS ? TagOrientation.Horizontal : TagOrientation.Vertical;

                                double tagX = center.X + gapFt;
                                double tagY = center.Y + offYFt;
                                var tagPos  = new XYZ(tagX, tagY, center.Z);

                                var newTag = IndependentTag.Create(doc, tagSymbol.Id, view.Id,
                                    new Reference(el), false, orient, tagPos);
                                result.Tagged++;

                                // Same smart-alignment idea as the main tagging flow:
                                // shift the tag so it reads cleanly next to the element
                                // instead of leaving it at the raw initial placement.
                                try
                                {
                                    doc.Regenerate();
                                    var tagBB = newTag.get_BoundingBox(view);
                                    if (tagBB != null)
                                    {
                                        double elRight = bb != null ? bb.Max.X : center.X;
                                        if (isNS)
                                        {
                                            double currentLeftX = tagBB.Min.X;
                                            double targetLeftX  = elRight + gapFt;
                                            newTag.TagHeadPosition = new XYZ(tagX + (targetLeftX - currentLeftX), tagY, center.Z);
                                        }
                                        else
                                        {
                                            double tagWidth = tagBB.Max.X - tagBB.Min.X;
                                            newTag.TagHeadPosition = new XYZ(elRight + gapFt + tagWidth * 0.5, tagY, center.Z);
                                        }
                                    }
                                }
                                catch { } // alignment failed -- tag is still placed, just at the raw initial position
                            }
                            catch { result.TagErrors++; }
                        }
                    }
                    else
                    {
                        result.NoTagReason = view == null
                            ? "no active view"
                            : !(view is ViewPlan || view is ViewSection)
                                ? "active view isn't a plan or section"
                                : "tag family 'ME-Tools_CircuitTag' not loaded -- run Project Health Check to fix this";
                    }

                    if (tx.GetStatus() == TransactionStatus.Started) tx.Commit();
                }
            }
            catch (Exception ex)
            {
                result.NoTagReason = "Reassign failed: " + ex.Message;
            }

            OnDone?.Invoke(result);
        }
    }
}

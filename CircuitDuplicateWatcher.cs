// CircuitDuplicateWatcher.cs -- ME-Tools | Circuit Tagger duplicate-apartment reassign
// Mayer E-Concept SRL
//
// Detects when elements that already carry Circuit Tagger's House/Apartment
// data (CAx_Building / CAx_Apartment) suddenly appear as newly-added elements
// in the document. That specific combination -- brand new element IDs, but
// values already filled in -- only happens when previously-tagged elements
// get duplicated: Copy/Paste, Paste Aligned, Mirror, Array, or placing a
// Group that contains them. A genuinely new placement (Family Placer, Lamp
// Placer, manually placing a family) always starts with those parameters
// empty, so this never fires for that case.
//
// On a match, prompts for a new House/Apartment so the duplicated apartment
// shows up as its own group in Circuit Tagger's Stats instead of merging
// into the original's counts.
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Events;
using Autodesk.Revit.UI;
using METools.FamilyPlacer;
using System.Collections.Generic;
using System.Linq;

namespace METools.CircuitDuplicate
{
    public static class CircuitDuplicateWatcher
    {
        private static CircuitDuplicateHandler _handler;
        private static ExternalEvent _event;
        private static bool _promptOpen; // one prompt at a time, even if DocumentChanged fires in bursts

        // Must be called from a valid API context (App.OnStartup does this) --
        // NOT lazily on first use from a button click. See CommentsHandler's
        // Ensure() for the exact failure mode this avoids.
        public static void Register(UIControlledApplication app)
        {
            _handler = new CircuitDuplicateHandler();
            _event = ExternalEvent.Create(_handler);
            _handler.OnDone = res =>
            {
                string msg = $"Reassigned {res.Updated} element(s).";
                if (res.Tagged > 0) msg += $" Placed {res.Tagged} tag(s) in the active view.";
                if (res.TagErrors > 0) msg += $" {res.TagErrors} tag(s) failed to place.";
                if (!string.IsNullOrEmpty(res.NoTagReason)) msg += $" No tags placed -- {res.NoTagReason}.";
                try { TaskDialog.Show("ME-Tools -- Circuit Tagger", msg); } catch { }
            };
            app.ControlledApplication.DocumentChanged += OnDocumentChanged;
        }

        private static void OnDocumentChanged(object sender, DocumentChangedEventArgs e)
        {
            try
            {
                if (_promptOpen) return;

                var addedIds = e.GetAddedElementIds();
                if (addedIds.Count == 0) return; // cheap early-exit for the common case (most edits add nothing)

                var doc = e.GetDocument();
                if (doc == null) return;

                var trackedCatIds = new HashSet<ElementId>(
                    METools.ProjectHealthCheckCollector.RequiredCategories.Select(c => new ElementId(c.Cat)));

                string oldBuilding = null, oldApartment = null;
                var matches = new List<ElementId>();

                foreach (var id in addedIds)
                {
                    Element el;
                    try { el = doc.GetElement(id); } catch { continue; }
                    if (el?.Category == null) continue;
                    if (!trackedCatIds.Contains(el.Category.Id)) continue;

                    string b = null, a = null;
                    try { b = el.LookupParameter(CircuitTaggerHandler.PARAM_BUILDING)?.AsString(); } catch { }
                    try { a = el.LookupParameter(CircuitTaggerHandler.PARAM_APARTMENT)?.AsString(); } catch { }
                    if (string.IsNullOrWhiteSpace(b) && string.IsNullOrWhiteSpace(a)) continue; // fresh placement, not a duplicate

                    matches.Add(id);
                    if (oldBuilding == null && !string.IsNullOrWhiteSpace(b)) oldBuilding = b;
                    if (oldApartment == null && !string.IsNullOrWhiteSpace(a)) oldApartment = a;
                }

                if (matches.Count == 0) return;

                _promptOpen = true;
                var prompt = new CircuitDuplicatePromptWindow(oldBuilding ?? "", oldApartment ?? "", matches.Count);
                prompt.Closed += (s, e2) => _promptOpen = false;
                prompt.OnApply = (newBuilding, newApartment) =>
                {
                    _handler.Request = new ReassignRequest
                    {
                        ElementIds  = matches,
                        NewBuilding = newBuilding,
                        NewApartment = newApartment,
                    };
                    _event.Raise();
                };
                prompt.Show();
            }
            catch { /* never let this watcher break the user's actual edit */ }
        }
    }
}

// DuplicateFamilyHandler.cs -- ME-Tools | Duplicate Family Finder
// Mayer E-Concept SRL
//
// Detects families that are very likely the same family loaded twice under
// different names -- a real, confirmed pattern seen earlier this session
// (e.g. "_HLSE_CAx WD_Bezug_UKD_OKB1", a stray "1" suffix from exactly this
// kind of duplication). Deliberately does NOT try to detect this by
// matching naming conventions (stripped " 1"/"(2)"/etc. suffixes) -- most
// electrical/MEP family names legitimately end in numbers as part of their
// real name (voltages, diameters, standards), so a naming-pattern approach
// would false-positive constantly. Instead, groups families by category +
// their exact, sorted set of type names: two independently-created,
// genuinely-different families essentially never happen to have the exact
// same complete type list by coincidence, so a match on that is a strong,
// content-based signal rather than a guess about spelling.
//
// Modeless + ExternalEvent, same as every other Diagnostics-hub tool --
// only reachable through the (already license-gated) Diagnostics hub, so
// no separate license check of its own is needed here.
using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace METools
{
    public class DuplicateFamilyHandler : IExternalEventHandler
    {
        public DuplicateFamilyRequest Request { get; set; } = new DuplicateFamilyRequest();
        public Action<List<DuplicateFamilyGroup>> OnScanDone { get; set; }
        public Action<ElementId> OnDeleteDone { get; set; }
        public Action<string> OnStatus { get; set; }

        public string GetName() => "Duplicate Family Finder";

        public void Execute(UIApplication app)
        {
            var req = Request;
            if (req == null || req.Action == DuplicateFamilyAction.None) return;
            var doc = app.ActiveUIDocument?.Document;
            var uiDoc = app.ActiveUIDocument;
            if (doc == null || uiDoc == null)
            {
                if (req.Action == DuplicateFamilyAction.BackToDiagnostics) DiagnosticsCommand.Open(app);
                else OnStatus?.Invoke("No active document.");
                return;
            }

            switch (req.Action)
            {
                case DuplicateFamilyAction.Scan:              ExecuteScan(doc); break;
                case DuplicateFamilyAction.GoTo:               ExecuteGoTo(doc, uiDoc, req); break;
                case DuplicateFamilyAction.Delete:             ExecuteDelete(doc, req); break;
                case DuplicateFamilyAction.BackToDiagnostics:  DiagnosticsCommand.Open(app); break;
            }
        }

        // Extracted so DiagnosticsHandler's "Run All Checks" orchestrator can
        // reuse the exact same scan this tool's own "Scan" button runs.
        public static List<DuplicateFamilyGroup> ScanForDuplicates(Document doc)
        {
            var groups = new List<DuplicateFamilyGroup>();
            try
            {
                // One pass over every placed instance, not one collector
                // call per family/type -- the same reasoning as this
                // session's other performance fixes (Circuit Tagger, Lamp
                // Placer, Activity Log Watcher): scales with document size
                // once, rather than with document size times candidate
                // count.
                var instanceCountByTypeId = new Dictionary<long, int>();
                var firstInstanceByTypeId = new Dictionary<long, ElementId>();
                foreach (var fi in new FilteredElementCollector(doc).OfClass(typeof(FamilyInstance)).Cast<FamilyInstance>())
                {
                    ElementId tid;
                    try { tid = fi.GetTypeId(); } catch { continue; }
                    if (tid == null || tid == ElementId.InvalidElementId) continue;
                    instanceCountByTypeId[tid.Value] = instanceCountByTypeId.TryGetValue(tid.Value, out var n) ? n + 1 : 1;
                    if (!firstInstanceByTypeId.ContainsKey(tid.Value)) firstInstanceByTypeId[tid.Value] = fi.Id;
                }

                var byKey = new Dictionary<string, List<Family>>();
                foreach (Family fam in new FilteredElementCollector(doc).OfClass(typeof(Family)))
                {
                    Category cat;
                    try { cat = fam.FamilyCategory; } catch { continue; }
                    if (cat == null) continue; // system/in-place families aren't what this is looking for

                    List<string> typeNames;
                    try
                    {
                        typeNames = fam.GetFamilySymbolIds()
                            .Select(id => doc.GetElement(id)?.Name)
                            .Where(n => !string.IsNullOrEmpty(n))
                            .OrderBy(n => n, StringComparer.Ordinal)
                            .ToList();
                    }
                    catch { continue; }
                    if (typeNames.Count == 0) continue;

                    string key = cat.Name + "|" + string.Join(",", typeNames);
                    if (!byKey.TryGetValue(key, out var list)) byKey[key] = list = new List<Family>();
                    list.Add(fam);
                }

                foreach (var kv in byKey)
                {
                    if (kv.Value.Count < 2) continue; // unique signature -- not a duplicate

                    int barIdx = kv.Key.IndexOf('|');
                    var group = new DuplicateFamilyGroup
                    {
                        CategoryName  = barIdx >= 0 ? kv.Key.Substring(0, barIdx) : kv.Key,
                        TypeSignature = barIdx >= 0 ? kv.Key.Substring(barIdx + 1) : "",
                    };

                    foreach (var fam in kv.Value)
                    {
                        int typeCount = 0, instCount = 0;
                        ElementId firstInst = ElementId.InvalidElementId;
                        try
                        {
                            foreach (var symId in fam.GetFamilySymbolIds())
                            {
                                typeCount++;
                                if (instanceCountByTypeId.TryGetValue(symId.Value, out var c)) instCount += c;
                                if (firstInst == ElementId.InvalidElementId && firstInstanceByTypeId.TryGetValue(symId.Value, out var fid))
                                    firstInst = fid;
                            }
                        }
                        catch { }

                        group.Members.Add(new DuplicateFamilyMember
                        {
                            FamilyId = fam.Id, FamilyName = fam.Name, TypeCount = typeCount,
                            InstanceCount = instCount, FirstInstanceId = firstInst,
                        });
                    }
                    // Most-used first -- makes "this is probably the one to
                    // keep" obvious without having to compare numbers by eye.
                    group.Members.Sort((a, b) => b.InstanceCount.CompareTo(a.InstanceCount));
                    groups.Add(group);
                }
            }
            catch { }

            groups.Sort((a, b) => string.Compare(a.CategoryName, b.CategoryName, StringComparison.OrdinalIgnoreCase));
            return groups;
        }

        private void ExecuteScan(Document doc)
        {
            OnScanDone?.Invoke(ScanForDuplicates(doc));
        }

        // Same proven pattern as Find Stray Elements / Collision Checker /
        // Imported Objects this session: switch view, select, zoom via
        // ZoomAndCenterRectangle rather than ShowElements.
        private void ExecuteGoTo(Document doc, UIDocument uiDoc, DuplicateFamilyRequest req)
        {
            if (req.TargetInstanceId == null || req.TargetInstanceId == ElementId.InvalidElementId)
            {
                OnStatus?.Invoke("No placed instance to go to -- this family has none.");
                return;
            }
            try
            {
                var el = doc.GetElement(req.TargetInstanceId);
                if (el == null) { OnStatus?.Invoke("That instance no longer exists."); return; }

                ElementId ownerViewId;
                try { ownerViewId = el.OwnerViewId; } catch { ownerViewId = ElementId.InvalidElementId; }

                View targetView = null;
                if (ownerViewId != ElementId.InvalidElementId)
                    targetView = doc.GetElement(ownerViewId) as View;
                else
                    targetView = uiDoc.ActiveView; // model-space element -- whatever view is already open can show it

                if (targetView != null && targetView.Id != uiDoc.ActiveView?.Id)
                    uiDoc.ActiveView = targetView;
                uiDoc.Selection.SetElementIds(new List<ElementId> { req.TargetInstanceId });
                try { uiDoc.RefreshActiveView(); } catch { }

                var effectiveView = targetView ?? uiDoc.ActiveView;
                BoundingBoxXYZ bbox = null;
                try { bbox = el.get_BoundingBox(effectiveView) ?? el.get_BoundingBox(null); } catch { }
                if (bbox != null)
                {
                    double padX = Math.Max((bbox.Max.X - bbox.Min.X) * 0.5, 2.0 / 304.8);
                    double padY = Math.Max((bbox.Max.Y - bbox.Min.Y) * 0.5, 2.0 / 304.8);
                    var min = new XYZ(bbox.Min.X - padX, bbox.Min.Y - padY, bbox.Min.Z);
                    var max = new XYZ(bbox.Max.X + padX, bbox.Max.Y + padY, bbox.Max.Z);
                    var openUiView = uiDoc.GetOpenUIViews().FirstOrDefault(uv => uv.ViewId == uiDoc.ActiveView?.Id);
                    openUiView?.ZoomAndCenterRectangle(min, max);
                }
            }
            catch { }
        }

        // Only ever offered in the UI for a family with zero placed
        // instances (see DuplicateFamilyWindow) -- safe regardless of
        // whether it's genuinely a duplicate, the same reasoning Purge
        // Unused already relies on elsewhere in this app.
        private void ExecuteDelete(Document doc, DuplicateFamilyRequest req)
        {
            try
            {
                using (var tx = new Transaction(doc, "ME-Tools: Delete Duplicate Family"))
                {
                    tx.Start();
                    doc.Delete(req.TargetFamilyId);
                    tx.Commit();
                }
                OnDeleteDone?.Invoke(req.TargetFamilyId);
            }
            catch (Exception ex)
            {
                OnStatus?.Invoke("Delete failed: " + ex.Message);
            }
        }
    }
}

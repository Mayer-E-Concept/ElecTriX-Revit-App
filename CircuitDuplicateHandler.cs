// CircuitDuplicateHandler.cs -- ME-Tools | Circuit Tagger duplicate-apartment reassign
// Mayer E-Concept SRL
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using System;

namespace METools.CircuitDuplicate
{
    public class CircuitDuplicateHandler : IExternalEventHandler
    {
        public ReassignRequest Request { get; set; }
        public Action<ReassignResult> OnDone { get; set; }

        public string GetName() => "ME-Tools Circuit Duplicate Reassign";

        public void Execute(UIApplication app)
        {
            var doc = app.ActiveUIDocument?.Document;
            var req = Request;
            if (doc == null || req == null || req.ElementIds.Count == 0) return;

            int updated = 0;
            try
            {
                using (var tx = new Transaction(doc, "ME-Tools: Reassign House/Apartment"))
                {
                    tx.Start();
                    foreach (var id in req.ElementIds)
                    {
                        Element el;
                        try { el = doc.GetElement(id); } catch { continue; }
                        if (el == null) continue;

                        bool changed = false;
                        try
                        {
                            var pB = el.LookupParameter("CAx_Building");
                            if (pB != null && !pB.IsReadOnly) { pB.Set(req.NewBuilding); changed = true; }
                        }
                        catch { }
                        try
                        {
                            var pA = el.LookupParameter("CAx_Apartment");
                            if (pA != null && !pA.IsReadOnly) { pA.Set(req.NewApartment); changed = true; }
                        }
                        catch { }
                        if (changed) updated++;
                    }
                    if (tx.GetStatus() == TransactionStatus.Started) tx.Commit();
                }
            }
            catch { }

            OnDone?.Invoke(new ReassignResult { Updated = updated });
        }
    }
}

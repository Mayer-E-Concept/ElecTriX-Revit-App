// CircuitDuplicateModels.cs -- ME-Tools | Circuit Tagger duplicate-apartment reassign
// Mayer E-Concept SRL
using Autodesk.Revit.DB;
using System.Collections.Generic;

namespace METools.CircuitDuplicate
{
    public class ReassignRequest
    {
        public List<ElementId> ElementIds = new List<ElementId>();
        public string NewBuilding  = "";
        public string NewApartment = "";
    }

    public class ReassignResult
    {
        public int Updated;
    }
}

// DuplicateFamilyModels.cs -- ME-Tools | Duplicate Family Finder
// Mayer E-Concept SRL
using System.Collections.Generic;
using Autodesk.Revit.DB;

namespace METools
{
    public enum DuplicateFamilyAction { None, Scan, GoTo, Delete, BackToDiagnostics }

    // One family within a duplicate group.
    public class DuplicateFamilyMember
    {
        public ElementId FamilyId;
        public string    FamilyName;
        public int       TypeCount;
        public int       InstanceCount;
        public ElementId FirstInstanceId; // InvalidElementId if InstanceCount == 0 -- nothing to Go To
    }

    // A group of 2+ families sharing the same category and exact same set of
    // type names -- confirmed via this app's own detection logic to be by
    // far the most reliable signal that they're really the same family
    // loaded twice under different names, rather than guessing from
    // naming conventions (which vary a lot and false-positive easily on
    // legitimate names that happen to end in a number).
    public class DuplicateFamilyGroup
    {
        public string CategoryName;
        public string TypeSignature; // the shared, sorted type-name list, for display
        public List<DuplicateFamilyMember> Members = new List<DuplicateFamilyMember>();
    }

    public class DuplicateFamilyRequest
    {
        public DuplicateFamilyAction Action;
        public ElementId TargetFamilyId;   // for GoTo / Delete
        public ElementId TargetInstanceId; // for GoTo specifically
    }
}

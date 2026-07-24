// IfcLevelImportModels.cs -- ME-Tools | IFC Level Importer
// Mayer E-Concept SRL
using System.Collections.Generic;

namespace METools.IfcImport
{
    // One row in the window's checkbox table -- wraps a parsed IfcLevelInfo
    // plus the UI-only IsSelected state and a note if it can't be imported
    // as-is (e.g. a Revit level with that exact name already exists).
    public class IfcLevelRow
    {
        public IfcLevelInfo Info;
        public bool IsSelected;
        public string BlockReason; // null = importable
        public bool Importable => string.IsNullOrEmpty(BlockReason);
    }

    public class IfcLevelImportRequest
    {
        public List<IfcLevelInfo> LevelsToCreate = new List<IfcLevelInfo>();
        public double LengthUnitToMeters = 1.0;
    }

    public class IfcLevelImportResultInfo
    {
        public int Created;
        public int Skipped;
        public List<string> SkippedNames = new List<string>();
    }
}

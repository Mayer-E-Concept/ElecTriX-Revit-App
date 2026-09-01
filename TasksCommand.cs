// TasksCommand.cs -- ME-Tools | Tasks ribbon command
// Mayer E-Concept SRL
//
// No longer requires an open document -- the window is a cross-project
// dashboard now, and the shared folder path it reads from comes from
// Comments' own machine-level setting, not from anything in a Document.
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace METools.Tasks
{
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class TasksCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            TasksWindow.ShowOrActivate(commandData.Application);
            return Result.Succeeded;
        }
    }
}

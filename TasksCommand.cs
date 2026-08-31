// TasksCommand.cs -- ME-Tools | Tasks ribbon command
// Mayer E-Concept SRL
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using METools.Comments;

namespace METools.Tasks
{
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class TasksCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var uiApp = commandData.Application;
            var doc = uiApp.ActiveUIDocument?.Document;
            if (doc == null)
            {
                message = "Open a project before using Tasks.";
                return Result.Failed;
            }

            // Same GUID CommentsStorage already stamps into this project --
            // a task file for this project lives right next to its
            // comments file, keyed by the same id.
            var projectId = CommentsStorage.GetOrCreateProjectId(doc);
            if (string.IsNullOrEmpty(projectId))
            {
                message = "Could not identify this project.";
                return Result.Failed;
            }

            TasksWindow.ShowOrActivate(uiApp, projectId);
            return Result.Succeeded;
        }
    }
}

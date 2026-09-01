// TasksHandler.cs -- ME-Tools | Tasks ExternalEvent handler
// Mayer E-Concept SRL
//
// Every action from the modeless TasksWindow -- even Claim, Release and
// MarkDone, which are pure file I/O with no Revit API involved -- is
// queued through here rather than running inline from a WPF click
// handler, mirroring CommentsHandler's request/action pattern exactly.
//
// Since the window now shows tasks from every project at once (not just
// whichever one it was opened from), a mutation has to be told which
// project's file to touch per request -- see TasksRequest.TaskProjectId --
// rather than this handler holding one fixed ProjectId for its whole
// lifetime the way the original single-project version did.
using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace METools.Tasks
{
    public class TasksHandler : IExternalEventHandler
    {
        public TasksRequest Request { get; set; }

        // Fired once Execute finishes, with the reloaded cross-project
        // task list, freshly computed stats, and an optional message to
        // surface (an error, or a storage warning).
        public Action<List<ProjectTask>, TaskStats, string> OnComplete { get; set; }

        public void Execute(UIApplication app)
        {
            var request = Request;
            if (request == null) return;

            string error = null;

            switch (request.Action)
            {
                case TasksAction.Claim:
                    {
                        var result = TasksStorage.TryClaim(request.TaskProjectId, request.TaskId, request.CurrentUser, out var claimedBy, out error);
                        if (result == ClaimResult.AlreadyClaimed)
                            error = $"Someone beat you to it -- already claimed by {claimedBy}.";
                        else if (result == ClaimResult.NotFound)
                            error = "That task is no longer on the list.";
                        break;
                    }

                case TasksAction.Release:
                    {
                        var result = TasksStorage.Release(request.TaskProjectId, request.TaskId, request.CurrentUser, out error);
                        if (result == ClaimResult.NotYours)
                            error = "That task isn't assigned to you, so it can't be released from here.";
                        else if (result == ClaimResult.NotFound)
                            error = "That task is no longer on the list.";
                        break;
                    }

                case TasksAction.MarkDone:
                    TasksStorage.MarkDone(request.TaskProjectId, request.TaskId, out error);
                    break;

                case TasksAction.GoToElement:
                    GoToElement(app, request.ReferencedElementId);
                    break;

                case TasksAction.AttachElement:
                    AttachElement(app, request);
                    break;

                case TasksAction.RegisterCurrentProject:
                    error = RegisterCurrentProject(app);
                    break;

                case TasksAction.MoveToProject:
                    TasksStorage.MoveTaskToProject(request.TaskProjectId, request.TargetProjectId, request.TaskId, out error);
                    break;

                case TasksAction.Delete:
                    TasksStorage.DeleteTask(request.TaskProjectId, request.TaskId, out error);
                    break;

                case TasksAction.Refresh:
                    // Pure reload -- nothing to do against the document.
                    break;
            }

            var list = TasksStorage.LoadAllAcrossProjects(out var warning);
            var stats = ComputeStats(list);
            OnComplete?.Invoke(list, stats, error ?? warning);
        }

        private static TaskStats ComputeStats(List<ProjectTask> list) => new TaskStats
        {
            Total = list.Count,
            Unassigned = list.Count(t => string.IsNullOrWhiteSpace(t.AssignedTo)),
            InProgress = list.Count(t => !string.IsNullOrWhiteSpace(t.AssignedTo) && t.Status != "done"),
            Done = list.Count(t => t.Status == "done"),
        };

        private void GoToElement(UIApplication app, string elementUniqueId)
        {
            var uidoc = app.ActiveUIDocument;
            if (uidoc == null || string.IsNullOrEmpty(elementUniqueId)) return;

            Element element;
            try { element = uidoc.Document.GetElement(elementUniqueId); }
            catch { element = null; }

            if (element == null) return; // stale reference, or the element belongs to a different project than what's open -- the reloaded list is still shown either way

            uidoc.Selection.SetElementIds(new List<ElementId> { element.Id });
            uidoc.ShowElements(element);
        }

        // Pins the currently-selected element to a task, mirroring
        // Comments' "+ Reference Item". Requires something already
        // selected in the model when the button is clicked.
        private void AttachElement(UIApplication app, TasksRequest request)
        {
            var uidoc = app.ActiveUIDocument;
            if (uidoc == null) return;

            var selectedIds = uidoc.Selection.GetElementIds();
            if (selectedIds.Count == 0) return;

            var doc = uidoc.Document;
            var element = doc.GetElement(selectedIds.First());
            if (element == null) return;

            TasksStorage.Mutate(request.TaskProjectId, list =>
            {
                var task = list.Find(t => t.Id == request.TaskId);
                if (task == null) return;
                task.ReferencedElementId = element.UniqueId;
                task.ReferencedSummary = $"{element.Category?.Name} - {element.Name}";
            }, out _);
        }

        public string GetName() => "METools Tasks Handler";

        // Pulls Project Name/Number/Address/Client Name straight out of
        // Revit's own Project Information -- and the document's file
        // title, which is always present even when those fields are left
        // blank (common in practice) -- rather than asking for any of it
        // to be typed by hand. Real project metadata is both less effort
        // and less error-prone than remembering to type "Hamburg_V2"
        // correctly into a JSON file.
        private string RegisterCurrentProject(UIApplication app)
        {
            var uidoc = app.ActiveUIDocument;
            if (uidoc == null)
                return "Open a project in Revit first, then try registering it.";

            var doc = uidoc.Document;
            var projectId = METools.Comments.CommentsStorage.GetOrCreateProjectId(doc);
            if (string.IsNullOrEmpty(projectId))
                return "Could not identify this project.";

            string ProjectField(string paramName)
            {
                try { return doc.ProjectInformation?.LookupParameter(paramName)?.AsString(); }
                catch { return null; }
            }

            var projectName = ProjectField("Project Name");
            var projectNumber = ProjectField("Project Number");
            var projectAddress = ProjectField("Project Address");
            var clientName = ProjectField("Client Name");
            var fileTitle = doc.Title;

            var displayName = !string.IsNullOrWhiteSpace(projectName) ? projectName : fileTitle;

            var keywords = new List<string> { fileTitle, projectName, projectNumber, projectAddress, clientName }
                .Where(k => !string.IsNullOrWhiteSpace(k) && k.Trim().Length >= 3)
                .Select(k => k.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            var ok = TasksStorage.RegisterProject(projectId, displayName, keywords, null, out var storageError);
            return ok
                ? $"Registered '{displayName}' for Tasks -- matches on: {string.Join(", ", keywords)}."
                : $"Could not register this project: {storageError}";
        }
    }
}

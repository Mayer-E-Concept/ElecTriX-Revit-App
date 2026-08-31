// TasksHandler.cs -- ME-Tools | Tasks ExternalEvent handler
// Mayer E-Concept SRL
//
// Every action from the modeless TasksWindow -- even Claim and MarkDone,
// which are pure file I/O with no Revit API involved -- is queued through
// here rather than running inline from a WPF click handler. That mirrors
// CommentsHandler's CommentsAction/CommentsRequest pattern exactly: one
// path for every action, so nobody has to remember which ones are
// "special" and safe to call directly.
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
        public string ProjectId { get; set; }

        // Fired once Execute finishes, with the reloaded task list and an
        // optional message to surface (an error, or a storage warning).
        public Action<List<ProjectTask>, string> OnComplete { get; set; }

        public void Execute(UIApplication app)
        {
            var request = Request;
            if (request == null) return;

            string error = null;

            switch (request.Action)
            {
                case TasksAction.Claim:
                    {
                        var result = TasksStorage.TryClaim(ProjectId, request.TaskId, request.CurrentUser, out var claimedBy, out error);
                        if (result == ClaimResult.AlreadyClaimed)
                            error = $"Someone beat you to it -- already claimed by {claimedBy}.";
                        else if (result == ClaimResult.NotFound)
                            error = "That task is no longer on the list.";
                        break;
                    }

                case TasksAction.MarkDone:
                    TasksStorage.MarkDone(ProjectId, request.TaskId, out error);
                    break;

                case TasksAction.GoToElement:
                    GoToElement(app, request.ReferencedElementId);
                    break;

                case TasksAction.AttachElement:
                    AttachElement(app, request);
                    break;

                case TasksAction.Refresh:
                    // Pure reload -- nothing to do against the document.
                    break;
            }

            var list = TasksStorage.LoadAll(ProjectId, out var warning);
            OnComplete?.Invoke(list, error ?? warning);
        }

        private void GoToElement(UIApplication app, string elementUniqueId)
        {
            var uidoc = app.ActiveUIDocument;
            if (uidoc == null || string.IsNullOrEmpty(elementUniqueId)) return;

            Element element;
            try { element = uidoc.Document.GetElement(elementUniqueId); }
            catch { element = null; }

            if (element == null) return; // stale reference -- the reloaded list is still shown either way

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

            TasksStorage.Mutate(ProjectId, list =>
            {
                var task = list.Find(t => t.Id == request.TaskId);
                if (task == null) return;
                task.ReferencedElementId = element.UniqueId;
                task.ReferencedSummary = $"{element.Category?.Name} - {element.Name}";
            }, out _);
        }

        public string GetName() => "METools Tasks Handler";
    }
}

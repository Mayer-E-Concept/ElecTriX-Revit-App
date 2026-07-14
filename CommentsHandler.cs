// CommentsHandler.cs -- ME-Tools | Project Comments
// Mayer E-Concept SRL
using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace METools.Comments
{
    public class CommentsHandler : IExternalEventHandler
    {
        public CommentsRequest Request;

        public Action<List<ProjectComment>> OnLoaded;
        public Action<string> OnError;
        public Action<string> OnCurrentLevel; // reports the active level's name, "" if none

        public void Execute(UIApplication app)
        {
            var uidoc = app.ActiveUIDocument;
            var doc = uidoc?.Document;
            if (doc == null) { OnError?.Invoke("No active document."); return; }

            var req = Request;
            if (req == null) return;

            try
            {
                var projectId = CommentsStorage.GetOrCreateProjectId(doc);

                switch (req.Action)
                {
                    case CommentsAction.Refresh:
                        OnLoaded?.Invoke(CommentsStorage.LoadAll(projectId));
                        OnCurrentLevel?.Invoke(CurrentLevelName(uidoc) ?? "");
                        break;

                    case CommentsAction.Add:
                        string author = SafeUsername(app);
                        string levelName = !string.IsNullOrWhiteSpace(req.LevelName)
                            ? req.LevelName : (CurrentLevelName(uidoc) ?? "(no level)");
                        bool ok = CommentsStorage.Mutate(projectId, list =>
                        {
                            list.Add(new ProjectComment
                            {
                                Author     = author,
                                LevelName  = levelName,
                                Text       = req.Text ?? "",
                                CreatedUtc = DateTime.UtcNow,
                                Status     = CommentStatus.Open,
                            });
                        }, out string err);
                        if (!ok) OnError?.Invoke(err);
                        OnLoaded?.Invoke(CommentsStorage.LoadAll(projectId));
                        break;

                    case CommentsAction.SetStatus:
                        string resolver = SafeUsername(app);
                        bool ok2 = CommentsStorage.Mutate(projectId, list =>
                        {
                            var c = list.FirstOrDefault(x => x.Id == req.CommentId);
                            if (c != null)
                            {
                                c.Status      = req.NewStatus;
                                c.ResolvedBy  = resolver;
                                c.ResolvedUtc = DateTime.UtcNow;
                            }
                        }, out string err2);
                        if (!ok2) OnError?.Invoke(err2);
                        OnLoaded?.Invoke(CommentsStorage.LoadAll(projectId));
                        break;

                    case CommentsAction.JumpToLevel:
                        JumpTo(uidoc, req.LevelName);
                        break;
                }
            }
            catch (Exception ex) { OnError?.Invoke(ex.Message); }
        }

        private static string SafeUsername(UIApplication app)
        {
            try { return app.Application.Username; } catch { return "Unknown"; }
        }

        public static string CurrentLevelName(UIDocument uidoc)
        {
            try { return (uidoc?.ActiveView as ViewPlan)?.GenLevel?.Name; }
            catch { return null; }
        }

        private void JumpTo(UIDocument uidoc, string levelName)
        {
            if (uidoc == null || string.IsNullOrWhiteSpace(levelName)) return;
            try
            {
                var doc = uidoc.Document;
                var level = new FilteredElementCollector(doc).OfClass(typeof(Level))
                    .Cast<Level>()
                    .FirstOrDefault(l => string.Equals(l.Name, levelName, StringComparison.OrdinalIgnoreCase));
                if (level == null) return;

                var plan = new FilteredElementCollector(doc).OfClass(typeof(ViewPlan))
                    .Cast<ViewPlan>()
                    .FirstOrDefault(v => !v.IsTemplate && v.ViewType == ViewType.FloorPlan
                                         && v.GenLevel != null && v.GenLevel.Id == level.Id);
                if (plan != null) uidoc.ActiveView = plan;
            }
            catch { }
        }

        public string GetName() => "ME-Tools Comments";

        // ── Static convenience API for the popup ───────────────────────────
        // The popup is a lightweight, standalone window that can appear whether
        // or not the main Comments management window is open, so it needs its
        // own always-available ExternalEvent rather than borrowing the main
        // window's instance. Mirrors AppSwitcher's Ensure()/static pattern.
        private static CommentsHandler _quickHandler;
        private static ExternalEvent _quickEvent;

        public static void MarkStatus(string commentId, CommentStatus status)
        {
            if (_quickEvent == null)
            {
                _quickHandler = new CommentsHandler();
                _quickEvent = ExternalEvent.Create(_quickHandler);
            }
            _quickHandler.Request = new CommentsRequest
            {
                Action = CommentsAction.SetStatus,
                CommentId = commentId,
                NewStatus = status,
            };
            _quickEvent.Raise();
        }

        public static void JumpToLevel(string levelName)
        {
            if (_quickEvent == null)
            {
                _quickHandler = new CommentsHandler();
                _quickEvent = ExternalEvent.Create(_quickHandler);
            }
            _quickHandler.Request = new CommentsRequest
            {
                Action = CommentsAction.JumpToLevel,
                LevelName = levelName,
            };
            _quickEvent.Raise();
        }
    }
}

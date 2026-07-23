// CommentsHandler.cs -- ME-Tools | Project Comments
// Mayer E-Concept SRL
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace METools.Comments
{
    public class CommentsHandler : IExternalEventHandler
    {
        public CommentsRequest Request;

        public Action<List<ProjectComment>> OnLoaded;
        public Action<string> OnError;
        public Action<string, string> OnCurrentLevel; // (levelName, scopeBoxName); "" if none
        public Action<bool, string> OnGoToElementResult; // (success, message-if-any)

        // Execute runs on Revit's own UI thread (required for any Revit API
        // access). The project-ID lookup and level/username reads below are
        // fast, in-memory, and Revit-API-dependent, so they stay synchronous
        // here. The actual comment file read/write, however, goes over the
        // shared network drive and can be genuinely slow (confirmed: 15-20s
        // observed on a real machine) -- if that ran synchronously here, a
        // slow moment on the network would freeze the ENTIRE Revit UI thread,
        // not just this feature, for as long as the network call took. That's
        // offloaded to a background thread below instead; it touches nothing
        // Revit-API-related, only plain file I/O, so it's safe off-thread.
        // The callbacks are safe to invoke from that background thread too --
        // every subscriber already wraps them in Dispatcher.Invoke (see
        // CommentsWindow's constructor), so the actual UI update still lands
        // correctly back on the UI thread regardless of which thread raises it.
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
                        OnCurrentLevel?.Invoke(CurrentLevelName(uidoc) ?? "", CurrentScopeBoxName(uidoc) ?? "");
                        Task.Run(() =>
                        {
                            try
                            {
                                var list = CommentsStorage.LoadAll(projectId, out string warning);
                                if (warning != null) OnError?.Invoke(warning);
                                OnLoaded?.Invoke(list);
                            }
                            catch (Exception ex) { OnError?.Invoke(ex.Message); }
                        });
                        break;

                    case CommentsAction.Add:
                    {
                        string author = SafeUsername(app);
                        string levelName = !string.IsNullOrWhiteSpace(req.LevelName)
                            ? req.LevelName : (CurrentLevelName(uidoc) ?? "(no level)");
                        // Always taken fresh from the current view, paired with
                        // whichever level was just resolved above -- there's no
                        // override path for this the way LevelName has, since
                        // nothing upstream currently needs one.
                        string scopeBoxName = CurrentScopeBoxName(uidoc) ?? "";
                        string text = req.Text ?? "";
                        string refElId = req.ReferencedElementId ?? "";
                        string refSummary = req.ReferencedSummary ?? "";
                        Task.Run(() =>
                        {
                            try
                            {
                                bool ok = CommentsStorage.Mutate(projectId, list =>
                                {
                                    list.Add(new ProjectComment
                                    {
                                        Author       = author,
                                        LevelName    = levelName,
                                        ScopeBoxName = scopeBoxName,
                                        Text         = text,
                                        CreatedUtc   = DateTime.UtcNow,
                                        Status       = CommentStatus.Open,
                                        ReferencedElementId = refElId,
                                        ReferencedSummary   = refSummary,
                                    });
                                }, out string err);
                                if (!ok) OnError?.Invoke(err);
                                OnLoaded?.Invoke(CommentsStorage.LoadAll(projectId));
                            }
                            catch (Exception ex) { OnError?.Invoke(ex.Message); }
                        });
                        break;
                    }

                    case CommentsAction.SetStatus:
                    {
                        string resolver = SafeUsername(app);
                        string commentId = req.CommentId;
                        var newStatus = req.NewStatus;
                        Task.Run(() =>
                        {
                            try
                            {
                                bool ok = CommentsStorage.Mutate(projectId, list =>
                                {
                                    var c = list.FirstOrDefault(x => x.Id == commentId);
                                    if (c != null)
                                    {
                                        c.Status      = newStatus;
                                        c.ResolvedBy  = resolver;
                                        c.ResolvedUtc = DateTime.UtcNow;
                                    }
                                }, out string err);
                                if (!ok) OnError?.Invoke(err);
                                OnLoaded?.Invoke(CommentsStorage.LoadAll(projectId));
                            }
                            catch (Exception ex) { OnError?.Invoke(ex.Message); }
                        });
                        break;
                    }

                    case CommentsAction.Delete:
                    {
                        string commentId = req.CommentId;
                        Task.Run(() =>
                        {
                            try
                            {
                                bool ok = CommentsStorage.Mutate(projectId, list =>
                                {
                                    list.RemoveAll(x => x.Id == commentId);
                                }, out string err);
                                if (!ok) OnError?.Invoke(err);
                                OnLoaded?.Invoke(CommentsStorage.LoadAll(projectId));
                            }
                            catch (Exception ex) { OnError?.Invoke(ex.Message); }
                        });
                        break;
                    }

                    case CommentsAction.JumpToLevel:
                        // Genuinely Revit-API-only (switching the active view),
                        // no network I/O involved, so this stays synchronous.
                        JumpTo(uidoc, req.LevelName, req.ScopeBoxName);
                        break;

                    case CommentsAction.GoToElement:
                        GoToElementInternal(uidoc, req.ReferencedElementId);
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

        // "Scope Box" is a genuine, standard Revit view parameter (confirmed
        // live: parameter id -1012202, display name "Scope Box") -- this is
        // the real mechanism Revit itself uses to separate views by building
        // section. Looked up by display name rather than a hardcoded
        // BuiltInParameter enum constant so this doesn't depend on getting an
        // exact enum name right from memory; "Scope Box" is the parameter's
        // actual, stable UI name for any English-language Revit install.
        public static string CurrentScopeBoxName(UIDocument uidoc)
        {
            try { return uidoc?.ActiveView?.LookupParameter("Scope Box")?.AsValueString(); }
            catch { return null; }
        }

        // Confirmed via live model inspection: two different building
        // sections (e.g. Haus 1 / Haus 2) can each have their own floor plan
        // view that reports the exact same Level name ("Obergeschoss 1" for
        // both), distinguished only by which Scope Box each view is assigned.
        // Matching on level name alone can jump to the wrong building
        // section's same-named level -- this collects every level and view
        // that shares the name, then narrows down by Scope Box when we have
        // one. Older comments saved before this fix won't have a
        // ScopeBoxName; those fall back to the first matching-level view,
        // same as before.
        private void JumpTo(UIDocument uidoc, string levelName, string scopeBoxName)
        {
            if (uidoc == null || string.IsNullOrWhiteSpace(levelName)) return;
            try
            {
                var doc = uidoc.Document;
                var levelIds = new HashSet<ElementId>(
                    new FilteredElementCollector(doc).OfClass(typeof(Level))
                        .Cast<Level>()
                        .Where(l => string.Equals(l.Name, levelName, StringComparison.OrdinalIgnoreCase))
                        .Select(l => l.Id));
                if (levelIds.Count == 0) return;

                var candidatePlans = new FilteredElementCollector(doc).OfClass(typeof(ViewPlan))
                    .Cast<ViewPlan>()
                    .Where(v => !v.IsTemplate && v.ViewType == ViewType.FloorPlan
                                && v.GenLevel != null && levelIds.Contains(v.GenLevel.Id))
                    .ToList();

                ViewPlan plan = null;
                if (!string.IsNullOrWhiteSpace(scopeBoxName))
                {
                    plan = candidatePlans.FirstOrDefault(v =>
                        string.Equals(v.LookupParameter("Scope Box")?.AsValueString(), scopeBoxName,
                                      StringComparison.OrdinalIgnoreCase));
                }
                if (plan == null) plan = candidatePlans.FirstOrDefault();

                if (plan != null) uidoc.ActiveView = plan;
            }
            catch { }
        }

        // Selects and zooms to a specific element via UIDocument.ShowElements --
        // the standard Revit API method for exactly this ("take me to this
        // element"), which picks an appropriate view and frames the element
        // automatically. Degrades gracefully if the element has since been
        // deleted, same philosophy as Activity Log's level navigation.
        private void GoToElementInternal(UIDocument uidoc, string elementIdStr)
        {
            if (uidoc == null) { OnGoToElementResult?.Invoke(false, "No active document."); return; }
            if (string.IsNullOrWhiteSpace(elementIdStr) || !int.TryParse(elementIdStr, out int idInt))
            { OnGoToElementResult?.Invoke(false, "No element recorded for this comment."); return; }

            try
            {
                var doc = uidoc.Document;
                var elementId = new ElementId(idInt);
                var el = doc.GetElement(elementId);
                if (el == null)
                { OnGoToElementResult?.Invoke(false, "That element no longer exists in this project."); return; }

                var ids = new List<ElementId> { elementId };
                uidoc.ShowElements(ids);
                uidoc.Selection.SetElementIds(ids);
                OnGoToElementResult?.Invoke(true, null);
            }
            catch (Exception ex) { OnGoToElementResult?.Invoke(false, ex.Message); }
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

        public static void JumpToLevel(string levelName, string scopeBoxName)
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
                ScopeBoxName = scopeBoxName,
            };
            _quickEvent.Raise();
        }

        public static void GoToElement(string elementId, Action<bool, string> onResult = null)
        {
            if (_quickEvent == null)
            {
                _quickHandler = new CommentsHandler();
                _quickEvent = ExternalEvent.Create(_quickHandler);
            }
            if (onResult != null) _quickHandler.OnGoToElementResult = onResult;
            _quickHandler.Request = new CommentsRequest
            {
                Action = CommentsAction.GoToElement,
                ReferencedElementId = elementId,
            };
            _quickEvent.Raise();
        }
    }
}

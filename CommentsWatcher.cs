// CommentsWatcher.cs -- ME-Tools | Project Comments background notifier
// Mayer E-Concept SRL
//
// Runs for the lifetime of the Revit session. Two triggers decide when to
// check for new comments:
//   1. Idling -- fires frequently while Revit is idle; throttled internally
//      to one actual check no more than every ~45 seconds. This uses Idling
//      rather than a WPF timer specifically because Idling's event args hand
//      over a live UIApplication, so every check already runs in a valid API
//      context -- no separate ExternalEvent round-trip needed just to read a
//      shared file and an Extensible Storage entity.
//   2. ViewActivated -- fires the instant the user switches views, so a
//      comment on the level just navigated to shows up immediately instead
//      of waiting for the next timed check.
// Comments already shown once this session, or authored by the current user,
// are never popped up again -- tracked in an in-memory set that resets only
// when Revit restarts.
using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Events;

namespace METools.Comments
{
    public static class CommentsWatcher
    {
        private static DateTime _lastCheck = DateTime.MinValue;
        private static readonly TimeSpan CheckInterval = TimeSpan.FromSeconds(45);
        private static readonly HashSet<string> _shownIds = new HashSet<string>();

        public static void Register(UIControlledApplication app)
        {
            app.Idling += OnIdling;
            app.ViewActivated += OnViewActivated;
        }

        private static void OnIdling(object sender, IdlingEventArgs e)
        {
            if (DateTime.UtcNow - _lastCheck < CheckInterval) return;
            _lastCheck = DateTime.UtcNow;
            try { CheckFor((sender as UIApplication)?.ActiveUIDocument, null); } catch { }
        }

        private static void OnViewActivated(object sender, ViewActivatedEventArgs e)
        {
            try
            {
                var view = e.CurrentActiveView as ViewPlan;
                var levelName = view?.GenLevel?.Name;
                // Same disambiguation as JumpTo/CurrentScopeBoxName: level names
                // alone can be ambiguous across building sections, so Scope Box
                // narrows a "new comment on this level" check down to the exact
                // section actually being viewed, not just any same-named level.
                string scopeBoxName = null;
                try { scopeBoxName = view?.LookupParameter("Scope Box")?.AsValueString(); } catch { }
                CheckFor((sender as UIApplication)?.ActiveUIDocument, levelName, scopeBoxName);
            }
            catch { }
        }

        private static void CheckFor(UIDocument uidoc, string onlyLevelName, string onlyScopeBoxName = null)
        {
            try
            {
                if (uidoc == null) return;
                var doc = uidoc.Document;
                if (doc == null || doc.IsFamilyDocument) return;
                if (METools.LicenseManager.IsTrialExpired) return; // silent gate, no nag dialog from a background check

                var folder = CommentsStorage.GetSharedFolder();
                if (string.IsNullOrWhiteSpace(folder)) return; // feature not configured yet -- nothing to check

                var projectId = CommentsStorage.GetOrCreateProjectId(doc);
                if (string.IsNullOrWhiteSpace(projectId)) return;

                string me = "";
                try { me = uidoc.Application.Application.Username; } catch { }

                var candidates = CommentsStorage.LoadAll(projectId).Where(c =>
                    c.Status == CommentStatus.Open &&
                    !string.Equals(c.Author, me, StringComparison.OrdinalIgnoreCase) &&
                    !_shownIds.Contains(c.Id));

                if (onlyLevelName != null)
                {
                    candidates = candidates.Where(c => string.Equals(c.LevelName, onlyLevelName, StringComparison.OrdinalIgnoreCase));
                    // Only narrow by Scope Box when the comment actually has one
                    // recorded -- older comments saved before this fix won't, and
                    // should still match on level name alone rather than being
                    // silently excluded forever.
                    if (!string.IsNullOrWhiteSpace(onlyScopeBoxName))
                        candidates = candidates.Where(c =>
                            string.IsNullOrWhiteSpace(c.ScopeBoxName) ||
                            string.Equals(c.ScopeBoxName, onlyScopeBoxName, StringComparison.OrdinalIgnoreCase));
                }

                var toShow = candidates.OrderBy(c => c.CreatedUtc).FirstOrDefault();
                if (toShow == null) return;

                _shownIds.Add(toShow.Id);
                ShowPopup(toShow);
            }
            catch { }
        }

        private static void ShowPopup(ProjectComment comment)
        {
            try
            {
                if (CommentsStorage.GetSoundEnabled())
                    try { System.Media.SystemSounds.Asterisk.Play(); } catch { }

                var popup = new CommentPopupWindow(comment);
                popup.Show();
            }
            catch { }
        }
    }
}

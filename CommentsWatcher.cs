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
        // Concurrent, not a plain HashSet: this is now touched from background
        // Task.Run work (see CheckFor), and Idling/ViewActivated can each kick
        // off a check whose background task is still running when the next
        // one starts -- a plain HashSet isn't safe under that kind of
        // concurrent access. TryAdd below also makes "have I shown this?" and
        // "mark it shown" one atomic step, so two overlapping checks can't
        // both decide to pop up the same comment.
        private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, byte> _shownIds
            = new System.Collections.Concurrent.ConcurrentDictionary<string, byte>();

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

                // Everything above needs live Revit API access (doc, uidoc),
                // so it stays on the main thread -- but it's all cheap. The one
                // genuinely slow part is the network read below (LoadAll reads
                // and parses the shared comments file), which doesn't need the
                // API at all, so it moves to a background thread. Captured
                // here, on the main thread, while it's guaranteed to be
                // available -- used to marshal back only for the final step
                // that needs it (showing the popup is a WPF operation and has
                // to happen on this thread).
                var dispatcher = System.Windows.Threading.Dispatcher.CurrentDispatcher;

                System.Threading.Tasks.Task.Run(() =>
                {
                    try
                    {
                        var candidates = CommentsStorage.LoadAll(projectId).Where(c =>
                            c.Status == CommentStatus.Open &&
                            !string.Equals(c.Author, me, StringComparison.OrdinalIgnoreCase) &&
                            !_shownIds.ContainsKey(c.Id));

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

                        // TryAdd is the atomic "claim this one" step -- if another
                        // overlapping check already claimed it first, this returns
                        // false and we skip showing a duplicate popup.
                        if (!_shownIds.TryAdd(toShow.Id, 0)) return;
                        dispatcher.Invoke(() => ShowPopup(toShow));
                    }
                    catch { }
                });
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

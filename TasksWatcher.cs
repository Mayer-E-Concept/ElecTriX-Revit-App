// TasksWatcher.cs -- ME-Tools | Project Tasks background notifier
// Mayer E-Concept SRL
//
// Mirrors CommentsWatcher.cs's structure directly -- same Idling-based
// throttled check, same "shown once per session" tracking via a
// ConcurrentDictionary that resets only on Revit restart, same reasoning
// for doing the network read on a background thread and only marshaling
// back to the UI thread for the popup itself.
//
// Two separate triggers decide when a task gets a popup, both reusing
// the exact same "shown once" set so neither can double-fire the same
// task:
// 1. Brand new -- ReceivedAtUtc is after this watcher's own start time,
//    so a task that already existed when Revit opened doesn't pop up
//    just because this is the first check.
// 2. Stale -- still "unassigned" and at least StaleAfter old. This is a
//    once-per-Revit-session reminder, not a persistent "have I ever
//    nagged about this" tracker: if it's still unassigned next time
//    Revit is opened, it reminds again, the same way a comment would
//    still show up again if it were somehow un-shown. A task that's been
//    claimed or marked done stops matching this check immediately, since
//    both conditions read Status fresh every time.
using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Events;
using METools.Comments;

namespace METools.Tasks
{
    public static class TasksWatcher
    {
        private static DateTime _lastCheck = DateTime.MinValue;
        private static readonly TimeSpan CheckInterval = TimeSpan.FromSeconds(45);

        // Anything received before this watcher started is "already
        // existed" rather than "new" -- without this, every unassigned
        // task in the system would pop up the moment Revit opens.
        private static readonly DateTime _watcherStartedUtc = DateTime.UtcNow;

        private static readonly TimeSpan StaleAfter = TimeSpan.FromDays(2);

        // Same concurrency reasoning as CommentsWatcher: touched from
        // background Task.Run work, and overlapping Idling checks are a
        // real possibility, not a hypothetical.
        private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, byte> _shownIds
            = new System.Collections.Concurrent.ConcurrentDictionary<string, byte>();

        public static void Register(UIControlledApplication app)
        {
            app.Idling += OnIdling;
        }

        private static void OnIdling(object sender, IdlingEventArgs e)
        {
            if (DateTime.UtcNow - _lastCheck < CheckInterval) return;
            _lastCheck = DateTime.UtcNow;
            try { CheckFor((sender as UIApplication)?.ActiveUIDocument); } catch { }
        }

        private static void CheckFor(UIDocument uidoc)
        {
            try
            {
                if (uidoc == null) return;
                var doc = uidoc.Document;
                if (doc == null || doc.IsFamilyDocument) return;
                if (METools.LicenseManager.IsTrialExpired) return; // silent gate, no nag dialog from a background check

                string me = "";
                try { me = uidoc.Application.Application.Username; } catch { }

                // Everything above needs live Revit API access, so it
                // stays on the main thread -- but the actual slow part
                // (reading every project's task file from the shared
                // folder) doesn't, so it moves to a background thread,
                // same split CommentsWatcher already uses.
                var dispatcher = System.Windows.Threading.Dispatcher.CurrentDispatcher;
                System.Threading.Tasks.Task.Run(() =>
                {
                    try
                    {
                        var all = TasksStorage.LoadAllAcrossProjects(out _);
                        var now = DateTime.UtcNow;

                        var toShow = all
                            .Where(t => !_shownIds.ContainsKey(t.Id))
                            .Where(t => t.Status != "done")
                            .Where(t =>
                                t.ReceivedAtUtc > _watcherStartedUtc ||
                                (t.Status == "unassigned" && now - t.ReceivedAtUtc >= StaleAfter))
                            .OrderBy(t => t.ReceivedAtUtc)
                            .FirstOrDefault();

                        if (toShow == null) return;

                        // Same atomic "claim this one" step CommentsWatcher
                        // uses -- if an overlapping check already claimed
                        // it, this returns false and no duplicate shows.
                        if (!_shownIds.TryAdd(toShow.Id, 0)) return;

                        var isStale = toShow.ReceivedAtUtc <= _watcherStartedUtc;
                        dispatcher.Invoke(() => ShowPopup(toShow, isStale, me));
                    }
                    catch { }
                });
            }
            catch { }
        }

        private static void ShowPopup(ProjectTask task, bool isStale, string currentUser)
        {
            try
            {
                if (CommentsStorage.GetSoundEnabled())
                    try { System.Media.SystemSounds.Asterisk.Play(); } catch { }

                var popup = new TaskPopupWindow(task, isStale, currentUser);
                popup.Show();
            }
            catch { }
        }
    }
}

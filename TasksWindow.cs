// TasksWindow.cs -- ME-Tools | Tasks window
// Mayer E-Concept SRL
//
// Built on MeToolsWindowBase like every other tool window in this suite.
// DockPanel ordering matters here: BuildStatusBar() is called BEFORE the
// scrollable body is added, so the status bar (explicit Dock.Bottom)
// claims its edge first and the body -- added last, with no Dock set --
// gets the "fill" treatment from RootDock's LastChildFill. Reversing that
// order would make the 26px status bar stretch to fill the window
// instead (WPF ignores the Dock property on whichever child is added
// last when LastChildFill is true).
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace METools.Tasks
{
    public class TasksWindow : MeToolsWindowBase
    {
        private static TasksWindow _instance;

        private readonly string _projectId;
        private readonly Document _doc;
        private readonly TasksHandler _handler;
        private readonly ExternalEvent _externalEvent;
        private readonly DispatcherTimer _autoRefreshTimer;

        private StackPanel _listPanel;
        private Button _openTabBtn;
        private Button _doneTabBtn;
        private bool _showDone;

        private string CurrentUsername => _doc?.Application?.Username ?? Environment.UserName;

        public static void ShowOrActivate(UIApplication uiApp, string projectId)
        {
            if (_instance != null && _instance.IsLoaded)
            {
                if (_instance._projectId == projectId)
                {
                    _instance.Activate();
                    return;
                }
                _instance.Close();
            }

            var handler = new TasksHandler { ProjectId = projectId };
            var externalEvent = ExternalEvent.Create(handler);
            _instance = new TasksWindow(uiApp.ActiveUIDocument.Document, projectId, handler, externalEvent);
            _instance.Show();
        }

        private TasksWindow(Document doc, string projectId, TasksHandler handler, ExternalEvent externalEvent)
        {
            _doc = doc;
            _projectId = projectId;
            _handler = handler;
            _externalEvent = externalEvent;
            _handler.OnComplete = (list, message) => Dispatcher.Invoke(() => RenderList(list, message));

            InitWindow("Tasks", 520);
            BuildStatusBar("", "Revit 2025");

            var content = new StackPanel { Margin = new Thickness(16, 12, 16, 12) };

            var tabsRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 10) };
            _openTabBtn = ToggleBtn("Open", true, () => SwitchTab(false));
            _doneTabBtn = ToggleBtn("Done", false, () => SwitchTab(true));
            tabsRow.Children.Add(_openTabBtn);
            tabsRow.Children.Add(new Border { Width = 8 });
            tabsRow.Children.Add(_doneTabBtn);
            tabsRow.Children.Add(new Border { Width = 16 });
            tabsRow.Children.Add(ActionBtn("Refresh", true, RequestRefresh));
            content.Children.Add(tabsRow);

            _listPanel = new StackPanel();
            var scroller = new ScrollViewer
            {
                Content = _listPanel,
                MaxHeight = 480,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            };
            content.Children.Add(scroller);

            // Last child added to RootDock -- fills remaining space, see
            // file header.
            RootDock.Children.Add(content);

            _autoRefreshTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
            _autoRefreshTimer.Tick += (s, e) => RequestRefresh();
            _autoRefreshTimer.Start();

            Closed += (s, e) =>
            {
                _autoRefreshTimer.Stop();
                if (_instance == this) _instance = null;
            };

            RequestRefresh();
        }

        private void SendRequest(TasksAction action, string taskId)
        {
            _handler.Request = new TasksRequest
            {
                Action = action,
                TaskId = taskId,
                CurrentUser = CurrentUsername,
            };
            _externalEvent.Raise();
        }

        private void SendGoTo(ProjectTask task)
        {
            _handler.Request = new TasksRequest
            {
                Action = TasksAction.GoToElement,
                TaskId = task.Id,
                ReferencedElementId = task.ReferencedElementId,
            };
            _externalEvent.Raise();
        }

        private void RequestRefresh()
        {
            _handler.Request = new TasksRequest { Action = TasksAction.Refresh };
            _externalEvent.Raise();
        }

        private void SwitchTab(bool showDone)
        {
            _showDone = showDone;
            UpdateToggle(_openTabBtn, !showDone);
            UpdateToggle(_doneTabBtn, showDone);
            RequestRefresh();
        }

        private void RenderList(List<ProjectTask> list, string message)
        {
            _listPanel.Children.Clear();

            if (!string.IsNullOrWhiteSpace(message))
                _listPanel.Children.Add(InfoBox(message));

            var filtered = list
                .Where(t => _showDone ? t.Status == "done" : t.Status != "done")
                .OrderByDescending(t => t.ReceivedAtUtc)
                .ToList();

            if (filtered.Count == 0)
            {
                _listPanel.Children.Add(new TextBlock
                {
                    Text = _showDone ? "No completed tasks yet." : "No open tasks.",
                    FontSize = 12,
                    Foreground = MeToolsTheme.BrMuted,
                    Margin = new Thickness(4, 8, 4, 8),
                });
            }
            else
            {
                foreach (var task in filtered)
                    _listPanel.Children.Add(BuildTaskRow(task));
            }

            var openCount = list.Count(t => t.Status != "done");
            var mineCount = list.Count(t => t.Status != "done" &&
                string.Equals(t.AssignedTo, CurrentUsername, StringComparison.OrdinalIgnoreCase));
            UpdateStatusBar($"{openCount} open \u00b7 {mineCount} assigned to you");

            ResizeToFitContent();
        }

        private Border BuildTaskRow(ProjectTask task)
        {
            var sp = new StackPanel();

            sp.Children.Add(new TextBlock
            {
                Text = string.IsNullOrWhiteSpace(task.TranslatedSubject) ? "(no subject)" : task.TranslatedSubject,
                FontSize = 13,
                FontWeight = FontWeights.SemiBold,
                Foreground = MeToolsTheme.BrText,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 4),
            });

            var received = task.ReceivedAtUtc.ToLocalTime().ToString("g");
            sp.Children.Add(new TextBlock
            {
                Text = $"{task.SenderName} <{task.SenderEmail}> \u00b7 {received} \u00b7 {task.Category} \u00b7 {task.Urgency} urgency",
                FontSize = 10.5,
                Foreground = MeToolsTheme.BrMuted,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 6),
            });

            if (!string.IsNullOrWhiteSpace(task.Summary))
            {
                sp.Children.Add(new TextBlock
                {
                    Text = task.Summary,
                    FontSize = 12,
                    Foreground = MeToolsTheme.BrText,
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(0, 0, 0, 6),
                });
            }

            if (task.RoutingMethod == "unassigned")
            {
                var guessNote = string.IsNullOrWhiteSpace(task.ProjectGuessRaw)
                    ? "Could not match this to a project automatically."
                    : $"Could not match this to a project automatically \u2014 possible match: \"{task.ProjectGuessRaw}\".";
                sp.Children.Add(InfoBox(guessNote));
            }

            if (task.AttachmentPaths.Count > 0 || task.BlockedAttachments.Count > 0)
            {
                var parts = new List<string>();
                if (task.AttachmentPaths.Count > 0) parts.Add($"{task.AttachmentPaths.Count} attachment(s) saved");
                if (task.BlockedAttachments.Count > 0) parts.Add($"{task.BlockedAttachments.Count} attachment(s) blocked");
                sp.Children.Add(new TextBlock
                {
                    Text = string.Join(" \u00b7 ", parts),
                    FontSize = 10.5,
                    Foreground = MeToolsTheme.BrMuted,
                    Margin = new Thickness(0, 0, 0, 6),
                });
            }

            var buttonsRow = new StackPanel { Orientation = Orientation.Horizontal };

            if (string.IsNullOrWhiteSpace(task.AssignedTo))
            {
                buttonsRow.Children.Add(ActionBtn("Assign to me", false, () => SendRequest(TasksAction.Claim, task.Id)));
            }
            else
            {
                var isMine = string.Equals(task.AssignedTo, CurrentUsername, StringComparison.OrdinalIgnoreCase);
                sp.Children.Add(new TextBlock
                {
                    Text = task.Status == "done" ? $"Done ({task.AssignedTo})" : $"Assigned to {task.AssignedTo}",
                    FontSize = 10.5,
                    FontWeight = FontWeights.Medium,
                    Foreground = isMine ? MeToolsTheme.BrActiveFg : MeToolsTheme.BrMuted,
                    Margin = new Thickness(0, 0, 0, 6),
                });

                if (isMine && task.Status != "done")
                    buttonsRow.Children.Add(ActionBtn("Mark done", false, () => SendRequest(TasksAction.MarkDone, task.Id)));
            }

            if (!string.IsNullOrWhiteSpace(task.ReferencedElementId))
            {
                if (buttonsRow.Children.Count > 0) buttonsRow.Children.Add(new Border { Width = 8 });
                buttonsRow.Children.Add(ActionBtn("Go there", true, () => SendGoTo(task)));
            }

            if (buttonsRow.Children.Count > 0)
                sp.Children.Add(buttonsRow);

            return new Border
            {
                Background = MeToolsTheme.BrSurface,
                BorderBrush = MeToolsTheme.BrBorder,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(12),
                Padding = new Thickness(12, 10, 12, 10),
                Margin = new Thickness(0, 0, 0, 10),
                Child = sp,
            };
        }
    }
}

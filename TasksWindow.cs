// TasksWindow.cs -- ME-Tools | Tasks window
// Mayer E-Concept SRL
//
// Built on MeToolsWindowBase like every other tool window in this suite.
// Unlike the first version, this is a cross-project dashboard, not a
// per-project view -- it loads every METools_Tasks_*.json file in the
// shared folder at once (see TasksStorage.LoadAllAcrossProjects), which
// is also why it no longer needs an open Document at all: the shared
// folder path comes from Comments' own machine-level setting, and the
// current username comes straight from the Application object, not from
// any specific project.
//
// DockPanel ordering matters here: BuildStatusBar() is called BEFORE the
// scrollable body is added, so the status bar (explicit Dock.Bottom)
// claims its edge first and the body -- added last, with no Dock set --
// gets the "fill" treatment from RootDock's LastChildFill. Reversing that
// order would make the 26px status bar stretch to fill the window
// instead (WPF ignores the Dock property on whichever child is added
// last when LastChildFill is true).
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Autodesk.Revit.UI;

namespace METools.Tasks
{
    public class TasksWindow : MeToolsWindowBase
    {
        private static TasksWindow _instance;

        private enum TaskTab { Unassigned, InProgress, Mine, Done }

        // Fully-qualified rather than a using directive, on purpose:
        // Autodesk.Revit.ApplicationServices.Application and
        // System.Windows.Application (needed for Thickness/FontWeights
        // elsewhere in this file) share the same short name -- importing
        // both is a straight ambiguous-reference compile error.
        private readonly Autodesk.Revit.ApplicationServices.Application _revitApp;
        private readonly TasksHandler _handler;
        private readonly ExternalEvent _externalEvent;
        private readonly DispatcherTimer _autoRefreshTimer;

        private Dictionary<string, string> _projectNames = new Dictionary<string, string>();
        private List<ProjectRegistryEntry> _registryEntries = new List<ProjectRegistryEntry>();
        private StackPanel _listPanel;
        private TextBlock _statTotal, _statUnassigned, _statInProgress, _statDone;
        private Button _unassignedTabBtn, _inProgressTabBtn, _mineTabBtn, _doneTabBtn;
        private TaskTab _currentTab = TaskTab.Unassigned;

        private string CurrentUsername => _revitApp?.Username ?? Environment.UserName;

        public static void ShowOrActivate(UIApplication uiApp)
        {
            if (_instance != null && _instance.IsLoaded)
            {
                _instance.Activate();
                return;
            }

            var handler = new TasksHandler();
            var externalEvent = ExternalEvent.Create(handler);
            _instance = new TasksWindow(uiApp.Application, handler, externalEvent);
            _instance.Show();
        }

        private TasksWindow(Autodesk.Revit.ApplicationServices.Application revitApp, TasksHandler handler, ExternalEvent externalEvent)
        {
            _revitApp = revitApp;
            _handler = handler;
            _externalEvent = externalEvent;
            _handler.OnComplete = (list, stats, message) => Dispatcher.Invoke(() => RenderList(list, stats, message));

            _registryEntries = TasksStorage.LoadProjectRegistry();
            _projectNames = BuildDisplayNameLookup(_registryEntries);

            InitWindow("Tasks", 600);
            BuildStatusBar("", "Revit 2025");

            var content = new StackPanel { Margin = new Thickness(16, 12, 16, 12) };

            content.Children.Add(BuildStatsStrip());

            var tabsRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 12, 0, 8) };
            _unassignedTabBtn = ToggleBtn("Unassigned", true, () => SwitchTab(TaskTab.Unassigned));
            _inProgressTabBtn = ToggleBtn("In Progress", false, () => SwitchTab(TaskTab.InProgress));
            _mineTabBtn = ToggleBtn("Mine", false, () => SwitchTab(TaskTab.Mine));
            _doneTabBtn = ToggleBtn("Done", false, () => SwitchTab(TaskTab.Done));
            foreach (var btn in new[] { _unassignedTabBtn, _inProgressTabBtn, _mineTabBtn, _doneTabBtn })
            {
                tabsRow.Children.Add(btn);
                tabsRow.Children.Add(new Border { Width = 6 });
            }
            content.Children.Add(tabsRow);

            // Its own row rather than crammed onto the end of the tabs row --
            // that's exactly what was cutting "Register current project" off.
            var actionsRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 10) };
            actionsRow.Children.Add(ActionBtn("Refresh", true, RequestRefresh));
            actionsRow.Children.Add(new Border { Width = 6 });
            actionsRow.Children.Add(ActionBtn("Register current project", true, RegisterCurrentProject));
            content.Children.Add(actionsRow);

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

        private StackPanel BuildStatChip(string label, out TextBlock valueBlock)
        {
            var box = new StackPanel { Margin = new Thickness(0, 0, 20, 0) };
            valueBlock = new TextBlock { Text = "0", FontSize = 20, FontWeight = FontWeights.Bold, Foreground = MeToolsTheme.BrText };
            box.Children.Add(valueBlock);
            box.Children.Add(new TextBlock { Text = label, FontSize = 10.5, Foreground = MeToolsTheme.BrMuted });
            return box;
        }

        private StackPanel BuildStatsStrip()
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            row.Children.Add(BuildStatChip("Total", out _statTotal));
            row.Children.Add(BuildStatChip("Unassigned", out _statUnassigned));
            row.Children.Add(BuildStatChip("In progress", out _statInProgress));
            row.Children.Add(BuildStatChip("Done", out _statDone));
            return row;
        }

        private void SendRequest(TasksAction action, ProjectTask task)
        {
            _handler.Request = new TasksRequest
            {
                Action = action,
                TaskId = task.Id,
                TaskProjectId = task.ProjectId,
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
                TaskProjectId = task.ProjectId,
                ReferencedElementId = task.ReferencedElementId,
            };
            _externalEvent.Raise();
        }

        private void RequestRefresh()
        {
            _handler.Request = new TasksRequest { Action = TasksAction.Refresh };
            _externalEvent.Raise();
        }

        private void RegisterCurrentProject()
        {
            _handler.Request = new TasksRequest { Action = TasksAction.RegisterCurrentProject };
            _externalEvent.Raise();
        }

        private void SwitchTab(TaskTab tab)
        {
            _currentTab = tab;
            UpdateToggle(_unassignedTabBtn, tab == TaskTab.Unassigned);
            UpdateToggle(_inProgressTabBtn, tab == TaskTab.InProgress);
            UpdateToggle(_mineTabBtn, tab == TaskTab.Mine);
            UpdateToggle(_doneTabBtn, tab == TaskTab.Done);
            RequestRefresh();
        }

        private static void OpenFolder(string path)
        {
            try
            {
                if (Directory.Exists(path))
                    Process.Start(new ProcessStartInfo("explorer.exe", $"\"{path}\"") { UseShellExecute = true });
            }
            catch
            {
                // Best-effort -- a missing/unreachable folder just means the
                // button quietly does nothing rather than crashing the window.
            }
        }

        private static Dictionary<string, string> BuildDisplayNameLookup(List<ProjectRegistryEntry> entries)
        {
            var result = new Dictionary<string, string>();
            foreach (var entry in entries)
            {
                if (!string.IsNullOrWhiteSpace(entry.ProjectId) && !string.IsNullOrWhiteSpace(entry.DisplayName))
                    result[entry.ProjectId] = entry.DisplayName;
            }
            return result;
        }

        // Same normalization scheme as MailBridge's ProjectRegistry.Resolve
        // -- case, underscores, hyphens, and extra spaces all collapsed to
        // single spaces -- kept in sync by hand across the two separate
        // .NET projects, same as the model shapes themselves.
        private static string Normalize(string s) =>
            System.Text.RegularExpressions.Regex.Replace((s ?? "").ToLowerInvariant(), @"[_\-\s]+", " ").Trim();

        // Only offered for tasks that landed in "unassigned" -- an
        // already-routed task never gets a suggestion, since it already
        // has a real answer. Matches the AI's ProjectGuessRaw against
        // every registered project's DisplayName and Keywords; either
        // containing the other counts as a match, with a minimum length
        // so short strings like "V2" alone can't match everything. This
        // is still just a suggestion a person clicks to confirm -- never
        // auto-applied -- so a slightly loose match here is fine; it
        // costs one extra glance, not a silent misfile.
        private ProjectRegistryEntry FindSuggestedProject(ProjectTask task)
        {
            if (task.RoutingMethod != "unassigned" || string.IsNullOrWhiteSpace(task.ProjectGuessRaw))
                return null;

            var guess = Normalize(task.ProjectGuessRaw);
            if (guess.Length < 3) return null;

            foreach (var entry in _registryEntries)
            {
                var candidates = new List<string> { entry.DisplayName };
                if (entry.Keywords != null) candidates.AddRange(entry.Keywords);

                foreach (var candidate in candidates)
                {
                    if (string.IsNullOrWhiteSpace(candidate)) continue;
                    var normCandidate = Normalize(candidate);
                    if (normCandidate.Length < 3) continue;
                    if (guess.Contains(normCandidate) || normCandidate.Contains(guess))
                        return entry;
                }
            }

            return null;
        }

        private void SendMoveRequest(ProjectTask task, ProjectRegistryEntry target)
        {
            _handler.Request = new TasksRequest
            {
                Action = TasksAction.MoveToProject,
                TaskId = task.Id,
                TaskProjectId = task.ProjectId,
                TargetProjectId = target.ProjectId,
            };
            _externalEvent.Raise();
        }

        // Deletion is the one action here with no undo -- Release, MarkDone,
        // and MoveToProject all leave the task recoverable in some form,
        // this doesn't. A confirm dialog is the actual safety net, not any
        // visual styling on the button itself.
        private void ConfirmAndDelete(ProjectTask task)
        {
            var subject = string.IsNullOrWhiteSpace(task.TranslatedSubject) ? "(no subject)" : task.TranslatedSubject;
            var result = MessageBox.Show(
                $"Delete this task permanently?\n\n\"{subject}\"\n\nThis can't be undone.",
                "Delete task", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No);

            if (result == MessageBoxResult.Yes)
                SendRequest(TasksAction.Delete, task);
        }

        private string DisplayProjectName(string projectId)
        {
            if (string.IsNullOrWhiteSpace(projectId) || projectId == "unassigned")
                return "No matching project";
            if (_projectNames.TryGetValue(projectId, out var name))
                return name;
            return projectId.Length > 10 ? projectId.Substring(0, 10) + "…" : projectId;
        }

        private void RenderList(List<ProjectTask> allTasks, TaskStats stats, string message)
        {
            _registryEntries = TasksStorage.LoadProjectRegistry();
            _projectNames = BuildDisplayNameLookup(_registryEntries);

            _statTotal.Text = stats.Total.ToString();
            _statUnassigned.Text = stats.Unassigned.ToString();
            _statInProgress.Text = stats.InProgress.ToString();
            _statDone.Text = stats.Done.ToString();

            _listPanel.Children.Clear();

            if (!string.IsNullOrWhiteSpace(message))
                _listPanel.Children.Add(InfoBox(message));

            IEnumerable<ProjectTask> filtered = _currentTab switch
            {
                TaskTab.Unassigned => allTasks.Where(t => string.IsNullOrWhiteSpace(t.AssignedTo)),
                TaskTab.InProgress => allTasks.Where(t => !string.IsNullOrWhiteSpace(t.AssignedTo) && t.Status != "done"),
                TaskTab.Mine => allTasks.Where(t => t.Status != "done" &&
                    string.Equals(t.AssignedTo, CurrentUsername, StringComparison.OrdinalIgnoreCase)),
                TaskTab.Done => allTasks.Where(t => t.Status == "done"),
                _ => allTasks,
            };

            var sorted = filtered.OrderByDescending(t => t.ReceivedAtUtc).ToList();

            if (sorted.Count == 0)
            {
                _listPanel.Children.Add(new TextBlock
                {
                    Text = "Nothing here.",
                    FontSize = 12,
                    Foreground = MeToolsTheme.BrMuted,
                    Margin = new Thickness(4, 8, 4, 8),
                });
            }
            else
            {
                foreach (var task in sorted)
                    _listPanel.Children.Add(BuildTaskRow(task));
            }

            ResizeToFitContent();
        }

        private Border BuildTaskRow(ProjectTask task)
        {
            var sp = new StackPanel();

            sp.Children.Add(new TextBlock
            {
                Text = DisplayProjectName(task.ProjectId),
                FontSize = 10.5,
                FontWeight = FontWeights.Medium,
                Foreground = MeToolsTheme.BrMuted,
                Margin = new Thickness(0, 0, 0, 2),
            });

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
                var suggestion = FindSuggestedProject(task);
                if (suggestion != null)
                {
                    sp.Children.Add(InfoBox($"Possible match: \"{task.ProjectGuessRaw}\" \u2192 {suggestion.DisplayName}"));
                    var moveRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 6) };
                    moveRow.Children.Add(ActionBtn($"Move to {suggestion.DisplayName}", false, () => SendMoveRequest(task, suggestion)));
                    sp.Children.Add(moveRow);
                }
                else
                {
                    var guessNote = string.IsNullOrWhiteSpace(task.ProjectGuessRaw)
                        ? "Could not match this to a project automatically."
                        : $"Could not match this to a project automatically \u2014 possible match: \"{task.ProjectGuessRaw}\".";
                    sp.Children.Add(InfoBox(guessNote));
                }
            }

            // Attachments get a highlighted box of their own, not just a
            // muted line -- easy to miss otherwise, and this was the whole
            // point of asking for it.
            if (task.AttachmentPaths.Count > 0)
            {
                var folder = Path.GetDirectoryName(task.AttachmentPaths[0]);
                var attBox = new Border
                {
                    Background = MeToolsTheme.BrSoftFill,
                    BorderBrush = MeToolsTheme.BrBorder,
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(8),
                    Padding = new Thickness(10, 8, 10, 8),
                    Margin = new Thickness(0, 0, 0, 6),
                };
                var attSp = new StackPanel();
                attSp.Children.Add(new TextBlock
                {
                    Text = $"\U0001F4CE {task.AttachmentPaths.Count} file(s) attached",
                    FontSize = 11.5,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = MeToolsTheme.BrText,
                });
                attSp.Children.Add(new TextBlock
                {
                    Text = folder,
                    FontSize = 10,
                    Foreground = MeToolsTheme.BrMuted,
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(0, 2, 0, 6),
                });
                attSp.Children.Add(ActionBtn("Open folder", true, () => OpenFolder(folder)));
                attBox.Child = attSp;
                sp.Children.Add(attBox);
            }

            if (task.BlockedAttachments.Count > 0)
            {
                sp.Children.Add(new TextBlock
                {
                    Text = $"{task.BlockedAttachments.Count} attachment(s) blocked (type/size)",
                    FontSize = 10.5,
                    Foreground = MeToolsTheme.BrMuted,
                    Margin = new Thickness(0, 0, 0, 6),
                });
            }

            var buttonsRow = new StackPanel { Orientation = Orientation.Horizontal };

            if (string.IsNullOrWhiteSpace(task.AssignedTo))
            {
                buttonsRow.Children.Add(ActionBtn("Assign to me", false, () => SendRequest(TasksAction.Claim, task)));
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
                {
                    buttonsRow.Children.Add(ActionBtn("Mark done", false, () => SendRequest(TasksAction.MarkDone, task)));
                    buttonsRow.Children.Add(new Border { Width = 8 });
                    buttonsRow.Children.Add(ActionBtn("Release", true, () => SendRequest(TasksAction.Release, task)));
                }
            }

            if (!string.IsNullOrWhiteSpace(task.ReferencedElementId))
            {
                if (buttonsRow.Children.Count > 0) buttonsRow.Children.Add(new Border { Width = 8 });
                buttonsRow.Children.Add(ActionBtn("Go there", true, () => SendGoTo(task)));
            }

            if (buttonsRow.Children.Count > 0) buttonsRow.Children.Add(new Border { Width = 8 });
            buttonsRow.Children.Add(ActionBtn("Delete", true, () => ConfirmAndDelete(task)));

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

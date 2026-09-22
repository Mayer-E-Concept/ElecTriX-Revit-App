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
using TextBox = System.Windows.Controls.TextBox;

namespace METools.Tasks
{
    public class TasksWindow : MeToolsWindowBase
    {
        private static TasksWindow _instance;

        private enum TaskTab { Unassigned, InProgress, Mine, Done }
        private enum OuterTab { Tasks, Comments, Projects }

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

        // Cached from the last RenderList call, purely so OnThemeChanged
        // can rebuild the visuals with the SAME data on a theme flip,
        // without needing a full reload through the ExternalEvent/Revit
        // round-trip just to recolor what's already on screen.
        private List<ProjectTask> _lastTasks = new List<ProjectTask>();
        private TaskStats _lastStats = new TaskStats();
        private string _lastMessage;

        // Outer Tasks/Comments split -- Comments used to be its own
        // separate window; it's a tab here now since both are really the
        // same thing ("per-project team coordination"), just with
        // different data shapes. Comments stays scoped to whichever
        // project is currently open (unlike the Tasks tab, which is
        // deliberately cross-project), since that's its actual nature.
        private Button _outerTabTasks, _outerTabComments, _outerTabProjects;
        private FrameworkElement _tasksTabContent, _commentsTabContent, _projectsTabContent;
        private StackPanel _projectsListPanel;
        private TextBlock _commentsProjectLabel;
        private TextBlock _commentsLevelLabel;
        private StackPanel _commentsListPanel;
        private TextBox _commentsInput;
        private TextBox _commentsAssignInput;
        private Button _commentsReferenceToggle;
        private bool _pendingIncludeReference;
        private CommentsTabResult _lastCommentsResult;
        private TextBlock _statTotal, _statUnassigned, _statInProgress, _statDone;
        private Button _unassignedTabBtn, _inProgressTabBtn, _mineTabBtn, _doneTabBtn;
        private TaskTab _currentTab = TaskTab.Unassigned;
        private Border _registrationBanner;

        // Tasks the user has clicked to expand in the Projects tab --
        // RenderProjectsTab rebuilds every row from scratch on every
        // refresh, so this is what keeps something the user just opened
        // from silently re-collapsing on the next auto-refresh tick.
        // Only meaningful for rows rendered with collapsible: true; the
        // Requests tab's own flat list never consults this at all.
        private readonly HashSet<string> _expandedTaskIds = new HashSet<string>();

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
            _handler.OnCommentsComplete = result => Dispatcher.Invoke(() => RenderComments(result));

            _registryEntries = TasksStorage.LoadProjectRegistry();
            _projectNames = BuildDisplayNameLookup(_registryEntries);

            InitWindow("Workboard", 600);
            BuildStatusBar("", "Revit 2025");

            // Outer tab row -- added first, so it's not the last child
            // RootDock sees (see file header on DockPanel ordering).
            var outerTabsRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(16, 10, 16, 0) };
            // Labels only, not the internal OuterTab.Tasks/Comments enum
            // values below -- the email-derived tab now reads "Requests"
            // and the in-model note tab (which already had a full
            // assign/resolve workflow) now reads "Tasks", since that's
            // the more useful, general-purpose name for it. Internal
            // identifiers deliberately kept as-is throughout this file
            // and TasksHandler/TasksStorage -- renaming those too would
            // touch the on-disk JSON schema and file names that
            // MailBridge is already writing into in production, which is
            // a much bigger, riskier change than what was actually asked
            // for here.
            _outerTabTasks = ToggleBtn("Requests", true, () => SwitchOuterTab(OuterTab.Tasks));
            _outerTabComments = ToggleBtn("Tasks", false, () => SwitchOuterTab(OuterTab.Comments));
            _outerTabProjects = ToggleBtn("Projects", false, () => SwitchOuterTab(OuterTab.Projects));
            outerTabsRow.Children.Add(_outerTabTasks);
            outerTabsRow.Children.Add(new Border { Width = 6 });
            outerTabsRow.Children.Add(_outerTabComments);
            outerTabsRow.Children.Add(new Border { Width = 6 });
            outerTabsRow.Children.Add(_outerTabProjects);
            DockPanel.SetDock(outerTabsRow, Dock.Top);
            RootDock.Children.Add(outerTabsRow);

            _tasksTabContent = BuildTasksTabContent();
            _commentsTabContent = BuildCommentsTabPanel();
            _commentsTabContent.Visibility = Visibility.Collapsed;
            _projectsTabContent = BuildProjectsTabContent();
            _projectsTabContent.Visibility = Visibility.Collapsed;

            var outerContainer = new Grid();
            outerContainer.Children.Add(_tasksTabContent);
            outerContainer.Children.Add(_commentsTabContent);
            outerContainer.Children.Add(_projectsTabContent);

            // Last child added to RootDock -- fills remaining space, see
            // file header.
            RootDock.Children.Add(outerContainer);

            _autoRefreshTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
            _autoRefreshTimer.Tick += (s, e) =>
            {
                if (_commentsTabContent.Visibility == Visibility.Visible) RequestLoadComments();
                else RequestRefresh();
            };
            _autoRefreshTimer.Start();

            Closed += (s, e) =>
            {
                _autoRefreshTimer.Stop();
                if (_instance == this) _instance = null;
            };

            RequestRefresh();
        }

        // Everything that used to be built directly in the constructor --
        // stats strip, inner Unassigned/In Progress/Mine/Done tabs, the
        // task list -- unchanged, just now returned as one element so it
        // can be a sibling of the Comments panel instead of the window's
        // only content.
        private FrameworkElement BuildTasksTabContent()
        {
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

            // Hidden by default -- RenderList shows this specifically
            // when a project is open and confirmed unregistered, so an
            // email mentioning it has nowhere deterministic to route to.
            // A missing registration is easy to forget (it's a one-time,
            // manual step with no other reminder), and silently routing
            // to Unassigned forever is a worse failure mode than a
            // visible nudge.
            _registrationBanner = InfoBox("");
            _registrationBanner.Visibility = Visibility.Collapsed;
            content.Children.Add(_registrationBanner);

            _listPanel = new StackPanel();
            var scroller = new ScrollViewer
            {
                Content = _listPanel,
                MaxHeight = 480,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            };
            content.Children.Add(scroller);

            return content;
        }

        // Grouped by registered project (Unassigned first, since those are
        // the ones actually needing a human decision), reusing
        // BuildTaskRow for each task -- same visual language as the
        // Tasks tab, just organized differently. Populated fresh every
        // time RenderList runs, from the same already-loaded task list,
        // rather than a separate ExternalEvent round-trip -- there's
        // nothing here that isn't already sitting in memory.
        private FrameworkElement BuildProjectsTabContent()
        {
            var content = new StackPanel { Margin = new Thickness(16, 12, 16, 12) };
            content.Children.Add(ActionBtn("Refresh", true, RequestRefresh));

            _projectsListPanel = new StackPanel { Margin = new Thickness(0, 10, 0, 0) };
            var scroller = new ScrollViewer
            {
                Content = _projectsListPanel,
                MaxHeight = 480,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            };
            content.Children.Add(scroller);

            return content;
        }

        private void RenderProjectsTab(List<ProjectTask> allTasks)
        {
            _projectsListPanel.Children.Clear();

            var unassigned = allTasks.Where(t => t.ProjectId == "unassigned").OrderByDescending(t => t.ReceivedAtUtc).ToList();
            _projectsListPanel.Children.Add(Sec($"UNASSIGNED ({unassigned.Count})"));
            if (unassigned.Count == 0)
            {
                _projectsListPanel.Children.Add(new TextBlock
                {
                    Text = "Nothing unassigned.", FontSize = 12, Foreground = MeToolsTheme.BrMuted,
                    Margin = new Thickness(4, 0, 4, 14),
                });
            }
            else
            {
                foreach (var task in unassigned)
                    _projectsListPanel.Children.Add(BuildTaskRow(task));
                _projectsListPanel.Children.Add(new Border { Height = 8 });
            }

            foreach (var entry in _registryEntries.OrderBy(r => r.DisplayName, StringComparer.OrdinalIgnoreCase))
            {
                var projectTasks = allTasks.Where(t => t.ProjectId == entry.ProjectId)
                    .OrderByDescending(t => t.ReceivedAtUtc).ToList();
                _projectsListPanel.Children.Add(Sec($"{entry.DisplayName} ({projectTasks.Count})"));
                if (projectTasks.Count == 0)
                {
                    _projectsListPanel.Children.Add(new TextBlock
                    {
                        Text = "No requests yet.", FontSize = 12, Foreground = MeToolsTheme.BrMuted,
                        Margin = new Thickness(4, 0, 4, 14),
                    });
                }
                else
                {
                    foreach (var task in projectTasks)
                        _projectsListPanel.Children.Add(BuildTaskRow(task, collapsible: true));
                    _projectsListPanel.Children.Add(new Border { Height = 8 });
                }
            }

            if (_registryEntries.Count == 0)
            {
                _projectsListPanel.Children.Add(new TextBlock
                {
                    Text = "No projects registered yet -- open one in Revit and click \"Register current project\" on the Requests tab.",
                    FontSize = 12, Foreground = MeToolsTheme.BrMuted, TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(4, 0, 4, 8),
                });
            }
        }

        private FrameworkElement BuildCommentsTabPanel()
        {
            var panel = new StackPanel { Margin = new Thickness(16, 12, 16, 12) };

            _commentsProjectLabel = new TextBlock
            {
                Text = "", FontSize = 12, FontWeight = FontWeights.SemiBold,
                Foreground = MeToolsTheme.BrText, Margin = new Thickness(0, 0, 0, 4),
            };
            panel.Children.Add(_commentsProjectLabel);

            _commentsLevelLabel = new TextBlock
            {
                Text = "", FontSize = 10.5, Foreground = MeToolsTheme.BrMuted, Margin = new Thickness(0, 0, 0, 8),
            };
            panel.Children.Add(_commentsLevelLabel);

            _commentsInput = new TextBox
            {
                FontSize = 12, Padding = new Thickness(6), TextWrapping = TextWrapping.Wrap,
                AcceptsReturn = true, Height = 50,
                Background = MeToolsTheme.BrInput, Foreground = MeToolsTheme.BrText,
                BorderBrush = MeToolsTheme.BrBorder,
            };
            panel.Children.Add(_commentsInput);

            var optionsRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 6) };
            _commentsReferenceToggle = ToggleBtn("Reference selected element", false, ToggleIncludeReference);
            optionsRow.Children.Add(_commentsReferenceToggle);
            optionsRow.Children.Add(new Border { Width = 12 });
            optionsRow.Children.Add(new TextBlock
            {
                Text = "Assign to:", FontSize = 11, Foreground = MeToolsTheme.BrMuted,
                VerticalAlignment = VerticalAlignment.Center,
            });
            optionsRow.Children.Add(new Border { Width = 6 });
            _commentsAssignInput = new TextBox
            {
                Width = 140, FontSize = 11, Padding = new Thickness(4),
                Background = MeToolsTheme.BrInput, Foreground = MeToolsTheme.BrText,
                BorderBrush = MeToolsTheme.BrBorder, VerticalContentAlignment = VerticalAlignment.Center,
            };
            optionsRow.Children.Add(_commentsAssignInput);
            panel.Children.Add(optionsRow);

            var addRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 12) };
            addRow.Children.Add(ActionBtn("Add comment", false, SendAddComment));
            panel.Children.Add(addRow);

            _commentsListPanel = new StackPanel();
            var scroller = new ScrollViewer
            {
                Content = _commentsListPanel, MaxHeight = 380,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            };
            panel.Children.Add(scroller);

            return panel;
        }

        private void ToggleIncludeReference()
        {
            _pendingIncludeReference = !_pendingIncludeReference;
            UpdateToggle(_commentsReferenceToggle, _pendingIncludeReference);
        }

        private void SwitchOuterTab(OuterTab tab)
        {
            UpdateToggle(_outerTabTasks, tab == OuterTab.Tasks);
            UpdateToggle(_outerTabComments, tab == OuterTab.Comments);
            UpdateToggle(_outerTabProjects, tab == OuterTab.Projects);
            _tasksTabContent.Visibility = tab == OuterTab.Tasks ? Visibility.Visible : Visibility.Collapsed;
            _commentsTabContent.Visibility = tab == OuterTab.Comments ? Visibility.Visible : Visibility.Collapsed;
            _projectsTabContent.Visibility = tab == OuterTab.Projects ? Visibility.Visible : Visibility.Collapsed;
            if (tab == OuterTab.Comments) RequestLoadComments();
        }

        private void RequestLoadComments()
        {
            _handler.Request = new TasksRequest { Action = TasksAction.LoadComments };
            _externalEvent.Raise();
        }

        private void SendAddComment()
        {
            var text = _commentsInput.Text;
            if (string.IsNullOrWhiteSpace(text)) return;

            _handler.Request = new TasksRequest
            {
                Action = TasksAction.AddComment,
                CommentText = text,
                CommentAssignedTo = _commentsAssignInput.Text ?? "",
                IncludeSelectedElementAsReference = _pendingIncludeReference,
                CurrentUser = CurrentUsername,
            };
            _externalEvent.Raise();

            _commentsInput.Text = "";
            _commentsAssignInput.Text = "";
            _pendingIncludeReference = false;
            UpdateToggle(_commentsReferenceToggle, false);
        }

        private void SendSetCommentStatus(string commentId, METools.Comments.CommentStatus status)
        {
            _handler.Request = new TasksRequest
            {
                Action = TasksAction.SetCommentStatus, CommentId = commentId,
                NewCommentStatus = status, CurrentUser = CurrentUsername,
            };
            _externalEvent.Raise();
        }

        private void SendSetCommentAssignedTo(string commentId, string assignedTo)
        {
            _handler.Request = new TasksRequest { Action = TasksAction.SetCommentAssignedTo, CommentId = commentId, CommentAssignedTo = assignedTo ?? "" };
            _externalEvent.Raise();
        }

        private void SendGoToCommentElement(string elementId)
        {
            _handler.Request = new TasksRequest { Action = TasksAction.GoToCommentElement, ReferencedElementId = elementId };
            _externalEvent.Raise();
        }

        private void SendJumpToCommentLevel(string levelName, string scopeBoxName)
        {
            _handler.Request = new TasksRequest { Action = TasksAction.JumpToCommentLevel, CommentLevelName = levelName, CommentScopeBoxName = scopeBoxName };
            _externalEvent.Raise();
        }

        private void ConfirmAndDeleteComment(METools.Comments.ProjectComment c)
        {
            var subject = string.IsNullOrWhiteSpace(c.Text) ? "(empty comment)" : c.Text;
            var result = MessageBox.Show(
                $"Delete this comment permanently?\n\n\"{subject}\"\n\nThis can't be undone.",
                "Delete comment", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No);
            if (result != MessageBoxResult.Yes) return;

            _handler.Request = new TasksRequest { Action = TasksAction.DeleteComment, CommentId = c.Id };
            _externalEvent.Raise();
        }

        private void RenderComments(CommentsTabResult result)
        {
            _lastCommentsResult = result;
            _commentsListPanel.Children.Clear();

            if (!result.HasOpenProject)
            {
                _commentsProjectLabel.Text = "";
                _commentsLevelLabel.Text = "";
                _commentsListPanel.Children.Add(InfoBox("Open a project in Revit to see and add its comments."));
                return;
            }

            _commentsProjectLabel.Text = $"Comments for: {result.ProjectDisplayName}";
            _commentsLevelLabel.Text = string.IsNullOrWhiteSpace(result.CurrentLevelName)
                ? "New comments will be tagged with no level (open a floor plan view to tag one)."
                : $"New comments will be tagged: {result.CurrentLevelName}" +
                  (string.IsNullOrWhiteSpace(result.CurrentScopeBoxName) ? "" : $" ({result.CurrentScopeBoxName})");

            if (!string.IsNullOrWhiteSpace(result.Message))
                _commentsListPanel.Children.Add(InfoBox(result.Message));

            if (result.Comments.Count == 0)
            {
                _commentsListPanel.Children.Add(new TextBlock
                {
                    Text = "No comments yet.", FontSize = 12, Foreground = MeToolsTheme.BrMuted,
                    Margin = new Thickness(4, 8, 4, 8),
                });
                return;
            }

            foreach (var c in result.Comments.OrderByDescending(x => x.CreatedUtc))
                _commentsListPanel.Children.Add(BuildCommentRow(c));
        }

        private Border BuildCommentRow(METools.Comments.ProjectComment c)
        {
            var sp = new StackPanel();

            sp.Children.Add(new TextBlock
            {
                Text = c.Text, FontSize = 12.5, Foreground = MeToolsTheme.BrText,
                TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 4),
            });

            var statusWord = c.Status switch
            {
                METools.Comments.CommentStatus.Done => $"done ({c.ResolvedBy})",
                METools.Comments.CommentStatus.Ignored => $"ignored ({c.ResolvedBy})",
                _ => "open",
            };
            var levelPart = string.IsNullOrWhiteSpace(c.LevelName) ? "" : $" \u00b7 {c.LevelName}" +
                (string.IsNullOrWhiteSpace(c.ScopeBoxName) ? "" : $" ({c.ScopeBoxName})");
            var meta = $"{c.Author} \u00b7 {c.CreatedUtc.ToLocalTime():g}{levelPart} \u00b7 {statusWord}";
            sp.Children.Add(new TextBlock
            {
                Text = meta, FontSize = 10.5, Foreground = MeToolsTheme.BrMuted,
                Margin = new Thickness(0, 0, 0, 4),
            });

            if (!string.IsNullOrWhiteSpace(c.ReferencedSummary))
            {
                sp.Children.Add(new TextBlock
                {
                    Text = $"\U0001F4CC {c.ReferencedSummary}", FontSize = 10.5, Foreground = MeToolsTheme.BrMuted,
                    Margin = new Thickness(0, 0, 0, 6), TextWrapping = TextWrapping.Wrap,
                });
            }

            // Assign To -- pre-filled with the current value, its own
            // "Set" button rather than saving on every keystroke.
            var assignRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 6) };
            assignRow.Children.Add(new TextBlock
            {
                Text = "Assigned to:", FontSize = 10.5, Foreground = MeToolsTheme.BrMuted,
                VerticalAlignment = VerticalAlignment.Center,
            });
            assignRow.Children.Add(new Border { Width = 6 });
            var assignBox = new TextBox
            {
                Width = 120, FontSize = 10.5, Padding = new Thickness(3),
                Text = c.AssignedTo ?? "", Background = MeToolsTheme.BrInput,
                Foreground = MeToolsTheme.BrText, BorderBrush = MeToolsTheme.BrBorder,
            };
            assignRow.Children.Add(assignBox);
            assignRow.Children.Add(new Border { Width = 6 });
            assignRow.Children.Add(ActionBtn("Set", true, () => SendSetCommentAssignedTo(c.Id, assignBox.Text)));
            sp.Children.Add(assignRow);

            var btnRow = new StackPanel { Orientation = Orientation.Horizontal };
            void AddBtn(string label, Action onClick)
            {
                if (btnRow.Children.Count > 0) btnRow.Children.Add(new Border { Width = 6 });
                btnRow.Children.Add(ActionBtn(label, true, onClick));
            }

            if (c.Status != METools.Comments.CommentStatus.Done)
                AddBtn("Mark done", () => SendSetCommentStatus(c.Id, METools.Comments.CommentStatus.Done));
            if (c.Status != METools.Comments.CommentStatus.Ignored)
                AddBtn("Ignore", () => SendSetCommentStatus(c.Id, METools.Comments.CommentStatus.Ignored));
            if (c.Status != METools.Comments.CommentStatus.Open)
                AddBtn("Reopen", () => SendSetCommentStatus(c.Id, METools.Comments.CommentStatus.Open));
            if (!string.IsNullOrWhiteSpace(c.ReferencedElementId))
                AddBtn("Go to", () => SendGoToCommentElement(c.ReferencedElementId));
            if (!string.IsNullOrWhiteSpace(c.LevelName))
                AddBtn("Jump to level", () => SendJumpToCommentLevel(c.LevelName, c.ScopeBoxName));
            AddBtn("Delete", () => ConfirmAndDeleteComment(c));

            sp.Children.Add(btnRow);

            return new Border
            {
                Background = MeToolsTheme.BrSurface, BorderBrush = MeToolsTheme.BrBorder,
                BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(10),
                Padding = new Thickness(10, 8, 10, 8), Margin = new Thickness(0, 0, 0, 8),
                Child = sp,
            };
        }

        // MeToolsWindowBase calls this whenever the light/dark theme
        // flips -- rebuilding with the same cached data (rather than
        // reloading through the ExternalEvent) is enough, since the
        // colors are recomputed fresh on every RenderList call regardless.
        // Without this override, the cards would just sit with whatever
        // color was baked in until the next natural refresh happened to
        // rebuild them -- not permanently wrong, just slow to catch up.
        protected override void OnThemeChanged()
        {
            RenderList(_lastTasks, _lastStats, _lastMessage);
            if (_lastCommentsResult != null) RenderComments(_lastCommentsResult);
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

        // Only offered for tasks that landed in "unassigned" -- an
        // already-routed task never gets a suggestion, since it already
        // has a real answer. Matches the AI's ProjectGuessRaw against
        // every registered project's DisplayName and Keywords; either
        // containing the other counts as a match, with a minimum length
        // so short strings like "V2" alone can't match everything.
        //
        // Returns every match, not just one -- two projects sharing a
        // code (e.g. "P238 Schwanstrasse" and "P238 BOS Haus 2" both
        // registered under "P238") is a completely normal way project
        // numbering works, not a rare edge case. Silently picking
        // whichever one happened to be registered first would be exactly
        // the kind of wrong-project misfile this whole feature exists to
        // avoid -- so when there's more than one candidate, the window
        // shows all of them and a person picks, rather than guessing on
        // their behalf.
        private List<ProjectRegistryEntry> FindSuggestedProjects(ProjectTask task)
        {
            var result = new List<ProjectRegistryEntry>();
            if (task.ProjectId != "unassigned" || string.IsNullOrWhiteSpace(task.ProjectGuessRaw))
                return result;

            var guess = TasksStorage.Normalize(task.ProjectGuessRaw);
            if (guess.Length < 3) return result;

            foreach (var entry in _registryEntries)
            {
                var candidates = new List<string> { entry.DisplayName };
                if (entry.Keywords != null) candidates.AddRange(entry.Keywords);

                foreach (var candidate in candidates)
                {
                    if (string.IsNullOrWhiteSpace(candidate)) continue;
                    var normCandidate = TasksStorage.Normalize(candidate);
                    if (normCandidate.Length < 3) continue;
                    if (guess.Contains(normCandidate) || normCandidate.Contains(guess))
                    {
                        result.Add(entry);
                        break; // one matching keyword is enough to include this project once
                    }
                }
            }

            return result;
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
                $"Delete this request permanently?\n\n\"{subject}\"\n\nThis can't be undone.",
                "Delete request", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No);

            if (result == MessageBoxResult.Yes)
                SendRequest(TasksAction.Delete, task);
        }

        // A plain hex value defined here rather than relying on
        // MeToolsTheme.CRed -- that member's exact type (Color vs Brush)
        // isn't something this file can verify without compiling, and a
        // wrong guess there is a build break. This is a standalone,
        // reasonably universal warning red.
        private static readonly System.Windows.Media.Brush StaleWarningBrush =
            new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xE5, 0x48, 0x4D));

        private const double StaleUnassignedHours = 48;

        private static string FormatAge(DateTime receivedUtc)
        {
            var span = DateTime.UtcNow - receivedUtc;
            if (span.TotalMinutes < 1) return "just now";
            if (span.TotalMinutes < 60) return $"{(int)span.TotalMinutes}m ago";
            if (span.TotalHours < 24) return $"{(int)span.TotalHours}h ago";
            return $"{(int)span.TotalDays}d ago";
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
            _lastTasks = allTasks;
            _lastStats = stats;
            _lastMessage = message;

            _registryEntries = TasksStorage.LoadProjectRegistry();
            _projectNames = BuildDisplayNameLookup(_registryEntries);

            // Only shown when a project is genuinely open and confirmed
            // NOT in the registry -- CurrentProjectFileTitle is null
            // whenever nothing's open at all, which isn't the same thing
            // and shouldn't nag.
            if (_handler.CurrentProjectFileTitle != null && !_handler.CurrentProjectRegistered)
            {
                ((TextBlock)_registrationBanner.Child).Text =
                    $"'{_handler.CurrentProjectFileTitle}' isn't registered yet -- emails mentioning it will land in " +
                    "Unassigned instead of routing here automatically. Click \"Register current project\" above to fix that.";
                _registrationBanner.Visibility = Visibility.Visible;
            }
            else
            {
                _registrationBanner.Visibility = Visibility.Collapsed;
            }

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

            RenderProjectsTab(allTasks);

            ResizeToFitContent();
        }

        // collapsible=true is used specifically for rows shown under a
        // real project in the Projects tab -- once a request has a home,
        // it doesn't need to shout its full translated content on every
        // glance the way something still needing a decision does. The
        // Requests tab's own flat list, and the Unassigned group at the
        // top of the Projects tab, both call this with the default
        // (false) and always render fully expanded, exactly as before.
        private Border BuildTaskRow(ProjectTask task, bool collapsible = false)
        {
            var sp = new StackPanel();
            var isExpanded = !collapsible || _expandedTaskIds.Contains(task.Id);

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
            var age = FormatAge(task.ReceivedAtUtc);
            sp.Children.Add(new TextBlock
            {
                Text = $"{task.SenderName} <{task.SenderEmail}> \u00b7 {received} ({age}) \u00b7 {task.Category} \u00b7 {task.Urgency} urgency",
                FontSize = 10.5,
                Foreground = MeToolsTheme.BrMuted,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 6),
            });

            // A quiet nudge, not a loud alarm: only for tasks nobody has
            // claimed yet, past a threshold long enough that it's genuinely
            // been sitting rather than just "arrived this morning." Shown
            // even collapsed -- "nobody's on this yet" is exactly the kind
            // of status this feature is meant to keep visible, not hide.
            if (string.IsNullOrWhiteSpace(task.AssignedTo) && task.Status != "done" &&
                (DateTime.UtcNow - task.ReceivedAtUtc).TotalHours >= StaleUnassignedHours)
            {
                sp.Children.Add(new TextBlock
                {
                    Text = $"Waiting {age} with nobody assigned",
                    FontSize = 10.5,
                    FontWeight = FontWeights.Medium,
                    Foreground = StaleWarningBrush,
                    Margin = new Thickness(0, 0, 0, 6),
                });
            }

            // Assignment status -- also shown even collapsed (this is the
            // "who's on it" info worth seeing at a glance), but the actual
            // Mark done/Release/Assign-to-me buttons are actions, so those
            // wait for the expanded view along with everything else below.
            var isMine = string.Equals(task.AssignedTo, CurrentUsername, StringComparison.OrdinalIgnoreCase);
            if (!string.IsNullOrWhiteSpace(task.AssignedTo))
            {
                sp.Children.Add(new TextBlock
                {
                    Text = task.Status == "done" ? $"Done ({task.AssignedTo})" : $"Assigned to {task.AssignedTo}",
                    FontSize = 10.5,
                    FontWeight = FontWeights.Medium,
                    Foreground = isMine ? MeToolsTheme.BrActiveFg : MeToolsTheme.BrMuted,
                    Margin = new Thickness(0, 0, 0, 6),
                });
            }

            if (collapsible && !isExpanded)
            {
                sp.Children.Add(ActionBtn("Show details", true, () =>
                {
                    _expandedTaskIds.Add(task.Id);
                    RenderProjectsTab(_lastTasks);
                }));

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

            if (task.ProjectId == "unassigned")
            {
                if (task.RoutingMethod == "ambiguous-domain" || task.RoutingMethod == "ambiguous-keyword")
                {
                    sp.Children.Add(InfoBox(
                        "The sender's domain or a registered keyword matches more than one project -- " +
                        "routing was skipped rather than guess which one. Fix the overlap in the project registry, or pick one below if it's already narrowed down."));
                }

                var suggestions = FindSuggestedProjects(task);
                if (suggestions.Count == 1)
                {
                    sp.Children.Add(InfoBox($"Possible match: \"{task.ProjectGuessRaw}\" \u2192 {suggestions[0].DisplayName}"));
                    var moveRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 6) };
                    moveRow.Children.Add(ActionBtn($"Move to {suggestions[0].DisplayName}", false, () => SendMoveRequest(task, suggestions[0])));
                    sp.Children.Add(moveRow);
                }
                else if (suggestions.Count > 1)
                {
                    var names = string.Join(", ", suggestions.Select(s => s.DisplayName));
                    sp.Children.Add(InfoBox(
                        $"\"{task.ProjectGuessRaw}\" matches more than one registered project ({names}) -- " +
                        "pick the right one rather than risk the wrong one:"));
                    var moveRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 6) };
                    foreach (var candidate in suggestions)
                    {
                        if (moveRow.Children.Count > 0) moveRow.Children.Add(new Border { Width = 6 });
                        moveRow.Children.Add(ActionBtn(candidate.DisplayName, true, () => SendMoveRequest(task, candidate)));
                    }
                    sp.Children.Add(moveRow);
                }
                else
                {
                    var guessNote = string.IsNullOrWhiteSpace(task.ProjectGuessRaw)
                        ? "Could not match this to a project automatically."
                        : $"Could not match this to a project automatically \u2014 possible match: \"{task.ProjectGuessRaw}\".";
                    sp.Children.Add(InfoBox(guessNote));
                }

                // Always available, regardless of whether a suggestion
                // above found anything -- a real project's own internal
                // codename (e.g. "MRQS-PG24") can differ completely from
                // what an email informally calls it, in which case even
                // fuzzy matching against ProjectGuessRaw finds nothing to
                // suggest. This is the actual fallback for that case: a
                // registered project is a registered project, whether or
                // not this specific task's own text happens to resemble
                // it.
                if (_registryEntries.Count > 0)
                {
                    var manualRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 6) };
                    var picker = StyledCombo();
                    picker.Width = 180;
                    picker.DisplayMemberPath = "DisplayName";
                    picker.ItemsSource = _registryEntries.OrderBy(r => r.DisplayName, StringComparer.OrdinalIgnoreCase).ToList();
                    manualRow.Children.Add(picker);
                    manualRow.Children.Add(new Border { Width = 6 });
                    manualRow.Children.Add(ActionBtn("Move to project", true, () =>
                    {
                        if (picker.SelectedItem is ProjectRegistryEntry chosen)
                            SendMoveRequest(task, chosen);
                    }));
                    sp.Children.Add(manualRow);
                }
            }

            // Reassign -- distinct from the first-time-assignment picker
            // above (which only ever shows for Unassigned): this is
            // specifically for undoing a wrong guess once a request
            // already has a project, whether that project was picked
            // automatically or by hand and turned out to be the wrong one.
            // Only offered on rows that came in collapsible, i.e. only
            // from the Projects tab -- the flat Requests tab has no
            // per-project grouping for this to make sense against.
            if (collapsible && task.ProjectId != "unassigned" && _registryEntries.Count > 0)
            {
                var reassignRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 6) };
                var reassignPicker = StyledCombo();
                reassignPicker.Width = 180;
                reassignPicker.DisplayMemberPath = "DisplayName";
                reassignPicker.ItemsSource = _registryEntries
                    .Where(r => r.ProjectId != task.ProjectId)
                    .OrderBy(r => r.DisplayName, StringComparer.OrdinalIgnoreCase).ToList();
                reassignRow.Children.Add(reassignPicker);
                reassignRow.Children.Add(new Border { Width = 6 });
                reassignRow.Children.Add(ActionBtn("Reassign to project", true, () =>
                {
                    if (reassignPicker.SelectedItem is ProjectRegistryEntry chosen)
                        SendMoveRequest(task, chosen);
                }));
                sp.Children.Add(reassignRow);
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
            else if (isMine && task.Status != "done")
            {
                buttonsRow.Children.Add(ActionBtn("Mark done", false, () => SendRequest(TasksAction.MarkDone, task)));
                buttonsRow.Children.Add(new Border { Width = 8 });
                buttonsRow.Children.Add(ActionBtn("Release", true, () => SendRequest(TasksAction.Release, task)));
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

            // Collapse it back -- only for rows that came in collapsible
            // and got expanded; lets the user put one away again without
            // waiting for the next refresh to do it for them.
            if (collapsible)
            {
                sp.Children.Add(ActionBtn("Hide details", true, () =>
                {
                    _expandedTaskIds.Remove(task.Id);
                    RenderProjectsTab(_lastTasks);
                }));
            }

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

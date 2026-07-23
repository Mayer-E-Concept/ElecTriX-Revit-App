// CommentsWindow.cs -- ME-Tools | Project Comments
// Mayer E-Concept SRL -- Pure C# WPF, no XAML
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Autodesk.Revit.UI;
using Button   = System.Windows.Controls.Button;
using ComboBox = System.Windows.Controls.ComboBox;
using TextBox  = System.Windows.Controls.TextBox;

namespace METools.Comments
{
    public class CommentsWindow : METools.MeToolsWindowBase
    {
        private readonly ExternalEvent   _extEvent;
        private readonly CommentsHandler _handler;
        private readonly Autodesk.Revit.UI.UIApplication _uiApp;

        private List<ProjectComment> _all = new List<ProjectComment>();
        private string _currentLevel = "";
        private string _currentScopeBox = "";
        private string _statusFilter = "Open"; // "" = All, else CommentStatus.ToString()

        private TextBlock  _levelLabel;
        private TextBox    _tbNewComment;
        private StackPanel _statusBar_Filters;
        private StackPanel _rowsPanel;
        private TextBlock  _countLabel;

        // Pending "reference an item" state for the comment currently being
        // composed -- cleared after the comment is actually added.
        private string _pendingRefElementId = "";
        private string _pendingRefSummary = "";
        private StackPanel _refChipHost;
        private ComboBox _assignCombo;

        // Settings row (kept inline in this window rather than in the shared
        // Settings window, since the shared folder + sound toggle are specific
        // to this one feature).
        private TextBox _tbSharedFolder;
        private Button  _soundToggleBtn;
        private bool    _soundOn;

        protected override string AppKey => "Comments";

        public CommentsWindow(ExternalEvent extEvent, CommentsHandler handler, Autodesk.Revit.UI.UIApplication uiApp)
        {
            _extEvent = extEvent;
            _handler  = handler;
            _uiApp    = uiApp;
            _handler.OnLoaded = list => Dispatcher.Invoke(() =>
            {
                _all = list;
                RebuildList();
                PopulateAssignCombo();
                ResizeToFitContent();
            });
            _handler.OnError  = msg  => Dispatcher.Invoke(() => { if (StatusLeft != null) StatusLeft.Text = msg; });
            _handler.OnCurrentLevel = (lvl, sb) => Dispatcher.Invoke(() =>
            {
                _currentLevel = lvl ?? "";
                _currentScopeBox = sb ?? "";
                if (_levelLabel != null)
                {
                    var combined = CombinedLabel(_currentLevel, _currentScopeBox);
                    _levelLabel.Text = string.IsNullOrEmpty(_currentLevel)
                        ? "Current level: (open a floor plan view to tag a comment to a level)"
                        : $"Current level: {combined}";
                }
            });
            _handler.OnGoToElementResult = (success, msg) => Dispatcher.Invoke(() =>
            {
                if (StatusLeft != null)
                    StatusLeft.Text = success ? "Switched to and selected that item." : ("Couldn't go there: " + msg);
            });

            _soundOn = CommentsStorage.GetSoundEnabled();

            InitWindow("Comments", width: 560);
            BuildStatusBar("Loading comments…", "Revit");
            BuildUi();
        }

        private void BuildUi()
        {
            var scroller = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, MaxHeight = 480 };
            var root = new StackPanel { Margin = new Thickness(16) };
            scroller.Content = root;
            RootDock.Children.Add(scroller);

            // ── Shared folder + sound settings ─────────────────────────────
            root.Children.Add(Sec("Shared Folder"));
            root.Children.Add(new TextBlock
            {
                Text = "A network folder everyone on the team can reach. Comments for every project are stored here.",
                FontSize = 10.5, Foreground = MeToolsTheme.BrMuted, TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(2, 0, 0, 6),
            });
            var folderRow = new Grid { Margin = new Thickness(0, 0, 0, 14) };
            folderRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            folderRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            _tbSharedFolder = new TextBox
            {
                Height = 30, FontSize = 12, VerticalContentAlignment = VerticalAlignment.Center,
                Padding = new Thickness(8, 0, 8, 0),
                Background = MeToolsTheme.BrInput, Foreground = MeToolsTheme.BrText,
                BorderBrush = MeToolsTheme.BrBorder, BorderThickness = new Thickness(1),
                Text = CommentsStorage.GetSharedFolder(),
            };
            _tbSharedFolder.LostFocus += (s, e) => CommentsStorage.SetSharedFolder(_tbSharedFolder.Text?.Trim() ?? "");
            Grid.SetColumn(_tbSharedFolder, 0);
            folderRow.Children.Add(_tbSharedFolder);

            var browseBtn = MakeBtn("Browse…", true, () =>
            {
                try
                {
                    // WPF has no native folder picker -- this is the standard trick:
                    // an OpenFileDialog in "pick a folder" mode, matching how
                    // FamilyBrowserWindow/ProjectTransferWindow already use
                    // Microsoft.Win32 dialogs elsewhere in this project (rather
                    // than adding a new System.Windows.Forms dependency just for
                    // a folder picker).
                    var dlg = new Microsoft.Win32.OpenFileDialog
                    {
                        Title = "Select the shared comments folder",
                        CheckFileExists = false,
                        FileName = "Select This Folder",
                        Filter = "Folder|no.files",
                    };
                    if (!string.IsNullOrWhiteSpace(_tbSharedFolder.Text))
                        dlg.InitialDirectory = _tbSharedFolder.Text;
                    if (dlg.ShowDialog() == true)
                    {
                        var folder = System.IO.Path.GetDirectoryName(dlg.FileName);
                        if (!string.IsNullOrWhiteSpace(folder))
                        {
                            _tbSharedFolder.Text = folder;
                            CommentsStorage.SetSharedFolder(folder);
                        }
                    }
                }
                catch { }
            });
            browseBtn.Margin = new Thickness(8, 0, 0, 0);
            Grid.SetColumn(browseBtn, 1);
            folderRow.Children.Add(browseBtn);
            root.Children.Add(folderRow);

            _soundToggleBtn = MakeBtn(SoundLabel(), false, () =>
            {
                _soundOn = !_soundOn;
                CommentsStorage.SetSoundEnabled(_soundOn);
                _soundToggleBtn.Content = SoundLabel();
            });
            _soundToggleBtn.HorizontalAlignment = HorizontalAlignment.Left;
            _soundToggleBtn.Margin = new Thickness(0, 0, 0, 18);
            root.Children.Add(_soundToggleBtn);

            // ── Leave a new comment ──────────────────────────────────────
            root.Children.Add(Sec("Leave A Comment"));
            _levelLabel = new TextBlock
            {
                Text = "Current level: (open a floor plan view to tag a comment to a level)",
                FontSize = 11, Foreground = MeToolsTheme.BrMuted, Margin = new Thickness(2, 0, 0, 6),
            };
            root.Children.Add(_levelLabel);

            _tbNewComment = new TextBox
            {
                Height = 60, FontSize = 12.5, TextWrapping = TextWrapping.Wrap, AcceptsReturn = true,
                Padding = new Thickness(8), VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Background = MeToolsTheme.BrInput, Foreground = MeToolsTheme.BrText,
                BorderBrush = MeToolsTheme.BrBorder, BorderThickness = new Thickness(1),
                Margin = new Thickness(0, 0, 0, 8),
            };
            SetPlaceholder(_tbNewComment, "e.g. Need 4 more lamps on this level…");
            root.Children.Add(_tbNewComment);

            var refRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
            var refBtn = MakeBtn("+ Reference Item", true, OnReferenceItemClicked);
            refRow.Children.Add(refBtn);
            _refChipHost = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(8, 0, 0, 0) };
            refRow.Children.Add(_refChipHost);
            root.Children.Add(refRow);
            RenderRefChip();

            var assignRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 12) };
            assignRow.Children.Add(new TextBlock
            {
                Text = "Assign to (optional):", FontSize = 11, Foreground = MeToolsTheme.BrMuted,
                VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0),
            });
            _assignCombo = MeToolsWindowBase.StyledCombo(26, 11);
            _assignCombo.Width = 200;
            _assignCombo.IsEditable = true;
            assignRow.Children.Add(_assignCombo);
            root.Children.Add(assignRow);

            var addBtn = MakeBtn("+ Add Comment", false, () =>
            {
                var text = _tbNewComment.Text;
                if (text == "e.g. Need 4 more lamps on this level…" || string.IsNullOrWhiteSpace(text)) return;
                if (string.IsNullOrWhiteSpace(CommentsStorage.GetSharedFolder()))
                {
                    if (StatusLeft != null) StatusLeft.Text = "Set a shared folder above first.";
                    return;
                }
                _handler.Request = new CommentsRequest
                {
                    Action = CommentsAction.Add, Text = text,
                    LevelName = _currentLevel, ScopeBoxName = _currentScopeBox,
                    ReferencedElementId = _pendingRefElementId,
                    ReferencedSummary   = _pendingRefSummary,
                    AssignedTo = (_assignCombo.Text ?? "").Trim(),
                };
                _extEvent.Raise();
                _tbNewComment.Text = "";
                SetPlaceholder(_tbNewComment, "e.g. Need 4 more lamps on this level…");
                _pendingRefElementId = "";
                _pendingRefSummary = "";
                _assignCombo.Text = "";
                RenderRefChip();
            });
            addBtn.HorizontalAlignment = HorizontalAlignment.Left;
            addBtn.Margin = new Thickness(0, 0, 0, 18);
            root.Children.Add(addBtn);

            // ── All comments ─────────────────────────────────────────────
            root.Children.Add(Sec("All Comments"));

            _statusBar_Filters = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
            root.Children.Add(_statusBar_Filters);
            RebuildFilterBar();

            _countLabel = new TextBlock { FontSize = 10.5, Foreground = MeToolsTheme.BrMuted, Margin = new Thickness(2, 0, 0, 6) };
            root.Children.Add(_countLabel);

            _rowsPanel = new StackPanel();
            root.Children.Add(_rowsPanel);

            var refreshBtn = MakeBtn("Refresh", true, () =>
            {
                _handler.Request = new CommentsRequest { Action = CommentsAction.Refresh };
                _extEvent.Raise();
            });
            refreshBtn.HorizontalAlignment = HorizontalAlignment.Left;
            refreshBtn.Margin = new Thickness(0, 10, 0, 0);
            root.Children.Add(refreshBtn);
        }

        private string SoundLabel() => _soundOn ? "🔊 Notification sound: On" : "🔇 Notification sound: Off";

        // Level names alone can be ambiguous (confirmed live: different
        // building sections can share an identically-named level), so the
        // Scope Box is appended wherever a level is shown or grouped by.
        private static string CombinedLabel(string levelName, string scopeBoxName) =>
            string.IsNullOrWhiteSpace(scopeBoxName) ? levelName : $"{levelName} ({scopeBoxName})";

        private static string LocationLabel(ProjectComment c) => CombinedLabel(c.LevelName, c.ScopeBoxName);

        // Natural sort: splits each name into text/number runs so "Obergeschoss 10"
        // sorts after "Obergeschoss 2" instead of before it (plain string sort
        // compares the "1" before the "2" and gets that backwards). Duplicated
        // locally rather than shared with StatisticsCommand.cs's identical
        // helper, matching this project's existing per-file convention (e.g.
        // SetPlaceholder) for small helpers rather than a new shared utility class.
        private static List<string> NaturalSortKey(string s)
        {
            var parts = new List<string>();
            var current = new System.Text.StringBuilder();
            bool? lastWasDigit = null;
            foreach (var ch in s)
            {
                bool isDigit = char.IsDigit(ch);
                if (lastWasDigit != null && isDigit != lastWasDigit)
                {
                    parts.Add(current.ToString());
                    current.Clear();
                }
                current.Append(ch);
                lastWasDigit = isDigit;
            }
            if (current.Length > 0) parts.Add(current.ToString());
            return parts;
        }

        private static int CompareNatural(string a, string b)
        {
            var pa = NaturalSortKey(a ?? "");
            var pb = NaturalSortKey(b ?? "");
            for (int i = 0; i < Math.Min(pa.Count, pb.Count); i++)
            {
                bool numA = int.TryParse(pa[i], out int na);
                bool numB = int.TryParse(pb[i], out int nb);
                int cmp = (numA && numB) ? na.CompareTo(nb)
                                          : string.Compare(pa[i], pb[i], StringComparison.OrdinalIgnoreCase);
                if (cmp != 0) return cmp;
            }
            return pa.Count.CompareTo(pb.Count);
        }

        // Simple placeholder behaviour for a plain TextBox -- duplicated locally
        // the same way every other window in this project does it (it's a small
        // per-file helper, not something MeToolsWindowBase provides).
        private void SetPlaceholder(TextBox tb, string placeholder)
        {
            tb.Text = placeholder;
            tb.Foreground = MeToolsTheme.BrMuted;
            tb.GotFocus += (s, e) =>
            {
                if (tb.Text == placeholder) { tb.Text = ""; tb.Foreground = MeToolsTheme.BrText; }
            };
            tb.LostFocus += (s, e) =>
            {
                if (string.IsNullOrWhiteSpace(tb.Text)) { tb.Text = placeholder; tb.Foreground = MeToolsTheme.BrMuted; }
            };
        }

        private void RebuildFilterBar()
        {
            _statusBar_Filters.Children.Clear();
            var defs = new (string Key, string Label)[]
            {
                ("Open", "Open"),
                ("Done", "Done"),
                ("Ignored", "Ignored"),
                ("", "All"),
            };
            foreach (var (key, label) in defs)
            {
                var btn = ToggleBtn(label, _statusFilter == key, () => { _statusFilter = key; RebuildFilterBar(); RebuildList(); });
                btn.Margin = new Thickness(0, 0, 6, 0);
                _statusBar_Filters.Children.Add(btn);
            }
        }

        // Same reasoning as SettingsWindow.ResizeToFitActiveTab(): InitWindow's
        // Loaded handler measures the window once and freezes its height so
        // the resize grip doesn't fight WPF's auto-sizing. Here the trigger
        // for needing a re-measure isn't a tab switch, it's that comments
        // load asynchronously (OnLoaded fires after a background Task.Run) --
        // so the freeze happens while the list is still empty, and the
        // window never grows once the real comments arrive a moment later.
        private void PopulateAssignCombo()
        {
            if (_assignCombo == null) return;
            var typed = _assignCombo.Text;
            var names = _all.Select(x => x.Author).Concat(_all.Select(x => x.AssignedTo))
                .Where(n => !string.IsNullOrWhiteSpace(n))
                .Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(n => n).ToList();
            _assignCombo.Items.Clear();
            foreach (var n in names) _assignCombo.Items.Add(n);
            _assignCombo.Text = typed; // preserve whatever the user was already typing
        }

        private void ResizeToFitContent()
        {
            try
            {
                SizeToContent = SizeToContent.Height;
                UpdateLayout();
                Height = ActualHeight;
                SizeToContent = SizeToContent.Manual;
            }
            catch { }
        }

        private void RebuildList()
        {
            _rowsPanel.Children.Clear();
            var filtered = _all.Where(c => string.IsNullOrEmpty(_statusFilter) || c.Status.ToString() == _statusFilter)
                                .ToList();
            _countLabel.Text = $"{filtered.Count} of {_all.Count} total";

            if (filtered.Count == 0)
            {
                _rowsPanel.Children.Add(new TextBlock
                {
                    Text = "No comments here yet.", FontSize = 11.5, Foreground = MeToolsTheme.BrMuted,
                    Margin = new Thickness(2, 8, 0, 8),
                });
                return;
            }

            var byAuthor = filtered
                .GroupBy(c => c.Author, StringComparer.OrdinalIgnoreCase)
                .OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase);

            foreach (var authorGroup in byAuthor)
            {
                _rowsPanel.Children.Add(new TextBlock
                {
                    Text = authorGroup.Key, FontSize = 13, FontWeight = FontWeights.Bold,
                    Foreground = MeToolsTheme.BrAccent, Margin = new Thickness(0, 14, 0, 6),
                });

                var byLevel = authorGroup
                    .GroupBy(c => LocationLabel(c))
                    .OrderBy(g => g.Key, Comparer<string>.Create(CompareNatural));

                foreach (var levelGroup in byLevel)
                {
                    _rowsPanel.Children.Add(new TextBlock
                    {
                        Text = levelGroup.Key, FontSize = 11, FontWeight = FontWeights.SemiBold,
                        Foreground = MeToolsTheme.BrMuted, Margin = new Thickness(4, 0, 0, 6),
                    });

                    foreach (var c in levelGroup.OrderByDescending(c => c.CreatedUtc))
                        _rowsPanel.Children.Add(BuildRow(c));
                }
            }
        }

        private Border BuildRow(ProjectComment c)
        {
            var border = new Border
            {
                Background = MeToolsTheme.BrRow, BorderBrush = MeToolsTheme.BrBorder,
                BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(5),
                Margin = new Thickness(0, 0, 0, 8), Padding = new Thickness(12, 10, 12, 10),
            };
            var stack = new StackPanel();
            border.Child = stack;

            var topRow = new Grid();
            topRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            topRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var meta = new TextBlock
            {
                FontSize = 11, Foreground = MeToolsTheme.BrMuted,
                Text = $"{LocalTime(c.CreatedUtc):g}",
            };
            Grid.SetColumn(meta, 0);
            topRow.Children.Add(meta);

            var statusChip = new Border
            {
                Background = ChipColor(c.Status), CornerRadius = new CornerRadius(3),
                Padding = new Thickness(6, 1, 6, 1), HorizontalAlignment = HorizontalAlignment.Right,
            };
            statusChip.Child = new TextBlock { Text = c.Status.ToString(), FontSize = 9.5, Foreground = Brushes.White };
            Grid.SetColumn(statusChip, 1);
            topRow.Children.Add(statusChip);
            stack.Children.Add(topRow);

            stack.Children.Add(new TextBlock
            {
                Text = c.Text, FontSize = 12.5, Foreground = MeToolsTheme.BrText, TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 6, 0, 8),
            });

            if (!string.IsNullOrEmpty(c.AssignedTo))
            {
                string me = "";
                try { me = _uiApp?.Application?.Username ?? ""; } catch { }
                bool isMe = !string.IsNullOrEmpty(me) && string.Equals(me, c.AssignedTo, StringComparison.OrdinalIgnoreCase);

                stack.Children.Add(new Border
                {
                    Background = isMe
                        ? new SolidColorBrush(Color.FromArgb(50, MeToolsTheme.CPetrol.R, MeToolsTheme.CPetrol.G, MeToolsTheme.CPetrol.B))
                        : MeToolsTheme.BrInfoBox,
                    BorderBrush = isMe ? MeToolsTheme.BrPetrol : MeToolsTheme.BrBorder,
                    BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(4),
                    Padding = new Thickness(8, 3, 8, 3), Margin = new Thickness(0, 0, 0, 8),
                    HorizontalAlignment = HorizontalAlignment.Left,
                    Child = new TextBlock
                    {
                        Text = (isMe ? "Assigned to you — " : "Assigned to ") + c.AssignedTo,
                        FontSize = 11, FontWeight = isMe ? FontWeights.SemiBold : FontWeights.Normal,
                        Foreground = isMe ? MeToolsTheme.BrPetrol : MeToolsTheme.BrInfoText,
                    },
                });
            }

            if (c.Status != CommentStatus.Open && !string.IsNullOrEmpty(c.ResolvedBy))
            {
                stack.Children.Add(new TextBlock
                {
                    Text = $"{c.Status} by {c.ResolvedBy}" + (c.ResolvedUtc.HasValue ? $" — {LocalTime(c.ResolvedUtc.Value):g}" : ""),
                    FontSize = 10, Foreground = MeToolsTheme.BrMuted, Margin = new Thickness(0, 0, 0, 8),
                });
            }

            var btnRow = new StackPanel { Orientation = Orientation.Horizontal };
            stack.Children.Add(btnRow);

            var assignEditRow = new StackPanel
            {
                Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0),
                Visibility = Visibility.Collapsed,
            };
            var assignEditBox = new TextBox
            {
                Width = 160, Height = 26, FontSize = 11, Text = c.AssignedTo,
                VerticalContentAlignment = VerticalAlignment.Center,
                Background = MeToolsTheme.BrInput, Foreground = MeToolsTheme.BrText,
                BorderBrush = MeToolsTheme.BrBorder, BorderThickness = new Thickness(1),
                Padding = new Thickness(6, 0, 6, 0),
            };
            assignEditRow.Children.Add(assignEditBox);
            var assignSetBtn = MakeBtn("Set", false, () =>
            {
                _handler.Request = new CommentsRequest
                {
                    Action = CommentsAction.SetAssignedTo, CommentId = c.Id,
                    AssignedTo = (assignEditBox.Text ?? "").Trim(),
                };
                _extEvent.Raise();
            });
            assignSetBtn.Margin = new Thickness(6, 0, 0, 0);
            assignEditRow.Children.Add(assignSetBtn);
            stack.Children.Add(assignEditRow);

            var goBtn = MakeBtn("Go There", true, () =>
            {
                _handler.Request = new CommentsRequest
                {
                    Action = CommentsAction.JumpToLevel,
                    LevelName = c.LevelName, ScopeBoxName = c.ScopeBoxName,
                };
                _extEvent.Raise();
            });
            goBtn.Margin = new Thickness(0, 0, 6, 0);
            btnRow.Children.Add(goBtn);

            if (!string.IsNullOrEmpty(c.ReferencedElementId))
            {
                var goItemBtn = MakeBtn("Go to Item", true, () =>
                {
                    _handler.Request = new CommentsRequest
                    {
                        Action = CommentsAction.GoToElement,
                        ReferencedElementId = c.ReferencedElementId,
                    };
                    _extEvent.Raise();
                });
                goItemBtn.Margin = new Thickness(0, 0, 6, 0);
                goItemBtn.ToolTip = c.ReferencedSummary;
                btnRow.Children.Add(goItemBtn);
            }

            var assignBtn = MakeBtn(string.IsNullOrEmpty(c.AssignedTo) ? "Assign" : "Change", true, () =>
            {
                assignEditRow.Visibility = assignEditRow.Visibility == Visibility.Visible
                    ? Visibility.Collapsed : Visibility.Visible;
            });
            assignBtn.Margin = new Thickness(0, 0, 6, 0);
            btnRow.Children.Add(assignBtn);

            if (c.Status != CommentStatus.Done)
            {
                var doneBtn = MakeBtn("Mark Done", false, () => SetStatus(c.Id, CommentStatus.Done));
                doneBtn.Margin = new Thickness(0, 0, 6, 0);
                btnRow.Children.Add(doneBtn);
            }
            if (c.Status != CommentStatus.Ignored)
            {
                var ignoreBtn = MakeBtn("Ignore", true, () => SetStatus(c.Id, CommentStatus.Ignored));
                btnRow.Children.Add(ignoreBtn);
            }
            if (c.Status != CommentStatus.Open)
            {
                var reopenBtn = MakeBtn("Reopen", true, () => SetStatus(c.Id, CommentStatus.Open));
                btnRow.Children.Add(reopenBtn);
            }

            // Unlike the other actions here, this one can't be undone from
            // within the app (Ignore/Done can always be Reopened) -- so it's
            // the one action that gets an explicit confirmation step first.
            var deleteBtn = MakeBtn("Delete", true, () =>
            {
                var result = TaskDialog.Show(
                    "Delete Comment",
                    $"Permanently delete this comment by {c.Author}?\n\n\"{c.Text}\"",
                    TaskDialogCommonButtons.Yes | TaskDialogCommonButtons.No);
                if (result != TaskDialogResult.Yes) return;

                _handler.Request = new CommentsRequest { Action = CommentsAction.Delete, CommentId = c.Id };
                _extEvent.Raise();
            });
            deleteBtn.Margin = new Thickness(0, 0, 6, 0);
            btnRow.Children.Add(deleteBtn);

            return border;
        }

        private void SetStatus(string id, CommentStatus status)
        {
            _handler.Request = new CommentsRequest { Action = CommentsAction.SetStatus, CommentId = id, NewStatus = status };
            _extEvent.Raise();
        }

        private static DateTime LocalTime(DateTime utc) => utc.Kind == DateTimeKind.Utc ? utc.ToLocalTime() : utc;

        private static Brush ChipColor(CommentStatus status)
        {
            switch (status)
            {
                case CommentStatus.Done:    return MeToolsTheme.BrGreen;
                case CommentStatus.Ignored: return MeToolsTheme.BrSecText;
                default:                    return MeToolsTheme.BrAccent;
            }
        }

        // Same Hide-window / PickObject / Show-window pattern already
        // established in CircuitTaggerWindow.cs's OnSelectClicked -- works
        // because this window is modeless and Revit's API allows a direct
        // synchronous pick call from here, no ExternalEvent round-trip needed
        // just to capture a selection.
        private void OnReferenceItemClicked()
        {
            Hide();
            try
            {
                var uidoc = _uiApp?.ActiveUIDocument;
                if (uidoc == null) return;
                var r = uidoc.Selection.PickObject(Autodesk.Revit.UI.Selection.ObjectType.Element,
                    "Click the element to reference, then Esc to cancel");
                var doc = uidoc.Document;
                var el = doc.GetElement(r.ElementId);
                if (el != null)
                {
                    _pendingRefElementId = r.ElementId.IntegerValue.ToString();

                    string family = "", typeName = "";
                    if (el is Autodesk.Revit.DB.FamilyInstance fi)
                    {
                        try { family = fi.Symbol?.Family?.Name ?? ""; } catch { }
                        try { typeName = fi.Symbol?.Name ?? ""; } catch { }
                    }
                    else
                    {
                        try { typeName = doc.GetElement(el.GetTypeId())?.Name ?? ""; } catch { }
                    }
                    string cat = el.Category?.Name ?? "Element";
                    var parts = new List<string> { cat };
                    if (!string.IsNullOrEmpty(family)) parts.Add(family);
                    if (!string.IsNullOrEmpty(typeName)) parts.Add(typeName);
                    _pendingRefSummary = string.Join(" - ", parts);
                }
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException) { /* Esc pressed -- fine, nothing referenced */ }
            catch { }
            finally
            {
                Show();
                RenderRefChip();
            }
        }

        private void RenderRefChip()
        {
            if (_refChipHost == null) return;
            _refChipHost.Children.Clear();
            if (string.IsNullOrEmpty(_pendingRefElementId)) return;

            var chip = new Border
            {
                Background = MeToolsTheme.BrInfoBox, CornerRadius = new CornerRadius(10),
                Padding = new Thickness(8, 3, 4, 3), VerticalAlignment = VerticalAlignment.Center,
            };
            var sp = new StackPanel { Orientation = Orientation.Horizontal };
            sp.Children.Add(new TextBlock
            {
                Text = _pendingRefSummary, FontSize = 10.5, Foreground = MeToolsTheme.BrInfoText,
                VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0),
                MaxWidth = 260, TextTrimming = TextTrimming.CharacterEllipsis,
            });
            var xBtn = new Button
            {
                Content = "\u00D7", FontSize = 12, Width = 18, Height = 18, Padding = new Thickness(0),
                Background = Brushes.Transparent, BorderThickness = new Thickness(0),
                Foreground = MeToolsTheme.BrMuted, Cursor = Cursors.Hand,
            };
            xBtn.Click += (s, e) => { _pendingRefElementId = ""; _pendingRefSummary = ""; RenderRefChip(); };
            sp.Children.Add(xBtn);
            chip.Child = sp;
            _refChipHost.Children.Add(chip);
        }

        private Button MakeBtn(string label, bool isOutline, Action onClick)
        {
            var btn = new Button
            {
                Content = label, Height = 28, Padding = new Thickness(10, 0, 10, 0), FontSize = 11.5,
                Cursor = Cursors.Hand,
                Background = isOutline ? MeToolsTheme.BrBtnBg : MeToolsTheme.BrAccent,
                BorderBrush = isOutline ? MeToolsTheme.BrBtnBorder : MeToolsTheme.BrAccent,
                BorderThickness = new Thickness(1),
                Foreground = isOutline ? MeToolsTheme.BrText : MeToolsTheme.BrOnAccent,
            };
            btn.Template = RoundedBtnTemplate();
            btn.Click += (s, e) => onClick();
            return btn;
        }

        protected override void OnThemeChanged()
        {
            base.OnThemeChanged();
            RebuildList();
        }
    }
}

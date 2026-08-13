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
        private string _statusFilter = "Open"; // "" = All, else CommentStatus.ToString() -- key stays English, only the Label shown is localized

        private TextBlock  _levelLabel;
        private TextBox    _tbNewComment;
        private StackPanel _statusBar_Filters;
        private StackPanel _rowsPanel;
        private TextBlock  _countLabel;
        private Button     _btnMarkSelectedDone;
        private readonly HashSet<string> _selectedForBulk = new HashSet<string>();

        // Pending "reference an item" state for the comment currently being
        // composed -- cleared after the comment is actually added.
        private string _pendingRefElementId = "";
        private string _pendingRefSummary = "";
        private StackPanel _refChipHost;
        private TextBox _assignBox;

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
                if (StatusLeft != null) StatusLeft.Text = S._("comments.refreshed");
            });
            _handler.OnError  = msg  => Dispatcher.Invoke(() => { if (StatusLeft != null) StatusLeft.Text = msg; });
            _handler.OnBulkStatusDone = count => Dispatcher.Invoke(() =>
            {
                _selectedForBulk.Clear();
                UpdateMarkSelectedDoneButton();
                if (StatusLeft != null)
                    StatusLeft.Text = string.Format(S._(count == 1 ? "comments.marked_done_1" : "comments.marked_done_n"), count);
            });
            _handler.OnCurrentLevel = (lvl, sb) => Dispatcher.Invoke(() =>
            {
                _currentLevel = lvl ?? "";
                _currentScopeBox = sb ?? "";
                if (_levelLabel != null)
                {
                    var combined = CombinedLabel(_currentLevel, _currentScopeBox);
                    _levelLabel.Text = string.IsNullOrEmpty(_currentLevel)
                        ? S._("comments.current_level_none")
                        : string.Format(S._("comments.current_level"), combined);
                }
            });
            _handler.OnGoToElementResult = (success, msg) => Dispatcher.Invoke(() =>
            {
                if (StatusLeft != null)
                    StatusLeft.Text = success ? S._("comments.switched_to_item") : string.Format(S._("comments.couldnt_go"), msg);
            });

            _soundOn = CommentsStorage.GetSoundEnabled();

            S.SetLanguage(SettingsStore.Language ?? "en");
            InitWindow(S._("comments.window_title"), width: 560);
            BuildStatusBar(S._("comments.loading"), "Revit 2025/2026");
            BuildUi();
        }

        private void BuildUi()
        {
            // Footer FIRST (Dock.Bottom before the fill element) -- same
            // reasoning as ActivityLogWindow.Build(): DockPanel needs its
            // docked children added before the "fill" element for
            // LastChildFill to work, and this is what keeps these two
            // buttons always visible regardless of how tall the scrollable
            // content above ends up being (previously they were the LAST
            // items inside that same scroller, which is exactly why they
            // needed scrolling to reach even with zero comments loaded --
            // the settings + leave-a-comment sections above them were
            // already tall enough on their own to push them past the fold).
            var footer = new Border
            {
                Background = MeToolsTheme.BrFooter,
                BorderBrush = MeToolsTheme.BrBorder, BorderThickness = new Thickness(0, 1, 0, 0),
                Padding = new Thickness(14, 10, 14, 10),
            };
            DockPanel.SetDock(footer, Dock.Bottom);
            var footerBtnRow = new StackPanel { Orientation = Orientation.Horizontal };
            var refreshBtn = MakeBtn(S._("comments.refresh"), true, () =>
            {
                if (StatusLeft != null) StatusLeft.Text = S._("comments.refreshing");
                _handler.Request = new CommentsRequest { Action = CommentsAction.Refresh };
                _extEvent.Raise();
            });
            var exportBtn = MakeBtn(S._("comments.export"), false, OnExportClicked);
            refreshBtn.Margin = new Thickness(0, 0, 8, 0);
            footerBtnRow.Children.Add(refreshBtn);
            footerBtnRow.Children.Add(exportBtn);
            footer.Child = footerBtnRow;
            RootDock.Children.Add(footer);

            var scroller = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, MaxHeight = 480, Background = MeToolsTheme.BrBg };
            var root = new StackPanel { Margin = new Thickness(16) };
            scroller.Content = root;
            RootDock.Children.Add(scroller);

            // ── Shared folder + sound settings ─────────────────────────────
            root.Children.Add(Sec(S._("comments.shared_folder")));
            root.Children.Add(new TextBlock
            {
                Text = S._("comments.shared_folder_hint"),
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

            var browseBtn = MakeBtn(S._("comments.browse"), true, () =>
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
                        Title = S._("comments.select_folder_title"),
                        CheckFileExists = false,
                        FileName = S._("comments.select_this_folder"),
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
            root.Children.Add(Sec(S._("comments.leave_comment")));
            _levelLabel = new TextBlock
            {
                Text = S._("comments.current_level_none"),
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
            SetPlaceholder(_tbNewComment, S._("comments.new_comment_placeholder"));
            root.Children.Add(_tbNewComment);

            var refRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
            var refBtn = MakeBtn(S._("comments.reference_item"), true, OnReferenceItemClicked);
            refRow.Children.Add(refBtn);
            _refChipHost = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(8, 0, 0, 0) };
            refRow.Children.Add(_refChipHost);
            root.Children.Add(refRow);
            RenderRefChip();

            var assignRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 12) };
            assignRow.Children.Add(new TextBlock
            {
                Text = S._("comments.assign_optional"), FontSize = 11, Foreground = MeToolsTheme.BrMuted,
                VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0),
            });
            _assignBox = new TextBox
            {
                Height = 26, FontSize = 11, Width = 200,
                Background = MeToolsTheme.BrInput, Foreground = MeToolsTheme.BrText,
                BorderBrush = MeToolsTheme.BrBorder, BorderThickness = new Thickness(1),
                Padding = new Thickness(6, 0, 6, 0), VerticalContentAlignment = VerticalAlignment.Center,
                ToolTip = S._("comments.type_a_name"),
            };
            assignRow.Children.Add(_assignBox);
            root.Children.Add(assignRow);

            var addBtn = MakeBtn(S._("comments.add_comment"), false, () =>
            {
                var text = _tbNewComment.Text;
                if (text == S._("comments.new_comment_placeholder") || string.IsNullOrWhiteSpace(text)) return;
                if (string.IsNullOrWhiteSpace(CommentsStorage.GetSharedFolder()))
                {
                    if (StatusLeft != null) StatusLeft.Text = S._("comments.set_folder_first");
                    return;
                }
                _handler.Request = new CommentsRequest
                {
                    Action = CommentsAction.Add, Text = text,
                    LevelName = _currentLevel, ScopeBoxName = _currentScopeBox,
                    ReferencedElementId = _pendingRefElementId,
                    ReferencedSummary   = _pendingRefSummary,
                    AssignedTo = (_assignBox.Text ?? "").Trim(),
                };
                _extEvent.Raise();
                _tbNewComment.Text = "";
                SetPlaceholder(_tbNewComment, S._("comments.new_comment_placeholder"));
                _pendingRefElementId = "";
                _pendingRefSummary = "";
                _assignBox.Text = "";
                RenderRefChip();
            });
            addBtn.HorizontalAlignment = HorizontalAlignment.Left;
            addBtn.Margin = new Thickness(0, 0, 0, 18);
            root.Children.Add(addBtn);

            // ── All comments ─────────────────────────────────────────────
            root.Children.Add(Sec(S._("comments.all_comments")));

            _statusBar_Filters = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
            root.Children.Add(_statusBar_Filters);
            RebuildFilterBar();

            _countLabel = new TextBlock { FontSize = 10.5, Foreground = MeToolsTheme.BrMuted, Margin = new Thickness(2, 0, 0, 6) };
            root.Children.Add(_countLabel);

            _btnMarkSelectedDone = MakeBtn(S._("comments.mark_selected_done"), false, OnMarkSelectedDoneClicked);
            _btnMarkSelectedDone.IsEnabled = false;
            _btnMarkSelectedDone.HorizontalAlignment = HorizontalAlignment.Left;
            _btnMarkSelectedDone.Margin = new Thickness(0, 0, 0, 8);
            root.Children.Add(_btnMarkSelectedDone);

            _rowsPanel = new StackPanel();
            root.Children.Add(_rowsPanel);
        }

        private string SoundLabel() => _soundOn ? S._("comments.sound_on") : S._("comments.sound_off");

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
                ("Open", S._("comments.filter_open")),
                ("Done", S._("comments.filter_done")),
                ("Ignored", S._("comments.filter_ignored")),
                ("", S._("comments.filter_all")),
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
            if (_assignBox == null) return;
            var names = _all.Select(x => x.Author).Concat(_all.Select(x => x.AssignedTo))
                .Where(n => !string.IsNullOrWhiteSpace(n))
                .Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(n => n).ToList();
            _assignBox.ToolTip = names.Count == 0
                ? S._("comments.type_a_name")
                : S._("comments.type_a_name_prev") + string.Join(", ", names);
        }

        private void RebuildList()
        {
            _rowsPanel.Children.Clear();
            var filtered = _all.Where(c => string.IsNullOrEmpty(_statusFilter) || c.Status.ToString() == _statusFilter)
                                .ToList();
            _countLabel.Text = string.Format(S._("comments.count_of_total"), filtered.Count, _all.Count);

            if (filtered.Count == 0)
            {
                _rowsPanel.Children.Add(new TextBlock
                {
                    Text = S._("comments.no_comments_yet"), FontSize = 11.5, Foreground = MeToolsTheme.BrMuted,
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
            topRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            topRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            topRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            if (c.Status == CommentStatus.Open)
            {
                var cb = new CheckBox
                {
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, 0, 8, 0),
                    IsChecked = _selectedForBulk.Contains(c.Id),
                    ToolTip = S._("comments.select_for_bulk_tip"),
                };
                cb.Checked   += (s, e) => { _selectedForBulk.Add(c.Id); UpdateMarkSelectedDoneButton(); };
                cb.Unchecked += (s, e) => { _selectedForBulk.Remove(c.Id); UpdateMarkSelectedDoneButton(); };
                Grid.SetColumn(cb, 0);
                topRow.Children.Add(cb);
            }

            var meta = new TextBlock
            {
                FontSize = 11, Foreground = MeToolsTheme.BrMuted,
                Text = $"{LocalTime(c.CreatedUtc):g}",
            };
            Grid.SetColumn(meta, 1);
            topRow.Children.Add(meta);

            var statusChip = new Border
            {
                Background = ChipColor(c.Status), CornerRadius = new CornerRadius(3),
                Padding = new Thickness(6, 1, 6, 1), HorizontalAlignment = HorizontalAlignment.Right,
            };
            statusChip.Child = new TextBlock { Text = StatusLabel(c.Status), FontSize = 9.5, Foreground = Brushes.White };
            Grid.SetColumn(statusChip, 2);
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
                        ? new SolidColorBrush(Color.FromArgb(50, MeToolsTheme.CAccent.R, MeToolsTheme.CAccent.G, MeToolsTheme.CAccent.B))
                        : MeToolsTheme.BrInfoBox,
                    BorderBrush = isMe ? MeToolsTheme.BrAccent : MeToolsTheme.BrBorder,
                    BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(4),
                    Padding = new Thickness(8, 3, 8, 3), Margin = new Thickness(0, 0, 0, 8),
                    HorizontalAlignment = HorizontalAlignment.Left,
                    Child = new TextBlock
                    {
                        Text = (isMe ? S._("comments.assigned_to_you") : S._("comments.assigned_to")) + c.AssignedTo,
                        FontSize = 11, FontWeight = isMe ? FontWeights.SemiBold : FontWeights.Normal,
                        Foreground = isMe ? MeToolsTheme.BrAccent : MeToolsTheme.BrInfoText,
                    },
                });
            }

            if (c.Status != CommentStatus.Open && !string.IsNullOrEmpty(c.ResolvedBy))
            {
                stack.Children.Add(new TextBlock
                {
                    Text = string.Format(S._("comments.resolved_by"), StatusLabel(c.Status), c.ResolvedBy) + (c.ResolvedUtc.HasValue ? $" — {LocalTime(c.ResolvedUtc.Value):g}" : ""),
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
            var assignSetBtn = MakeBtn(S._("comments.set"), false, () =>
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

            var goBtn = MakeBtn(S._("comments.go_there"), true, () =>
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
                var goItemBtn = MakeBtn(S._("comments.go_to_item"), true, () =>
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

            var assignBtn = MakeBtn(string.IsNullOrEmpty(c.AssignedTo) ? S._("comments.assign") : S._("comments.change"), true, () =>
            {
                assignEditRow.Visibility = assignEditRow.Visibility == Visibility.Visible
                    ? Visibility.Collapsed : Visibility.Visible;
            });
            assignBtn.Margin = new Thickness(0, 0, 6, 0);
            btnRow.Children.Add(assignBtn);

            if (c.Status != CommentStatus.Done)
            {
                var doneBtn = MakeBtn(S._("comments.mark_done"), false, () => SetStatus(c.Id, CommentStatus.Done));
                doneBtn.Margin = new Thickness(0, 0, 6, 0);
                btnRow.Children.Add(doneBtn);
            }
            if (c.Status != CommentStatus.Ignored)
            {
                var ignoreBtn = MakeBtn(S._("comments.ignore"), true, () => SetStatus(c.Id, CommentStatus.Ignored));
                btnRow.Children.Add(ignoreBtn);
            }
            if (c.Status != CommentStatus.Open)
            {
                var reopenBtn = MakeBtn(S._("comments.reopen"), true, () => SetStatus(c.Id, CommentStatus.Open));
                btnRow.Children.Add(reopenBtn);
            }

            // Unlike the other actions here, this one can't be undone from
            // within the app (Ignore/Done can always be Reopened) -- so it's
            // the one action that gets an explicit confirmation step first.
            var deleteBtn = MakeBtn(S._("comments.delete"), true, () =>
            {
                var result = TaskDialog.Show(
                    S._("comments.delete_title"),
                    string.Format(S._("comments.delete_confirm"), c.Author, c.Text),
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

        private void UpdateMarkSelectedDoneButton()
        {
            if (_btnMarkSelectedDone == null) return;
            int n = _selectedForBulk.Count;
            _btnMarkSelectedDone.IsEnabled = n > 0;
            _btnMarkSelectedDone.Content = n > 0
                ? string.Format(S._("comments.mark_selected_done_n"), n)
                : S._("comments.mark_selected_done");
        }

        private void OnMarkSelectedDoneClicked()
        {
            if (_selectedForBulk.Count == 0) return;
            var ids = _selectedForBulk.ToList();
            if (StatusLeft != null) StatusLeft.Text = S._("comments.marking_selected_done");
            _handler.Request = new CommentsRequest
            {
                Action = CommentsAction.BulkSetStatus,
                CommentIds = ids,
                NewStatus = CommentStatus.Done,
            };
            _extEvent.Raise();
        }

        // Exports whatever is currently loaded (_all) -- not re-read from
        // disk, so this always matches exactly what's on screen, filters and
        // all being ignored deliberately: a close-out record should cover
        // every comment on the project, not just whichever filter happened
        // to be selected when Export was clicked.
        private void OnExportClicked()
        {
            if (_all == null || _all.Count == 0)
            {
                MessageBox.Show(S._("comments.export_none"), S._("comments.export_title"), MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var dlg = new Microsoft.Win32.SaveFileDialog
            {
                Title = S._("comments.export_dialog_title"), Filter = "CSV files (*.csv)|*.csv",
                FileName = $"Comments_{DateTime.Now:yyyyMMdd_HHmm}", DefaultExt = ".csv",
            };
            if (dlg.ShowDialog() != true) return;

            try
            {
                var sb = new System.Text.StringBuilder();
                sb.AppendLine("CreatedLocal,Author,Level,ScopeBox,Text,Status,AssignedTo,ResolvedBy,ResolvedLocal,ReferencedElement");
                foreach (var c in _all.OrderBy(x => x.CreatedUtc))
                {
                    sb.AppendLine(string.Join(",",
                        Q($"{LocalTime(c.CreatedUtc):yyyy-MM-dd HH:mm}"), Q(c.Author), Q(c.LevelName), Q(c.ScopeBoxName),
                        Q(c.Text), Q(c.Status.ToString()), Q(c.AssignedTo), Q(c.ResolvedBy),
                        Q(c.ResolvedUtc.HasValue ? $"{LocalTime(c.ResolvedUtc.Value):yyyy-MM-dd HH:mm}" : ""),
                        Q(c.ReferencedSummary)));
                }
                System.IO.File.WriteAllText(dlg.FileName, sb.ToString(), System.Text.Encoding.UTF8);
                if (StatusLeft != null) StatusLeft.Text = string.Format(S._("comments.exported_rows"), _all.Count);
                MessageBox.Show(string.Format(S._("comments.exported_rows_path"), _all.Count, dlg.FileName),
                    S._("comments.export_complete"), MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(string.Format(S._("comments.export_failed"), ex.Message), S._("comments.export_error"), MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        // Comment text is free-form user input, far more likely than Circuit
        // Tagger's short codes to contain commas, quotes, or line breaks --
        // this quoting is what keeps such a comment from corrupting the CSV's
        // column structure.
        private static string Q(string s)
        {
            s = s ?? "";
            if (s.Contains(",") || s.Contains("\"") || s.Contains("\n") || s.Contains("\r"))
                return "\"" + s.Replace("\"", "\"\"") + "\"";
            return s;
        }

        private static DateTime LocalTime(DateTime utc) => utc.Kind == DateTimeKind.Utc ? utc.ToLocalTime() : utc;

        private static string StatusLabel(CommentStatus status)
        {
            switch (status)
            {
                case CommentStatus.Done:    return S._("comments.status_done");
                case CommentStatus.Ignored: return S._("comments.status_ignored");
                default:                    return S._("comments.status_open");
            }
        }

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
                    S._("comments.pick_element_prompt"));
                var doc = uidoc.Document;
                var el = doc.GetElement(r.ElementId);
                if (el != null)
                {
                    _pendingRefElementId = r.ElementId.Value.ToString();

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
                    string cat = el.Category?.Name ?? S._("comments.element");
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

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

        private List<ProjectComment> _all = new List<ProjectComment>();
        private string _currentLevel = "";
        private string _statusFilter = "Open"; // "" = All, else CommentStatus.ToString()

        private TextBlock  _levelLabel;
        private TextBox    _tbNewComment;
        private StackPanel _statusBar_Filters;
        private StackPanel _rowsPanel;
        private TextBlock  _countLabel;

        // Settings row (kept inline in this window rather than in the shared
        // Settings window, since the shared folder + sound toggle are specific
        // to this one feature).
        private TextBox _tbSharedFolder;
        private Button  _soundToggleBtn;
        private bool    _soundOn;

        protected override string AppKey => "Comments";

        public CommentsWindow(ExternalEvent extEvent, CommentsHandler handler)
        {
            _extEvent = extEvent;
            _handler  = handler;
            _handler.OnLoaded = list => Dispatcher.Invoke(() => { _all = list; RebuildList(); });
            _handler.OnError  = msg  => Dispatcher.Invoke(() => { if (StatusLeft != null) StatusLeft.Text = msg; });
            _handler.OnCurrentLevel = lvl => Dispatcher.Invoke(() =>
            {
                _currentLevel = lvl ?? "";
                if (_levelLabel != null)
                    _levelLabel.Text = string.IsNullOrEmpty(_currentLevel)
                        ? "Current level: (open a floor plan view to tag a comment to a level)"
                        : $"Current level: {_currentLevel}";
            });

            _soundOn = CommentsStorage.GetSoundEnabled();

            InitWindow("Comments", width: 560);
            BuildStatusBar("Loading comments…", "Revit");
            BuildUi();
        }

        private void BuildUi()
        {
            var scroller = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
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

            var addBtn = MakeBtn("+ Add Comment", false, () =>
            {
                var text = _tbNewComment.Text;
                if (text == "e.g. Need 4 more lamps on this level…" || string.IsNullOrWhiteSpace(text)) return;
                if (string.IsNullOrWhiteSpace(CommentsStorage.GetSharedFolder()))
                {
                    if (StatusLeft != null) StatusLeft.Text = "Set a shared folder above first.";
                    return;
                }
                _handler.Request = new CommentsRequest { Action = CommentsAction.Add, Text = text, LevelName = _currentLevel };
                _extEvent.Raise();
                _tbNewComment.Text = "";
                SetPlaceholder(_tbNewComment, "e.g. Need 4 more lamps on this level…");
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

        private void RebuildList()
        {
            _rowsPanel.Children.Clear();
            var filtered = _all.Where(c => string.IsNullOrEmpty(_statusFilter) || c.Status.ToString() == _statusFilter)
                                .OrderByDescending(c => c.CreatedUtc)
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

            foreach (var c in filtered)
                _rowsPanel.Children.Add(BuildRow(c));
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
                Text = $"{c.Author}  •  {c.LevelName}  •  {LocalTime(c.CreatedUtc):g}",
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

            var goBtn = MakeBtn("Go There", true, () =>
            {
                _handler.Request = new CommentsRequest { Action = CommentsAction.JumpToLevel, LevelName = c.LevelName };
                _extEvent.Raise();
            });
            goBtn.Margin = new Thickness(0, 0, 6, 0);
            btnRow.Children.Add(goBtn);

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

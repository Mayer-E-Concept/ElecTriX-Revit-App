// ActivityLogWindow.cs -- ME-Tools | Activity Log & Time Tracker
// Mayer E-Concept SRL
//
// Three tabs sharing one window/footer/status bar:
//   - Activity      : unchanged filter bar + card list (Added/Modified/Deleted).
//   - Team Totals   : Time Tracker -- total time/sessions/last-active per user.
//   - My Sessions   : Time Tracker -- your own daily totals, expandable.
// Time Tracker was previously its own tool/window; merged in here because
// both are the same underlying idea -- per-user, per-project history read
// from the same shared network folder. The background tracking itself
// (TimeTrackerWatcher/TimeTrackerStorage) is untouched by this -- only the
// UI entry point moved. See TimeTrackerHandler.cs for the refresh handler.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Autodesk.Revit.UI;
using Button     = System.Windows.Controls.Button;
using Color      = System.Windows.Media.Color;
using ComboBox   = System.Windows.Controls.ComboBox;
using TextBox    = System.Windows.Controls.TextBox;

namespace METools.ActivityLog
{
    public class ActivityLogWindow : MeToolsWindowBase
    {
        // ── Activity tab ─────────────────────────────────────────────────────
        private readonly ExternalEvent            _evt;
        private readonly ActivityLogRefreshHandler _handler;
        private readonly ExternalEvent             _navEvt;
        private readonly ActivityLogNavigateHandler _navHandler;
        private List<ActivityLogEntry> _all = new List<ActivityLogEntry>();

        private ComboBox _userCmb;
        private Button _btnAll, _btnAdded, _btnModified, _btnDeleted;
        private ActivityAction? _actionFilter; // null = All

        private Button _btnDateAll, _btnDateToday, _btnDate7, _btnDate30;
        private DateTime? _dateFilterSinceUtc; // null = all time
        private TextBox _searchBox;
        private StackPanel _body;
        private ScrollViewer _scroll;
        private Border _warningBox; // shown above the Activity list when the shared folder isn't configured
        private Border _filterBar; // only relevant to the Activity tab -- hidden on the other two

        // ── Team Totals / My Sessions tabs (Time Tracker) ───────────────────
        private readonly ExternalEvent _ttEvt;
        private readonly METools.TimeTracker.TimeTrackerRefreshHandler _ttHandler;
        private readonly string _currentUser;
        private List<METools.TimeTracker.TimeSessionEntry> _ttAll = new List<METools.TimeTracker.TimeSessionEntry>();
        private DateTime? _liveSessionStartUtc; // the document open right now, if being tracked -- see TimeTrackerWatcher.GetCurrentSessionStart
        private StackPanel _panTeam, _panMine;

        // ── Tabs ─────────────────────────────────────────────────────────────
        private Border _tabActivity, _tabTeam, _tabMine, _activeTab;
        private StackPanel _activePanel;

        protected override string AppKey => "ActivityLog";

        public ActivityLogWindow(
            List<ActivityLogEntry> entries, string warning, ExternalEvent evt, ActivityLogRefreshHandler handler,
            ExternalEvent navEvt, ActivityLogNavigateHandler navHandler,
            List<METools.TimeTracker.TimeSessionEntry> ttEntries, string ttWarning,
            ExternalEvent ttEvt, METools.TimeTracker.TimeTrackerRefreshHandler ttHandler,
            string currentUser, DateTime? liveSessionStartUtc)
        {
            _evt     = evt;
            _handler = handler;
            _navEvt     = navEvt;
            _navHandler = navHandler;
            _ttEvt     = ttEvt;
            _ttHandler = ttHandler;
            _currentUser = currentUser ?? "";
            _liveSessionStartUtc = liveSessionStartUtc;

            _navHandler.OnDone = (success, msg) => Dispatcher.Invoke(() =>
            {
                StatusLeft.Text = success ? S._("activitylog.switched_level") : string.Format(S._("activitylog.couldnt_go"), msg);
            });
            _handler.OnResult = (result, w) => Dispatcher.Invoke(() =>
            {
                _all = result ?? new List<ActivityLogEntry>();
                PopulateUserFilter();
                RenderList();
                StatusLeft.Text = string.IsNullOrEmpty(w) ? string.Format(S._("activitylog.entries_count"), _all.Count) : w;
            });
            _ttHandler.OnResult = (result, w, liveStart) => Dispatcher.Invoke(() =>
            {
                _ttAll = result ?? new List<METools.TimeTracker.TimeSessionEntry>();
                _liveSessionStartUtc = liveStart;
                RenderTeam();
                RenderMine();
                // Both refreshes land around the same moment -- Activity
                // Log's own message (entry count or its warning) is the
                // more informative default, so only override it here if
                // Time Tracker specifically has something to report.
                if (!string.IsNullOrEmpty(w)) StatusLeft.Text = w;
            });

            S.SetLanguage(SettingsStore.Language ?? "en");
            InitWindow(S._("activitylog.title"), 620);
            Build();

            _all   = entries   ?? new List<ActivityLogEntry>();
            _ttAll = ttEntries ?? new List<METools.TimeTracker.TimeSessionEntry>();

            if (string.IsNullOrWhiteSpace(METools.Comments.CommentsStorage.GetSharedFolder()))
                _warningBox = InfoBox(S._("activitylog.no_folder_warning"));

            PopulateUserFilter();
            RenderList();
            RenderTeam();
            RenderMine();

            StatusLeft.Text = !string.IsNullOrEmpty(warning)   ? warning
                             : !string.IsNullOrEmpty(ttWarning) ? ttWarning
                             : string.Format(S._("activitylog.entries_count"), _all.Count);
        }

        // ── Build ────────────────────────────────────────────────────────────
        private void Build()
        {
            BuildStatusBar(S._("activitylog.loading"));

            // Footer FIRST (Dock.Bottom before the fill element).
            var footer = new Border
            {
                Background = MeToolsTheme.BrFooter,
                BorderBrush = MeToolsTheme.BrBorder, BorderThickness = new Thickness(0, 1, 0, 0),
                Padding = new Thickness(14, 10, 14, 10),
            };
            DockPanel.SetDock(footer, Dock.Bottom);
            var footerRow = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            var exportBtn = FooterBtn(S._("activitylog.export_csv"), primary: false, onClick: () =>
            {
                // Export whichever tab is actually showing -- Activity's own
                // list, or the Time Tracker sessions log.
                if (_activePanel == _body) ExportCsv();
                else ExportTimeTrackerCsv();
            });
            var refreshBtn = FooterBtn(S._("activitylog.refresh"), primary: true, onClick: () =>
            {
                StatusLeft.Text = S._("activitylog.refreshing");
                _evt.Raise();
                _ttEvt.Raise();
            });
            exportBtn.Margin = new Thickness(0, 0, 8, 0);
            footerRow.Children.Add(exportBtn);
            footerRow.Children.Add(refreshBtn);
            footer.Child = footerRow;
            RootDock.Children.Add(footer);

            // Tab bar (Dock.Top).
            _tabActivity = MakeTab(S._("activitylog.tab_activity"), MeToolsTheme.COrange, () => ShowTab(_tabActivity, _body));
            _tabTeam     = MakeTab(S._("timetracker.tab_team"),     MeToolsTheme.CPetrol,  () => ShowTab(_tabTeam, _panTeam));
            _tabMine     = MakeTab(S._("timetracker.tab_mine"),     MeToolsTheme.CGreen,   () => ShowTab(_tabMine, _panMine));
            var tabSp = new StackPanel { Orientation = Orientation.Horizontal };
            tabSp.Children.Add(_tabActivity);
            tabSp.Children.Add(_tabTeam);
            tabSp.Children.Add(_tabMine);
            var tabBar = new Border
            {
                Background = MeToolsTheme.BrHeader, BorderBrush = MeToolsTheme.BrBorder,
                BorderThickness = new Thickness(0, 0, 0, 1), Padding = new Thickness(4, 0, 0, 0),
                Child = tabSp,
            };
            DockPanel.SetDock(tabBar, Dock.Top);
            RootDock.Children.Add(tabBar);

            // Filters bar -- only relevant to the Activity tab; ShowTab()
            // toggles its visibility alongside the active panel.
            _filterBar = new Border
            {
                Background = MeToolsTheme.BrSurface,
                BorderBrush = MeToolsTheme.BrBorder, BorderThickness = new Thickness(0, 0, 0, 1),
                Padding = new Thickness(14, 10, 14, 10),
            };
            DockPanel.SetDock(_filterBar, Dock.Top);
            var filterSp = new StackPanel();

            var actionRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
            _btnAll      = ToggleBtn(S._("activitylog.filter_all"),      true,  () => SetActionFilter(null));
            _btnAdded    = ToggleBtn(S._("activitylog.filter_added"),    false, () => SetActionFilter(ActivityAction.Added));
            _btnModified = ToggleBtn(S._("activitylog.filter_modified"), false, () => SetActionFilter(ActivityAction.Modified));
            _btnDeleted  = ToggleBtn(S._("activitylog.filter_deleted"),   false, () => SetActionFilter(ActivityAction.Deleted));
            foreach (var b in new[] { _btnAll, _btnAdded, _btnModified, _btnDeleted })
                b.Margin = new Thickness(0, 0, 6, 0);
            actionRow.Children.Add(_btnAll);
            actionRow.Children.Add(_btnAdded);
            actionRow.Children.Add(_btnModified);
            actionRow.Children.Add(_btnDeleted);
            filterSp.Children.Add(actionRow);

            var dateRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
            _btnDateAll   = ToggleBtn(S._("activitylog.date_all"),    true,  () => SetDateFilter(null));
            _btnDateToday = ToggleBtn(S._("activitylog.date_today"),  false, () => SetDateFilter(DateTime.UtcNow.Date));
            _btnDate7     = ToggleBtn(S._("activitylog.date_7days"),  false, () => SetDateFilter(DateTime.UtcNow.Date.AddDays(-7)));
            _btnDate30    = ToggleBtn(S._("activitylog.date_30days"), false, () => SetDateFilter(DateTime.UtcNow.Date.AddDays(-30)));
            foreach (var b in new[] { _btnDateAll, _btnDateToday, _btnDate7, _btnDate30 })
                b.Margin = new Thickness(0, 0, 6, 0);
            dateRow.Children.Add(_btnDateAll);
            dateRow.Children.Add(_btnDateToday);
            dateRow.Children.Add(_btnDate7);
            dateRow.Children.Add(_btnDate30);
            filterSp.Children.Add(dateRow);

            var searchRow = new StackPanel { Orientation = Orientation.Horizontal };
            _userCmb = MeToolsWindowBase.StyledCombo(28, 12);
            _userCmb.Width = 160;
            _userCmb.Margin = new Thickness(0, 0, 8, 0);
            _userCmb.SelectionChanged += (s, e) => RenderList();
            searchRow.Children.Add(_userCmb);

            _searchBox = new TextBox
            {
                Width = 220, Height = 28, FontSize = 12,
                Background = MeToolsTheme.BrInput, Foreground = MeToolsTheme.BrText,
                BorderBrush = MeToolsTheme.BrBorder, BorderThickness = new Thickness(1),
                Padding = new Thickness(6, 0, 6, 0), VerticalContentAlignment = VerticalAlignment.Center,
                ToolTip = S._("activitylog.search_tip"),
            };
            _searchBox.TextChanged += (s, e) => RenderList();
            searchRow.Children.Add(_searchBox);
            filterSp.Children.Add(searchRow);

            _filterBar.Child = filterSp;
            RootDock.Children.Add(_filterBar);

            // Fill area: one scroller shared by all three tab panels,
            // toggled via Visibility -- a multi-tab window built this way
            // (zero layout space for inactive tabs) doesn't need
            // ResizeToFitContent().
            _scroll = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                MaxHeight  = 560,
                Background = MeToolsTheme.BrBg,
            };
            _body    = new StackPanel();
            _panTeam = new StackPanel { Visibility = Visibility.Collapsed };
            _panMine = new StackPanel { Visibility = Visibility.Collapsed };
            var outer = new StackPanel { Margin = new Thickness(14, 12, 14, 12) };
            outer.Children.Add(_body);
            outer.Children.Add(_panTeam);
            outer.Children.Add(_panMine);
            _scroll.Content = outer;
            RootDock.Children.Add(_scroll);

            ShowTab(_tabActivity, _body);
        }

        // ── Tab pill helpers ─────────────────────────────────────────────────
        private Border MakeTab(string label, Color tc, Action onClick)
        {
            var pill = new Border
            {
                CornerRadius = new CornerRadius(10), Padding = new Thickness(10, 2, 10, 2),
                Background = new SolidColorBrush(Color.FromArgb(35, tc.R, tc.G, tc.B)),
                Child = new TextBlock
                {
                    Text = label, FontSize = 11, FontWeight = FontWeights.SemiBold,
                    Foreground = MeToolsTheme.BrMuted, VerticalAlignment = VerticalAlignment.Center,
                },
            };
            var tab = new Border
            {
                Padding = new Thickness(8, 6, 8, 6), Cursor = System.Windows.Input.Cursors.Hand,
                Background = MeToolsTheme.BrHeader, BorderThickness = new Thickness(0, 0, 0, 2),
                BorderBrush = Brushes.Transparent, Child = pill, Tag = tc,
            };
            tab.MouseEnter += (s, e) => { if (tab != _activeTab) tab.Background = MeToolsTheme.BrBg; };
            tab.MouseLeave += (s, e) => { if (tab != _activeTab) tab.Background = MeToolsTheme.BrHeader; };
            tab.MouseLeftButtonDown += (s, e) => onClick();
            return tab;
        }

        private void ShowTab(Border tab, StackPanel panel)
        {
            foreach (var t in new[] { _tabActivity, _tabTeam, _tabMine })
            {
                if (t == null) continue;
                t.BorderBrush = Brushes.Transparent; t.Background = MeToolsTheme.BrHeader;
                if (t.Child is Border p)
                {
                    var tc2 = (Color)t.Tag;
                    p.Background = new SolidColorBrush(Color.FromArgb(30, tc2.R, tc2.G, tc2.B));
                    if (p.Child is TextBlock tb2) { tb2.Foreground = MeToolsTheme.BrMuted; tb2.FontWeight = FontWeights.SemiBold; }
                }
            }
            foreach (var p in new[] { _body, _panTeam, _panMine })
                if (p != null) p.Visibility = Visibility.Collapsed;

            _activeTab = tab; _activePanel = panel;
            var ac = (Color)tab.Tag;
            tab.BorderBrush = new SolidColorBrush(ac); tab.Background = MeToolsTheme.BrSurface;
            if (tab.Child is Border apill)
            {
                apill.Background = new SolidColorBrush(ac);
                if (apill.Child is TextBlock atb) { atb.Foreground = new SolidColorBrush(Color.FromRgb(230, 245, 245)); atb.FontWeight = FontWeights.Bold; }
            }
            if (panel != null) panel.Visibility = Visibility.Visible;

            // Filters only apply to the Activity tab.
            if (_filterBar != null) _filterBar.Visibility = (panel == _body) ? Visibility.Visible : Visibility.Collapsed;
        }

        // ── Activity tab ─────────────────────────────────────────────────────
        private void SetActionFilter(ActivityAction? action)
        {
            _actionFilter = action;
            UpdateToggle(_btnAll,      action == null);
            UpdateToggle(_btnAdded,    action == ActivityAction.Added);
            UpdateToggle(_btnModified, action == ActivityAction.Modified);
            UpdateToggle(_btnDeleted,  action == ActivityAction.Deleted);
            RenderList();
        }

        private void SetDateFilter(DateTime? sinceUtc)
        {
            _dateFilterSinceUtc = sinceUtc;
            UpdateToggle(_btnDateAll,   sinceUtc == null);
            UpdateToggle(_btnDateToday, sinceUtc == DateTime.UtcNow.Date);
            UpdateToggle(_btnDate7,     sinceUtc == DateTime.UtcNow.Date.AddDays(-7));
            UpdateToggle(_btnDate30,    sinceUtc == DateTime.UtcNow.Date.AddDays(-30));
            RenderList();
        }

        private void PopulateUserFilter()
        {
            var users = _all.Select(x => x.User).Where(u => !string.IsNullOrWhiteSpace(u))
                .Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(u => u).ToList();

            _userCmb.Items.Clear();
            _userCmb.Items.Add(new ComboBoxItem { Content = S._("activitylog.all_users"), Tag = "" });
            foreach (var u in users)
                _userCmb.Items.Add(new ComboBoxItem { Content = u, Tag = u });
            _userCmb.SelectedIndex = 0;
        }

        private void RenderList()
        {
            if (_body == null) return;

            _body.Children.Clear();
            if (_warningBox != null) _body.Children.Add(_warningBox);

            string userFilter = (_userCmb?.SelectedItem as ComboBoxItem)?.Tag as string ?? "";
            string search = _searchBox?.Text ?? "";
            bool hasSearch = !string.IsNullOrWhiteSpace(search);

            var filtered = _all.AsEnumerable();
            if (_actionFilter.HasValue)
                filtered = filtered.Where(x => x.Action == _actionFilter.Value);
            if (_dateFilterSinceUtc.HasValue)
                filtered = filtered.Where(x => x.TimestampUtc >= _dateFilterSinceUtc.Value);
            if (!string.IsNullOrEmpty(userFilter))
                filtered = filtered.Where(x => string.Equals(x.User, userFilter, StringComparison.OrdinalIgnoreCase));
            if (hasSearch)
                filtered = filtered.Where(x =>
                    (x.Category ?? "").IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0 ||
                    (x.FamilyName ?? "").IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0 ||
                    (x.TypeName ?? "").IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0 ||
                    (x.ElementId ?? "").IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0);

            var sorted = filtered.OrderByDescending(x => x.TimestampUtc).Take(500).ToList();

            if (sorted.Count == 0)
            {
                _body.Children.Add(new TextBlock
                {
                    Text = S._("activitylog.no_matching"), FontSize = 12, Foreground = MeToolsTheme.BrMuted,
                    HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 30, 0, 0),
                });
                return;
            }

            foreach (var entry in sorted)
                _body.Children.Add(BuildCard(entry));
        }

        private Border BuildCard(ActivityLogEntry entry)
        {
            var actionColor =
                entry.Action == ActivityAction.Added    ? MeToolsTheme.CGreen :
                entry.Action == ActivityAction.Deleted  ? MeToolsTheme.CRed   :
                                                           MeToolsTheme.COrange;

            var outer = new StackPanel();

            var line1 = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 3) };
            var badge = new Border
            {
                CornerRadius = new CornerRadius(9), Padding = new Thickness(7, 1, 7, 1),
                Background = new SolidColorBrush(Color.FromArgb(30, actionColor.R, actionColor.G, actionColor.B)),
                BorderBrush = new SolidColorBrush(actionColor), BorderThickness = new Thickness(1),
                Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center,
                Child = new TextBlock
                {
                    Text = ActionLabel(entry.Action), FontSize = 10, FontWeight = FontWeights.SemiBold,
                    Foreground = new SolidColorBrush(actionColor),
                },
            };
            line1.Children.Add(badge);
            line1.Children.Add(new TextBlock
            {
                Text = string.IsNullOrEmpty(entry.Category) ? S._("activitylog.unknown_category") : entry.Category,
                FontSize = 12, FontWeight = FontWeights.SemiBold, Foreground = MeToolsTheme.BrText,
                VerticalAlignment = VerticalAlignment.Center,
            });
            string famType = string.Join(" - ", new[] { entry.FamilyName, entry.TypeName }.Where(s => !string.IsNullOrWhiteSpace(s)));
            if (!string.IsNullOrEmpty(famType))
                line1.Children.Add(new TextBlock
                {
                    Text = "  " + famType, FontSize = 12, Foreground = MeToolsTheme.BrMuted,
                    VerticalAlignment = VerticalAlignment.Center,
                });
            outer.Children.Add(line1);

            var detailParts = new List<string>
            {
                entry.TimestampLocal.ToString("yyyy-MM-dd HH:mm"),
                string.IsNullOrEmpty(entry.User) ? S._("activitylog.unknown_user") : entry.User,
            };
            if (!string.IsNullOrEmpty(entry.LevelName)) detailParts.Add(entry.LevelName);
            detailParts.Add(S._("activitylog.id_prefix") + entry.ElementId);
            if (!string.IsNullOrEmpty(entry.TransactionNames)) detailParts.Add(entry.TransactionNames);

            var line2 = new StackPanel { Orientation = Orientation.Horizontal };
            line2.Children.Add(new TextBlock
            {
                Text = string.Join("  •  ", detailParts),
                FontSize = 10, Foreground = MeToolsTheme.BrMuted, TextWrapping = TextWrapping.Wrap,
                VerticalAlignment = VerticalAlignment.Center,
            });
            if (!string.IsNullOrEmpty(entry.LevelId))
            {
                var goBtn = new Button
                {
                    Content = S._("activitylog.go_to_level"), FontSize = 9, Height = 18,
                    Padding = new Thickness(6, 0, 6, 0), Margin = new Thickness(8, 0, 0, 0),
                    Background = MeToolsTheme.BrBtnBg, Foreground = MeToolsTheme.BrPetrol,
                    BorderBrush = MeToolsTheme.BrPetrol, BorderThickness = new Thickness(1),
                    Cursor = System.Windows.Input.Cursors.Hand,
                    Template = RoundedBtnTemplate(),
                };
                var capturedLevelId = entry.LevelId;
                goBtn.Click += (s, e) =>
                {
                    StatusLeft.Text = S._("activitylog.switching_level");
                    _navHandler.TargetLevelId = capturedLevelId;
                    _navEvt.Raise();
                };
                line2.Children.Add(goBtn);
            }
            outer.Children.Add(line2);

            return new Border
            {
                BorderBrush = MeToolsTheme.BrBorder, BorderThickness = new Thickness(0, 0, 0, 1),
                Padding = new Thickness(2, 8, 2, 8),
                Child = outer,
            };
        }

        private static string ActionLabel(ActivityAction action)
        {
            switch (action)
            {
                case ActivityAction.Added:    return S._("activitylog.action_added");
                case ActivityAction.Deleted:  return S._("activitylog.action_deleted");
                default:                      return S._("activitylog.action_modified");
            }
        }

        private void ExportCsv()
        {
            try
            {
                var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "METools");
                Directory.CreateDirectory(dir);
                var path = Path.Combine(dir, "activity_log_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".csv");

                var sb = new StringBuilder();
                sb.AppendLine("TimestampLocal,User,Action,Category,FamilyName,TypeName,Level,ElementId,TransactionNames");
                foreach (var e in _all.OrderByDescending(x => x.TimestampUtc))
                {
                    sb.AppendLine(string.Join(",", new[]
                    {
                        Csv(e.TimestampLocal.ToString("yyyy-MM-dd HH:mm:ss")),
                        Csv(e.User), Csv(e.Action.ToString()), Csv(e.Category),
                        Csv(e.FamilyName), Csv(e.TypeName), Csv(e.LevelName),
                        Csv(e.ElementId), Csv(e.TransactionNames),
                    }));
                }

                File.WriteAllText(path, sb.ToString(), new UTF8Encoding(true));
                StatusLeft.Text = string.Format(S._("activitylog.exported"), Path.GetFileName(path));
            }
            catch (Exception ex)
            {
                StatusLeft.Text = string.Format(S._("activitylog.export_failed"), ex.Message);
            }
        }

        // ── Team Totals / My Sessions tabs (ported from the standalone Time
        // Tracker window; logic unchanged, just living here now) ────────────

        private bool FolderNotConfigured() =>
            string.IsNullOrWhiteSpace(METools.Comments.CommentsStorage.GetSharedFolder());

        private void RenderTeam()
        {
            if (_panTeam == null) return;
            _panTeam.Children.Clear();
            if (FolderNotConfigured()) _panTeam.Children.Add(InfoBox(S._("timetracker.no_folder_warning")));

            if (_ttAll.Count == 0)
            {
                _panTeam.Children.Add(NoTimeDataText());
                return;
            }

            var totals = _ttAll
                .GroupBy(x => string.IsNullOrWhiteSpace(x.User) ? S._("timetracker.unknown_user") : x.User,
                          StringComparer.OrdinalIgnoreCase)
                .Select(g => new
                {
                    User          = g.Key,
                    TotalSeconds  = g.Sum(x => x.DurationSeconds),
                    SessionCount  = g.Count(),
                    LastActiveUtc = g.Max(x => x.EndUtc),
                })
                .OrderByDescending(x => x.TotalSeconds)
                .ToList();

            _panTeam.Children.Add(TeamHeaderRow());
            foreach (var row in totals)
            {
                bool isMe = string.Equals(row.User, _currentUser, StringComparison.OrdinalIgnoreCase);
                _panTeam.Children.Add(TeamRow(row.User, row.TotalSeconds, row.SessionCount, row.LastActiveUtc.ToLocalTime(), isMe));
            }
        }

        private Border TeamHeaderRow()
        {
            var g = TeamRowGrid();
            g.Children.Add(HeaderCell(S._("timetracker.col_user"), 0));
            g.Children.Add(HeaderCell(S._("timetracker.col_total"), 1));
            g.Children.Add(HeaderCell(S._("timetracker.col_sessions"), 2));
            g.Children.Add(HeaderCell(S._("timetracker.col_last_active"), 3));
            return new Border
            {
                BorderBrush = MeToolsTheme.BrBorder, BorderThickness = new Thickness(0, 0, 0, 1),
                Padding = new Thickness(2, 0, 2, 6), Margin = new Thickness(0, 0, 0, 4),
                Child = g,
            };
        }

        private static TextBlock HeaderCell(string text, int col)
        {
            var tb = new TextBlock
            {
                Text = text, FontSize = 11, FontWeight = FontWeights.SemiBold,
                Foreground = MeToolsTheme.BrSecText, VerticalAlignment = VerticalAlignment.Center,
            };
            Grid.SetColumn(tb, col);
            return tb;
        }

        private static Grid TeamRowGrid()
        {
            var g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(90) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(70) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(120) });
            return g;
        }

        private Border TeamRow(string user, double totalSeconds, int sessionCount, DateTime lastActiveLocal, bool isMe)
        {
            var g = TeamRowGrid();
            g.Margin = new Thickness(0, 6, 0, 6);

            var userText = new TextBlock
            {
                Text = user, FontSize = 12,
                FontWeight = isMe ? FontWeights.Bold : FontWeights.Normal,
                Foreground = isMe ? MeToolsTheme.BrPetrol : MeToolsTheme.BrText,
                VerticalAlignment = VerticalAlignment.Center,
            };
            Grid.SetColumn(userText, 0);

            var totalText = new TextBlock
            {
                Text = FormatDuration(totalSeconds), FontSize = 12, FontWeight = FontWeights.Bold,
                Foreground = MeToolsTheme.BrText, VerticalAlignment = VerticalAlignment.Center,
            };
            Grid.SetColumn(totalText, 1);

            var countText = new TextBlock
            {
                Text = sessionCount.ToString(), FontSize = 12, Foreground = MeToolsTheme.BrMuted,
                VerticalAlignment = VerticalAlignment.Center,
            };
            Grid.SetColumn(countText, 2);

            var lastText = new TextBlock
            {
                Text = lastActiveLocal.ToString("yyyy-MM-dd HH:mm"), FontSize = 11, Foreground = MeToolsTheme.BrMuted,
                VerticalAlignment = VerticalAlignment.Center,
            };
            Grid.SetColumn(lastText, 3);

            g.Children.Add(userText); g.Children.Add(totalText); g.Children.Add(countText); g.Children.Add(lastText);

            return new Border
            {
                BorderBrush = MeToolsTheme.BrBorder, BorderThickness = new Thickness(0, 0, 0, 1),
                Padding = new Thickness(2, 0, 2, 0), Child = g,
            };
        }

        private void RenderMine()
        {
            if (_panMine == null) return;
            _panMine.Children.Clear();
            if (FolderNotConfigured()) _panMine.Children.Add(InfoBox(S._("timetracker.no_folder_warning")));

            // Answers "how do I even start it" directly -- confirms tracking
            // is already running for the document that's open right now,
            // without waiting for it to close first.
            if (_liveSessionStartUtc.HasValue)
                _panMine.Children.Add(LiveSessionBanner(_liveSessionStartUtc.Value));

            var mine = _ttAll
                .Where(x => string.Equals(x.User, _currentUser, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(x => x.StartUtc)
                .ToList();

            if (mine.Count == 0)
            {
                _panMine.Children.Add(NoTimeDataText());
                return;
            }

            var days = mine
                .GroupBy(x => x.StartLocal.Date)
                .OrderByDescending(g => g.Key)
                .ToList();

            bool first = true;
            foreach (var day in days)
            {
                _panMine.Children.Add(DayGroup(day.Key, day.ToList(), expandedByDefault: first));
                first = false;
            }
        }

        private Border LiveSessionBanner(DateTime startUtc)
        {
            var elapsed = DateTime.UtcNow - startUtc;
            var c = MeToolsTheme.CGreen;
            return new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(30, c.R, c.G, c.B)),
                BorderBrush = new SolidColorBrush(c), BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(4), Padding = new Thickness(10, 7, 10, 7),
                Margin = new Thickness(0, 0, 0, 10),
                Child = new TextBlock
                {
                    Text = string.Format(S._("timetracker.live_session"), FormatDuration(Math.Max(0, elapsed.TotalSeconds))),
                    FontSize = 11.5, FontWeight = FontWeights.SemiBold, Foreground = new SolidColorBrush(c),
                    TextWrapping = TextWrapping.Wrap,
                },
            };
        }

        private Border DayGroup(DateTime date, List<METools.TimeTracker.TimeSessionEntry> sessions, bool expandedByDefault)
        {
            double dayTotal = sessions.Sum(s => s.DurationSeconds);

            var sessionList = new StackPanel
            {
                Margin = new Thickness(0, 6, 0, 4),
                Visibility = expandedByDefault ? Visibility.Visible : Visibility.Collapsed,
            };
            foreach (var s in sessions)
                sessionList.Children.Add(SessionRow(s));

            var chevron = new TextBlock
            {
                Text = expandedByDefault ? "\u25BE" : "\u25B8", FontSize = 11,
                Foreground = MeToolsTheme.BrMuted, Width = 16, VerticalAlignment = VerticalAlignment.Center,
            };

            var dateText = new TextBlock
            {
                Text = date.ToString("dddd, d MMMM yyyy"), FontSize = 12, FontWeight = FontWeights.SemiBold,
                Foreground = MeToolsTheme.BrText, VerticalAlignment = VerticalAlignment.Center,
            };

            var totalText = new TextBlock
            {
                Text = string.Format(S._("timetracker.daily_total"), FormatDuration(dayTotal)),
                FontSize = 11, FontWeight = FontWeights.Bold, Foreground = MeToolsTheme.BrPetrol,
                VerticalAlignment = VerticalAlignment.Center,
            };

            var headerGrid = new Grid { Margin = new Thickness(4, 6, 4, 6) };
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var left = new StackPanel { Orientation = Orientation.Horizontal };
            left.Children.Add(chevron);
            left.Children.Add(dateText);
            Grid.SetColumn(left, 0);
            Grid.SetColumn(totalText, 2);
            headerGrid.Children.Add(left);
            headerGrid.Children.Add(totalText);

            var header = new Border
            {
                Background = MeToolsTheme.BrHeader, CornerRadius = new CornerRadius(4),
                Cursor = System.Windows.Input.Cursors.Hand, Child = headerGrid,
            };
            header.MouseLeftButtonDown += (s, e) =>
            {
                bool nowVisible = sessionList.Visibility != Visibility.Visible;
                sessionList.Visibility = nowVisible ? Visibility.Visible : Visibility.Collapsed;
                chevron.Text = nowVisible ? "\u25BE" : "\u25B8";
            };

            var outer = new StackPanel { Margin = new Thickness(0, 0, 0, 8) };
            outer.Children.Add(header);
            outer.Children.Add(sessionList);
            return new Border { Child = outer };
        }

        private Grid SessionRow(METools.TimeTracker.TimeSessionEntry s)
        {
            var g = new Grid { Margin = new Thickness(20, 2, 4, 2) };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            string range = $"{s.StartLocal:HH:mm} \u2013 {s.EndLocal:HH:mm}";
            if (s.Recovered) range += "  " + S._("timetracker.recovered_tag");

            var rangeText = new TextBlock
            {
                Text = range, FontSize = 11.5,
                Foreground = s.Recovered ? MeToolsTheme.BrMuted : MeToolsTheme.BrText,
                FontStyle  = s.Recovered ? FontStyles.Italic : FontStyles.Normal,
                VerticalAlignment = VerticalAlignment.Center,
            };
            Grid.SetColumn(rangeText, 0);

            var durText = new TextBlock
            {
                Text = FormatDuration(s.DurationSeconds), FontSize = 11.5, FontWeight = FontWeights.SemiBold,
                Foreground = MeToolsTheme.BrMuted, VerticalAlignment = VerticalAlignment.Center,
            };
            Grid.SetColumn(durText, 1);

            g.Children.Add(rangeText); g.Children.Add(durText);
            return g;
        }

        private TextBlock NoTimeDataText() => new TextBlock
        {
            Text = S._("timetracker.no_data"), FontSize = 12, Foreground = MeToolsTheme.BrMuted,
            HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 30, 0, 0),
        };

        // Rounds to the nearest minute for display -- sessions are only ever
        // logged at 30s or longer (see TimeTrackerWatcher.MIN_SESSION_SECONDS),
        // so Math.Max(1, ...) guarantees a 30-45s session still reads "1m"
        // rather than rounding down to "0m" via banker's rounding.
        private static string FormatDuration(double totalSeconds)
        {
            int totalMinutes = totalSeconds > 0
                ? Math.Max(1, (int)Math.Round(totalSeconds / 60.0, MidpointRounding.AwayFromZero))
                : 0;
            int h = totalMinutes / 60;
            int m = totalMinutes % 60;
            return h > 0
                ? string.Format(S._("timetracker.duration_hm"), h, m)
                : string.Format(S._("timetracker.duration_m"), m);
        }

        private void ExportTimeTrackerCsv()
        {
            try
            {
                var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "METools");
                Directory.CreateDirectory(dir);
                var path = Path.Combine(dir, "time_tracker_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".csv");

                var sb = new StringBuilder();
                sb.AppendLine("StartLocal,EndLocal,DurationMinutes,User,Recovered");
                foreach (var e in _ttAll.OrderByDescending(x => x.StartUtc))
                {
                    sb.AppendLine(string.Join(",", new[]
                    {
                        Csv(e.StartLocal.ToString("yyyy-MM-dd HH:mm:ss")),
                        Csv(e.EndLocal.ToString("yyyy-MM-dd HH:mm:ss")),
                        Csv(Math.Round(e.DurationSeconds / 60.0, 1).ToString(System.Globalization.CultureInfo.InvariantCulture)),
                        Csv(e.User),
                        Csv(e.Recovered ? "yes" : "no"),
                    }));
                }

                File.WriteAllText(path, sb.ToString(), new UTF8Encoding(true));
                StatusLeft.Text = string.Format(S._("timetracker.exported"), Path.GetFileName(path));
            }
            catch (Exception ex)
            {
                StatusLeft.Text = string.Format(S._("timetracker.export_failed"), ex.Message);
            }
        }

        private static string Csv(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return s.Contains(",") || s.Contains("\"")
                ? "\"" + s.Replace("\"", "\"\"") + "\""
                : s;
        }
    }
}

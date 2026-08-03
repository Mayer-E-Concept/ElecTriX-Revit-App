// TimeTrackerWindow.cs -- ME-Tools | Time Tracker
// Mayer E-Concept SRL
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Autodesk.Revit.UI;
using Button = System.Windows.Controls.Button;
using Color  = System.Windows.Media.Color;

namespace METools.TimeTracker
{
    public class TimeTrackerWindow : MeToolsWindowBase
    {
        private readonly ExternalEvent               _evt;
        private readonly TimeTrackerRefreshHandler    _handler;
        private readonly string                       _currentUser;
        private List<TimeSessionEntry> _all = new List<TimeSessionEntry>();

        private Border _tabTeam, _tabMine;
        private StackPanel _panTeam, _panMine;
        private Border _activeTab;
        private StackPanel _activePanel;

        protected override string AppKey => "TimeTracker";

        // Recomputed on every render rather than cached in a single field --
        // the warning (when shown) needs its own fresh Border in each of the
        // two tab panels, since one WPF element can't be the child of two
        // panels at once. Cheap enough to just re-check the setting live.
        private bool FolderNotConfigured() =>
            string.IsNullOrWhiteSpace(METools.Comments.CommentsStorage.GetSharedFolder());

        public TimeTrackerWindow(List<TimeSessionEntry> entries, string warning, string currentUser,
                                  ExternalEvent evt, TimeTrackerRefreshHandler handler)
        {
            _evt         = evt;
            _handler     = handler;
            _currentUser = currentUser ?? "";

            _handler.OnResult = (result, w) => Dispatcher.Invoke(() =>
            {
                _all = result ?? new List<TimeSessionEntry>();
                RenderTeam();
                RenderMine();
                StatusLeft.Text = string.IsNullOrEmpty(w) ? SummaryText() : w;
            });

            S.SetLanguage(SettingsStore.Language ?? "en");
            InitWindow(S._("timetracker.title"), 560);
            Build();

            _all = entries ?? new List<TimeSessionEntry>();

            RenderTeam();
            RenderMine();
            StatusLeft.Text = string.IsNullOrEmpty(warning) ? SummaryText() : warning;
        }

        private string SummaryText() => string.Format(S._("timetracker.entries_count"), _all.Count);

        // ── Build ───────────────────────────────────────────────────────────
        private void Build()
        {
            BuildStatusBar(S._("timetracker.loading"));

            // Footer FIRST (Dock.Bottom before the fill element) -- see
            // MeToolsWindowBase's DockPanel fill-order note.
            var footer = new Border
            {
                Background = MeToolsTheme.BrFooter,
                BorderBrush = MeToolsTheme.BrBorder, BorderThickness = new Thickness(0, 1, 0, 0),
                Padding = new Thickness(14, 10, 14, 10),
            };
            DockPanel.SetDock(footer, Dock.Bottom);
            var footerRow = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            var exportBtn  = FooterBtn(S._("timetracker.export_csv"), primary: false, onClick: ExportCsv);
            var refreshBtn = FooterBtn(S._("timetracker.refresh"), primary: true, onClick: () =>
            {
                StatusLeft.Text = S._("timetracker.refreshing");
                _evt.Raise();
            });
            exportBtn.Margin = new Thickness(0, 0, 8, 0);
            footerRow.Children.Add(exportBtn);
            footerRow.Children.Add(refreshBtn);
            footer.Child = footerRow;
            RootDock.Children.Add(footer);

            // Tab bar (Dock.Top), same pill style as Circuit Tagger's.
            _panTeam = new StackPanel { Visibility = System.Windows.Visibility.Collapsed };
            _panMine = new StackPanel { Visibility = System.Windows.Visibility.Collapsed };

            _tabTeam = MakeTab(S._("timetracker.tab_team"), MeToolsTheme.CPetrol, () => ShowTab(_tabTeam, _panTeam));
            _tabMine = MakeTab(S._("timetracker.tab_mine"), MeToolsTheme.COrange, () => ShowTab(_tabMine, _panMine));
            var tabSp = new StackPanel { Orientation = Orientation.Horizontal };
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

            // Fill area: one scroller, both tab panels inside, toggled via
            // Visibility -- a multi-tab window built this way (zero layout
            // space for the inactive tab) doesn't need ResizeToFitContent().
            var scroll = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                MaxHeight  = 560,
                Background = MeToolsTheme.BrBg,
            };
            var outer = new StackPanel { Margin = new Thickness(14, 12, 14, 12) };
            outer.Children.Add(_panTeam);
            outer.Children.Add(_panMine);
            scroll.Content = outer;
            RootDock.Children.Add(scroll);

            ShowTab(_tabTeam, _panTeam);
        }

        // ── Tab pill helpers (same visual pattern as Circuit Tagger's tabs) ──
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
            foreach (var t in new[] { _tabTeam, _tabMine })
            {
                if (t == null) continue;
                t.BorderBrush = Brushes.Transparent; t.Background = MeToolsTheme.BrHeader;
                if (t.Child is Border p)
                {
                    var tc2 = (Color)t.Tag;
                    p.Background = new SolidColorBrush(Color.FromArgb(30, tc2.R, tc2.G, tc2.B));
                    if (p.Child is TextBlock tb2) tb2.Foreground = MeToolsTheme.BrMuted;
                }
            }
            foreach (var p in new[] { _panTeam, _panMine })
                if (p != null) p.Visibility = System.Windows.Visibility.Collapsed;

            _activeTab = tab; _activePanel = panel;
            var ac = (Color)tab.Tag;
            tab.BorderBrush = new SolidColorBrush(ac); tab.Background = MeToolsTheme.BrSurface;
            if (tab.Child is Border apill)
            {
                apill.Background = new SolidColorBrush(ac);
                if (apill.Child is TextBlock atb) { atb.Foreground = new SolidColorBrush(Color.FromRgb(230, 245, 245)); atb.FontWeight = FontWeights.Bold; }
            }
            if (panel != null) panel.Visibility = System.Windows.Visibility.Visible;
        }

        // ── Tab 1: Team Totals ───────────────────────────────────────────────
        private void RenderTeam()
        {
            if (_panTeam == null) return;
            _panTeam.Children.Clear();
            if (FolderNotConfigured()) _panTeam.Children.Add(InfoBox(S._("timetracker.no_folder_warning")));

            if (_all.Count == 0)
            {
                _panTeam.Children.Add(NoDataText());
                return;
            }

            var totals = _all
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

        // ── Tab 2: My Sessions (daily totals, expandable) ────────────────────
        private void RenderMine()
        {
            if (_panMine == null) return;
            _panMine.Children.Clear();
            if (FolderNotConfigured()) _panMine.Children.Add(InfoBox(S._("timetracker.no_folder_warning")));

            var mine = _all
                .Where(x => string.Equals(x.User, _currentUser, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(x => x.StartUtc)
                .ToList();

            if (mine.Count == 0)
            {
                _panMine.Children.Add(NoDataText());
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

        private Border DayGroup(DateTime date, List<TimeSessionEntry> sessions, bool expandedByDefault)
        {
            double dayTotal = sessions.Sum(s => s.DurationSeconds);

            var sessionList = new StackPanel
            {
                Margin = new Thickness(0, 6, 0, 4),
                Visibility = expandedByDefault ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed,
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
                bool nowVisible = sessionList.Visibility != System.Windows.Visibility.Visible;
                sessionList.Visibility = nowVisible ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;
                chevron.Text = nowVisible ? "\u25BE" : "\u25B8";
            };

            var outer = new StackPanel { Margin = new Thickness(0, 0, 0, 8) };
            outer.Children.Add(header);
            outer.Children.Add(sessionList);
            return new Border { Child = outer };
        }

        private Grid SessionRow(TimeSessionEntry s)
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

        private TextBlock NoDataText() => new TextBlock
        {
            Text = S._("timetracker.no_data"), FontSize = 12, Foreground = MeToolsTheme.BrMuted,
            HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 30, 0, 0),
        };

        // ── Shared formatting ────────────────────────────────────────────────
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

        // ── Export ───────────────────────────────────────────────────────────
        private void ExportCsv()
        {
            try
            {
                var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "METools");
                Directory.CreateDirectory(dir);
                var path = Path.Combine(dir, "time_tracker_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".csv");

                var sb = new StringBuilder();
                sb.AppendLine("StartLocal,EndLocal,DurationMinutes,User,Recovered");
                foreach (var e in _all.OrderByDescending(x => x.StartUtc))
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

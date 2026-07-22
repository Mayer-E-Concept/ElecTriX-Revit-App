// ActivityLogWindow.cs -- ME-Tools | Activity Log
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
using Button     = System.Windows.Controls.Button;
using Color      = System.Windows.Media.Color;
using ComboBox   = System.Windows.Controls.ComboBox;
using TextBox    = System.Windows.Controls.TextBox;

namespace METools.ActivityLog
{
    public class ActivityLogWindow : MeToolsWindowBase
    {
        private readonly ExternalEvent            _evt;
        private readonly ActivityLogRefreshHandler _handler;
        private readonly ExternalEvent             _navEvt;
        private readonly ActivityLogNavigateHandler _navHandler;
        private List<ActivityLogEntry> _all = new List<ActivityLogEntry>();

        private ComboBox _userCmb;
        private Button _btnAll, _btnAdded, _btnModified, _btnDeleted;
        private ActivityAction? _actionFilter; // null = All
        private TextBox _searchBox;
        private StackPanel _body;
        private ScrollViewer _scroll;
        private Border _warningBox; // shown above the list when the shared folder isn't configured

        protected override string AppKey => "ActivityLog";

        public ActivityLogWindow(List<ActivityLogEntry> entries, string warning, ExternalEvent evt, ActivityLogRefreshHandler handler,
                                  ExternalEvent navEvt, ActivityLogNavigateHandler navHandler)
        {
            _evt     = evt;
            _handler = handler;
            _navEvt     = navEvt;
            _navHandler = navHandler;
            _navHandler.OnDone = (success, msg) => Dispatcher.Invoke(() =>
            {
                StatusLeft.Text = success ? "Switched to that level's floor plan." : ("Couldn't go there: " + msg);
            });
            _handler.OnResult = (result, w) => Dispatcher.Invoke(() =>
            {
                _all = result ?? new List<ActivityLogEntry>();
                PopulateUserFilter();
                RenderList();
                StatusLeft.Text = string.IsNullOrEmpty(w) ? $"{_all.Count} entries." : w;
            });

            InitWindow("ElectriX -- Activity Log", 620);
            Build();

            _all = entries ?? new List<ActivityLogEntry>();

            if (string.IsNullOrWhiteSpace(METools.Comments.CommentsStorage.GetSharedFolder()))
            {
                _warningBox = InfoBox(
                    "No shared folder configured yet -- Activity Log uses the same shared folder as Comments. " +
                    "Set it once from the Comments tool's own Settings, and both features start working from then on.");
            }

            PopulateUserFilter();
            RenderList();
            StatusLeft.Text = string.IsNullOrEmpty(warning) ? $"{_all.Count} entries." : warning;
        }

        private void Build()
        {
            BuildStatusBar("Loading...");

            // Footer FIRST (Dock.Bottom before the fill element).
            var footer = new Border
            {
                Background = MeToolsTheme.BrFooter,
                BorderBrush = MeToolsTheme.BrBorder, BorderThickness = new Thickness(0, 1, 0, 0),
                Padding = new Thickness(14, 10, 14, 10),
            };
            DockPanel.SetDock(footer, Dock.Bottom);
            var footerRow = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            var exportBtn = FooterBtn("Export CSV", primary: false, onClick: ExportCsv);
            var refreshBtn = FooterBtn("Refresh", primary: true, onClick: () =>
            {
                StatusLeft.Text = "Refreshing...";
                _evt.Raise();
            });
            exportBtn.Margin = new Thickness(0, 0, 8, 0);
            footerRow.Children.Add(exportBtn);
            footerRow.Children.Add(refreshBtn);
            footer.Child = footerRow;
            RootDock.Children.Add(footer);

            // Filters bar (also docked, above the footer, below the fill area's top).
            var filterBar = new Border
            {
                Background = MeToolsTheme.BrSurface,
                BorderBrush = MeToolsTheme.BrBorder, BorderThickness = new Thickness(0, 0, 0, 1),
                Padding = new Thickness(14, 10, 14, 10),
            };
            DockPanel.SetDock(filterBar, Dock.Top);
            var filterSp = new StackPanel();

            var actionRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
            _btnAll      = ToggleBtn("All",      true,  () => SetActionFilter(null));
            _btnAdded    = ToggleBtn("Added",    false, () => SetActionFilter(ActivityAction.Added));
            _btnModified = ToggleBtn("Modified", false, () => SetActionFilter(ActivityAction.Modified));
            _btnDeleted  = ToggleBtn("Deleted",   false, () => SetActionFilter(ActivityAction.Deleted));
            foreach (var b in new[] { _btnAll, _btnAdded, _btnModified, _btnDeleted })
                b.Margin = new Thickness(0, 0, 6, 0);
            actionRow.Children.Add(_btnAll);
            actionRow.Children.Add(_btnAdded);
            actionRow.Children.Add(_btnModified);
            actionRow.Children.Add(_btnDeleted);
            filterSp.Children.Add(actionRow);

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
                ToolTip = "Search category / family / type / element id...",
            };
            _searchBox.TextChanged += (s, e) => RenderList();
            searchRow.Children.Add(_searchBox);
            filterSp.Children.Add(searchRow);

            filterBar.Child = filterSp;
            RootDock.Children.Add(filterBar);

            _scroll = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                MaxHeight  = 560,
                Background = MeToolsTheme.BrBg,
            };
            _body = new StackPanel { Margin = new Thickness(14, 12, 14, 12) };
            _scroll.Content = _body;
            RootDock.Children.Add(_scroll);
        }

        private void SetActionFilter(ActivityAction? action)
        {
            _actionFilter = action;
            UpdateToggle(_btnAll,      action == null);
            UpdateToggle(_btnAdded,    action == ActivityAction.Added);
            UpdateToggle(_btnModified, action == ActivityAction.Modified);
            UpdateToggle(_btnDeleted,  action == ActivityAction.Deleted);
            RenderList();
        }

        private void PopulateUserFilter()
        {
            var users = _all.Select(x => x.User).Where(u => !string.IsNullOrWhiteSpace(u))
                .Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(u => u).ToList();

            _userCmb.Items.Clear();
            _userCmb.Items.Add(new ComboBoxItem { Content = "-- All users --", Tag = "" });
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
                    Text = "No matching activity.", FontSize = 12, Foreground = MeToolsTheme.BrMuted,
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
                    Text = entry.Action.ToString(), FontSize = 10, FontWeight = FontWeights.SemiBold,
                    Foreground = new SolidColorBrush(actionColor),
                },
            };
            line1.Children.Add(badge);
            line1.Children.Add(new TextBlock
            {
                Text = string.IsNullOrEmpty(entry.Category) ? "(unknown category)" : entry.Category,
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
                string.IsNullOrEmpty(entry.User) ? "(unknown user)" : entry.User,
            };
            if (!string.IsNullOrEmpty(entry.LevelName)) detailParts.Add(entry.LevelName);
            detailParts.Add("ID " + entry.ElementId);
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
                    Content = "Go to Level", FontSize = 9, Height = 18,
                    Padding = new Thickness(6, 0, 6, 0), Margin = new Thickness(8, 0, 0, 0),
                    Background = MeToolsTheme.BrBtnBg, Foreground = MeToolsTheme.BrPetrol,
                    BorderBrush = MeToolsTheme.BrPetrol, BorderThickness = new Thickness(1),
                    Cursor = System.Windows.Input.Cursors.Hand,
                    Template = RoundedBtnTemplate(),
                };
                var capturedLevelId = entry.LevelId;
                goBtn.Click += (s, e) =>
                {
                    StatusLeft.Text = "Switching level...";
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
                StatusLeft.Text = "Exported: Documents\\METools\\" + Path.GetFileName(path);
            }
            catch (Exception ex)
            {
                StatusLeft.Text = "Export failed: " + ex.Message;
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

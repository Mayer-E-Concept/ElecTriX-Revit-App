// LevelManagerWindow.cs — ME-Tools | Level Manager
// Mayer E-Concept SRL — Pure C# WPF, no XAML
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using Autodesk.Revit.UI;
using Color      = System.Windows.Media.Color;
using ComboBox   = System.Windows.Controls.ComboBox;
using TextBox    = System.Windows.Controls.TextBox;

namespace METools.LevelManager
{
    public class LevelManagerWindow : METools.MeToolsWindowBase
    {
        private readonly ExternalEvent        _extEvent;
        private readonly LevelManagerHandler  _handler;

        private List<LevelRow> _all = new List<LevelRow>();
        private string _groupFilter = "";   // "" = All
        private string _zoneFilter  = "";   // "" = All zones
        private bool   _trueScale   = false;

        // ── UI refs ──────────────────────────────────────────────────────
        private StackPanel _groupBar;
        private ComboBox   _zoneCombo;
        private Button     _btnEven, _btnScale;
        private StackPanel _rowsPanel;
        private TextBlock  _countLabel;
        private Border     _selectedRowBorder;

        private TextBox _tbName, _tbElevation;

        // Stable color per auto-discovered group, cycling through the brand palette.
        private readonly Dictionary<string, Color> _groupColors = new Dictionary<string, Color>();
        private int _colorCursor = 0;

        protected override string AppKey => "LevelManager";

        public LevelManagerWindow(ExternalEvent extEvent, LevelManagerHandler handler)
        {
            _extEvent = extEvent;
            _handler  = handler;
            _handler.OnLoaded = rows => Dispatcher.Invoke(() =>
            {
                _all = rows;
                RebuildGroupBar();
                RebuildZoneCombo();
                RebuildList();
            });
            _handler.OnStatus = msg => Dispatcher.Invoke(() => { if (StatusLeft != null) StatusLeft.Text = msg; });

            InitWindow("Level Manager", width: 580);
            BuildStatusBar("Loading levels…", "Revit 2025");
            BuildUi();
        }

        // ═════════════════════════════════════════════════════════════════
        // LAYOUT
        // ═════════════════════════════════════════════════════════════════
        private void BuildUi()
        {
            var root = new StackPanel { Margin = new Thickness(14, 12, 14, 10) };
            RootDock.Children.Add(root);

            // ── Group filter row ────────────────────────────────────────
            root.Children.Add(Sec("Group"));
            _groupBar = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 10) };
            var groupScroller = new ScrollViewer
            {
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                VerticalScrollBarVisibility   = ScrollBarVisibility.Disabled,
                Content = _groupBar,
                Margin  = new Thickness(0, 0, 0, 10),
            };
            root.Children.Add(groupScroller);

            // ── Zone + spacing mode + refresh ───────────────────────────
            var ctrlRow = new Grid { Margin = new Thickness(0, 0, 0, 10) };
            ctrlRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            ctrlRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(10) });
            ctrlRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            ctrlRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            ctrlRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            _zoneCombo = StyledCombo(28, 12);
            _zoneCombo.MinWidth = 130;
            _zoneCombo.SelectionChanged += (s, e) =>
            {
                if (_zoneCombo.SelectedItem == null) return;
                var tag = (_zoneCombo.SelectedItem as ComboBoxItem)?.Tag as string ?? "";
                _zoneFilter = tag;
                RebuildList();
            };
            Grid.SetColumn(_zoneCombo, 0);
            ctrlRow.Children.Add(_zoneCombo);

            var spacingPanel = new StackPanel { Orientation = Orientation.Horizontal };
            _btnEven  = ToggleBtn("Compact",    !_trueScale, () => SetSpacingMode(false));
            _btnScale = ToggleBtn("True Scale",  _trueScale, () => SetSpacingMode(true));
            _btnScale.Margin = new Thickness(6, 0, 0, 0);
            spacingPanel.Children.Add(_btnEven);
            spacingPanel.Children.Add(_btnScale);
            Grid.SetColumn(spacingPanel, 2);
            ctrlRow.Children.Add(spacingPanel);

            _countLabel = new TextBlock
            {
                FontSize = 11, Foreground = MeToolsTheme.BrMuted,
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Right,
            };
            Grid.SetColumn(_countLabel, 3);
            ctrlRow.Children.Add(_countLabel);

            var refreshBtn = ActionBtn("⟳ Refresh", true, RequestRefresh);
            refreshBtn.Height = 28; refreshBtn.FontSize = 12; refreshBtn.Padding = new Thickness(10, 0, 10, 0);
            Grid.SetColumn(refreshBtn, 4);
            ctrlRow.Children.Add(refreshBtn);

            root.Children.Add(ctrlRow);

            // ── Section view ─────────────────────────────────────────────
            _rowsPanel = new StackPanel();
            var scroller = new ScrollViewer
            {
                Height = 360,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Content = _rowsPanel,
            };
            var scrollerBorder = new Border
            {
                BorderBrush = MeToolsTheme.BrBorder, BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(5), Background = MeToolsTheme.BrSurface,
                Child = scroller, Margin = new Thickness(0, 0, 0, 14),
            };
            root.Children.Add(scrollerBorder);

            // ── Add level panel ─────────────────────────────────────────
            root.Children.Add(Sec("Add Level"));

            var addGrid = new Grid();
            addGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            addGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(10) });
            addGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(110) });
            addGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(6) });
            addGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            addGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(10) });
            addGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            _tbName = new TextBox
            {
                Height = 30, FontSize = 12, VerticalContentAlignment = VerticalAlignment.Center,
                Padding = new Thickness(6, 0, 6, 0),
                Background = MeToolsTheme.BrInput, Foreground = MeToolsTheme.BrText,
                BorderBrush = MeToolsTheme.BrBorder, BorderThickness = new Thickness(1),
                CaretBrush = MeToolsTheme.BrText,
            };
            SetPlaceholder(_tbName, "New level name…");
            Grid.SetColumn(_tbName, 0);
            addGrid.Children.Add(_tbName);

            _tbElevation = Num("0.000");
            _tbElevation.Height = 30;
            Grid.SetColumn(_tbElevation, 2);
            addGrid.Children.Add(_tbElevation);

            var mLabel = new TextBlock
            {
                Text = "m", FontSize = 12, Foreground = MeToolsTheme.BrMuted,
                VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0, 0, 0),
            };
            Grid.SetColumn(mLabel, 4);
            addGrid.Children.Add(mLabel);

            var addBtn = ActionBtn("Add Level", false, OnAddLevel);
            addBtn.MinWidth = 110;
            Grid.SetColumn(addBtn, 6);
            addGrid.Children.Add(addBtn);

            root.Children.Add(addGrid);

            root.Children.Add(new TextBlock
            {
                Text = "Tip: click a level in the list to prefill its elevation +3.000 m as a starting point.",
                FontSize = 10.5, Foreground = MeToolsTheme.BrMuted,
                Margin = new Thickness(2, 6, 0, 0), TextWrapping = TextWrapping.Wrap,
            });
        }

        // ═════════════════════════════════════════════════════════════════
        // FILTER BARS (rebuilt once level data is loaded/refreshed)
        // ═════════════════════════════════════════════════════════════════
        private void RebuildGroupBar()
        {
            _groupBar.Children.Clear();

            var groups = _all.Select(r => r.GroupKey).Distinct()
                .OrderBy(g => string.IsNullOrEmpty(g) ? 1 : 0).ThenBy(g => g)
                .ToList();

            // Keep the currently selected filter if it still exists, else reset to All.
            if (!groups.Contains(_groupFilter)) _groupFilter = "";

            var allBtn = ToggleBtn("All", _groupFilter == "", () => SetGroupFilter(""));
            _groupBar.Children.Add(allBtn);

            foreach (var g in groups)
            {
                if (string.IsNullOrEmpty(g)) continue;
                var label = g;
                var btn = ToggleBtn(label, _groupFilter == g, () => SetGroupFilter(g));
                btn.Margin = new Thickness(6, 0, 0, 0);
                _groupBar.Children.Add(btn);
            }

            if (groups.Contains(""))
            {
                var otherBtn = ToggleBtn("Other", _groupFilter == "__other__", () => SetGroupFilter("__other__"));
                otherBtn.Margin = new Thickness(6, 0, 0, 0);
                _groupBar.Children.Add(otherBtn);
            }
        }

        private void RebuildZoneCombo()
        {
            var zones = _all.Select(r => r.ZoneKey).Where(z => !string.IsNullOrEmpty(z))
                .Distinct().OrderBy(z => z).ToList();

            _zoneCombo.Items.Clear();
            _zoneCombo.Items.Add(new ComboBoxItem { Content = "All Zones", Tag = "" });
            foreach (var z in zones)
                _zoneCombo.Items.Add(new ComboBoxItem { Content = z, Tag = z });

            var match = _zoneCombo.Items.Cast<ComboBoxItem>()
                .FirstOrDefault(i => (i.Tag as string) == _zoneFilter);
            _zoneCombo.SelectedItem = match ?? _zoneCombo.Items[0];
        }

        private void SetGroupFilter(string key)
        {
            _groupFilter = key;
            foreach (Button b in _groupBar.Children.OfType<Button>())
            {
                var label = (b.Content as string) ?? "";
                bool active = (label == "All" && key == "") ||
                              (label == "Other" && key == "__other__") ||
                              (label == key);
                UpdateToggle(b, active);
            }
            RebuildList();
        }

        private void SetSpacingMode(bool trueScale)
        {
            _trueScale = trueScale;
            UpdateToggle(_btnEven, !trueScale);
            UpdateToggle(_btnScale, trueScale);
            RebuildList();
        }

        // ═════════════════════════════════════════════════════════════════
        // SECTION VIEW
        // ═════════════════════════════════════════════════════════════════
        private const double RowHeightEven = 30;
        private const double MinGapScale   = 14;   // px, floor for True Scale mode
        private const double MaxGapScale   = 70;   // px, ceiling so one big gap doesn't dwarf the rest
        private const double PxPerMeter    = 9.0;

        private void RebuildList()
        {
            var filtered = _all.Where(MatchesFilter)
                .OrderByDescending(r => r.ElevationFt) // top of the list = highest level, like a real section
                .ToList();

            _rowsPanel.Children.Clear();
            _selectedRowBorder = null;

            for (int i = 0; i < filtered.Count; i++)
            {
                double h = RowHeightEven;
                if (_trueScale && i > 0)
                {
                    double deltaM = filtered[i - 1].ElevationM - filtered[i].ElevationM;
                    h = Math.Min(MaxGapScale, Math.Max(MinGapScale, deltaM * PxPerMeter));
                }
                _rowsPanel.Children.Add(BuildRow(filtered[i], h));
            }

            _countLabel.Text = filtered.Count == _all.Count
                ? $"{_all.Count} level(s)"
                : $"Showing {filtered.Count} of {_all.Count} level(s)";
        }

        private bool MatchesFilter(LevelRow r)
        {
            bool groupOk = _groupFilter == "" ? true
                : _groupFilter == "__other__" ? string.IsNullOrEmpty(r.GroupKey)
                : r.GroupKey == _groupFilter;
            bool zoneOk = _zoneFilter == "" || r.ZoneKey == _zoneFilter;
            return groupOk && zoneOk;
        }

        private Border BuildRow(LevelRow row, double height)
        {
            var color = ColorForGroup(row.GroupKey);

            var g = new Grid { Height = height };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(4) });   // 0 tick
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(22) });  // 1 bubble
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });     // 2 name + zone
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); // 3 leader
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(100) });  // 4 elevation

            var tick = new Border { Background = new SolidColorBrush(color) };
            Grid.SetColumn(tick, 0);
            g.Children.Add(tick);

            var bubble = new Ellipse
            {
                Width = 10, Height = 10, Stroke = new SolidColorBrush(color), StrokeThickness = 1.5,
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
            };
            Grid.SetColumn(bubble, 1);
            g.Children.Add(bubble);

            var nameRow = new StackPanel
            {
                Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(2, 0, 8, 0),
            };
            nameRow.Children.Add(new TextBlock
            {
                Text = row.Name, FontSize = height < 22 ? 10.5 : 12, FontWeight = FontWeights.SemiBold,
                Foreground = MeToolsTheme.BrText, VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
            });
            if (!string.IsNullOrEmpty(row.ZoneKey))
            {
                nameRow.Children.Add(new Border
                {
                    Background = MeToolsTheme.BrActiveBg, CornerRadius = new CornerRadius(3),
                    Padding = new Thickness(5, 0, 5, 0), Margin = new Thickness(6, 0, 0, 0),
                    VerticalAlignment = VerticalAlignment.Center,
                    Child = new TextBlock
                    {
                        Text = row.ZoneKey, FontSize = 9.5, FontWeight = FontWeights.Bold,
                        Foreground = MeToolsTheme.BrActiveFg, VerticalAlignment = VerticalAlignment.Center,
                    },
                });
            }
            Grid.SetColumn(nameRow, 2);
            g.Children.Add(nameRow);

            var leader = new Border
            {
                Height = 1, Background = MeToolsTheme.BrBorder,
                VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0, 8, 0),
            };
            Grid.SetColumn(leader, 3);
            g.Children.Add(leader);

            var elev = new TextBlock
            {
                Text = FormatElevation(row.ElevationM), FontFamily = new FontFamily("Consolas"),
                FontSize = 11.5, Foreground = MeToolsTheme.BrMuted,
                HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 8, 0),
            };
            Grid.SetColumn(elev, 4);
            g.Children.Add(elev);

            var container = new Border { Child = g, Cursor = Cursors.Hand };
            container.MouseEnter += (s, e) => { if (container != _selectedRowBorder) container.Background = MeToolsTheme.BrRowHov; };
            container.MouseLeave += (s, e) => { if (container != _selectedRowBorder) container.Background = Brushes.Transparent; };
            container.MouseLeftButtonUp += (s, e) => SelectRow(row, container);

            return container;
        }

        private void SelectRow(LevelRow row, Border container)
        {
            if (_selectedRowBorder != null) _selectedRowBorder.Background = Brushes.Transparent;
            container.Background = MeToolsTheme.BrActiveBg;
            _selectedRowBorder = container;

            _tbElevation.Text = (row.ElevationM + 3.0).ToString("0.000", CultureInfo.InvariantCulture);
        }

        private Color ColorForGroup(string groupKey)
        {
            var key = string.IsNullOrEmpty(groupKey) ? "__none__" : groupKey;
            if (_groupColors.TryGetValue(key, out var c)) return c;

            if (key == "__none__")
            {
                c = MeToolsTheme.CMuted;
            }
            else
            {
                var palette = new[]
                {
                    MeToolsTheme.CAccent, MeToolsTheme.COrange, MeToolsTheme.CGreen,
                    MeToolsTheme.CBlue,   MeToolsTheme.CPetrol,
                };
                c = palette[_colorCursor % palette.Length];
                _colorCursor++;
            }
            _groupColors[key] = c;
            return c;
        }

        private static string FormatElevation(double meters)
            => (meters >= 0 ? "+" : "") + meters.ToString("0.000", CultureInfo.InvariantCulture) + " m";

        // ═════════════════════════════════════════════════════════════════
        // ACTIONS
        // ═════════════════════════════════════════════════════════════════
        private void RequestRefresh()
        {
            if (StatusLeft != null) StatusLeft.Text = "Refreshing…";
            _handler.Request = new LevelManagerRequest { Action = LevelManagerAction.Refresh };
            _extEvent.Raise();
        }

        private void OnAddLevel()
        {
            var name = _tbName.Text?.Trim() ?? "";
            if (string.IsNullOrEmpty(name) || name == "New level name…")
            { if (StatusLeft != null) StatusLeft.Text = "Enter a name for the new level."; return; }

            var elevText = (_tbElevation.Text ?? "0").Replace(',', '.');
            if (!double.TryParse(elevText, NumberStyles.Float, CultureInfo.InvariantCulture, out var elevM))
            { if (StatusLeft != null) StatusLeft.Text = "Elevation must be a number (meters)."; return; }

            if (StatusLeft != null) StatusLeft.Text = "Creating level…";
            _handler.Request = new LevelManagerRequest
            {
                Action        = LevelManagerAction.AddLevel,
                NewName       = name,
                NewElevationM = elevM,
            };
            _extEvent.Raise();
        }

        // Simple placeholder behaviour for a plain TextBox (no dedicated control needed).
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

        protected override void OnThemeChanged()
        {
            // Re-apply combo styling and redraw so newly-themed brushes take effect.
            ApplyComboStyle(_zoneCombo);
            RebuildList();
        }
    }
}

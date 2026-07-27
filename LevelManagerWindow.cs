// LevelManagerWindow.cs — ME-Tools | Level & IFC Manager
// Mayer E-Concept SRL — Pure C# WPF, no XAML
//
// Two tabs in one window: "Project Levels" (the original Level Manager --
// unchanged below) and "Import from IFC" (folded in from what used to be a
// separate standalone ribbon tool/window).
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using METools.IfcImport;
using Color      = System.Windows.Media.Color;
using ComboBox   = System.Windows.Controls.ComboBox;
using TextBox    = System.Windows.Controls.TextBox;
using Grid       = System.Windows.Controls.Grid;

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

        // ── IFC import (second tab) ─────────────────────────────────────────
        private readonly UIApplication _uiApp;
        private readonly ExternalEvent _ifcExtEvent;
        private readonly IfcLevelImportHandler _ifcHandler;
        private readonly List<(string DisplayName, string Path)> _ifcDetected;
        private IfcParseResult _ifcParsed;
        private string _ifcFilePath;
        private readonly List<IfcLevelRow> _ifcRows = new List<IfcLevelRow>();
        private StackPanel _ifcSourcePanel, _ifcResultsPanel, _ifcTableList;
        private CheckBox _ifcSelectAllCb;
        private Button _ifcImportBtn;

        private StackPanel _projectLevelsRoot, _ifcRoot;
        private Button _tabProjectBtn, _tabIfcBtn;

        protected override string AppKey => "LevelManager";

        public LevelManagerWindow(
            ExternalEvent extEvent, LevelManagerHandler handler,
            UIApplication uiApp, List<(string DisplayName, string Path)> ifcDetected,
            ExternalEvent ifcExtEvent, IfcLevelImportHandler ifcHandler)
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

            _uiApp       = uiApp;
            _ifcDetected = ifcDetected ?? new List<(string, string)>();
            _ifcExtEvent = ifcExtEvent;
            _ifcHandler  = ifcHandler;

            InitWindow("Level & IFC Manager", width: 580);
            BuildStatusBar("Loading levels…", "Revit 2025/2026");
            BuildUi();

            _ifcHandler.OnDone  = res => Dispatcher.Invoke(() => OnIfcImportDone(res));
            _ifcHandler.OnError = msg => Dispatcher.Invoke(() => { if (StatusLeft != null) StatusLeft.Text = msg; });

            // Exactly one IFC already in the project -> load it immediately.
            if (_ifcDetected.Count == 1) LoadIfcSource(_ifcDetected[0].Path);
        }

        // ═════════════════════════════════════════════════════════════════
        // TOP-LEVEL LAYOUT: tab switcher + the two panels
        // ═════════════════════════════════════════════════════════════════
        private void BuildUi()
        {
            var outer = new StackPanel();
            RootDock.Children.Add(outer);

            outer.Children.Add(BuildModeTabs());

            _projectLevelsRoot = new StackPanel();
            outer.Children.Add(_projectLevelsRoot);
            BuildProjectLevelsUi(_projectLevelsRoot);

            _ifcRoot = new StackPanel { Visibility = Visibility.Collapsed };
            outer.Children.Add(_ifcRoot);
            BuildIfcUi(_ifcRoot);
        }

        private FrameworkElement BuildModeTabs()
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(14, 12, 14, 0) };
            _tabProjectBtn = ToggleBtn("Project Levels", true, () => SwitchMode(false));
            _tabIfcBtn     = ToggleBtn("Import from IFC", false, () => SwitchMode(true));
            _tabIfcBtn.Margin = new Thickness(6, 0, 0, 0);
            row.Children.Add(_tabProjectBtn);
            row.Children.Add(_tabIfcBtn);
            return row;
        }

        private void SwitchMode(bool ifcMode)
        {
            _projectLevelsRoot.Visibility = ifcMode ? Visibility.Collapsed : Visibility.Visible;
            _ifcRoot.Visibility           = ifcMode ? Visibility.Visible   : Visibility.Collapsed;
            UpdateToggle(_tabProjectBtn, !ifcMode);
            UpdateToggle(_tabIfcBtn, ifcMode);
        }

        // ═════════════════════════════════════════════════════════════════
        // PROJECT LEVELS TAB (this is the original Level Manager, unchanged
        // apart from taking its root panel as a parameter instead of adding
        // its own directly to RootDock).
        // ═════════════════════════════════════════════════════════════════
        private void BuildProjectLevelsUi(StackPanel root)
        {
            root.Margin = new Thickness(14, 12, 14, 10);

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

        // ═════════════════════════════════════════════════════════════════
        // IMPORT FROM IFC TAB (folded in from the former standalone
        // IfcLevelImportWindow -- logic unchanged, just renamed/re-homed)
        // ═════════════════════════════════════════════════════════════════
        private void BuildIfcUi(StackPanel sp)
        {
            sp.Margin = new Thickness(14, 12, 14, 10);

            sp.Children.Add(Sec("Source"));
            _ifcSourcePanel = new StackPanel();
            sp.Children.Add(_ifcSourcePanel);

            _ifcResultsPanel = new StackPanel();
            sp.Children.Add(_ifcResultsPanel);

            RebuildIfcSourcePanel();
            RebuildIfcResultsPanel();
        }

        private void RebuildIfcSourcePanel()
        {
            _ifcSourcePanel.Children.Clear();

            if (_ifcFilePath != null)
            {
                var row = new Grid { Margin = new Thickness(0, 0, 0, 14) };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                var infoPanel = new StackPanel();
                infoPanel.Children.Add(new TextBlock { Text = "Currently loaded", FontSize = 10, Foreground = MeToolsTheme.BrMuted });
                infoPanel.Children.Add(new TextBlock
                {
                    Text = System.IO.Path.GetFileName(_ifcFilePath), FontSize = 13, FontWeight = FontWeights.SemiBold,
                    Foreground = MeToolsTheme.BrText, TextWrapping = TextWrapping.Wrap,
                });
                Grid.SetColumn(infoPanel, 0); row.Children.Add(infoPanel);

                var changeBtn = ActionBtn("Change source...", true, OnIfcChangeSourceClicked);
                changeBtn.VerticalAlignment = VerticalAlignment.Center;
                Grid.SetColumn(changeBtn, 1); row.Children.Add(changeBtn);

                _ifcSourcePanel.Children.Add(row);
                return;
            }

            if (_ifcDetected.Count > 0)
            {
                _ifcSourcePanel.Children.Add(new TextBlock
                {
                    Text = _ifcDetected.Count == 1 ? "Found in this project:" : $"Found {_ifcDetected.Count} in this project:",
                    FontSize = 11, Foreground = MeToolsTheme.BrMuted, Margin = new Thickness(0, 0, 0, 6),
                });
                foreach (var d in _ifcDetected) _ifcSourcePanel.Children.Add(BuildIfcDetectedRow(d));
                _ifcSourcePanel.Children.Add(new TextBlock
                {
                    Text = "or", FontSize = 10, Foreground = MeToolsTheme.BrMuted,
                    HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 2, 0, 8),
                });
                var browseBtnSecondary = ActionBtn("Browse for a different IFC file...", true, BrowseAndLoadIfc);
                browseBtnSecondary.Margin = new Thickness(0, 0, 0, 14);
                _ifcSourcePanel.Children.Add(browseBtnSecondary);
                return;
            }

            var card = new Border
            {
                Background = MeToolsTheme.BrSurface, BorderBrush = MeToolsTheme.BrBorder,
                BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(6),
                Padding = new Thickness(20, 20, 20, 20), Margin = new Thickness(0, 0, 0, 14),
            };
            var cardSp = new StackPanel();
            cardSp.Children.Add(new TextBlock
            {
                Text = "No IFC file found in this project", FontSize = 14, FontWeight = FontWeights.SemiBold,
                Foreground = MeToolsTheme.BrText, Margin = new Thickness(0, 0, 0, 4),
            });
            cardSp.Children.Add(new TextBlock
            {
                Text = "Checked for linked and imported IFC files -- nothing here matches. Browse for one directly to get started.",
                FontSize = 11, Foreground = MeToolsTheme.BrMuted, TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 16),
            });
            var browseBtn = ActionBtn("Browse for an IFC file...", false, BrowseAndLoadIfc);
            browseBtn.Height = 40; browseBtn.FontSize = 13; browseBtn.HorizontalAlignment = HorizontalAlignment.Stretch;
            cardSp.Children.Add(browseBtn);
            card.Child = cardSp;
            _ifcSourcePanel.Children.Add(card);
        }

        private Border BuildIfcDetectedRow((string DisplayName, string Path) d)
        {
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var infoPanel = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            infoPanel.Children.Add(new TextBlock { Text = d.DisplayName, FontSize = 12.5, FontWeight = FontWeights.SemiBold, Foreground = MeToolsTheme.BrText });
            infoPanel.Children.Add(new TextBlock { Text = d.Path, FontSize = 9.5, Foreground = MeToolsTheme.BrMuted, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 1, 0, 0) });
            Grid.SetColumn(infoPanel, 0); grid.Children.Add(infoPanel);

            var useBtn = ActionBtn("Use this", false, () => LoadIfcSource(d.Path));
            useBtn.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(useBtn, 1); grid.Children.Add(useBtn);

            return new Border
            {
                Background = MeToolsTheme.BrSurface, BorderBrush = MeToolsTheme.BrBorder,
                BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(4),
                Padding = new Thickness(10, 8, 10, 8), Margin = new Thickness(0, 0, 0, 8),
                Child = grid,
            };
        }

        private void OnIfcChangeSourceClicked()
        {
            _ifcFilePath = null; _ifcParsed = null; _ifcRows.Clear();
            RebuildIfcSourcePanel();
            RebuildIfcResultsPanel();
            if (StatusLeft != null) StatusLeft.Text = "Select an IFC source above to begin.";
        }

        private void BrowseAndLoadIfc()
        {
            var dlg = new Microsoft.Win32.OpenFileDialog
            {
                Title = "Select an IFC file",
                Filter = "IFC files (*.ifc)|*.ifc|All files (*.*)|*.*",
                CheckFileExists = true,
            };
            if (dlg.ShowDialog() == true) LoadIfcSource(dlg.FileName);
        }

        private void LoadIfcSource(string path)
        {
            var parsed = IfcLiteReader.Parse(path);
            if (!parsed.Success)
            {
                if (StatusLeft != null) StatusLeft.Text = parsed.FatalError ?? "Could not parse this file.";
                return;
            }
            _ifcParsed = parsed;
            _ifcFilePath = path;
            _ifcRows.Clear();
            RebuildIfcSourcePanel();
            RebuildIfcResultsPanel();
            if (StatusLeft != null) StatusLeft.Text = "Loaded " + System.IO.Path.GetFileName(path) + ".";
        }

        private void RebuildIfcResultsPanel()
        {
            _ifcResultsPanel.Children.Clear();

            if (_ifcParsed == null)
            {
                _ifcResultsPanel.Children.Add(new TextBlock
                {
                    Text = "Levels, units and location info will appear here once a source is loaded.",
                    FontSize = 11, FontStyle = FontStyles.Italic, Foreground = MeToolsTheme.BrMuted,
                    TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 6),
                });
                return;
            }

            var doc = _uiApp.ActiveUIDocument?.Document;

            _ifcResultsPanel.Children.Add(new TextBlock
            {
                Text = $"Schema: {_ifcParsed.SchemaVersion}", FontSize = 10.5, Foreground = MeToolsTheme.BrMuted,
                Margin = new Thickness(0, 0, 0, 12),
            });

            // -- Units ---------------------------------------------------------
            _ifcResultsPanel.Children.Add(Sec("Units"));
            var (revitLabel, revitKind) = DescribeRevitLengthUnit(doc);
            bool mismatch = _ifcParsed.LengthUnitKind != IfcLengthUnitKind.Unknown
                            && revitKind != IfcLengthUnitKind.Unknown
                            && _ifcParsed.LengthUnitKind != revitKind;

            var unitGrid = new Grid();
            unitGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            unitGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var ifcUnitBox = IfcUnitTile("This IFC file", _ifcParsed.LengthUnitLabel, mismatch);
            var rvtUnitBox = IfcUnitTile("Your Revit project", revitLabel, mismatch);
            Grid.SetColumn(ifcUnitBox, 0); unitGrid.Children.Add(ifcUnitBox);
            Grid.SetColumn(rvtUnitBox, 1); unitGrid.Children.Add(rvtUnitBox);
            _ifcResultsPanel.Children.Add(unitGrid);

            if (mismatch)
            {
                double ratio = _ifcParsed.LengthUnitToMeters / RevitUnitToMeters(revitKind);
                _ifcResultsPanel.Children.Add(IfcInfoBoxWarn(
                    $"Unit mismatch: this IFC file is in {_ifcParsed.LengthUnitLabel}, but your Revit project's units are set to {revitLabel}. " +
                    $"One raw unit in the file equals {ratio:0.####}x what the same number would mean in your project. " +
                    "Level elevations below are converted correctly regardless -- this warning is so you don't get caught out entering or eyeballing other values (like dimensions from this file's drawings) assuming the wrong scale."));
            }
            else
            {
                _ifcResultsPanel.Children.Add(InfoBox("No unit mismatch detected between this file and your project."));
            }

            // -- Site / location (read-only, informational only) ---------------
            if (_ifcParsed.Site.HasAnyInfo)
            {
                _ifcResultsPanel.Children.Add(Sec("Site / Location (read-only -- nothing here changes your project)"));
                _ifcResultsPanel.Children.Add(BuildIfcSitePanel());
            }

            // -- Warnings --------------------------------------------------------
            if (_ifcParsed.Warnings.Count > 0)
            {
                _ifcResultsPanel.Children.Add(Sec($"Notes ({_ifcParsed.Warnings.Count})"));
                var warnBox = new Border
                {
                    Background = MeToolsTheme.BrSurface, BorderBrush = MeToolsTheme.BrBorder,
                    BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(4),
                    Padding = new Thickness(10, 6, 10, 6), Margin = new Thickness(0, 0, 0, 12),
                };
                var warnPanel = new StackPanel();
                foreach (var w in _ifcParsed.Warnings)
                    warnPanel.Children.Add(new TextBlock
                    {
                        Text = "\u2022 " + w, FontSize = 10.5, Foreground = MeToolsTheme.BrMuted,
                        TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 2),
                    });
                warnBox.Child = warnPanel;
                _ifcResultsPanel.Children.Add(warnBox);
            }

            // -- Levels table ------------------------------------------------------
            _ifcResultsPanel.Children.Add(Sec($"Levels found ({_ifcParsed.Levels.Count})"));

            var existingNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                if (doc != null)
                    foreach (var l in new FilteredElementCollector(doc).OfClass(typeof(Level)).Cast<Level>())
                        existingNames.Add(l.Name);
            }
            catch { }

            foreach (var info in _ifcParsed.Levels)
            {
                bool clash = existingNames.Contains((info.Name ?? "").Trim());
                _ifcRows.Add(new IfcLevelRow
                {
                    Info = info,
                    IsSelected = !clash,
                    BlockReason = clash ? "A level with this name already exists -- will be skipped" : null,
                });
            }

            var tableBorder = new Border
            {
                BorderBrush = MeToolsTheme.BrBorder, BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(4), ClipToBounds = true, Margin = new Thickness(0, 0, 0, 10),
            };
            var tableOuter = new StackPanel();
            tableOuter.Children.Add(BuildIfcTableHeader());
            _ifcTableList = new StackPanel();
            foreach (var row in _ifcRows) _ifcTableList.Children.Add(BuildIfcTableRow(row, revitKind));
            tableOuter.Children.Add(_ifcTableList);
            tableBorder.Child = tableOuter;
            _ifcResultsPanel.Children.Add(tableBorder);

            _ifcImportBtn = ActionBtn("Import Selected Levels", false, OnIfcImportClicked);
            _ifcResultsPanel.Children.Add(_ifcImportBtn);
        }

        private Border IfcUnitTile(string title, string label, bool warn)
        {
            return new Border
            {
                Background = warn ? new SolidColorBrush(Color.FromArgb(30, MeToolsTheme.CRed.R, MeToolsTheme.CRed.G, MeToolsTheme.CRed.B)) : MeToolsTheme.BrSurface,
                BorderBrush = warn ? new SolidColorBrush(MeToolsTheme.CRed) : MeToolsTheme.BrBorder,
                BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(4),
                Padding = new Thickness(10, 8, 10, 8), Margin = new Thickness(0, 0, 6, 10),
                Child = new StackPanel
                {
                    Children =
                    {
                        new TextBlock { Text = title, FontSize = 10, Foreground = MeToolsTheme.BrMuted },
                        new TextBlock { Text = label, FontSize = 13, FontWeight = FontWeights.SemiBold, Foreground = MeToolsTheme.BrText, TextWrapping = TextWrapping.Wrap },
                    },
                },
            };
        }

        private FrameworkElement BuildIfcSitePanel()
        {
            var panel = new StackPanel { Margin = new Thickness(0, 0, 0, 10) };
            var site = _ifcParsed.Site;

            if (site.LocalX.HasValue)
            {
                double toM = _ifcParsed.LengthUnitToMeters;
                double x = (site.LocalX ?? 0) * toM, y = (site.LocalY ?? 0) * toM, z = (site.LocalZ ?? 0) * toM;
                panel.Children.Add(IfcInfoLine("Site's local placement",
                    $"X = {x:0.###} m,  Y = {y:0.###} m,  Z = {z:0.###} m  (relative to this IFC file's own origin/placement -- not necessarily your project's origin)"));
            }
            if (site.LatitudeDeg.HasValue && site.LongitudeDeg.HasValue)
            {
                panel.Children.Add(IfcInfoLine("Geographic reference",
                    $"Lat {site.LatitudeDeg:0.000000}\u00B0, Lon {site.LongitudeDeg:0.000000}\u00B0" +
                    (site.RefElevationRaw.HasValue ? $", elevation {(site.RefElevationRaw.Value * _ifcParsed.LengthUnitToMeters):0.##} m" : "")));
            }
            if (site.MapEastings.HasValue)
            {
                panel.Children.Add(IfcInfoLine("Survey coordinates (IfcMapConversion)",
                    $"Easting {site.MapEastings:0.###},  Northing {site.MapNorthings:0.###}" +
                    (site.MapOrthogonalHeight.HasValue ? $",  Height {site.MapOrthogonalHeight:0.###}" : "")));
            }
            return panel;
        }

        private FrameworkElement IfcInfoLine(string label, string value)
        {
            var sp = new StackPanel { Margin = new Thickness(0, 0, 0, 8) };
            sp.Children.Add(new TextBlock { Text = label, FontSize = 10.5, Foreground = MeToolsTheme.BrMuted });
            sp.Children.Add(new TextBlock { Text = value, FontSize = 12, Foreground = MeToolsTheme.BrText, TextWrapping = TextWrapping.Wrap });
            return sp;
        }

        private Grid BuildIfcTableHeader()
        {
            var grid = new Grid { Background = MeToolsTheme.BrHeader, MinHeight = 28 };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(30) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(130) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) });

            _ifcSelectAllCb = new CheckBox { IsChecked = true, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
            _ifcSelectAllCb.Click += (s, e) =>
            {
                bool check = _ifcSelectAllCb.IsChecked == true;
                foreach (var row in _ifcRows.Where(r => r.Importable)) row.IsSelected = check;
                RebuildIfcRows();
            };
            Grid.SetColumn(_ifcSelectAllCb, 0); grid.Children.Add(_ifcSelectAllCb);

            var headers = new (int col, string text)[] { (1, "Level name"), (2, "Elevation (file unit)"), (3, "Elevation (your project)") };
            foreach (var (col, text) in headers)
            {
                var tb = new TextBlock
                {
                    Text = text, FontSize = 9.5, FontWeight = FontWeights.SemiBold, Foreground = MeToolsTheme.BrMuted,
                    VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 4, 0),
                };
                Grid.SetColumn(tb, col); grid.Children.Add(tb);
            }
            return grid;
        }

        private Border BuildIfcTableRow(IfcLevelRow row, IfcLengthUnitKind revitKind)
        {
            var grid = new Grid { MinHeight = 30 };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(30) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(130) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) });

            var cb = new CheckBox
            {
                IsChecked = row.IsSelected, IsEnabled = row.Importable,
                VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center,
            };
            cb.Checked   += (s, e) => row.IsSelected = true;
            cb.Unchecked += (s, e) => row.IsSelected = false;
            Grid.SetColumn(cb, 0); grid.Children.Add(cb);

            var nameStack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            nameStack.Children.Add(new TextBlock
            {
                Text = row.Info.Name, FontSize = 12, Foreground = row.Importable ? MeToolsTheme.BrText : MeToolsTheme.BrMuted,
                Margin = new Thickness(6, 4, 4, 0),
            });
            if (!row.Importable)
                nameStack.Children.Add(new TextBlock
                {
                    Text = row.BlockReason, FontSize = 9.5, Foreground = MeToolsTheme.BrOrange,
                    Margin = new Thickness(6, 0, 4, 4), TextWrapping = TextWrapping.Wrap,
                });
            Grid.SetColumn(nameStack, 1); grid.Children.Add(nameStack);

            var rawTb = new TextBlock
            {
                Text = $"{row.Info.ElevationRaw:0.###}", FontSize = 11.5, Foreground = MeToolsTheme.BrMuted,
                VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 4, 0),
            };
            Grid.SetColumn(rawTb, 2); grid.Children.Add(rawTb);

            double meters = row.Info.ElevationRaw * _ifcParsed.LengthUnitToMeters;
            double converted = meters / RevitUnitToMeters(revitKind);
            var convTb = new TextBlock
            {
                Text = $"{converted:0.###}", FontSize = 11.5, FontWeight = FontWeights.SemiBold, Foreground = MeToolsTheme.BrText,
                VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 4, 0),
            };
            Grid.SetColumn(convTb, 3); grid.Children.Add(convTb);

            return new Border
            {
                Background = MeToolsTheme.BrRow, BorderBrush = MeToolsTheme.BrBorder,
                BorderThickness = new Thickness(0, 0, 0, 1), Child = grid,
            };
        }

        private void RebuildIfcRows()
        {
            _ifcTableList.Children.Clear();
            var doc = _uiApp.ActiveUIDocument?.Document;
            var (_, revitKind) = DescribeRevitLengthUnit(doc);
            foreach (var row in _ifcRows) _ifcTableList.Children.Add(BuildIfcTableRow(row, revitKind));
        }

        private void OnIfcImportClicked()
        {
            var selected = _ifcRows.Where(r => r.IsSelected && r.Importable).Select(r => r.Info).ToList();
            if (selected.Count == 0) { if (StatusLeft != null) StatusLeft.Text = "Nothing selected."; return; }

            _ifcImportBtn.IsEnabled = false;
            if (StatusLeft != null) StatusLeft.Text = $"Creating {selected.Count} level(s)...";
            _ifcHandler.Request = new IfcLevelImportRequest
            {
                LevelsToCreate = selected,
                LengthUnitToMeters = _ifcParsed.LengthUnitToMeters,
            };
            _ifcExtEvent.Raise();
        }

        private void OnIfcImportDone(IfcLevelImportResultInfo res)
        {
            _ifcImportBtn.IsEnabled = true;
            string msg = $"Created {res.Created} level(s).";
            if (res.Skipped > 0) msg += $" Skipped {res.Skipped}: {string.Join(", ", res.SkippedNames)}.";
            if (StatusLeft != null) StatusLeft.Text = msg;
            // The Project Levels tab's own data is now stale (new levels exist) -- refresh it too.
            RequestRefresh();
        }

        // -- Unit helpers --------------------------------------------------------
        private static (string Label, IfcLengthUnitKind Kind) DescribeRevitLengthUnit(Document doc)
        {
            if (doc == null) return ("(no active document)", IfcLengthUnitKind.Unknown);
            try
            {
                var fo = doc.GetUnits().GetFormatOptions(SpecTypeId.Length);
                var uid = fo.GetUnitTypeId();
                if (uid == UnitTypeId.Millimeters) return ("Millimetres (mm)", IfcLengthUnitKind.Millimeter);
                if (uid == UnitTypeId.Centimeters) return ("Centimetres (cm)", IfcLengthUnitKind.Centimeter);
                if (uid == UnitTypeId.Meters) return ("Metres (m)", IfcLengthUnitKind.Meter);
                if (uid == UnitTypeId.Feet) return ("Feet (ft)", IfcLengthUnitKind.Foot);
                if (uid == UnitTypeId.FeetFractionalInches) return ("Feet & fractional inches", IfcLengthUnitKind.Foot);
                if (uid == UnitTypeId.Inches) return ("Inches (in)", IfcLengthUnitKind.Inch);
                if (uid == UnitTypeId.FractionalInches) return ("Fractional inches", IfcLengthUnitKind.Inch);
                return (uid.TypeId, IfcLengthUnitKind.Unknown);
            }
            catch { return ("(unknown)", IfcLengthUnitKind.Unknown); }
        }

        private static double RevitUnitToMeters(IfcLengthUnitKind kind)
        {
            switch (kind)
            {
                case IfcLengthUnitKind.Millimeter: return 0.001;
                case IfcLengthUnitKind.Centimeter: return 0.01;
                case IfcLengthUnitKind.Decimeter:  return 0.1;
                case IfcLengthUnitKind.Meter:      return 1.0;
                case IfcLengthUnitKind.Kilometer:  return 1000.0;
                case IfcLengthUnitKind.Foot:       return 0.3048;
                case IfcLengthUnitKind.Inch:       return 0.0254;
                default: return 1.0;
            }
        }

        private Border IfcInfoBoxWarn(string text) => new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(28, MeToolsTheme.CRed.R, MeToolsTheme.CRed.G, MeToolsTheme.CRed.B)),
            BorderBrush = new SolidColorBrush(MeToolsTheme.CRed), BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(5), Padding = new Thickness(10, 8, 10, 8), Margin = new Thickness(0, 0, 0, 12),
            Child = new TextBlock { Text = text, FontSize = 11, Foreground = MeToolsTheme.BrText, TextWrapping = TextWrapping.Wrap },
        };
    }
}

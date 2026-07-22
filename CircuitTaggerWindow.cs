// CircuitTaggerWindow.cs -- ME-Tools | Circuit Tagger
// Mayer E-Concept SRL -- Pure C# WPF, no XAML
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Color      = System.Windows.Media.Color;
using ComboBox   = System.Windows.Controls.ComboBox;
using Grid       = System.Windows.Controls.Grid;
using Path       = System.IO.Path;
using TextBox    = System.Windows.Controls.TextBox;
using Visibility = System.Windows.Visibility;

namespace METools.FamilyPlacer
{
    public class CircuitTaggerWindow : METools.MeToolsWindowBase
    {
        protected override string AppKey => "CircuitTagger";
        private readonly UIApplication        _uiApp;
        private readonly ExternalEvent        _extEvent;
        private readonly CircuitTaggerHandler _handler;

        private readonly List<TaggedElementInfo> _selected = new List<TaggedElementInfo>();

        // Tabs
        private Border     _tabTag, _tabStats, _tabSettings;
        private StackPanel _panTag, _panStats, _panSettings;
        private Border     _activeTab;
        private StackPanel _activePanel;

        // Input fields
        private TextBox   _tbVorsicherung, _tbFI, _tbStromkreis, _tbSubIndex, _tbBeleuchtungskreis, _tbSubLabel;
        private ComboBox  _cbApartment, _cbBuilding;
        private TextBox   _tbGapMm, _tbStackGapMm;
        // Settings tab controls
        private TextBox   _tbSetGapMm, _tbSetOffsetYMm, _tbSetStackGapMm;
        private TextBox   _tbSetFontName, _tbSetFontSizeMm, _tbSetColorHex;
        private CheckBox  _cbSetBold, _cbSetItalic, _cbSetShowBorder, _cbSetOpaque;
        private CheckBox  _cbSetUnderline;
        private TextBox[] _extraSettingsTb;
        private ComboBox  _cbSetHAlign;
        private Border    _settingsColorSwatch;
        private CircuitTaggerSettingsData _settingsData;

        // Lists
        private StackPanel _selectionList, _statsList;

        // Status
        private TextBlock _lblStatus, _lblSelCount;

        // Theme tracking
        private readonly List<TextBox>  _allInputs = new List<TextBox>();
        private readonly List<ComboBox> _allCombos = new List<ComboBox>();
        private readonly List<Border>   _allRows   = new List<Border>();

        public CircuitTaggerWindow(UIApplication uiApp, ExternalEvent extEvent, CircuitTaggerHandler handler)
        {
            _uiApp = uiApp; _extEvent = extEvent; _handler = handler;
            InitWindow("ElecTriX -- Circuit Tagger", 760);
            MaxHeight = Math.Min(820, SystemParameters.WorkArea.Height - 60);
            _settingsData = CircuitTaggerSettings.Load();
            WireHandler();
            Build();
            RequestReadDropdowns();
        }

        // ?? Wire handler callbacks ????????????????????????????????????????
        private void WireHandler()
        {
            _handler.OnStatus = msg => Dispatcher.Invoke(() =>
            {
                if (_lblStatus != null) _lblStatus.Text = msg;
                UpdateStatusBar(msg);
            });
            _handler.OnApartmentValues = vals => Dispatcher.Invoke(() =>
            {
                if (_cbApartment == null) return;
                var cur = _cbApartment.Text;
                _cbApartment.Items.Clear();
                foreach (var v in vals) _cbApartment.Items.Add(v);
                _cbApartment.Text = cur;
            });
            _handler.OnBuildingValues = vals => Dispatcher.Invoke(() =>
            {
                if (_cbBuilding == null) return;
                var cur = _cbBuilding.Text;
                _cbBuilding.Items.Clear();
                foreach (var v in vals) _cbBuilding.Items.Add(v);
                _cbBuilding.Text = cur;
            });
            _handler.OnDone = () => Dispatcher.Invoke(() => { RefreshStats(); });
            _handler.OnError = msg => Dispatcher.Invoke(() =>
                MessageBox.Show(msg, "ME-Tools -- Circuit Tagger", MessageBoxButton.OK, MessageBoxImage.Warning));
            _handler.OnParamsLoaded = loaded => Dispatcher.Invoke(() =>
            {
                if (_tbVorsicherung      != null) _tbVorsicherung.Text      = loaded.Vorsicherung      ?? "";
                if (_tbFI                != null) _tbFI.Text                = loaded.FI                ?? "";
                if (_tbStromkreis        != null) _tbStromkreis.Text        = loaded.Stromkreis        ?? "";
                if (_tbSubIndex          != null) _tbSubIndex.Text          = loaded.SubIndex          ?? "";
                if (_tbBeleuchtungskreis != null) _tbBeleuchtungskreis.Text = loaded.Beleuchtungskreis ?? "";
                if (_cbApartment         != null) _cbApartment.Text         = loaded.Apartment         ?? "";
                if (_cbBuilding          != null) _cbBuilding.Text          = loaded.Building          ?? "";
                if (_tbSubLabel          != null) _tbSubLabel.Text          = loaded.SubLabel          ?? "";
                UpdateStatusBar("Params loaded. Edit and click Apply & Tag.");
            });
        }

        // ?? Build ?????????????????????????????????????????????????????????
        private void Build()
        {
            BuildStatusBar("Ready -- select elements to tag");
            var tabBar = BuildTabBar();
            DockPanel.SetDock(tabBar, Dock.Top);
            RootDock.Children.Add(tabBar);
            BuildFooter();

            var contentGrid = new Grid { Background = MeToolsTheme.BrBg };
            contentGrid.Children.Add(Watermark());
            var scroll = new ScrollViewer
            {
                VerticalScrollBarVisibility   = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Background = Brushes.Transparent,
                Padding    = new Thickness(16, 12, 16, 10),
            };
            var outer = new StackPanel();
            outer.Children.Add(_panTag);
            outer.Children.Add(_panStats);
            outer.Children.Add(_panSettings);
            scroll.Content   = outer;
            contentGrid.Children.Add(scroll);
            RootDock.Children.Add(contentGrid);
            ShowTab(_tabTag, _panTag);
        }

        // ?? Tab bar ???????????????????????????????????????????????????????
        private Border BuildTabBar()
        {
            _panTag      = BuildTagPanel();
            _panStats    = BuildStatsPanel();
            _panSettings = BuildSettingsPanel();

            _tabTag      = MakeTab("Tag Elements",  MeToolsTheme.CPetrol, () => ShowTab(_tabTag,      _panTag));
            _tabStats    = MakeTab("Circuit Stats",  MeToolsTheme.COrange, () => { ShowTab(_tabStats, _panStats); RefreshStats(); });
            _tabSettings = MakeTab("Settings",       MeToolsTheme.CGreen,  () => ShowTab(_tabSettings, _panSettings));
            var sp = new StackPanel { Orientation = Orientation.Horizontal };
            sp.Children.Add(_tabTag); sp.Children.Add(_tabStats); sp.Children.Add(_tabSettings);
            return new Border
            {
                Background = MeToolsTheme.BrHeader, BorderBrush = MeToolsTheme.BrBorder,
                BorderThickness = new Thickness(0, 0, 0, 1), Padding = new Thickness(4, 0, 0, 0),
                Child = sp,
            };
        }

        private Border MakeTab(string label, Color tc, Action onClick)
        {
            var pill = new Border
            {
                CornerRadius = new CornerRadius(10), Padding = new Thickness(10, 2, 10, 2),
                Background = new SolidColorBrush(Color.FromArgb(35, tc.R, tc.G, tc.B)),
                Child = new TextBlock { Text = label, FontSize = 11, FontWeight = FontWeights.SemiBold,
                    Foreground = MeToolsTheme.BrMuted, VerticalAlignment = VerticalAlignment.Center },
            };
            var tab = new Border
            {
                Padding = new Thickness(8, 6, 8, 6), Cursor = Cursors.Hand,
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
            foreach (var t in new[] { _tabTag, _tabStats, _tabSettings })
            {
                if (t == null) continue;
                t.BorderBrush = Brushes.Transparent; t.Background = MeToolsTheme.BrHeader;
                if (t.Child is Border p) {
                    var tc2 = (Color)t.Tag;
                    p.Background = new SolidColorBrush(Color.FromArgb(30, tc2.R, tc2.G, tc2.B));
                    if (p.Child is TextBlock tb2) tb2.Foreground = MeToolsTheme.BrMuted;
                }
            }
            foreach (var p in new[] { _panTag, _panStats, _panSettings })
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
        }

        // ???????????????????????????????????????????????????????????????????
        // TAB 1 -- TAG ELEMENTS (compact)
        // ???????????????????????????????????????????????????????????????????
        private StackPanel BuildTagPanel()
        {
            var sp = new StackPanel { Visibility = Visibility.Collapsed };

            // -- Selection row
            sp.Children.Add(SecH("Element Selection"));
            var selRow = new Grid { Margin = new Thickness(0, 4, 0, 4) };
            selRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            selRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            _lblSelCount = new TextBlock { Text = "0 elements selected", FontSize = 11,
                Foreground = MeToolsTheme.BrMuted, VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(_lblSelCount, 0); selRow.Children.Add(_lblSelCount);
            var btnRow = new StackPanel { Orientation = Orientation.Horizontal };
            var btnSel  = SmallBtn("+ Select in Revit", true,  OnSelectClicked);
            var btnLoad = SmallBtn("Load",               false, OnLoadFromSelectionClicked);
            btnLoad.ToolTip = "Select an already-tagged element in Revit, click Load to fill in all fields from it.";
            var btnClr  = SmallBtn("Clear",              false, OnClearClicked);
            btnSel.Margin  = new Thickness(0, 0, 6, 0);
            btnLoad.Margin = new Thickness(0, 0, 6, 0);
            btnRow.Children.Add(btnSel); btnRow.Children.Add(btnLoad); btnRow.Children.Add(btnClr);
            Grid.SetColumn(btnRow, 1); selRow.Children.Add(btnRow);
            sp.Children.Add(selRow);

            var selBox = new Border
            {
                BorderBrush = MeToolsTheme.BrBorder, BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(5), ClipToBounds = true, MinHeight = 80, MaxHeight = 200,
            };
            var selScroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
            _selectionList = new StackPanel();
            selScroll.Content = _selectionList; selBox.Child = selScroll;
            sp.Children.Add(selBox);

            sp.Children.Add(Div());

            // -- Circuit Parameters (2x2 grid + sub-index)
            sp.Children.Add(SecH("Circuit Parameters"));
            sp.Children.Add(InfoBox("FI + Stromkreis = tag label (e.g. 1 + F1 = 1F1). Sub-index adds _N suffix (e.g. 1F1_1 for the switch/lamp group). Leave blank to skip."));

            var p2 = new Grid { Margin = new Thickness(0, 4, 0, 0) };
            p2.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            p2.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
            p2.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            p2.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
            p2.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(0.6, GridUnitType.Star) });
            p2.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            p2.RowDefinitions.Add(new RowDefinition { Height = new GridLength(8) });
            p2.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var vsCard = InlineCard("Vorsicherung", "Upstream fuse (e.g. B25A)", out _tbVorsicherung);
            Grid.SetRow(vsCard, 0); Grid.SetColumn(vsCard, 0); p2.Children.Add(vsCard);

            var fiCard = InlineCard("FI (RCD number)", "e.g. 1, 2", out _tbFI);
            Grid.SetRow(fiCard, 0); Grid.SetColumn(fiCard, 2); p2.Children.Add(fiCard);

            var subCard = InlineCard("Sub-index", "Number only. Empty = no suffix.", out _tbSubIndex);
            Grid.SetRow(subCard, 0); Grid.SetColumn(subCard, 4); p2.Children.Add(subCard);

            var skCard = InlineCard("Stromkreis", "Circuit branch, e.g. F1, F2", out _tbStromkreis);
            Grid.SetRow(skCard, 2); Grid.SetColumn(skCard, 0); p2.Children.Add(skCard);

            var bkCard = InlineCard("Beleuchtungskreis", "Lighting circuit, e.g. L1 (optional)", out _tbBeleuchtungskreis);
            Grid.SetRow(bkCard, 2); Grid.SetColumn(bkCard, 2); p2.Children.Add(bkCard);

            // Preview box in 5th column, row 2
            var prevBox = new Border
            {
                Background = MeToolsTheme.BrSurface, BorderBrush = MeToolsTheme.BrBorder,
                BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(4),
                Padding = new Thickness(8, 6, 8, 6),
            };
            var prevSp = new StackPanel();
            prevSp.Children.Add(new TextBlock { Text = "PREVIEW", FontSize = 8, FontWeight = FontWeights.SemiBold,
                Foreground = MeToolsTheme.BrMuted, Margin = new Thickness(0, 0, 0, 3) });
            var prevLabel = new TextBlock { Text = "--", FontSize = 22, FontWeight = FontWeights.Bold,
                FontFamily = new FontFamily("Consolas"), Foreground = MeToolsTheme.BrPetrol };
            prevSp.Children.Add(prevLabel); prevBox.Child = prevSp;
            Grid.SetRow(prevBox, 2); Grid.SetColumn(prevBox, 4); p2.Children.Add(prevBox);

            sp.Children.Add(p2);
            _allInputs.Add(_tbVorsicherung); _allInputs.Add(_tbFI); _allInputs.Add(_tbSubIndex);
            _allInputs.Add(_tbStromkreis); _allInputs.Add(_tbBeleuchtungskreis);

            Action updatePreview = () =>
            {
                var fi = (_tbFI?.Text ?? "").Trim();
                var sk = (_tbStromkreis?.Text ?? "").Trim();
                var sub = (_tbSubIndex?.Text ?? "").Trim();
                var lbl = fi + sk + (string.IsNullOrEmpty(sub) ? "" : "_" + sub);
                prevLabel.Text       = string.IsNullOrEmpty(lbl) ? "--" : lbl;
                prevLabel.Foreground = string.IsNullOrEmpty(lbl) ? MeToolsTheme.BrMuted : MeToolsTheme.BrPetrol;
            };
            _tbFI.TextChanged         += (s, e) => updatePreview();
            _tbStromkreis.TextChanged += (s, e) => updatePreview();
            _tbSubIndex.TextChanged   += (s, e) => updatePreview();

            // Sub-label (secondary annotation tag: a, b, c...)
            sp.Children.Add(Div());
            sp.Children.Add(SecH("Secondary Tag (Optional)"));
            sp.Children.Add(InfoBox("Generates a separate text annotation near each element (e.g. 'a', 'b'). This is independent of the circuit tag. Leave blank to skip."));
            var subLabelCard = InlineCard("Secondary Label", "e.g. a, b, c. A separate text tag is placed near each element.", out _tbSubLabel);
            sp.Children.Add(subLabelCard);
            _allInputs.Add(_tbSubLabel);

            sp.Children.Add(Div());

            // -- Group Tags (Apartment + Building side by side)
            sp.Children.Add(SecH("Group Tags (invisible)"));
            sp.Children.Add(InfoBox("Apartment and Building are stored as shared parameters on each element but not shown in the view. They group elements for the stats list and Excel export."));

            var gRow = new Grid { Margin = new Thickness(0, 4, 0, 0) };
            gRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            gRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
            gRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var aptCard = ComboCard("Apartment / Group", "Type new or pick from dropdown", out _cbApartment);
            Grid.SetColumn(aptCard, 0); gRow.Children.Add(aptCard);

            var bldCard = ComboCard("Building / Haus", "Type new or pick from dropdown", out _cbBuilding);
            Grid.SetColumn(bldCard, 2); gRow.Children.Add(bldCard);
            sp.Children.Add(gRow);
            _allCombos.Add(_cbApartment); _allCombos.Add(_cbBuilding);

            sp.Children.Add(Div());
            _lblStatus = new TextBlock { Text = "Ready.", FontSize = 11, Foreground = MeToolsTheme.BrMuted,
                TextWrapping = TextWrapping.Wrap };
            sp.Children.Add(_lblStatus);
            return sp;
        }

        // ???????????????????????????????????????????????????????????????????
        // TAB 2 -- CIRCUIT STATS (grouped by Building -> Apartment -> Circuit)
        // ???????????????????????????????????????????????????????????????????
        private StackPanel BuildStatsPanel()
        {
            var sp = new StackPanel { Visibility = Visibility.Collapsed };

            var hdrRow = new Grid { Margin = new Thickness(0, 0, 0, 6) };
            hdrRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            hdrRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var hdrTb = SecH("Circuit Statistics");
            Grid.SetColumn(hdrTb, 0); hdrRow.Children.Add(hdrTb);
            var btnRefStats = SmallBtn("Refresh", false, () => RefreshStats());
            btnRefStats.Margin = new Thickness(12, 0, 0, 0);
            btnRefStats.Padding = new Thickness(16, 0, 16, 0);
            Grid.SetColumn(btnRefStats, 1); hdrRow.Children.Add(btnRefStats);
            sp.Children.Add(hdrRow);

            sp.Children.Add(InfoBox("Stats read parameter values from elements -- not from visual tags. Deleting a tag does NOT clear the circuit data. Use 'Clear Circuit Data' on a row to wipe the parameters from all elements in that circuit."));
            var container = new Border
            {
                BorderBrush = MeToolsTheme.BrBorder, BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(4), ClipToBounds = true,
            };
            _statsList = new StackPanel();
            StatsHeader(_statsList);
            container.Child = _statsList; sp.Children.Add(container);
            return sp;
        }

        private void StatsHeader(StackPanel sp)
        {
            var grid = new Grid { Background = MeToolsTheme.BrHeader, MinHeight = 26 };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(6) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(180) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(52) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(52) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(42) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(54) });

            // Total column gets a subtle tint so it reads as a summary column,
            // not a fifth badge in the Sock./Lamp/Sw. sequence.
            var totalBg = new Border { Background = new SolidColorBrush(Color.FromArgb(18, MeToolsTheme.COrange.R, MeToolsTheme.COrange.G, MeToolsTheme.COrange.B)) };
            Grid.SetColumn(totalBg, 6); grid.Children.Add(totalBg);

            // Thin vertical dividers between the four numeric columns -- this is
            // what makes it impossible to miscount which value sits under which
            // header (the actual cause of the earlier Sock./Sw. mix-up).
            foreach (int col in new[] { 3, 4, 5, 6 })
            {
                var divider = new Border
                {
                    Width = 1, Background = MeToolsTheme.BrBorder,
                    HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 4, 0, 4),
                };
                Grid.SetColumn(divider, col); grid.Children.Add(divider);
            }

            var headers = new (int col, string text)[]
            {
                (2, "Circuit / Vorsicherung"),
                (3, "Sock."),
                (4, "Lamp"),
                (5, "Sw."),
                (6, "Total"),
            };
            foreach (var (col, text) in headers)
            {
                var tb = new TextBlock
                {
                    Text = text, FontSize = 9, FontWeight = FontWeights.SemiBold,
                    Foreground = MeToolsTheme.BrMuted, VerticalAlignment = VerticalAlignment.Center,
                    HorizontalAlignment = col >= 3 ? HorizontalAlignment.Center : HorizontalAlignment.Left,
                    Margin = new Thickness(col >= 3 ? 4 : 8, 0, 4, 0),
                };
                Grid.SetColumn(tb, col); grid.Children.Add(tb);
            }
            sp.Children.Add(new Border
            {
                BorderBrush = MeToolsTheme.BrBorder, BorderThickness = new Thickness(0, 0, 0, 1),
                Child = grid,
            });
        }

        private void RefreshStats()
        {
            if (_statsList == null) return;
            _statsList.Children.Clear();
            StatsHeader(_statsList); // re-add header after clear

            var doc = _uiApp.ActiveUIDocument?.Document;
            if (doc == null) return;

            var rows = CircuitTaggerHandler.ReadAllTaggedElements(doc);
            if (rows.Count == 0) { _statsList.Children.Add(EmptyRow("No tagged elements found.")); return; }

            // Group: building -> apartment -> circuit base -> sub-circuits
            var byBuilding = rows
                .GroupBy(r => r.Building ?? "")
                .OrderBy(g => g.Key);

            bool anyRow = false;
            foreach (var bldGrp in byBuilding)
            {
                // Building header
                _statsList.Children.Add(GroupHeader(
                    string.IsNullOrEmpty(bldGrp.Key) ? "(No Building)" : "Building: " + bldGrp.Key,
                    MeToolsTheme.CPetrol));

                var byApt = bldGrp.GroupBy(r => r.Apartment ?? "").OrderBy(g => g.Key);
                foreach (var aptGrp in byApt)
                {
                    // Apartment header
                    _statsList.Children.Add(GroupHeader(
                        "  " + (string.IsNullOrEmpty(aptGrp.Key) ? "(No Apartment)" : aptGrp.Key),
                        MeToolsTheme.COrange));

                    // Group circuits by base (strip sub-index)
                    var byBase = aptGrp
                        .GroupBy(r => CircuitTaggerHandler.GetCircuitBase(r.CircuitLabel ?? ""))
                        .OrderBy(g => g.Key);

                    foreach (var baseGrp in byBase)
                    {
                        // All elements in this base circuit
                        var allEl = baseGrp.ToList();
                        int sockets  = allEl.Count(r => CatIsSocket(r));
                        int lamps    = allEl.Count(r => CatIsLamp(r));
                        int switches = allEl.Count(r => CatIsSwitch(r));
                        int other    = Math.Max(0, allEl.Count - sockets - lamps - switches);

                        var stat = new CircuitStatRow
                        {
                            CircuitBase  = baseGrp.Key,
                            CircuitLabel = baseGrp.Key,
                            Vorsicherung = allEl.FirstOrDefault()?.Vorsicherung ?? "",
                            FI           = allEl.FirstOrDefault()?.FI ?? "",
                            Apartment    = aptGrp.Key,
                            Building     = bldGrp.Key,
                            CountSockets = sockets + other,
                            CountLamps   = lamps,
                            CountSwitches = switches,
                        };
                        _statsList.Children.Add(BuildStatsRow(stat));

                        // Sub-circuit rows (indented)
                        var subCircuits = allEl
                            .GroupBy(r => r.CircuitLabel ?? "")
                            .Where(g => g.Key != baseGrp.Key)
                            .OrderBy(g => g.Key);

                        foreach (var subGrp in subCircuits)
                        {
                            var subEl = subGrp.ToList();
                            int ss = subEl.Count(r => CatIsSocket(r));
                            int sl = subEl.Count(r => CatIsLamp(r));
                            int sw = subEl.Count(r => CatIsSwitch(r));
                            int so = Math.Max(0, subEl.Count - ss - sl - sw);
                            var sub = new CircuitStatRow
                            {
                                CircuitBase   = baseGrp.Key,
                                CircuitLabel  = subGrp.Key,
                                CountSockets  = ss + so,
                                CountLamps    = sl,
                                CountSwitches = sw,
                            };
                            _statsList.Children.Add(BuildStatsRow(sub, isSubRow: true));
                        }
                        anyRow = true;
                    }
                }
            }
            if (!anyRow) _statsList.Children.Add(EmptyRow("No tagged elements found."));
        }

        // -- Category classification -- uses integer IDs (locale-independent) --
        // -2001060 = OST_ElectricalFixtures (sockets, outlets)
        // -2001120 = OST_LightingFixtures   (lamps, ceiling lights)
        // -2001040 = OST_LightingDevices    (switches, dimmers)
        // -2008090 = OST_DataDevices
        // -2008093 = OST_FireAlarmDevices
        // -2008094 = OST_CommunicationDevices
        // -2008095 = OST_SecurityDevices
        private static bool CatIsSocket(ExportRow r)  => r.CategoryId == -2001060;
        private static bool CatIsLamp(ExportRow r)    => r.CategoryId == -2001120;
        private static bool CatIsSwitch(ExportRow r)  => r.CategoryId == -2001040 || r.CategoryId == -2008090
                                                       || r.CategoryId == -2008093 || r.CategoryId == -2008094
                                                       || r.CategoryId == -2008095;

        private Border GroupHeader(string text, Color color)
        {
            return new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(20, color.R, color.G, color.B)),
                BorderBrush = MeToolsTheme.BrBorder, BorderThickness = new Thickness(0, 0, 0, 1),
                Padding = new Thickness(10, 5, 10, 5),
                Child = new TextBlock
                {
                    Text = text, FontSize = 11, FontWeight = FontWeights.SemiBold,
                    Foreground = new SolidColorBrush(color),
                },
            };
        }

        private Border BuildStatsRow(CircuitStatRow stat, bool isSubRow = false)
        {
            var grid = new Grid { MinHeight = 32 };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(isSubRow ? 20 : 6) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(180) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(52) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(52) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(42) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(54) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });  // clear btn

            // Total column tint + column dividers, matching StatsHeader exactly.
            var totalBg = new Border { Background = new SolidColorBrush(Color.FromArgb(14, MeToolsTheme.COrange.R, MeToolsTheme.COrange.G, MeToolsTheme.COrange.B)) };
            Grid.SetColumn(totalBg, 6); grid.Children.Add(totalBg);
            foreach (int col in new[] { 3, 4, 5, 6 })
            {
                var divider = new Border
                {
                    Width = 1, Background = MeToolsTheme.BrBorder,
                    HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 3, 0, 3),
                };
                Grid.SetColumn(divider, col); grid.Children.Add(divider);
            }

            UIElement badge = CircuitBadge(stat.CircuitLabel, isSubRow);
            ((FrameworkElement)badge).Margin         = new Thickness(0, 4, 8, 4);
            ((FrameworkElement)badge).VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(badge, 1); grid.Children.Add(badge);

            var vs = TC(stat.Vorsicherung, small: true);
            Grid.SetColumn(vs, 2); grid.Children.Add(vs);

            var cb1 = CountBadge(stat.CountSockets,  MeToolsTheme.CPetrol);
            var cb2 = CountBadge(stat.CountLamps,     MeToolsTheme.CBlue);
            var cb3 = CountBadge(stat.CountSwitches,  MeToolsTheme.CGreen);
            var cb4 = CountBadge(stat.Total,          MeToolsTheme.COrange);
            Grid.SetColumn(cb1, 3); grid.Children.Add(cb1);
            Grid.SetColumn(cb2, 4); grid.Children.Add(cb2);
            Grid.SetColumn(cb3, 5); grid.Children.Add(cb3);
            Grid.SetColumn(cb4, 6); grid.Children.Add(cb4);

            // Clear button -- always visible on the right
            var capturedLabel = stat.CircuitLabel;
            var clearBtn = new Button
            {
                Content = "Clear", Height = 20, FontSize = 9,
                Background = new SolidColorBrush(Color.FromArgb(0, 0, 0, 0)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(180, 60, 60)),
                BorderThickness = new Thickness(1), Cursor = Cursors.Hand,
                Foreground = new SolidColorBrush(Color.FromRgb(180, 60, 60)),
                Margin = new Thickness(6, 0, 6, 0), Padding = new Thickness(8, 0, 8, 0),
                VerticalAlignment = VerticalAlignment.Center,
                ToolTip = "Clears all circuit parameters from elements with this circuit label. Does NOT delete the visual tags.",
                Template = RoundedBtnTemplate(),
            };
            clearBtn.Click += (s, e) => OnClearCircuitData(capturedLabel);
            Grid.SetColumn(clearBtn, 7); grid.Children.Add(clearBtn);

            var row = new Border
            {
                Background = isSubRow ? MeToolsTheme.BrBg : MeToolsTheme.BrRow,
                BorderBrush = MeToolsTheme.BrBorder, BorderThickness = new Thickness(0, 0, 0, 1),
                Child = grid,
            };
            row.MouseEnter += (s, e) => row.Background = MeToolsTheme.BrActiveBg;
            row.MouseLeave += (s, e) => row.Background = isSubRow ? MeToolsTheme.BrBg : MeToolsTheme.BrRow;
            _allRows.Add(row);
            return row;
        }

        private void OnClearCircuitData(string circuitLabel)
        {
            var result = MessageBox.Show(
                $"This will clear all circuit parameters from all elements with circuit '{circuitLabel}'.\n\nVisual tags are NOT deleted -- delete those manually in Revit.\n\nContinue?",
                "Clear Circuit Data", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (result != MessageBoxResult.Yes) return;

            _handler.Request = new CircuitTaggerRequest
            {
                Action            = CircuitTaggerAction.ClearCircuitData,
                CircuitLabelToClear = circuitLabel,
            };
            _extEvent.Raise();
            UpdateStatusBar($"Clearing circuit data for '{circuitLabel}'...");
        }

        // Public method called by CircuitTaggerCommand.OnDocChanged
        public void RefreshStatsIfVisible()
        {
            if (_activePanel == _panStats) RefreshStats();
        }

        // ???????????????????????????????????????????????????????????????????
        // TAB 3 -- SETTINGS
        // ???????????????????????????????????????????????????????????????????
        private StackPanel BuildSettingsPanel()
        {
            var sp = new StackPanel { Visibility = Visibility.Collapsed };
            var s = _settingsData ?? new CircuitTaggerSettingsData();

            // -- Tag Placement -------------------------------------------------
            sp.Children.Add(SecH("Tag Placement"));
            sp.Children.Add(InfoBox("X offset: horizontal distance from element right edge to tag. Y offset: vertical shift (positive = up, negative = down). Stack gap: spacing between stacked tags on same wall."));

            var pGrid = new Grid { Margin = new Thickness(0, 4, 0, 0) };
            pGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            pGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
            pGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            pGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
            pGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var gapCard = InlineCard("X Offset (mm)", "Horizontal distance from element right edge to tag", out _tbSetGapMm);
            _tbSetGapMm.Text = s.GapMm.ToString();
            Grid.SetColumn(gapCard, 0); pGrid.Children.Add(gapCard);

            var yCard = InlineCard("Y Offset (mm)", "Vertical shift. Positive = up, negative = down", out _tbSetOffsetYMm);
            _tbSetOffsetYMm.Text = s.OffsetYMm.ToString();
            Grid.SetColumn(yCard, 2); pGrid.Children.Add(yCard);

            var stkCard2 = InlineCard("Stack Gap (mm)", "Gap between stacked tags on same wall", out _tbSetStackGapMm);
            _tbSetStackGapMm.Text = s.StackGapMm.ToString();
            Grid.SetColumn(stkCard2, 4); pGrid.Children.Add(stkCard2);
            sp.Children.Add(pGrid);
            _allInputs.Add(_tbSetGapMm); _allInputs.Add(_tbSetOffsetYMm); _allInputs.Add(_tbSetStackGapMm);

            sp.Children.Add(Div());

            // -- Secondary Label Style (matches Revit TextNoteType parameters exactly) ----
            sp.Children.Add(SecH("Secondary Label Style"));
            sp.Children.Add(InfoBox("All options match Revit's TextNoteType parameters exactly. Saved and applied when you click Apply & Tag."));

            // == GRAPHICS section ==
            sp.Children.Add(new TextBlock { Text = "GRAPHICS", FontSize = 9, FontWeight = FontWeights.SemiBold,
                Foreground = MeToolsTheme.BrMuted, Margin = new Thickness(0, 8, 0, 6) });

            // Color row
            var gfxGrid = new Grid { Margin = new Thickness(0, 0, 0, 8) };
            gfxGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            gfxGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
            gfxGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            gfxGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
            gfxGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            gfxGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            gfxGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(8) });
            gfxGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            // Color card with Revit color picker button
            var colorCard = new Border
            {
                Background = MeToolsTheme.BrSurface, BorderBrush = MeToolsTheme.BrBorder,
                BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(5),
                Padding = new Thickness(12, 10, 12, 10),
            };
            var colorSp = new StackPanel();
            colorSp.Children.Add(new TextBlock { Text = "COLOR", FontSize = 9, FontWeight = FontWeights.SemiBold,
                Foreground = MeToolsTheme.BrMuted, Margin = new Thickness(0, 0, 0, 6) });
            var colorRow = new StackPanel { Orientation = Orientation.Horizontal };
            _settingsColorSwatch = new Border
            {
                Width = 32, Height = 32, CornerRadius = new CornerRadius(4),
                BorderBrush = MeToolsTheme.BrBorder, BorderThickness = new Thickness(1),
                Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center,
                Cursor = Cursors.Hand, ToolTip = "Click to open Revit color picker",
            };
            try
            {
                var swc2 = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(s.SubLabelColorHex);
                _settingsColorSwatch.Background = new SolidColorBrush(swc2);
            }
            catch { _settingsColorSwatch.Background = new SolidColorBrush(Colors.Black); }

            _tbSetColorHex = new TextBox
            {
                Text = s.SubLabelColorHex, Width = 80, Height = 28, FontSize = 11,
                FontFamily = new FontFamily("Consolas"), Background = MeToolsTheme.BrInput,
                Foreground = MeToolsTheme.BrInputFg, BorderBrush = MeToolsTheme.BrBorder,
                BorderThickness = new Thickness(1), Padding = new Thickness(4, 0, 4, 0),
                VerticalContentAlignment = VerticalAlignment.Center,
                ToolTip = "Hex color e.g. #FF8000",
            };
            _tbSetColorHex.TextChanged += (se, ev) =>
            {
                try
                {
                    var hex = _tbSetColorHex.Text.Trim();
                    if (!hex.StartsWith("#")) hex = "#" + hex;
                    var c2 = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(hex);
                    _settingsColorSwatch.Background = new SolidColorBrush(c2);
                }
                catch { }
            };

            // Revit native color picker button
            var pickColorBtn = SmallBtn("Pick...", false, () =>
            {
                try
                {
                    var dlg = new Autodesk.Revit.UI.ColorSelectionDialog();

                    if (dlg.Show() == Autodesk.Revit.UI.ItemSelectionDialogResult.Confirmed)
                    {
                        var rc = dlg.SelectedColor;
                        var newHex = $"#{rc.Red:X2}{rc.Green:X2}{rc.Blue:X2}";
                        _tbSetColorHex.Text = newHex;
                        _settingsColorSwatch.Background = new SolidColorBrush(
                            System.Windows.Media.Color.FromRgb(rc.Red, rc.Green, rc.Blue));
                    }
                }
                catch (Exception ex2)
                {
                    MessageBox.Show("Color picker error: " + ex2.Message);
                }
            });
            pickColorBtn.Margin = new Thickness(8, 0, 0, 0);
            _allInputs.Add(_tbSetColorHex);
            colorRow.Children.Add(_settingsColorSwatch);
            colorRow.Children.Add(_tbSetColorHex);
            colorRow.Children.Add(pickColorBtn);
            colorSp.Children.Add(colorRow);
            colorCard.Child = colorSp;
            Grid.SetRow(colorCard, 0); Grid.SetColumn(colorCard, 0); gfxGrid.Children.Add(colorCard);

            // Line Weight card
            var lwCard = new Border
            {
                Background = MeToolsTheme.BrSurface, BorderBrush = MeToolsTheme.BrBorder,
                BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(5),
                Padding = new Thickness(12, 10, 12, 10),
            };
            var lwSp = new StackPanel();
            lwSp.Children.Add(new TextBlock { Text = "LINE WEIGHT", FontSize = 9, FontWeight = FontWeights.SemiBold,
                Foreground = MeToolsTheme.BrMuted, Margin = new Thickness(0, 0, 0, 6) });
            TextBox tbLW;
            var lwBox = new TextBox { Height = 28, FontSize = 12, Text = s.SubLabelLineWeight.ToString(),
                Background = MeToolsTheme.BrInput, Foreground = MeToolsTheme.BrInputFg,
                BorderBrush = MeToolsTheme.BrBorder, BorderThickness = new Thickness(1),
                Padding = new Thickness(6, 0, 6, 0), VerticalContentAlignment = VerticalAlignment.Center,
                ToolTip = "Revit line weight 1-16" };
            lwSp.Children.Add(lwBox); lwCard.Child = lwSp;
            tbLW = lwBox; _allInputs.Add(tbLW);
            Grid.SetRow(lwCard, 0); Grid.SetColumn(lwCard, 2); gfxGrid.Children.Add(lwCard);

            // Leader/Border Offset card
            TextBox tbLeaderOffset;
            var loCard2 = new Border
            {
                Background = MeToolsTheme.BrSurface, BorderBrush = MeToolsTheme.BrBorder,
                BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(5),
                Padding = new Thickness(12, 10, 12, 10),
            };
            var loSp2 = new StackPanel();
            loSp2.Children.Add(new TextBlock { Text = "LEADER/BORDER OFFSET (mm)", FontSize = 9, FontWeight = FontWeights.SemiBold,
                Foreground = MeToolsTheme.BrMuted, Margin = new Thickness(0, 0, 0, 6) });
            var loBox2 = new TextBox { Height = 28, FontSize = 12, Text = s.SubLabelLeaderOffsetMm.ToString(),
                Background = MeToolsTheme.BrInput, Foreground = MeToolsTheme.BrInputFg,
                BorderBrush = MeToolsTheme.BrBorder, BorderThickness = new Thickness(1),
                Padding = new Thickness(6, 0, 6, 0), VerticalContentAlignment = VerticalAlignment.Center,
                ToolTip = "Leader/border offset in mm" };
            loSp2.Children.Add(loBox2); loCard2.Child = loSp2;
            tbLeaderOffset = loBox2; _allInputs.Add(tbLeaderOffset);
            Grid.SetRow(loCard2, 0); Grid.SetColumn(loCard2, 4); gfxGrid.Children.Add(loCard2);

            // Row 2: Background, Show Border, Leader Arrowhead
            var bgCard = new Border
            {
                Background = MeToolsTheme.BrSurface, BorderBrush = MeToolsTheme.BrBorder,
                BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(5),
                Padding = new Thickness(12, 10, 12, 10),
            };
            var bgSp = new StackPanel();
            bgSp.Children.Add(new TextBlock { Text = "BACKGROUND + BORDER", FontSize = 9, FontWeight = FontWeights.SemiBold,
                Foreground = MeToolsTheme.BrMuted, Margin = new Thickness(0, 0, 0, 6) });
            _cbSetOpaque      = new CheckBox { Content = "Opaque Background",  IsChecked = s.SubLabelOpaque,      Foreground = MeToolsTheme.BrText, Margin = new Thickness(0, 0, 0, 4) };
            _cbSetShowBorder  = new CheckBox { Content = "Show Border",          IsChecked = s.SubLabelShowBorder,  Foreground = MeToolsTheme.BrText };
            bgSp.Children.Add(_cbSetOpaque); bgSp.Children.Add(_cbSetShowBorder);
            bgCard.Child = bgSp;
            Grid.SetRow(bgCard, 2); Grid.SetColumn(bgCard, 0); gfxGrid.Children.Add(bgCard);

            // Leader Arrowhead
            var laCard = new Border
            {
                Background = MeToolsTheme.BrSurface, BorderBrush = MeToolsTheme.BrBorder,
                BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(5),
                Padding = new Thickness(12, 10, 12, 10),
            };
            var laSp = new StackPanel();
            laSp.Children.Add(new TextBlock { Text = "LEADER ARROWHEAD", FontSize = 9, FontWeight = FontWeights.SemiBold,
                Foreground = MeToolsTheme.BrMuted, Margin = new Thickness(0, 0, 0, 6) });
            _cbSetHAlign = new ComboBox { Height = 28, FontSize = 11, IsEditable = false,
                Background = MeToolsTheme.BrInput, Foreground = MeToolsTheme.BrInputFg, BorderBrush = MeToolsTheme.BrBorder };
            foreach (var arrow in new[] { "None", "Arrow 30 Deg", "Arrow Filled 30 Deg", "Dot Small", "Dot Medium" })
                _cbSetHAlign.Items.Add(arrow);
            _cbSetHAlign.SelectedItem = s.SubLabelHAlign;
            _allCombos.Add(_cbSetHAlign);
            laSp.Children.Add(_cbSetHAlign); laCard.Child = laSp;
            Grid.SetRow(laCard, 2); Grid.SetColumn(laCard, 2); gfxGrid.Children.Add(laCard);

            sp.Children.Add(gfxGrid);
            sp.Children.Add(new Border { Height = 1, Background = MeToolsTheme.BrBorder, Margin = new Thickness(0, 10, 0, 10) });

            // == TEXT section ==
            sp.Children.Add(new TextBlock { Text = "TEXT", FontSize = 9, FontWeight = FontWeights.SemiBold,
                Foreground = MeToolsTheme.BrMuted, Margin = new Thickness(0, 0, 0, 6) });

            var txtGrid = new Grid { Margin = new Thickness(0, 0, 0, 8) };
            txtGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            txtGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
            txtGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(0.7, GridUnitType.Star) });
            txtGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
            txtGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(0.7, GridUnitType.Star) });
            txtGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            txtGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(8) });
            txtGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            // Font card with searchable ComboBox from system fonts
            var fontCard2 = new Border
            {
                Background = MeToolsTheme.BrSurface, BorderBrush = MeToolsTheme.BrBorder,
                BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(5),
                Padding = new Thickness(12, 10, 12, 10),
            };
            var fontSp = new StackPanel();
            fontSp.Children.Add(new TextBlock { Text = "TEXT FONT", FontSize = 9, FontWeight = FontWeights.SemiBold,
                Foreground = MeToolsTheme.BrMuted, Margin = new Thickness(0, 0, 0, 5) });

            // Searchable editable ComboBox
            var fontCombo = new ComboBox
            {
                Height = 32, FontSize = 12, IsEditable = true,
                Background = MeToolsTheme.BrInput, Foreground = MeToolsTheme.BrInputFg,
                BorderBrush = MeToolsTheme.BrBorder, BorderThickness = new Thickness(1),
                IsTextSearchEnabled = true,
            };

            // Populate with system fonts using WPF's font API (no System.Drawing needed)
            try
            {
                var systemFonts = System.Windows.Media.Fonts.SystemFontFamilies
                    .Select(f => f.Source)
                    .Where(n => !string.IsNullOrEmpty(n))
                    .OrderBy(n => n)
                    .ToList();
                foreach (var fontName in systemFonts)
                    fontCombo.Items.Add(fontName);
            }
            catch { }

            fontCombo.Text = s.SubLabelFontName;
            fontCombo.SelectionChanged += (se, ev) =>
            {
                if (fontCombo.SelectedItem is string fn && _tbSetFontName != null)
                    _tbSetFontName.Text = fn;
            };
            // Use TextInput event for editable ComboBox text tracking
            fontCombo.AddHandler(System.Windows.Controls.Primitives.TextBoxBase.TextChangedEvent,
                new System.Windows.Controls.TextChangedEventHandler((se, ev) =>
                {
                    if (_tbSetFontName != null) _tbSetFontName.Text = fontCombo.Text;
                }));
            fontSp.Children.Add(fontCombo);
            fontCard2.Child = fontSp;

            // Hidden TextBox to store selected font for OnSaveSettings compatibility
            _tbSetFontName = new TextBox { Text = s.SubLabelFontName, Visibility = Visibility.Collapsed };
            _allCombos.Add(fontCombo);
            _allInputs.Add(_tbSetFontName);

            Grid.SetRow(fontCard2, 0); Grid.SetColumn(fontCard2, 0); txtGrid.Children.Add(fontCard2);

            var sizeCard2 = InlineCard("Text Size (mm)", "Font size in mm e.g. 2.0", out _tbSetFontSizeMm);
            _tbSetFontSizeMm.Text = s.SubLabelFontSizeMm.ToString();
            Grid.SetRow(sizeCard2, 0); Grid.SetColumn(sizeCard2, 2); txtGrid.Children.Add(sizeCard2);

            TextBox tbTabSize;
            var tabCard = InlineCard("Tab Size (mm)", "Tab size in mm e.g. 12.7", out tbTabSize);
            tabCard.Tag = tbTabSize;
            tbTabSize.Text = s.SubLabelTabSizeMm.ToString();
            Grid.SetRow(tabCard, 0); Grid.SetColumn(tabCard, 4); txtGrid.Children.Add(tabCard);
            _allInputs.Add(tbTabSize);

            TextBox tbWidthFactor;
            var wfCard = InlineCard("Width Factor", "Text width factor e.g. 0.75", out tbWidthFactor);
            tbWidthFactor.Text = s.SubLabelWidthFactor.ToString();
            Grid.SetRow(wfCard, 2); Grid.SetColumn(wfCard, 2); txtGrid.Children.Add(wfCard);
            _allInputs.Add(tbWidthFactor);

            // Text style checkboxes
            var txtChkCard = new Border
            {
                Background = MeToolsTheme.BrSurface, BorderBrush = MeToolsTheme.BrBorder,
                BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(5),
                Padding = new Thickness(12, 10, 12, 10),
            };
            var txtChkSp = new StackPanel();
            txtChkSp.Children.Add(new TextBlock { Text = "TEXT STYLE", FontSize = 9, FontWeight = FontWeights.SemiBold,
                Foreground = MeToolsTheme.BrMuted, Margin = new Thickness(0, 0, 0, 6) });
            _cbSetBold      = new CheckBox { Content = "Bold",      IsChecked = s.SubLabelBold,      Foreground = MeToolsTheme.BrText, Margin = new Thickness(0, 0, 0, 4) };
            _cbSetItalic    = new CheckBox { Content = "Italic",    IsChecked = s.SubLabelItalic,    Foreground = MeToolsTheme.BrText, Margin = new Thickness(0, 0, 0, 4) };
            _cbSetUnderline = new CheckBox { Content = "Underline", IsChecked = s.SubLabelUnderline, Foreground = MeToolsTheme.BrText };
            txtChkSp.Children.Add(_cbSetBold); txtChkSp.Children.Add(_cbSetItalic); txtChkSp.Children.Add(_cbSetUnderline);
            txtChkCard.Child = txtChkSp;
            Grid.SetRow(txtChkCard, 2); Grid.SetColumn(txtChkCard, 0); txtGrid.Children.Add(txtChkCard);
            sp.Children.Add(txtGrid);
            _allInputs.Add(_tbSetFontName); _allInputs.Add(_tbSetFontSizeMm);

            // Store extra textboxes for OnSaveSettings via Tag
            tbLW.Tag         = "LineWeight";
            tbLeaderOffset.Tag = "LeaderOffset";
            tbTabSize.Tag    = "TabSize";
            tbWidthFactor.Tag = "WidthFactor";
            _extraSettingsTb = new[] { tbLW, tbLeaderOffset, tbTabSize, tbWidthFactor };

            sp.Children.Add(Div());

            // Save button            sp.Children.Add(Div());

            // Save button
            var saveBtn = MakeFooterBtn("Save Settings as Default", true, OnSaveSettings);
            saveBtn.Margin = new Thickness(0, 0, 0, 0);
            sp.Children.Add(saveBtn);
            sp.Children.Add(new TextBlock { Text = "Settings are saved to %APPDATA%\\METools\\circuit-tagger.json and applied on next tag placement.",
                FontSize = 10, Foreground = MeToolsTheme.BrMuted, Margin = new Thickness(0, 8, 0, 0),
                TextWrapping = TextWrapping.Wrap });
            return sp;
        }

        private void OnSaveSettings()
        {
            var d = _settingsData ?? new CircuitTaggerSettingsData();
            if (double.TryParse(_tbSetGapMm?.Text,     out var g))  d.GapMm      = g;
            if (double.TryParse(_tbSetOffsetYMm?.Text,  out var oy)) d.OffsetYMm  = oy;
            if (double.TryParse(_tbSetStackGapMm?.Text, out var sg)) d.StackGapMm = sg;

            d.SubLabelFontName    = _tbSetFontName?.Text?.Trim()   ?? d.SubLabelFontName;
            if (double.TryParse(_tbSetFontSizeMm?.Text, out var fs)) d.SubLabelFontSizeMm = fs;
            d.SubLabelColorHex    = _tbSetColorHex?.Text?.Trim()   ?? d.SubLabelColorHex;
            d.SubLabelBold        = _cbSetBold?.IsChecked      == true;
            d.SubLabelItalic      = _cbSetItalic?.IsChecked    == true;
            d.SubLabelUnderline   = _cbSetUnderline?.IsChecked == true;
            d.SubLabelShowBorder  = _cbSetShowBorder?.IsChecked == true;
            d.SubLabelOpaque      = _cbSetOpaque?.IsChecked    == true;
            d.SubLabelHAlign      = _cbSetHAlign?.SelectedItem?.ToString() ?? d.SubLabelHAlign;

            // Read extra fields stored in _extraSettingsTb
            if (_extraSettingsTb != null)
            {
                foreach (var tb in _extraSettingsTb)
                {
                    if (tb?.Tag is string tag)
                    {
                        if (tag == "LineWeight"    && int.TryParse(tb.Text, out var lw))   d.SubLabelLineWeight     = lw;
                        if (tag == "LeaderOffset"  && double.TryParse(tb.Text, out var lo)) d.SubLabelLeaderOffsetMm = lo;
                        if (tag == "TabSize"       && double.TryParse(tb.Text, out var ts)) d.SubLabelTabSizeMm      = ts;
                        if (tag == "WidthFactor"   && double.TryParse(tb.Text, out var wf)) d.SubLabelWidthFactor    = wf;
                    }
                }
            }

            _settingsData = d;
            CircuitTaggerSettings.Save(d);
            UpdateStatusBar("Settings saved.");
            MessageBox.Show("Settings saved. They will be applied on the next Apply & Tag.",
                "ME-Tools -- Settings Saved", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        // ???????????????????????????????????????????????????????????????????
        // FOOTER
        // ???????????????????????????????????????????????????????????????????
        private void BuildFooter()
        {
            var footer = new Border
            {
                Background = MeToolsTheme.BrFooter, BorderBrush = MeToolsTheme.BrBorder,
                BorderThickness = new Thickness(0, 1, 0, 0), Padding = new Thickness(12, 8, 12, 8),
            };
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var hint = new TextBlock { Text = "Select elements, fill parameters, then click Apply.",
                FontSize = 11, Foreground = MeToolsTheme.BrMuted, VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(hint, 0); grid.Children.Add(hint);

            var btnSp = new StackPanel { Orientation = Orientation.Horizontal };
            var btnExp = MakeFooterBtn("Export Excel", false, OnExportClicked);
            btnExp.Margin = new Thickness(0, 0, 8, 0);
            btnSp.Children.Add(btnExp);
            btnSp.Children.Add(MakeFooterBtn("Apply & Tag", true, OnApplyClicked));
            Grid.SetColumn(btnSp, 1); grid.Children.Add(btnSp);

            footer.Child = grid;
            DockPanel.SetDock(footer, Dock.Bottom);
            RootDock.Children.Add(footer);
        }

        // ???????????????????????????????????????????????????????????????????
        // ACTIONS
        // ???????????????????????????????????????????????????????????????????
        private void OnSelectClicked()
        {
            Hide();
            try
            {
                var uiDoc = _uiApp.ActiveUIDocument;
                if (uiDoc == null) { Show(); return; }
                var filter = new ElectricalElementFilter();
                var picked = uiDoc.Selection.PickObjects(
                    Autodesk.Revit.UI.Selection.ObjectType.Element, filter,
                    "Select elements to tag -- press Finish or ESC when done");
                var doc   = uiDoc.Document;
                var phase = new FilteredElementCollector(doc).OfClass(typeof(Phase)).Cast<Phase>().LastOrDefault();
                foreach (var ref_ in picked)
                {
                    if (_selected.Any(x => x.ElementId == ref_.ElementId)) continue;
                    var el = doc.GetElement(ref_.ElementId);
                    if (el == null) continue;
                    _selected.Add(new TaggedElementInfo
                    {
                        ElementId    = ref_.ElementId,
                        CategoryName = el.Category?.Name ?? "Element",
                        FamilyName   = (el as FamilyInstance)?.Symbol?.Family?.Name ?? el.Name ?? "",
                        RoomName     = GetRoomNameForEl(doc, el as FamilyInstance, phase),
                    });
                }
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException) { }
            catch (Exception ex) { MessageBox.Show(ex.Message, "Select Elements"); }
            finally { Show(); RefreshSelectionList(); }
        }

        private void OnClearClicked() { _selected.Clear(); RefreshSelectionList(); }

        private void OnLoadFromSelectionClicked()
        {
            _handler.Request = new CircuitTaggerRequest { Action = CircuitTaggerAction.LoadParamsFromSelection };
            _extEvent.Raise();
            UpdateStatusBar("Loading params from selected element...");
        }

        private void OnApplyClicked()
        {
            if (_selected.Count == 0)
            {
                MessageBox.Show("Please select at least one element first.",
                    "ME-Tools -- Circuit Tagger", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            _handler.TagStyle = new CircuitTagStyle
            {
                GapMm      = _settingsData?.GapMm      ?? 50.0,
                OffsetYMm  = _settingsData?.OffsetYMm  ?? 0.0,
                StackGapMm = _settingsData?.StackGapMm ?? 8.0,
            };
            _handler.Settings = _settingsData ?? new CircuitTaggerSettingsData();
            _handler.Request = new CircuitTaggerRequest
            {
                Action            = CircuitTaggerAction.WriteParamsAndPlaceTags,
                ElementIds        = _selected.Select(x => x.ElementId).ToList(),
                Vorsicherung      = _tbVorsicherung?.Text?.Trim()      ?? "",
                FI                = _tbFI?.Text?.Trim()                ?? "",
                Stromkreis        = _tbStromkreis?.Text?.Trim()        ?? "",
                SubIndex          = _tbSubIndex?.Text?.Trim()          ?? "",
                Beleuchtungskreis = _tbBeleuchtungskreis?.Text?.Trim() ?? "",
                Apartment         = _cbApartment?.Text?.Trim()         ?? "",
                Building          = _cbBuilding?.Text?.Trim()          ?? "",
                SubLabel          = _tbSubLabel?.Text?.Trim()          ?? "",
            };
            _extEvent.Raise();
            UpdateStatusBar("Writing parameters...");
        }

        private void OnExportClicked()
        {
            var doc = _uiApp.ActiveUIDocument?.Document;
            if (doc == null) return;
            var rows = CircuitTaggerHandler.ReadAllTaggedElements(doc);
            if (rows.Count == 0)
            {
                MessageBox.Show("No tagged elements found.", "Export", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            var dlg = new Microsoft.Win32.SaveFileDialog
            {
                Title = "Export Circuit Data", Filter = "CSV files (*.csv)|*.csv",
                FileName = $"CircuitExport_{DateTime.Now:yyyyMMdd_HHmm}", DefaultExt = ".csv",
            };
            if (dlg.ShowDialog() != true) return;
            try
            {
                var sb = new StringBuilder();
                sb.AppendLine("Building,Apartment,Circuit,Vorsicherung,FI,Stromkreis,Beleuchtungskreis,Category,Family,Room,ElementId");
                foreach (var r in rows.OrderBy(x => x.Building).ThenBy(x => x.Apartment).ThenBy(x => x.CircuitLabel))
                {
                    sb.AppendLine(string.Join(",", Q(r.Building), Q(r.Apartment), Q(r.CircuitLabel),
                        Q(r.Vorsicherung), Q(r.FI), Q(r.Stromkreis), Q(r.Beleuchtungskreis),
                        Q(r.Category), Q(r.FamilyName), Q(r.Room), Q(r.ElementId)));
                }
                File.WriteAllText(dlg.FileName, sb.ToString(), Encoding.UTF8);
                UpdateStatusBar($"Exported {rows.Count} rows.");
                MessageBox.Show($"Exported {rows.Count} rows.\n{dlg.FileName}", "Export Complete",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Export failed:\n" + ex.Message, "Export Error", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void RequestReadDropdowns()
        {
            _handler.Request = new CircuitTaggerRequest { Action = CircuitTaggerAction.ReadApartmentValues };
            _extEvent.Raise();
        }

        // ?? Selection list refresh ????????????????????????????????????????
        private void RefreshSelectionList()
        {
            if (_selectionList == null) return;
            _selectionList.Children.Clear();
            if (_selected.Count == 0)
            {
                _selectionList.Children.Add(new TextBlock
                {
                    Text = "No elements selected. Click '+ Select in Revit'.",
                    FontSize = 11, Foreground = MeToolsTheme.BrMuted, Margin = new Thickness(10, 8, 10, 8),
                });
            }
            else
            {
                foreach (var info in _selected)
                {
                    var row = new Grid { MinHeight = 28 };
                    row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                    row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                    row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(80) });
                    row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(24) });

                    var catBadge = new Border
                    {
                        CornerRadius = new CornerRadius(3), Padding = new Thickness(5, 1, 5, 1),
                        Margin = new Thickness(6, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center,
                        Background = MeToolsTheme.BrActiveBg, BorderBrush = MeToolsTheme.BrPetrol, BorderThickness = new Thickness(1),
                        Child = new TextBlock { Text = CatShort(info.CategoryName), FontSize = 9,
                            Foreground = MeToolsTheme.BrPetrol, FontWeight = FontWeights.SemiBold },
                    };
                    var famTb  = new TextBlock { Text = info.FamilyName, FontSize = 11, Foreground = MeToolsTheme.BrText,
                        VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis,
                        Margin = new Thickness(6, 0, 4, 0) };
                    var roomTb = new TextBlock { Text = info.RoomName, FontSize = 10, Foreground = MeToolsTheme.BrMuted,
                        VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis,
                        Margin = new Thickness(0, 0, 4, 0) };
                    var captured = info;
                    var removeBtn = new Button
                    {
                        Content = "x", Width = 18, Height = 18, FontSize = 10,
                        Background = Brushes.Transparent, BorderBrush = Brushes.Transparent,
                        Foreground = MeToolsTheme.BrMuted, Cursor = Cursors.Hand,
                        VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 4, 0),
                    };
                    removeBtn.Click += (s, e) => { _selected.Remove(captured); RefreshSelectionList(); };

                    Grid.SetColumn(catBadge,  0); row.Children.Add(catBadge);
                    Grid.SetColumn(famTb,     1); row.Children.Add(famTb);
                    Grid.SetColumn(roomTb,    2); row.Children.Add(roomTb);
                    Grid.SetColumn(removeBtn, 3); row.Children.Add(removeBtn);

                    var rowBorder = new Border
                    {
                        BorderBrush = MeToolsTheme.BrBorder, BorderThickness = new Thickness(0, 0, 0, 1),
                        Background = MeToolsTheme.BrRow, Child = row,
                    };
                    rowBorder.MouseEnter += (s, e) => rowBorder.Background = MeToolsTheme.BrActiveBg;
                    rowBorder.MouseLeave += (s, e) => rowBorder.Background = MeToolsTheme.BrRow;
                    _selectionList.Children.Add(rowBorder);
                }
            }
            if (_lblSelCount != null)
                _lblSelCount.Text = _selected.Count == 0 ? "0 elements selected"
                    : $"{_selected.Count} element{(_selected.Count == 1 ? "" : "s")} selected";
        }

        // ?? Theme ?????????????????????????????????????????????????????????
        protected override void OnThemeChanged()
        {
            foreach (var tb in _allInputs)  { tb.Background = MeToolsTheme.BrInput; tb.Foreground = MeToolsTheme.BrInputFg; tb.BorderBrush = MeToolsTheme.BrBorder; }
            foreach (var cb in _allCombos)  { cb.Background = MeToolsTheme.BrInput; cb.Foreground = MeToolsTheme.BrInputFg; cb.BorderBrush = MeToolsTheme.BrBorder; }
            foreach (var r  in _allRows)    { r.Background  = MeToolsTheme.BrRow;   r.BorderBrush = MeToolsTheme.BrBorder; }
            if (_activeTab != null) ShowTab(_activeTab, _activePanel);
        }

        // ?? UI Helpers ????????????????????????????????????????????????????
        private static TextBlock SecH(string text) => new TextBlock
        {
            Text = text.ToUpper(), FontSize = 10, FontWeight = FontWeights.SemiBold,
            Foreground = MeToolsTheme.BrMuted, Margin = new Thickness(0, 0, 0, 6),
        };

        private Border Div() => new Border
        {
            Height = 1, Background = MeToolsTheme.BrBorder, Margin = new Thickness(0, 16, 0, 16),
        };

        private Border InlineCard(string label, string hint, out TextBox tb)
        {
            var card = new Border
            {
                Background = MeToolsTheme.BrSurface, BorderBrush = MeToolsTheme.BrBorder,
                BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(5),
                Padding = new Thickness(12, 10, 12, 10),
            };
            var sp = new StackPanel();
            sp.Children.Add(new TextBlock { Text = label.ToUpper(), FontSize = 9, FontWeight = FontWeights.SemiBold,
                Foreground = MeToolsTheme.BrMuted, Margin = new Thickness(0, 0, 0, 5) });
            var box = new TextBox
            {
                Height = 32, FontSize = 13, FontFamily = new FontFamily("Consolas"), FontWeight = FontWeights.SemiBold,
                Background = MeToolsTheme.BrInput, Foreground = MeToolsTheme.BrInputFg,
                BorderBrush = MeToolsTheme.BrBorder, BorderThickness = new Thickness(1),
                Padding = new Thickness(6, 0, 6, 0), VerticalContentAlignment = VerticalAlignment.Center,
                ToolTip = hint,
            };
            sp.Children.Add(box);
            sp.Children.Add(new TextBlock { Text = hint, FontSize = 10,
                Foreground = MeToolsTheme.BrMuted, TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 4, 0, 0) });
            card.Child = sp; tb = box;
            return card;
        }

        private Border ComboCard(string label, string hint, out ComboBox cb)
        {
            var card = new Border
            {
                Background = MeToolsTheme.BrSurface, BorderBrush = MeToolsTheme.BrBorder,
                BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(5),
                Padding = new Thickness(12, 10, 12, 10),
            };
            var sp = new StackPanel();
            sp.Children.Add(new TextBlock { Text = label.ToUpper(), FontSize = 9, FontWeight = FontWeights.SemiBold,
                Foreground = MeToolsTheme.BrMuted, Margin = new Thickness(0, 0, 0, 5) });
            var combo = new ComboBox
            {
                Height = 32, FontSize = 13, IsEditable = true,
                FontFamily = new FontFamily("Consolas"), FontWeight = FontWeights.SemiBold,
                Background = MeToolsTheme.BrInput, Foreground = MeToolsTheme.BrInputFg,
                BorderBrush = MeToolsTheme.BrBorder, ToolTip = hint,
            };
            sp.Children.Add(combo);
            card.Child = sp; cb = combo;
            return card;
        }

        private UIElement CircuitBadge(string label, bool isSubRow)
        {
            if (string.IsNullOrEmpty(label)) return TC("--", small: true);
            return new Border
            {
                CornerRadius = new CornerRadius(3), Padding = new Thickness(5, 2, 5, 2),
                VerticalAlignment = VerticalAlignment.Center,
                Background = new SolidColorBrush(Color.FromArgb(isSubRow ? (byte)15 : (byte)30,
                    MeToolsTheme.CPetrol.R, MeToolsTheme.CPetrol.G, MeToolsTheme.CPetrol.B)),
                BorderBrush = MeToolsTheme.BrPetrol, BorderThickness = new Thickness(1),
                Child = new TextBlock { Text = label, FontSize = isSubRow ? 9 : 11,
                    FontWeight = FontWeights.Bold, FontFamily = new FontFamily("Consolas"),
                    Foreground = MeToolsTheme.BrPetrol },
            };
        }

        private UIElement CountBadge(int count, Color color)
        {
            if (count == 0)
            {
                var dash = TC("--", small: true);
                dash.HorizontalAlignment = HorizontalAlignment.Center;
                dash.Margin = new Thickness(0);
                return dash;
            }
            return new Border
            {
                CornerRadius = new CornerRadius(9), Padding = new Thickness(5, 1, 5, 1),
                Margin = new Thickness(4, 4, 4, 4), VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center,
                Background = new SolidColorBrush(Color.FromArgb(22, color.R, color.G, color.B)),
                BorderBrush = new SolidColorBrush(color), BorderThickness = new Thickness(1),
                Child = new TextBlock { Text = count.ToString(), FontSize = 10,
                    FontWeight = FontWeights.SemiBold, Foreground = new SolidColorBrush(color) },
            };
        }

        private TextBlock TC(string text, bool small = false) => new TextBlock
        {
            Text = text ?? "", FontSize = small ? 10 : 11, Foreground = MeToolsTheme.BrText,
            VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 4, 0),
            TextTrimming = TextTrimming.CharacterEllipsis,
        };

        private static Border EmptyRow(string text) => new Border
        {
            Padding = new Thickness(12, 10, 12, 10),
            Child = new TextBlock { Text = text, FontSize = 11, Foreground = MeToolsTheme.BrMuted,
                HorizontalAlignment = HorizontalAlignment.Center },
        };

        private Button MakeFooterBtn(string label, bool primary, Action onClick)
        {
            var bgN = primary ? MeToolsTheme.BrPetrol : MeToolsTheme.BrBtnBg;
            var bgH = primary ? MeToolsTheme.BrPetrolDark : MeToolsTheme.BrActiveBg;

            // Build a template that respects Padding properly
            var f = new System.Windows.FrameworkElementFactory(typeof(Border));
            f.SetBinding(Border.BackgroundProperty,        new System.Windows.Data.Binding("Background")        { RelativeSource = new System.Windows.Data.RelativeSource(System.Windows.Data.RelativeSourceMode.TemplatedParent) });
            f.SetBinding(Border.BorderBrushProperty,       new System.Windows.Data.Binding("BorderBrush")       { RelativeSource = new System.Windows.Data.RelativeSource(System.Windows.Data.RelativeSourceMode.TemplatedParent) });
            f.SetBinding(Border.BorderThicknessProperty,   new System.Windows.Data.Binding("BorderThickness")   { RelativeSource = new System.Windows.Data.RelativeSource(System.Windows.Data.RelativeSourceMode.TemplatedParent) });
            f.SetValue(Border.CornerRadiusProperty, new CornerRadius(5));
            var cp = new System.Windows.FrameworkElementFactory(typeof(ContentPresenter));
            cp.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            cp.SetValue(ContentPresenter.VerticalAlignmentProperty,   VerticalAlignment.Center);
            cp.SetValue(ContentPresenter.MarginProperty, new Thickness(20, 0, 20, 0));
            f.AppendChild(cp);
            var tmpl = new System.Windows.Controls.ControlTemplate(typeof(Button)) { VisualTree = f };

            var b = new Button
            {
                Content = label, Height = 32, FontSize = 12,
                FontWeight = primary ? FontWeights.SemiBold : FontWeights.Normal,
                Background = bgN,
                BorderBrush = primary ? MeToolsTheme.BrPetrol : MeToolsTheme.BrBtnBorder,
                BorderThickness = new Thickness(1),
                Foreground = primary ? Brushes.White : MeToolsTheme.BrText,
                Cursor = Cursors.Hand,
                Template = tmpl,
            };
            b.MouseEnter += (s, e) => b.Background = bgH;
            b.MouseLeave += (s, e) => b.Background = bgN;
            b.Click      += (s, e) => onClick();
            return b;
        }

        private Button SmallBtn(string label, bool primary, Action onClick)
        {
            var bgN = primary ? MeToolsTheme.BrPetrol : MeToolsTheme.BrBtnBg;
            var bgH = primary ? MeToolsTheme.BrPetrolDark : MeToolsTheme.BrActiveBg;

            var f = new System.Windows.FrameworkElementFactory(typeof(Border));
            f.SetBinding(Border.BackgroundProperty,      new System.Windows.Data.Binding("Background")      { RelativeSource = new System.Windows.Data.RelativeSource(System.Windows.Data.RelativeSourceMode.TemplatedParent) });
            f.SetBinding(Border.BorderBrushProperty,     new System.Windows.Data.Binding("BorderBrush")     { RelativeSource = new System.Windows.Data.RelativeSource(System.Windows.Data.RelativeSourceMode.TemplatedParent) });
            f.SetBinding(Border.BorderThicknessProperty, new System.Windows.Data.Binding("BorderThickness") { RelativeSource = new System.Windows.Data.RelativeSource(System.Windows.Data.RelativeSourceMode.TemplatedParent) });
            f.SetValue(Border.CornerRadiusProperty, new CornerRadius(5));
            var cp = new System.Windows.FrameworkElementFactory(typeof(ContentPresenter));
            cp.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            cp.SetValue(ContentPresenter.VerticalAlignmentProperty,   VerticalAlignment.Center);
            cp.SetValue(ContentPresenter.MarginProperty, new Thickness(14, 0, 14, 0));
            f.AppendChild(cp);
            var tmpl = new System.Windows.Controls.ControlTemplate(typeof(Button)) { VisualTree = f };

            var b = new Button
            {
                Content = label, Height = 30, FontSize = 11,
                FontWeight = primary ? FontWeights.SemiBold : FontWeights.Normal,
                Background = bgN, BorderBrush = primary ? MeToolsTheme.BrPetrol : MeToolsTheme.BrBtnBorder,
                BorderThickness = new Thickness(1),
                Foreground = primary ? Brushes.White : MeToolsTheme.BrText,
                Cursor = Cursors.Hand, Template = tmpl,
            };
            b.MouseEnter += (s, e) => b.Background = bgH;
            b.MouseLeave += (s, e) => b.Background = bgN;
            b.Click      += (s, e) => onClick();
            return b;
        }

        private void UpdateStatusBar(string msg) { if (StatusLeft != null) StatusLeft.Text = msg; }

        private static string CatShort(string cat)
        {
            if (cat.Contains("Lighting") && cat.Contains("Fixture")) return "LAMP";
            if (cat.Contains("Electrical") && cat.Contains("Fixture")) return "SOCK";
            if (cat.Contains("Device")) return "SW";
            if (cat.Contains("Equipment")) return "PANEL";
            return "EL";
        }

        private static string Q(string s)
        {
            s = s ?? "";
            if (s.Contains(",") || s.Contains("\"") || s.Contains("\n"))
                return "\"" + s.Replace("\"", "\"\"") + "\"";
            return s;
        }

        private static string GetRoomNameForEl(Document doc, FamilyInstance fi, Phase phase)
        {
            if (fi == null) return "";
            try { if (fi.Room  != null) return fi.Room.Name  ?? ""; } catch { }
            try { if (fi.Space != null) return fi.Space.Name ?? ""; } catch { }
            try
            {
                var lp = fi.Location as LocationPoint;
                if (lp != null)
                {
                    var r = phase != null ? doc.GetRoomAtPoint(lp.Point, phase) : doc.GetRoomAtPoint(lp.Point);
                    if (r != null) return r.Name ?? "";
                }
            }
            catch { }
            return "";
        }
    }

    public class ElectricalElementFilter : Autodesk.Revit.UI.Selection.ISelectionFilter
    {
        private static readonly BuiltInCategory[] Allowed = new[]
        {
            BuiltInCategory.OST_ElectricalFixtures, BuiltInCategory.OST_LightingFixtures,
            BuiltInCategory.OST_LightingDevices,    BuiltInCategory.OST_ElectricalEquipment,
            BuiltInCategory.OST_DataDevices,        BuiltInCategory.OST_FireAlarmDevices,
            BuiltInCategory.OST_CommunicationDevices, BuiltInCategory.OST_SecurityDevices,
            BuiltInCategory.OST_NurseCallDevices,   BuiltInCategory.OST_TelephoneDevices,
        };
        public bool AllowElement(Element elem)
        {
            if (elem?.Category == null) return false;
            return Allowed.Contains((BuiltInCategory)elem.Category.Id.IntegerValue);
        }
        public bool AllowReference(Reference reference, XYZ position) => false;
    }
}

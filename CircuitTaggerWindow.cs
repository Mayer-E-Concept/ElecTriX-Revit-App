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
        private ComboBox  _cbTagFamily;
        private List<TagFamilyOption> _tagFamilyOptions = new List<TagFamilyOption>();
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
        private StackPanel _untaggedPanel;
        private List<UntaggedElementInfo> _lastUntaggedResults;

        // Stats tab: circuit labels checked for bulk "Clear Selected", and the
        // button that triggers it -- persists across RefreshStats() calls that
        // rebuild the rows, so it's cleared explicitly rather than relying on
        // the (re-created) row checkboxes to reset it.
        private readonly HashSet<string> _selectedForClear = new HashSet<string>();
        private Button _btnClearSelected;
        private readonly List<CheckBox> _allRowCheckboxes = new List<CheckBox>();
        private CheckBox _selectAllCheckbox;

        // Status
        private TextBlock _lblStatus, _lblSelCount;

        // Theme tracking
        private readonly List<TextBox>  _allInputs = new List<TextBox>();
        private readonly List<ComboBox> _allCombos = new List<ComboBox>();
        private readonly List<Border>   _allRows   = new List<Border>();

        public CircuitTaggerWindow(UIApplication uiApp, ExternalEvent extEvent, CircuitTaggerHandler handler)
        {
            _uiApp = uiApp; _extEvent = extEvent; _handler = handler;
            S.SetLanguage(SettingsStore.Language ?? "en");
            InitWindow(S._("circuittagger.title"), 660);
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
                MessageBox.Show(msg, S._("circuittagger.title"), MessageBoxButton.OK, MessageBoxImage.Warning));
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
                UpdateStatusBar(S._("circuittagger.params_loaded"));
            });
        }

        // ?? Build ?????????????????????????????????????????????????????????
        private void Build()
        {
            BuildStatusBar(S._("circuittagger.ready_select"));
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

            _tabTag      = MakeTab(S._("circuittagger.tab_tag"),      MeToolsTheme.CPetrol, () => { ShowTab(_tabTag, _panTag); RefreshTagFamilyOptions(); });
            _tabStats    = MakeTab(S._("circuittagger.tab_stats"),    MeToolsTheme.COrange, () => { ShowTab(_tabStats, _panStats); RefreshStats(); });
            _tabSettings = MakeTab(S._("circuittagger.tab_settings"), MeToolsTheme.CGreen,  () => ShowTab(_tabSettings, _panSettings));
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
            sp.Children.Add(SecH(S._("circuittagger.element_selection")));
            var selRow = new Grid { Margin = new Thickness(0, 4, 0, 4) };
            selRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            selRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            _lblSelCount = new TextBlock { Text = S._("circuittagger.elements_selected_0"), FontSize = 11,
                Foreground = MeToolsTheme.BrMuted, VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(_lblSelCount, 0); selRow.Children.Add(_lblSelCount);
            var btnRow = new StackPanel { Orientation = Orientation.Horizontal };
            var btnSel  = SmallBtn(S._("circuittagger.select_in_revit"), true,  OnSelectClicked);
            var btnLoad = SmallBtn(S._("circuittagger.load"),               false, OnLoadFromSelectionClicked);
            btnLoad.ToolTip = S._("circuittagger.load_tip");
            var btnClr  = SmallBtn(S._("circuittagger.clear"),              false, OnClearClicked);
            btnSel.Margin  = new Thickness(0, 0, 6, 0);
            btnLoad.Margin = new Thickness(0, 0, 6, 0);
            btnRow.Children.Add(btnSel); btnRow.Children.Add(btnLoad); btnRow.Children.Add(btnClr);
            Grid.SetColumn(btnRow, 1); selRow.Children.Add(btnRow);
            sp.Children.Add(selRow);

            var selBox = new Border
            {
                BorderBrush = MeToolsTheme.BrBorder, BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(5), ClipToBounds = true, MinHeight = 60, MaxHeight = 160,
            };
            var selScroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
            _selectionList = new StackPanel();
            selScroll.Content = _selectionList; selBox.Child = selScroll;
            sp.Children.Add(selBox);

            sp.Children.Add(Div(10));

            // -- Tag Family: one row -- inline label, fixed-width combo,
            // Refresh button. No standalone section header, no visible hint
            // paragraph (the hint is a tooltip on the combo now). Lets
            // Stefan switch between e.g. a lamp/socket tag and a fire alarm
            // tag without leaving this window.
            var tfRow = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            tfRow.Children.Add(new TextBlock
            {
                Text = S._("circuittagger.tag_family_label"), FontSize = 11, FontWeight = FontWeights.SemiBold,
                Foreground = MeToolsTheme.BrMuted, VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 8, 0),
            });
            _cbTagFamily = CompactComboStrict(S._("circuittagger.tag_family_hint"), 210);
            _cbTagFamily.Margin = new Thickness(0, 0, 8, 0);
            tfRow.Children.Add(_cbTagFamily);
            var btnTfRefresh = SmallBtn(S._("circuittagger.refresh"), false, RefreshTagFamilyOptions);
            tfRow.Children.Add(btnTfRefresh);
            sp.Children.Add(tfRow);
            _allCombos.Add(_cbTagFamily);
            _cbTagFamily.SelectionChanged += (s, e) =>
            {
                var chosen = _cbTagFamily.SelectedItem as TagFamilyOption;
                if (chosen == null) return;
                _settingsData = _settingsData ?? new CircuitTaggerSettingsData();
                _settingsData.TagFamilyName = chosen.FamilyName;
                _settingsData.TagTypeName   = chosen.TypeName;
                CircuitTaggerSettings.Save(_settingsData);
            };
            RefreshTagFamilyOptions();

            sp.Children.Add(Div(10));

            // -- Circuit Parameters: narrow fields sized to what actually
            // goes in them (a couple of digits or letters), flowing in one
            // row instead of a tall 2-row grid. Per-field examples that used
            // to sit visibly below each box are now tooltips; the formula
            // itself stays as a compact caption since it's non-obvious.
            sp.Children.Add(SecH(S._("circuittagger.circuit_parameters")));
            sp.Children.Add(Caption(S._("circuittagger.circuit_params_hint")));

            var pRow = new WrapPanel { Orientation = Orientation.Horizontal };
            pRow.Children.Add(CompactField("Vorsicherung", S._("circuittagger.fuse_hint"), 58, out _tbVorsicherung));
            pRow.Children.Add(CompactField("FI", S._("circuittagger.fi_hint"), 40, out _tbFI));
            pRow.Children.Add(CompactField(S._("circuittagger.subindex_label"), S._("circuittagger.subindex_hint"), 40, out _tbSubIndex));
            pRow.Children.Add(CompactField("Stromkreis", S._("circuittagger.circuit_hint"), 54, out _tbStromkreis));
            pRow.Children.Add(CompactField("Beleuchtungskreis", S._("circuittagger.lighting_circuit_hint"), 54, out _tbBeleuchtungskreis));

            // Preview chip -- flows in the same row as the fields it previews
            var prevBox = new Border
            {
                Background = MeToolsTheme.BrSurface, BorderBrush = MeToolsTheme.BrBorder,
                BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(4),
                Padding = new Thickness(10, 3, 10, 3), Margin = new Thickness(0, 0, 0, 8),
                VerticalAlignment = VerticalAlignment.Top,
            };
            var prevSp = new StackPanel();
            prevSp.Children.Add(new TextBlock { Text = S._("circuittagger.preview"), FontSize = 8, FontWeight = FontWeights.SemiBold,
                Foreground = MeToolsTheme.BrMuted, Margin = new Thickness(0, 0, 0, 2) });
            var prevLabel = new TextBlock { Text = "--", FontSize = 16, FontWeight = FontWeights.Bold,
                FontFamily = new FontFamily("Consolas"), Foreground = MeToolsTheme.BrAccent };
            prevSp.Children.Add(prevLabel); prevBox.Child = prevSp;
            pRow.Children.Add(prevBox);

            sp.Children.Add(pRow);
            _allInputs.Add(_tbVorsicherung); _allInputs.Add(_tbFI); _allInputs.Add(_tbSubIndex);
            _allInputs.Add(_tbStromkreis); _allInputs.Add(_tbBeleuchtungskreis);

            Action updatePreview = () =>
            {
                var fi = (_tbFI?.Text ?? "").Trim();
                var sk = (_tbStromkreis?.Text ?? "").Trim();
                var sub = (_tbSubIndex?.Text ?? "").Trim();
                var lbl = fi + sk + (string.IsNullOrEmpty(sub) ? "" : "_" + sub);
                prevLabel.Text       = string.IsNullOrEmpty(lbl) ? "--" : lbl;
                prevLabel.Foreground = string.IsNullOrEmpty(lbl) ? MeToolsTheme.BrMuted : MeToolsTheme.BrAccent;
            };
            _tbFI.TextChanged         += (s, e) => updatePreview();
            _tbStromkreis.TextChanged += (s, e) => updatePreview();
            _tbSubIndex.TextChanged   += (s, e) => updatePreview();

            sp.Children.Add(Div(10));

            // -- Secondary Label + Group Tags share one row now -- each
            // field already carries its own small caps label, so a second
            // full section header per group would just be repeating that.
            // Captions stay (they explain non-obvious behavior); the
            // per-field hover tooltips are gone per request.
            sp.Children.Add(Caption(S._("circuittagger.secondary_tag_hint")));
            sp.Children.Add(Caption(S._("circuittagger.group_tags_hint")));

            var gRow = new Grid { Margin = new Thickness(0, 2, 0, 0) };
            gRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            gRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(16) });
            gRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            gRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
            gRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var secField = CompactField(S._("circuittagger.secondary_label"), null, 140, out _tbSubLabel);
            Grid.SetColumn(secField, 0); gRow.Children.Add(secField);

            var aptCard = ComboCard("Apartment / Group", null, out _cbApartment);
            Grid.SetColumn(aptCard, 2); gRow.Children.Add(aptCard);

            var bldCard = ComboCard("Building / Haus", null, out _cbBuilding);
            Grid.SetColumn(bldCard, 4); gRow.Children.Add(bldCard);
            sp.Children.Add(gRow);
            _allInputs.Add(_tbSubLabel);
            _allCombos.Add(_cbApartment); _allCombos.Add(_cbBuilding);

            sp.Children.Add(Div(10));
            _lblStatus = new TextBlock { Text = S._("circuittagger.ready"), FontSize = 11, Foreground = MeToolsTheme.BrMuted,
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
            hdrRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var hdrTb = SecH(S._("circuittagger.circuit_statistics"));
            Grid.SetColumn(hdrTb, 0); hdrRow.Children.Add(hdrTb);
            _btnClearSelected = SmallBtn(S._("circuittagger.clear_selected"), false, OnClearSelectedClicked);
            _btnClearSelected.Margin = new Thickness(12, 0, 0, 0);
            _btnClearSelected.Padding = new Thickness(16, 0, 16, 0);
            _btnClearSelected.IsEnabled = false;
            _btnClearSelected.Foreground = new SolidColorBrush(Color.FromRgb(180, 60, 60));
            Grid.SetColumn(_btnClearSelected, 1); hdrRow.Children.Add(_btnClearSelected);
            var btnRefStats = SmallBtn(S._("circuittagger.refresh"), false, () => RefreshStats());
            btnRefStats.Margin = new Thickness(8, 0, 0, 0);
            btnRefStats.Padding = new Thickness(16, 0, 16, 0);
            Grid.SetColumn(btnRefStats, 2); hdrRow.Children.Add(btnRefStats);
            sp.Children.Add(hdrRow);

            sp.Children.Add(InfoBox(S._("circuittagger.stats_hint")));
            var container = new Border
            {
                BorderBrush = MeToolsTheme.BrBorder, BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(4), ClipToBounds = true,
            };
            _statsList = new StackPanel();
            StatsHeader(_statsList);
            container.Child = _statsList; sp.Children.Add(container);

            // -- Find Untagged Elements: QA pass before issuing drawings --
            sp.Children.Add(new Border { Height = 1, Background = MeToolsTheme.BrBorder, Margin = new Thickness(0, 14, 0, 10) });
            var untaggedHdrRow = new Grid { Margin = new Thickness(0, 0, 0, 6) };
            untaggedHdrRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            untaggedHdrRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var untaggedHdrTb = SecH(S._("circuittagger.find_untagged"));
            Grid.SetColumn(untaggedHdrTb, 0); untaggedHdrRow.Children.Add(untaggedHdrTb);
            var btnFindUntagged = SmallBtn(S._("circuittagger.find_untagged_btn"), false, OnFindUntaggedClicked);
            btnFindUntagged.Padding = new Thickness(16, 0, 16, 0);
            Grid.SetColumn(btnFindUntagged, 1); untaggedHdrRow.Children.Add(btnFindUntagged);
            sp.Children.Add(untaggedHdrRow);
            sp.Children.Add(InfoBox(S._("circuittagger.find_untagged_hint")));

            _untaggedPanel = new StackPanel();
            sp.Children.Add(_untaggedPanel);

            return sp;
        }

        private void StatsHeader(StackPanel sp)
        {
            var grid = new Grid { Background = MeToolsTheme.BrHeader, MinHeight = 26 };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(28) });  // select-all checkbox
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(6) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(70) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(180) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); // empty spacer -- pushes badges to the right edge
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(52) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(52) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(42) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(54) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(72) }); // unused -- matches the row grid's Clear-button column exactly

            // Select-all/none checkbox for the whole visible list, in the header.
            var selectAllCb = new CheckBox
            {
                VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center,
                ToolTip = S._("circuittagger.select_all_tip"),
            };
            selectAllCb.Click += (s, e) =>
            {
                bool check = selectAllCb.IsChecked == true;
                foreach (var cb in _allRowCheckboxes) cb.IsChecked = check; // fires each row's Checked/Unchecked handler below
            };
            _selectAllCheckbox = selectAllCb;
            Grid.SetColumn(selectAllCb, 0); grid.Children.Add(selectAllCb);

            // Total column gets a subtle tint so it reads as a summary column,
            // not a fifth badge in the Sock./Lamp/Sw. sequence.
            var totalBg = new Border { Background = new SolidColorBrush(Color.FromArgb(18, MeToolsTheme.COrange.R, MeToolsTheme.COrange.G, MeToolsTheme.COrange.B)) };
            Grid.SetColumn(totalBg, 8); grid.Children.Add(totalBg);

            // Thin vertical dividers between the four numeric columns -- this is
            // what makes it impossible to miscount which value sits under which
            // header (the actual cause of the earlier Sock./Sw. mix-up).
            foreach (int col in new[] { 5, 6, 7, 8 })
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
                (3, "Circuit / Vorsicherung"),
                (5, S._("circuittagger.col_sock")),
                (6, S._("circuittagger.col_lamp")),
                (7, S._("circuittagger.col_sw")),
                (8, S._("circuittagger.col_total")),
            };
            foreach (var (col, text) in headers)
            {
                var tb = new TextBlock
                {
                    Text = text, FontSize = 9, FontWeight = FontWeights.SemiBold,
                    Foreground = MeToolsTheme.BrMuted, VerticalAlignment = VerticalAlignment.Center,
                    HorizontalAlignment = col >= 5 ? HorizontalAlignment.Center : HorizontalAlignment.Left,
                    Margin = new Thickness(col >= 5 ? 4 : 8, 0, 4, 0),
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
            UpdateStatusBar(S._("circuittagger.stats_refreshing"));
            _statsList.Children.Clear();
            _allRowCheckboxes.Clear();
            _selectedForClear.Clear();
            if (_selectAllCheckbox != null) _selectAllCheckbox.IsChecked = false;
            UpdateClearSelectedButtonState();
            StatsHeader(_statsList); // re-add header after clear

            var doc = _uiApp.ActiveUIDocument?.Document;
            if (doc == null) { UpdateStatusBar(S._("circuittagger.no_tagged_found")); return; }

            var rows = CircuitTaggerHandler.ReadAllTaggedElements(doc);
            if (rows.Count == 0) { _statsList.Children.Add(EmptyRow(S._("circuittagger.no_tagged_found"))); UpdateStatusBar(S._("circuittagger.no_tagged_found")); return; }

            // Group: building -> apartment -> circuit base -> sub-circuits
            var byBuilding = rows
                .GroupBy(r => r.Building ?? "")
                .OrderBy(g => g.Key);

            bool anyRow = false;
            foreach (var bldGrp in byBuilding)
            {
                // Building header
                _statsList.Children.Add(GroupHeader(
                    string.IsNullOrEmpty(bldGrp.Key) ? S._("circuittagger.no_building") : S._("circuittagger.building_prefix") + bldGrp.Key,
                    MeToolsTheme.CPetrol));

                var byApt = bldGrp.GroupBy(r => r.Apartment ?? "").OrderBy(g => g.Key);
                foreach (var aptGrp in byApt)
                {
                    // Apartment header
                    _statsList.Children.Add(GroupHeader(
                        "  " + (string.IsNullOrEmpty(aptGrp.Key) ? S._("circuittagger.no_apartment") : aptGrp.Key),
                        MeToolsTheme.COrange));

                    // Group circuits by base (strip sub-index)
                    var byBase = aptGrp
                        .GroupBy(r => CircuitTaggerHandler.GetCircuitBase(r.CircuitLabel ?? ""))
                        .OrderBy(g => g.Key);

                    foreach (var baseGrp in byBase)
                    {
                        // All elements sharing this base (includes sub-circuits like
                        // "2F2_1") -- used below to find the sub-circuits themselves,
                        // but NOT for the parent row's own counts (see baseOnlyEl).
                        var allEl = baseGrp.ToList();

                        // Only the elements tagged with the exact base label (no
                        // sub-index) belong to the parent row. Using allEl here was
                        // the bug: it silently folded every sub-circuit's elements
                        // into the parent's counts too, so anything tagged "2F2_1"
                        // showed up in both "2F2" and "2F2_1" simultaneously.
                        var baseOnlyEl = allEl.Where(r => (r.CircuitLabel ?? "") == baseGrp.Key).ToList();
                        int sockets  = baseOnlyEl.Count(r => CatIsSocket(r));
                        int lamps    = baseOnlyEl.Count(r => CatIsLamp(r));
                        int switches = baseOnlyEl.Count(r => CatIsSwitch(r));
                        int other    = Math.Max(0, baseOnlyEl.Count - sockets - lamps - switches);

                        var stat = new CircuitStatRow
                        {
                            CircuitBase  = baseGrp.Key,
                            CircuitLabel = baseGrp.Key,
                            Vorsicherung = baseOnlyEl.FirstOrDefault()?.Vorsicherung ?? allEl.FirstOrDefault()?.Vorsicherung ?? "",
                            FI           = baseOnlyEl.FirstOrDefault()?.FI ?? allEl.FirstOrDefault()?.FI ?? "",
                            Apartment    = aptGrp.Key,
                            Building     = bldGrp.Key,
                            CountSockets = sockets + other,
                            CountLamps   = lamps,
                            CountSwitches = switches,
                            Elements     = baseOnlyEl,
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
                                Elements      = subEl,
                            };
                            _statsList.Children.Add(BuildStatsRow(sub, isSubRow: true));
                        }
                        anyRow = true;
                    }
                }
            }
            if (!anyRow) _statsList.Children.Add(EmptyRow(S._("circuittagger.no_tagged_found")));
            UpdateStatusBar(S._("circuittagger.stats_refreshed"));
        }

        // Runs synchronously, same as RefreshStats -- FindUntagged is a
        // read-only scan (FilteredElementCollector + parameter reads), which
        // this codebase already treats as safe to call directly from a
        // button click without an ExternalEvent (matches RefreshStats above
        // and Family Browser's Select Instances).
        private void OnFindUntaggedClicked()
        {
            var doc = _uiApp.ActiveUIDocument?.Document;
            if (doc == null) return;

            UpdateStatusBar(S._("circuittagger.finding_untagged"));
            _lastUntaggedResults = CircuitTaggerHandler.FindUntagged(doc);
            RenderUntaggedResults();
            UpdateStatusBar(string.Format(
                S._(_lastUntaggedResults.Count == 1 ? "circuittagger.untagged_found_1" : "circuittagger.untagged_found_n"),
                _lastUntaggedResults.Count));
        }

        private void RenderUntaggedResults()
        {
            if (_untaggedPanel == null) return;
            _untaggedPanel.Children.Clear();

            var results = _lastUntaggedResults;
            if (results == null) return;

            if (results.Count == 0)
            {
                _untaggedPanel.Children.Add(new TextBlock
                {
                    Text = S._("circuittagger.no_untagged_found"), FontSize = 11.5,
                    Foreground = MeToolsTheme.BrGreen, Margin = new Thickness(0, 2, 0, 4),
                });
                return;
            }

            var selectAllBtn = SmallBtn(S._("circuittagger.select_all_untagged"), true, OnSelectAllUntaggedClicked);
            selectAllBtn.Margin = new Thickness(0, 0, 0, 8);
            selectAllBtn.HorizontalAlignment = HorizontalAlignment.Left;
            _untaggedPanel.Children.Add(selectAllBtn);

            // Grouped by level, same visual language as the tagged Stats list
            // (GroupHeader), so this reads as part of the same tool rather
            // than a bolted-on list.
            foreach (var lvlGrp in results.GroupBy(r => string.IsNullOrEmpty(r.LevelName) ? S._("circuittagger.no_level") : r.LevelName)
                                          .OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase))
            {
                _untaggedPanel.Children.Add(GroupHeader(lvlGrp.Key + $" ({lvlGrp.Count()})", MeToolsTheme.CPetrol));

                foreach (var item in lvlGrp.OrderBy(r => r.RoomName, StringComparer.OrdinalIgnoreCase))
                {
                    var row = new TextBlock
                    {
                        FontSize = 11, Foreground = MeToolsTheme.BrText, Margin = new Thickness(14, 2, 0, 2),
                        Text = $"{item.FamilyName}  \u2014  {(string.IsNullOrEmpty(item.RoomName) ? "?" : item.RoomName)}",
                    };
                    _untaggedPanel.Children.Add(row);
                }
            }
        }

        private void OnSelectAllUntaggedClicked()
        {
            var uidoc = _uiApp?.ActiveUIDocument;
            var results = _lastUntaggedResults;
            if (uidoc == null || results == null || results.Count == 0) return;

            try
            {
                uidoc.Selection.SetElementIds(results.Select(r => r.ElementId).ToList());
                UpdateStatusBar(string.Format(S._("circuittagger.untagged_selected"), results.Count));
            }
            catch (Exception ex)
            {
                UpdateStatusBar(string.Format(S._("circuittagger.error"), ex.Message));
            }
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
        // -2008087 = OST_LightingDevices -- confirmed live against this project's
        // actual "_E_CAx Wechselschalter" switch family (was missing entirely,
        // which is why switches fell through into the socket count instead).
        private static bool CatIsSwitch(ExportRow r)  => r.CategoryId == -2008087 || r.CategoryId == -2001040
                                                       || r.CategoryId == -2008090 || r.CategoryId == -2008093
                                                       || r.CategoryId == -2008094 || r.CategoryId == -2008095;

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

        private FrameworkElement BuildStatsRow(CircuitStatRow stat, bool isSubRow = false)
        {
            var grid = new Grid { MinHeight = 32 };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(28) }); // select checkbox
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(isSubRow ? 20 : 6) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(70) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(180) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); // empty spacer -- matches header
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(52) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(52) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(42) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(54) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(72) });  // clear btn -- fixed, must match the empty spacer column added to the header (widened from 45 -- now that Padding actually applies (see RoundedBtnTemplate fix), the button's real margin+padding no longer fits in the old width)

            // Select checkbox for bulk clearing -- one per circuit row (base
            // AND sub-circuit rows each get their own, matching how the
            // per-row Clear button already treats them as independently
            // clearable circuits).
            var selectCb = new CheckBox
            {
                VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center,
                IsChecked = _selectedForClear.Contains(stat.CircuitLabel),
            };
            var capturedLabelForSelect = stat.CircuitLabel;
            selectCb.Checked   += (s, e) => { _selectedForClear.Add(capturedLabelForSelect);    UpdateClearSelectedButtonState(); };
            selectCb.Unchecked += (s, e) => { _selectedForClear.Remove(capturedLabelForSelect); UpdateClearSelectedButtonState(); };
            _allRowCheckboxes.Add(selectCb);
            Grid.SetColumn(selectCb, 0); grid.Children.Add(selectCb);

            // Total column tint + column dividers, matching StatsHeader exactly.
            var totalBg = new Border { Background = new SolidColorBrush(Color.FromArgb(14, MeToolsTheme.COrange.R, MeToolsTheme.COrange.G, MeToolsTheme.COrange.B)) };
            Grid.SetColumn(totalBg, 8); grid.Children.Add(totalBg);
            foreach (int col in new[] { 5, 6, 7, 8 })
            {
                var divider = new Border
                {
                    Width = 1, Background = MeToolsTheme.BrBorder,
                    HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 3, 0, 3),
                };
                Grid.SetColumn(divider, col); grid.Children.Add(divider);
            }

            // Circuit label + expand/collapse toggle, side by side.
            var labelRow = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            var expandArrow = new TextBlock
            {
                Text = "\u25B8", FontSize = 10, Foreground = MeToolsTheme.BrMuted,
                Margin = new Thickness(0, 0, 4, 0), VerticalAlignment = VerticalAlignment.Center,
                Cursor = Cursors.Hand,
            };
            labelRow.Children.Add(expandArrow);
            UIElement badge = CircuitBadge(stat.CircuitLabel, isSubRow);
            ((FrameworkElement)badge).Margin         = new Thickness(0, 4, 8, 4);
            ((FrameworkElement)badge).VerticalAlignment = VerticalAlignment.Center;
            ((FrameworkElement)badge).HorizontalAlignment = HorizontalAlignment.Left;
            labelRow.Children.Add(badge);
            Grid.SetColumn(labelRow, 2); grid.Children.Add(labelRow);

            var vs = TC(stat.Vorsicherung, small: true);
            Grid.SetColumn(vs, 3); grid.Children.Add(vs);

            var cb1 = CountBadge(stat.CountSockets,  MeToolsTheme.CPetrol);
            var cb2 = CountBadge(stat.CountLamps,     MeToolsTheme.CBlue);
            var cb3 = CountBadge(stat.CountSwitches,  MeToolsTheme.CGreen);
            var cb4 = CountBadge(stat.Total,          MeToolsTheme.COrange);
            Grid.SetColumn(cb1, 5); grid.Children.Add(cb1);
            Grid.SetColumn(cb2, 6); grid.Children.Add(cb2);
            Grid.SetColumn(cb3, 7); grid.Children.Add(cb3);
            Grid.SetColumn(cb4, 8); grid.Children.Add(cb4);

            // Clear button -- always visible on the right (clears just this one circuit)
            var capturedLabel = stat.CircuitLabel;
            var clearBtn = new Button
            {
                Content = S._("circuittagger.clear"), Height = 20, FontSize = 9,
                Background = new SolidColorBrush(Color.FromArgb(0, 0, 0, 0)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(180, 60, 60)),
                BorderThickness = new Thickness(1), Cursor = Cursors.Hand,
                Foreground = new SolidColorBrush(Color.FromRgb(180, 60, 60)),
                Margin = new Thickness(6, 0, 6, 0), Padding = new Thickness(8, 0, 8, 0),
                VerticalAlignment = VerticalAlignment.Center,
                ToolTip = S._("circuittagger.clear_row_tip"),
                Template = RoundedBtnTemplate(),
            };
            clearBtn.Click += (s, e) => SendClearRequest(new List<string> { capturedLabel });
            Grid.SetColumn(clearBtn, 9); grid.Children.Add(clearBtn);

            var row = new Border
            {
                Background = isSubRow ? MeToolsTheme.BrBg : MeToolsTheme.BrRow,
                BorderBrush = MeToolsTheme.BrBorder, BorderThickness = new Thickness(0, 0, 0, 1),
                Child = grid,
            };
            row.MouseEnter += (s, e) => row.Background = MeToolsTheme.BrActiveBg;
            row.MouseLeave += (s, e) => row.Background = isSubRow ? MeToolsTheme.BrBg : MeToolsTheme.BrRow;
            _allRows.Add(row);

            // Collapsible per-level breakdown, built lazily the first time it's
            // expanded (most rows will never be opened in a given session).
            Border detailPanel = null;
            bool expanded = false, built = false;
            expandArrow.MouseLeftButtonUp += (s, e) =>
            {
                expanded = !expanded;
                expandArrow.Text = expanded ? "\u25BE" : "\u25B8";
                if (expanded && !built)
                {
                    detailPanel.Child = BuildLevelBreakdown(stat.Elements, isSubRow);
                    built = true;
                }
                detailPanel.Visibility = expanded ? Visibility.Visible : Visibility.Collapsed;
                e.Handled = true;
            };

            detailPanel = new Border { Visibility = Visibility.Collapsed };

            var wrapper = new StackPanel();
            wrapper.Children.Add(row);
            wrapper.Children.Add(detailPanel);
            return wrapper;
        }

        // Groups a circuit's tagged elements by level, for the Stats tab's
        // expandable detail view: circuit -> level sub-heading -> individual
        // items. LevelName comes from CircuitTaggerHandler.GetLevelName,
        // which checks INSTANCE_SCHEDULE_ONLY_LEVEL_PARAM before falling back
        // to Element.LevelId -- same method Fix Level and Activity Log use,
        // since plain Element.LevelId is frequently blank for these families.
        private FrameworkElement BuildLevelBreakdown(List<ExportRow> elements, bool isSubRow)
        {
            var panel = new StackPanel { Margin = new Thickness(isSubRow ? 44 : 30, 4, 10, 8) };
            if (elements == null || elements.Count == 0)
            {
                panel.Children.Add(new TextBlock
                {
                    Text = S._("circuittagger.no_element_details"), FontSize = 10.5,
                    Foreground = MeToolsTheme.BrMuted, FontStyle = FontStyles.Italic,
                });
                return panel;
            }

            var byLevel = elements
                .GroupBy(e => string.IsNullOrEmpty(e.LevelName) ? S._("circuittagger.no_level") : e.LevelName)
                .OrderBy(g => g.Key);

            foreach (var lvlGrp in byLevel)
            {
                panel.Children.Add(new TextBlock
                {
                    Text = lvlGrp.Key, FontSize = 10.5, FontWeight = FontWeights.SemiBold,
                    Foreground = MeToolsTheme.BrAccent, Margin = new Thickness(0, 6, 0, 2),
                });
                foreach (var el in lvlGrp.OrderBy(e => e.FamilyName))
                {
                    string desc = string.IsNullOrEmpty(el.Room) ? el.FamilyName : $"{el.FamilyName}  ({el.Room})";
                    panel.Children.Add(new TextBlock
                    {
                        Text = $"{desc}  \u2022  ID {el.ElementId}", FontSize = 10,
                        Foreground = MeToolsTheme.BrMuted, Margin = new Thickness(10, 0, 0, 1),
                        TextWrapping = TextWrapping.Wrap,
                    });
                }
            }
            return panel;
        }

        // Shared by the per-row "Clear" button (single-item list) and
        // "Clear Selected" (however many are checked) -- one confirmation,
        // one request, one Revit-thread round trip regardless of count.
        private void SendClearRequest(List<string> circuitLabels)
        {
            if (circuitLabels == null || circuitLabels.Count == 0) return;

            string prompt = circuitLabels.Count == 1
                ? string.Format(S._("circuittagger.clear_confirm_1"), circuitLabels[0])
                : string.Format(S._("circuittagger.clear_confirm_n"), circuitLabels.Count)
                  + string.Join(", ", circuitLabels.Take(15)) + (circuitLabels.Count > 15 ? string.Format(S._("circuittagger.clear_confirm_more"), circuitLabels.Count - 15) : "")
                  + S._("circuittagger.clear_confirm_tail");

            var result = MessageBox.Show(prompt, S._("circuittagger.clear_title"), MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (result != MessageBoxResult.Yes) return;

            _handler.Request = new CircuitTaggerRequest
            {
                Action = CircuitTaggerAction.ClearCircuitData,
                CircuitLabelsToClear = circuitLabels,
            };
            _extEvent.Raise();
            UpdateStatusBar(circuitLabels.Count == 1
                ? string.Format(S._("circuittagger.clearing_1"), circuitLabels[0])
                : string.Format(S._("circuittagger.clearing_n"), circuitLabels.Count));

            // Optimistic: RefreshStats() (triggered by OnDone once the clear
            // finishes) rebuilds the rows and clears selection anyway, but
            // resetting the button now avoids a double-click queuing a second
            // clear on the same labels while the first is still in flight.
            _selectedForClear.Clear();
            UpdateClearSelectedButtonState();
        }

        private void OnClearSelectedClicked() => SendClearRequest(_selectedForClear.ToList());

        private void UpdateClearSelectedButtonState()
        {
            if (_btnClearSelected == null) return;
            int n = _selectedForClear.Count;
            _btnClearSelected.IsEnabled = n > 0;
            _btnClearSelected.Content = n > 0 ? string.Format(S._("circuittagger.clear_selected_n"), n) : S._("circuittagger.clear_selected");
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
            sp.Children.Add(SecH(S._("circuittagger.tag_placement")));
            sp.Children.Add(InfoBox(S._("circuittagger.tag_placement_hint")));

            var pGrid = new Grid { Margin = new Thickness(0, 4, 0, 0) };
            pGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            pGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
            pGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            pGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
            pGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var gapCard = InlineCard(S._("circuittagger.x_offset"), S._("circuittagger.x_offset_hint"), out _tbSetGapMm);
            _tbSetGapMm.Text = s.GapMm.ToString();
            Grid.SetColumn(gapCard, 0); pGrid.Children.Add(gapCard);

            var yCard = InlineCard(S._("circuittagger.y_offset"), S._("circuittagger.y_offset_hint"), out _tbSetOffsetYMm);
            _tbSetOffsetYMm.Text = s.OffsetYMm.ToString();
            Grid.SetColumn(yCard, 2); pGrid.Children.Add(yCard);

            var stkCard2 = InlineCard(S._("circuittagger.stack_gap"), S._("circuittagger.stack_gap_hint"), out _tbSetStackGapMm);
            _tbSetStackGapMm.Text = s.StackGapMm.ToString();
            Grid.SetColumn(stkCard2, 4); pGrid.Children.Add(stkCard2);
            sp.Children.Add(pGrid);
            _allInputs.Add(_tbSetGapMm); _allInputs.Add(_tbSetOffsetYMm); _allInputs.Add(_tbSetStackGapMm);

            sp.Children.Add(Div(16));

            // -- Secondary Label Style (matches Revit TextNoteType parameters exactly) ----
            sp.Children.Add(SecH(S._("circuittagger.secondary_label_style")));
            sp.Children.Add(InfoBox(S._("circuittagger.label_style_hint")));

            // == GRAPHICS section ==
            sp.Children.Add(new TextBlock { Text = S._("circuittagger.graphics"), FontSize = 9, FontWeight = FontWeights.SemiBold,
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
            colorSp.Children.Add(new TextBlock { Text = S._("circuittagger.color"), FontSize = 9, FontWeight = FontWeights.SemiBold,
                Foreground = MeToolsTheme.BrMuted, Margin = new Thickness(0, 0, 0, 6) });
            var colorRow = new StackPanel { Orientation = Orientation.Horizontal };
            _settingsColorSwatch = new Border
            {
                Width = 32, Height = 32, CornerRadius = new CornerRadius(4),
                BorderBrush = MeToolsTheme.BrBorder, BorderThickness = new Thickness(1),
                Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center,
                Cursor = Cursors.Hand, ToolTip = S._("circuittagger.color_picker_tip"),
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
                ToolTip = S._("circuittagger.hex_color_tip"),
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
            var pickColorBtn = SmallBtn(S._("circuittagger.pick"), false, () =>
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
                    MessageBox.Show(S._("circuittagger.color_picker_error") + ex2.Message);
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
            lwSp.Children.Add(new TextBlock { Text = S._("circuittagger.line_weight"), FontSize = 9, FontWeight = FontWeights.SemiBold,
                Foreground = MeToolsTheme.BrMuted, Margin = new Thickness(0, 0, 0, 6) });
            TextBox tbLW;
            var lwBox = new TextBox { Height = 28, FontSize = 12, Text = s.SubLabelLineWeight.ToString(),
                Background = MeToolsTheme.BrInput, Foreground = MeToolsTheme.BrInputFg,
                BorderBrush = MeToolsTheme.BrBorder, BorderThickness = new Thickness(1),
                Padding = new Thickness(6, 0, 6, 0), VerticalContentAlignment = VerticalAlignment.Center,
                ToolTip = S._("circuittagger.line_weight_tip") };
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
            loSp2.Children.Add(new TextBlock { Text = S._("circuittagger.leader_offset"), FontSize = 9, FontWeight = FontWeights.SemiBold,
                Foreground = MeToolsTheme.BrMuted, Margin = new Thickness(0, 0, 0, 6) });
            var loBox2 = new TextBox { Height = 28, FontSize = 12, Text = s.SubLabelLeaderOffsetMm.ToString(),
                Background = MeToolsTheme.BrInput, Foreground = MeToolsTheme.BrInputFg,
                BorderBrush = MeToolsTheme.BrBorder, BorderThickness = new Thickness(1),
                Padding = new Thickness(6, 0, 6, 0), VerticalContentAlignment = VerticalAlignment.Center,
                ToolTip = S._("circuittagger.leader_offset_tip") };
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
            bgSp.Children.Add(new TextBlock { Text = S._("circuittagger.background_border"), FontSize = 9, FontWeight = FontWeights.SemiBold,
                Foreground = MeToolsTheme.BrMuted, Margin = new Thickness(0, 0, 0, 6) });
            _cbSetOpaque      = new CheckBox { Content = S._("circuittagger.opaque_bg"),  IsChecked = s.SubLabelOpaque,      Foreground = MeToolsTheme.BrText, Margin = new Thickness(0, 0, 0, 4) };
            _cbSetShowBorder  = new CheckBox { Content = S._("circuittagger.show_border"),          IsChecked = s.SubLabelShowBorder,  Foreground = MeToolsTheme.BrText };
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
            laSp.Children.Add(new TextBlock { Text = S._("circuittagger.leader_arrowhead"), FontSize = 9, FontWeight = FontWeights.SemiBold,
                Foreground = MeToolsTheme.BrMuted, Margin = new Thickness(0, 0, 0, 6) });
            _cbSetHAlign = new ComboBox { Height = 28, FontSize = 11, IsEditable = false };
            ApplyComboStyle(_cbSetHAlign); // was setting Background/Foreground/BorderBrush directly, which the default Template mostly ignores -- same gap as CompactComboStrict/ComboCard in the shared base file
            foreach (var arrow in new[] { "None", "Arrow 30 Deg", "Arrow Filled 30 Deg", "Dot Small", "Dot Medium" })
                _cbSetHAlign.Items.Add(arrow);
            _cbSetHAlign.SelectedItem = s.SubLabelHAlign;
            _allCombos.Add(_cbSetHAlign);
            laSp.Children.Add(_cbSetHAlign); laCard.Child = laSp;
            Grid.SetRow(laCard, 2); Grid.SetColumn(laCard, 2); gfxGrid.Children.Add(laCard);

            sp.Children.Add(gfxGrid);
            sp.Children.Add(new Border { Height = 1, Background = MeToolsTheme.BrBorder, Margin = new Thickness(0, 10, 0, 10) });

            // == TEXT section ==
            sp.Children.Add(new TextBlock { Text = S._("circuittagger.text"), FontSize = 9, FontWeight = FontWeights.SemiBold,
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
            fontSp.Children.Add(new TextBlock { Text = S._("circuittagger.text_font"), FontSize = 9, FontWeight = FontWeights.SemiBold,
                Foreground = MeToolsTheme.BrMuted, Margin = new Thickness(0, 0, 0, 5) });

            // Searchable editable ComboBox
            var fontCombo = new ComboBox
            {
                Height = 32, FontSize = 12, IsEditable = true,
                IsTextSearchEnabled = true,
            };
            ApplyComboStyle(fontCombo); // same fix -- IsEditable=true here specifically needed the shared template's new PART_EditableTextBox to keep typing working

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

            var sizeCard2 = InlineCard(S._("circuittagger.text_size"), S._("circuittagger.text_size_hint"), out _tbSetFontSizeMm);
            _tbSetFontSizeMm.Text = s.SubLabelFontSizeMm.ToString();
            Grid.SetRow(sizeCard2, 0); Grid.SetColumn(sizeCard2, 2); txtGrid.Children.Add(sizeCard2);

            TextBox tbTabSize;
            var tabCard = InlineCard(S._("circuittagger.tab_size"), S._("circuittagger.tab_size_hint"), out tbTabSize);
            tabCard.Tag = tbTabSize;
            tbTabSize.Text = s.SubLabelTabSizeMm.ToString();
            Grid.SetRow(tabCard, 0); Grid.SetColumn(tabCard, 4); txtGrid.Children.Add(tabCard);
            _allInputs.Add(tbTabSize);

            TextBox tbWidthFactor;
            var wfCard = InlineCard(S._("circuittagger.width_factor"), S._("circuittagger.width_factor_hint"), out tbWidthFactor);
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
            txtChkSp.Children.Add(new TextBlock { Text = S._("circuittagger.text_style"), FontSize = 9, FontWeight = FontWeights.SemiBold,
                Foreground = MeToolsTheme.BrMuted, Margin = new Thickness(0, 0, 0, 6) });
            _cbSetBold      = new CheckBox { Content = S._("circuittagger.bold"),      IsChecked = s.SubLabelBold,      Foreground = MeToolsTheme.BrText, Margin = new Thickness(0, 0, 0, 4) };
            _cbSetItalic    = new CheckBox { Content = S._("circuittagger.italic"),    IsChecked = s.SubLabelItalic,    Foreground = MeToolsTheme.BrText, Margin = new Thickness(0, 0, 0, 4) };
            _cbSetUnderline = new CheckBox { Content = S._("circuittagger.underline"), IsChecked = s.SubLabelUnderline, Foreground = MeToolsTheme.BrText };
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

            sp.Children.Add(Div(16));

            // Save button            sp.Children.Add(Div());

            // Save button
            var saveBtn = MakeFooterBtn(S._("circuittagger.save_defaults"), true, OnSaveSettings);
            saveBtn.Margin = new Thickness(0, 0, 0, 0);
            sp.Children.Add(saveBtn);
            sp.Children.Add(new TextBlock { Text = S._("circuittagger.settings_saved_path_hint"),
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
            UpdateStatusBar(S._("circuittagger.settings_saved"));
            MessageBox.Show(S._("circuittagger.settings_saved_msg"),
                S._("circuittagger.settings_saved_title"), MessageBoxButton.OK, MessageBoxImage.Information);
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

            var hint = new TextBlock { Text = S._("circuittagger.footer_hint"),
                FontSize = 11, Foreground = MeToolsTheme.BrMuted, VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(hint, 0); grid.Children.Add(hint);

            var btnSp = new StackPanel { Orientation = Orientation.Horizontal };
            var btnExp = MakeFooterBtn(S._("circuittagger.export_excel"), false, OnExportClicked);
            btnExp.Margin = new Thickness(0, 0, 8, 0);
            btnSp.Children.Add(btnExp);
            btnSp.Children.Add(MakeFooterBtn(S._("circuittagger.apply_and_tag"), true, OnApplyClicked));
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
            var uiDoc = _uiApp?.ActiveUIDocument;
            if (uiDoc == null) { Show(); return; }
            var doc   = uiDoc.Document;
            var view  = uiDoc.ActiveView;
            var phase = new FilteredElementCollector(doc).OfClass(typeof(Phase)).Cast<Phase>().LastOrDefault();
            var filter = new ElectricalElementFilter();

            // BUG FIXED HERE: this used to call PickObjects (plural) once
            // and only ever add anything to _selected after the WHOLE pick
            // loop finished. PickObjects is all-or-nothing -- a stray click
            // on empty space mid-loop (missing an intended element, or just
            // clicking the plan by mistake) can silently clear everything
            // picked so far IN THAT SAME LOOP, a real and well-known Revit
            // quirk, not something this app was doing. With a large
            // selection, that meant one bad click near the end could wipe
            // the entire batch, which read as "there's a limit" -- there
            // isn't one, it just needed a more robust picking loop.
            //
            // Now: pick ONE element at a time and commit it to _selected
            // immediately, so a later stray click can only ever cost that
            // one attempt, never anything already committed. Also marks
            // already-queued elements with a graphic override (see
            // SetPendingMark) before the first pick and after every new
            // one, so it's visually obvious what's already queued when
            // coming back to add more after a previous round.
            // BUG FIXED HERE (again): the mark itself was applying correctly
            // (confirmed -- this isn't the same "wiped by PickObject" issue
            // as the old ambient-selection approach), but it was never
            // actually PAINTED before the next blocking PickObject call
            // seized the interactive loop. Committing a transaction queues a
            // repaint; it doesn't force one to happen immediately, and
            // jumping straight into a modal pick call can mean that queued
            // repaint never gets flushed. RefreshActiveView() forces it
            // synchronously right now, before Revit's own pick-mode cursor
            // takes over -- the same fix Autodesk's own forum recommends for
            // this exact "highlight during PickObjects" scenario.
            SetPendingMark(doc, view, _selected.Select(x => x.ElementId), true);
            try { uiDoc.RefreshActiveView(); } catch { }
            try
            {
                while (true)
                {
                    Reference picked;
                    try
                    {
                        picked = uiDoc.Selection.PickObject(
                            Autodesk.Revit.UI.Selection.ObjectType.Element, filter,
                            S._("circuittagger.select_prompt"));
                    }
                    catch (Autodesk.Revit.Exceptions.OperationCanceledException) { break; }

                    if (picked == null) continue;
                    if (_selected.Any(x => x.ElementId == picked.ElementId)) continue; // already queued -- ignore, don't duplicate
                    var el = doc.GetElement(picked.ElementId);
                    if (el == null) continue;

                    _selected.Add(new TaggedElementInfo
                    {
                        ElementId    = picked.ElementId,
                        CategoryName = el.Category?.Name ?? "Element",
                        CategoryId   = (int)(el.Category?.Id?.Value ?? 0),
                        FamilyName   = (el as FamilyInstance)?.Symbol?.Family?.Name ?? el.Name ?? "",
                        RoomName     = GetRoomNameForEl(doc, el as FamilyInstance, phase),
                    });
                    SetPendingMark(doc, view, new[] { picked.ElementId }, true); // mark the just-added element right away
                    try { uiDoc.RefreshActiveView(); } catch { }
                }
            }
            catch (Exception ex) { MessageBox.Show(ex.Message, S._("circuittagger.select_elements_title")); }
            finally
            {
                Show();
                RefreshSelectionList();
                // Deliberately NOT cleared here -- the whole point is that
                // the mark stays visible after this picking session ends,
                // so coming back later to add more still shows what's
                // already queued. See OnClearClicked and the per-row
                // remove button for where it actually gets cleared.
            }
        }

        // BUG FIXED HERE: this used to call Selection.SetElementIds() to
        // highlight already-queued elements, refreshed before and during
        // the pick loop. That never actually worked -- confirmed against
        // Autodesk's own Revit API forum (a long-standing, documented
        // behavior, not a bug specific to this app): calling PickObject or
        // PickObjects clears whatever's in the active Selection set the
        // instant the pick loop starts, so anything set via SetElementIds
        // right before it is wiped before it can ever be seen.
        //
        // Graphic overrides on the view are a genuinely different
        // mechanism -- they're a property of the view/element pair, not
        // of "selection" at all, so entering or leaving a pick loop has no
        // effect on them. Same technique already used elsewhere in this
        // file for the sub-label color override.
        private static readonly Autodesk.Revit.DB.Color PendingTagColor = new Autodesk.Revit.DB.Color(255, 60, 170); // bold magenta -- distinct from existing red linework and from Revit's own selection blue

        private void SetPendingMark(Document doc, View view, IEnumerable<ElementId> ids, bool on)
        {
            if (doc == null || view == null) return;
            var idList = ids?.Where(id => id != null && id != ElementId.InvalidElementId).ToList();
            if (idList == null || idList.Count == 0) return;
            try
            {
                using (var tx = new Transaction(doc, on ? "ME-Tools: Mark Pending Tag" : "ME-Tools: Clear Pending Tag Mark"))
                {
                    tx.Start();
                    foreach (var id in idList)
                    {
                        try
                        {
                            var ogs = new OverrideGraphicSettings();
                            if (on)
                            {
                                ogs.SetProjectionLineColor(PendingTagColor);
                                ogs.SetProjectionLineWeight(6);
                            }
                            view.SetElementOverrides(id, ogs); // no color/weight set = reset to default when on == false
                        }
                        catch { }
                    }
                    tx.Commit();
                }
            }
            catch { }
        }

        private void OnClearClicked()
        {
            var uiDoc = _uiApp?.ActiveUIDocument;
            SetPendingMark(uiDoc?.Document, uiDoc?.ActiveView, _selected.Select(x => x.ElementId), false);
            try { uiDoc?.RefreshActiveView(); } catch { }
            _selected.Clear();
            RefreshSelectionList();
        }

        private void OnLoadFromSelectionClicked()
        {
            _handler.Request = new CircuitTaggerRequest { Action = CircuitTaggerAction.LoadParamsFromSelection };
            _extEvent.Raise();
            UpdateStatusBar(S._("circuittagger.loading_params"));
        }

        private void OnApplyClicked()
        {
            if (_selected.Count == 0)
            {
                MessageBox.Show(S._("circuittagger.select_one_first"),
                    S._("circuittagger.title"), MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            _handler.TagStyle = new CircuitTagStyle
            {
                GapMm      = _settingsData?.GapMm      ?? 50.0,
                OffsetYMm  = _settingsData?.OffsetYMm  ?? 0.0,
                StackGapMm = _settingsData?.StackGapMm ?? 8.0,
            };
            _handler.Settings = _settingsData ?? new CircuitTaggerSettingsData();
            var chosenTagFamily = _cbTagFamily?.SelectedItem as TagFamilyOption;
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
                TagSymbolId          = chosenTagFamily?.SymbolId ?? ElementId.InvalidElementId,
                TagFamilyDisplayName = chosenTagFamily?.DisplayName ?? "",
            };
            _extEvent.Raise();
            UpdateStatusBar(S._("circuittagger.writing_params"));
        }

        private void OnExportClicked()
        {
            var doc = _uiApp.ActiveUIDocument?.Document;
            if (doc == null) return;
            var rows = CircuitTaggerHandler.ReadAllTaggedElements(doc);
            if (rows.Count == 0)
            {
                MessageBox.Show(S._("circuittagger.export_no_tagged"), S._("circuittagger.export_title"), MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            var dlg = new Microsoft.Win32.SaveFileDialog
            {
                Title = S._("circuittagger.export_circuit_data"), Filter = "CSV files (*.csv)|*.csv",
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
                UpdateStatusBar(string.Format(S._("circuittagger.exported_rows"), rows.Count));
                MessageBox.Show(string.Format(S._("circuittagger.exported_rows_path"), rows.Count, dlg.FileName), S._("circuittagger.export_complete"),
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(string.Format(S._("circuittagger.export_failed"), ex.Message), S._("circuittagger.export_error"), MessageBoxButton.OK, MessageBoxImage.Warning);
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
                    Text = S._("circuittagger.no_elements_selected"),
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
                        Background = MeToolsTheme.BrActiveBg, BorderBrush = MeToolsTheme.BrAccent, BorderThickness = new Thickness(1),
                        Child = new TextBlock { Text = CatShort(info.CategoryId), FontSize = 9,
                            Foreground = MeToolsTheme.BrAccent, FontWeight = FontWeights.SemiBold },
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
                    removeBtn.Click += (s, e) =>
                    {
                        var uiDoc = _uiApp?.ActiveUIDocument;
                        SetPendingMark(uiDoc?.Document, uiDoc?.ActiveView, new[] { captured.ElementId }, false);
                        try { uiDoc?.RefreshActiveView(); } catch { }
                        _selected.Remove(captured);
                        RefreshSelectionList();
                    };

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
                _lblSelCount.Text = _selected.Count == 0 ? S._("circuittagger.elements_selected_0")
                    : string.Format(S._(_selected.Count == 1 ? "circuittagger.n_elements_selected_1" : "circuittagger.n_elements_selected_n"), _selected.Count);
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
        // (SecH, Div, CompactField, CompactComboStrict, InlineCard, ComboCard
        // now live on MeToolsWindowBase -- shared with BatchParamsWindow and
        // any future tool, instead of each window keeping its own copy.)

        // A plain muted caption line -- like InfoBox's text, but without the
        // colored background/padding, for hints worth keeping visible
        // (explains non-obvious behavior) without eating a full card's worth
        // of vertical space.
        private TextBlock Caption(string text) => new TextBlock
        {
            Text = text, FontSize = 10, Foreground = MeToolsTheme.BrMuted,
            TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 6),
        };

        // Re-scans the project for every loaded Multi-Category Tag family/
        // type and repopulates the picker. Called on window construction, on
        // explicit Refresh, and whenever the Tag tab is (re)shown, so a
        // family loaded after the window was opened still shows up. This is
        // a read-only FilteredElementCollector query -- like the Stats tab's
        // direct doc reads elsewhere in this window, it doesn't need the
        // ExternalEvent round trip that write operations use.
        private void RefreshTagFamilyOptions()
        {
            if (_cbTagFamily == null) return;
            var doc = _uiApp.ActiveUIDocument?.Document;
            if (doc == null) return;

            var prevSelection = _cbTagFamily.SelectedItem as TagFamilyOption;
            _tagFamilyOptions = CircuitTaggerHandler.GetAvailableTagFamilies(doc);

            _cbTagFamily.ItemsSource = null;
            _cbTagFamily.ItemsSource = _tagFamilyOptions;

            if (_tagFamilyOptions.Count == 0)
            {
                _cbTagFamily.SelectedItem = null;
                UpdateStatusBar(S._("circuittagger.tag_family_none"));
                return;
            }

            // Prefer: (1) whatever was already selected before this refresh,
            // (2) the family/type remembered from a previous session,
            // (3) the original hardcoded default family, (4) the first one.
            TagFamilyOption pick = null;
            if (prevSelection != null)
                pick = _tagFamilyOptions.FirstOrDefault(o =>
                    string.Equals(o.FamilyName, prevSelection.FamilyName, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(o.TypeName,   prevSelection.TypeName,   StringComparison.OrdinalIgnoreCase));
            if (pick == null && _settingsData != null && !string.IsNullOrEmpty(_settingsData.TagFamilyName))
                pick = _tagFamilyOptions.FirstOrDefault(o =>
                    string.Equals(o.FamilyName, _settingsData.TagFamilyName, StringComparison.OrdinalIgnoreCase) &&
                    (string.IsNullOrEmpty(_settingsData.TagTypeName) ||
                     string.Equals(o.TypeName, _settingsData.TagTypeName, StringComparison.OrdinalIgnoreCase)));
            if (pick == null)
                pick = _tagFamilyOptions.FirstOrDefault(o =>
                    string.Equals(o.FamilyName, "ME-Tools_CircuitTag", StringComparison.OrdinalIgnoreCase));
            if (pick == null)
                pick = _tagFamilyOptions[0];

            _cbTagFamily.SelectedItem = pick;
        }

        private UIElement CircuitBadge(string label, bool isSubRow)
        {
            if (string.IsNullOrEmpty(label)) return TC("--", small: true);
            return new Border
            {
                CornerRadius = new CornerRadius(3), Padding = new Thickness(5, 2, 5, 2),
                VerticalAlignment = VerticalAlignment.Center,
                Background = new SolidColorBrush(Color.FromArgb(isSubRow ? (byte)15 : (byte)30,
                    MeToolsTheme.CAccent.R, MeToolsTheme.CAccent.G, MeToolsTheme.CAccent.B)),
                BorderBrush = MeToolsTheme.BrAccent, BorderThickness = new Thickness(1),
                Child = new TextBlock { Text = label, FontSize = isSubRow ? 9 : 11,
                    FontWeight = FontWeights.Bold, FontFamily = new FontFamily("Consolas"),
                    Foreground = MeToolsTheme.BrAccent },
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
            bool dark = MeToolsTheme.Current == MeTheme.Dark;
            var bgN = primary ? MeToolsTheme.BrPrimaryFill : MeToolsTheme.BrBtnBg;
            var bgH = primary ? (dark ? MeToolsTheme.BrAccentHover : MeToolsTheme.BrPetrolDark) : MeToolsTheme.BrActiveBg;

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
                BorderBrush = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Foreground = primary ? MeToolsTheme.BrPrimaryFg : MeToolsTheme.BrText,
                Cursor = Cursors.Hand,
                Template = tmpl,
            };
            if (primary) b.Effect = MeToolsTheme.PrimaryButtonGlow();
            b.MouseEnter += (s, e) => b.Background = bgH;
            b.MouseLeave += (s, e) => b.Background = bgN;
            b.Click      += (s, e) => onClick();
            return b;
        }

        private Button SmallBtn(string label, bool primary, Action onClick)
        {
            bool dark = MeToolsTheme.Current == MeTheme.Dark;
            var bgN = primary ? MeToolsTheme.BrPrimaryFill : MeToolsTheme.BrBtnBg;
            var bgH = primary ? (dark ? MeToolsTheme.BrAccentHover : MeToolsTheme.BrPetrolDark) : MeToolsTheme.BrActiveBg;

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
                Background = bgN, BorderBrush = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Foreground = primary ? MeToolsTheme.BrPrimaryFg : MeToolsTheme.BrText,
                Cursor = Cursors.Hand, Template = tmpl,
            };
            if (primary) b.Effect = MeToolsTheme.PrimaryButtonGlow();
            b.MouseEnter += (s, e) => b.Background = bgH;
            b.MouseLeave += (s, e) => b.Background = bgN;
            b.Click      += (s, e) => onClick();
            return b;
        }

        // Locale-independent -- matches the same confirmed category IDs used
        // by CatIsSocket/CatIsLamp/CatIsSwitch. The previous version matched
        // against el.Category.Name (e.g. "Lighting Fixtures"), which is
        // Revit's OWN display language for categories -- a separate setting
        // from ME-Tools' own UI language -- so this silently broke the
        // moment Revit itself ran in German or Romanian instead of English.
        private static string CatShort(int categoryId)
        {
            if (categoryId == -2001120) return "LAMP";                    // OST_LightingFixtures
            if (categoryId == -2001060) return "SOCK";                    // OST_ElectricalFixtures
            if (categoryId == -2008087 || categoryId == -2008090
                || categoryId == -2008093 || categoryId == -2008094
                || categoryId == -2008095) return "SW";                   // OST_LightingDevices + related
            if (categoryId == -2001040) return "PANEL";                   // OST_ElectricalEquipment
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
        // Same source of truth as CircuitTaggerHandler.GetElectricalCategories
        // (METools.ProjectHealthCheckCollector.RequiredCategories) -- kept as
        // a HashSet, built once, rather than re-materializing the LINQ query
        // on every AllowElement call during an active PickObject loop.
        private static readonly HashSet<BuiltInCategory> Allowed =
            new HashSet<BuiltInCategory>(CircuitTaggerHandler.GetElectricalCategories());
        public bool AllowElement(Element elem)
        {
            if (elem?.Category == null) return false;
            return Allowed.Contains((BuiltInCategory)elem.Category.Id.Value);
        }
        public bool AllowReference(Reference reference, XYZ position) => false;
    }
}

// KonfigFensterWindow.cs — ME-Tools | Stromkreis-Konfiguration
// Mayer E-Concept SRL — Reines C# WPF mit Tabs
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Text.Json;
using Autodesk.Revit.DB;
using Color      = System.Windows.Media.Color;
using Ellipse    = System.Windows.Shapes.Ellipse;
using Grid       = System.Windows.Controls.Grid;
using TextBox    = System.Windows.Controls.TextBox;
using Visibility = System.Windows.Visibility;

namespace METools.FamilyPlacer
{
    public class KonfigFensterWindow : METools.MeToolsWindowBase
    {
        private readonly Document        _doc;
        private readonly KonfigViewModel _vm;

        // Tab-System
        private Border      _tab1, _tab2, _tab3, _tab4;
        private StackPanel  _panel1, _panel2, _panel3, _panel4;
        private Border      _activeTab;
        private StackPanel  _activePanel;
        // Apartment order for the Rooms tab (list of WohnungsId strings, user-reorderable)
        private List<string> _apartmentOrder = new List<string>();

        // Footer
        private TextBlock   _txtZugeordnet, _txtOffen, _txtAuto;
        private Border      _footerBorder;

        // SK-Eingabefelder für Theme-Update
        private readonly List<TextBox>  _allSkInputs        = new List<TextBox>();
        private readonly List<ComboBox> _allComboBoxes      = new List<ComboBox>();
        private ComboBox                _namingSchemeCombo;

        public KonfigFensterWindow(Document doc)
        {
            _doc = doc;
            _vm  = new KonfigViewModel(doc);
            InitWindow("ElecTriX — Circuit Configuration", 1080, isDialog: true);
            MaxHeight = System.Math.Min(780, System.Windows.SystemParameters.WorkArea.Height - 80);
            Build();
        }

        public bool GespeichertErfolgreich => _vm.GespeichertErfolgreich;

        void Build()
        {
            // ── Footer ZUERST (Dock.Bottom muss vor Fill kommen!) ─────────
            BuildFooter();

            // ── StatusBar (auch Dock.Bottom) ──────────────────────────────
            BuildStatusBar($"{_vm.RaumZeilen.Count + _vm.SonderZeilen.Count} entries");

            // ── Tab-Leiste ────────────────────────────────────────────────
            var tabBar = BuildTabBar();
            DockPanel.SetDock(tabBar, Dock.Top);
            RootDock.Children.Add(tabBar);

            // ── Inhalt (Fill) ─────────────────────────────────────────────
            var contentGrid = new Grid { Background = MeToolsTheme.BrBg };
            contentGrid.Children.Add(Watermark());

            var scroll = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Background = System.Windows.Media.Brushes.Transparent,
                Padding    = new Thickness(16, 12, 16, 8),
            };

            var outerStack = new StackPanel();
            outerStack.Children.Add(_panel1);
            outerStack.Children.Add(_panel2);
            outerStack.Children.Add(_panel3);
            outerStack.Children.Add(_panel4);
            scroll.Content = outerStack;
            contentGrid.Children.Add(scroll);

            RootDock.Children.Add(contentGrid);

            // Panel 1 aktiv
            ShowTab(_tab1, _panel1);
        }

        // ── Tab-Leiste ────────────────────────────────────────────────────
        Border BuildTabBar()
        {
            var bar = new Border
            {
                Background      = MeToolsTheme.BrHeader,
                BorderBrush     = MeToolsTheme.BrBorder,
                BorderThickness = new Thickness(0, 0, 0, 1),
                Padding         = new Thickness(4, 0, 0, 0),
            };
            var sp = new StackPanel { Orientation = Orientation.Horizontal };

            _apartmentOrder = _vm.RaumZeilen
                .Select(z => z.Verteiler ?? "")
                .Distinct().OrderBy(id => id).ToList();

            _panel1 = BuildRaeumePanel();
            _panel2 = BuildSonderPanel();
            _panel3 = BuildVerteilerPanel();
            _panel4 = BuildPresetsPanel();

            _tab1 = MakeTab("Rooms",           MeToolsTheme.CPetrol,  () => ShowTab(_tab1, _panel1));
            _tab2 = MakeTab("Special Outlets", MeToolsTheme.COrange,  () => ShowTab(_tab2, _panel2));
            _tab3 = MakeTab("Panels",          MeToolsTheme.CBlue,   () => ShowTab(_tab3, _panel3));
            _tab4 = MakeTab("Presets",         Color.FromRgb(138,99,210), () => ShowTab(_tab4, _panel4));

            sp.Children.Add(_tab1);
            sp.Children.Add(_tab2);
            sp.Children.Add(_tab3);
            sp.Children.Add(_tab4);
            bar.Child = sp;
            return bar;
        }

        Border MakeTab(string label, Color tabColor, Action onClick)
        {
            // Pill-Badge Style wie Mockup
            var pill = new Border
            {
                CornerRadius    = new CornerRadius(12),
                Padding         = new Thickness(10, 3, 10, 3),
                Background      = new SolidColorBrush(Color.FromArgb(40,
                    tabColor.R, tabColor.G, tabColor.B)),
                Child = new TextBlock
                {
                    Text      = label,
                    FontSize  = 12,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = MeToolsTheme.BrMuted,
                    VerticalAlignment = VerticalAlignment.Center,
                }
            };
            var tab = new Border
            {
                Padding         = new Thickness(10, 8, 10, 8),
                Cursor          = System.Windows.Input.Cursors.Hand,
                Background      = MeToolsTheme.BrHeader,
                BorderThickness = new Thickness(0, 0, 0, 2),
                BorderBrush     = System.Windows.Media.Brushes.Transparent,
                Child = pill,
                Tag   = tabColor,
            };
            tab.MouseEnter += (s, e) =>
            {
                if (tab != _activeTab) tab.Background = MeToolsTheme.BrBg;
            };
            tab.MouseLeave += (s, e) =>
            {
                if (tab != _activeTab) tab.Background = MeToolsTheme.BrHeader;
            };
            tab.MouseLeftButtonDown += (s, e) => onClick();
            return tab;
        }

        void ShowTab(Border tab, StackPanel panel)
        {
            foreach (var t in new[] { _tab1, _tab2, _tab3 })
            {
                if (t == null) continue;
                t.BorderBrush = System.Windows.Media.Brushes.Transparent;
                t.Background  = MeToolsTheme.BrHeader;
                if (t.Child is Border pill)
                {
                    var tc = (Color)t.Tag;
                    pill.Background = new SolidColorBrush(Color.FromArgb(30, tc.R, tc.G, tc.B));
                    if (pill.Child is TextBlock ptb) ptb.Foreground = MeToolsTheme.BrMuted;
                }
            }
            foreach (var p in new[] { _panel1, _panel2, _panel3 })
                if (p != null) p.Visibility = Visibility.Collapsed;

            _activeTab   = tab;
            _activePanel = panel;
            var activeColor = (Color)tab.Tag;
            var activeBrush = new SolidColorBrush(activeColor);
            tab.BorderBrush = activeBrush;
            tab.Background  = MeToolsTheme.BrSurface;
            if (tab.Child is Border activePill)
            {
                // Stark sichtbar: volle Farbe als Hintergrund
                // Dark: helle Farbe als Hintergrund, Light: noch heller
                // Volle Farbe als Hintergrund = immer gut sichtbar
                activePill.Background = new SolidColorBrush(activeColor);
                if (activePill.Child is TextBlock atb)
                {
                    atb.Foreground = new SolidColorBrush(Color.FromRgb(230, 245, 245));
                    atb.FontWeight = FontWeights.Bold;
                    atb.FontSize   = 12;
                }
            }
            if (panel != null) panel.Visibility = Visibility.Visible;
        }

        // ── Panel 1: Räume mit 4 Scheme-Parameter-Feldern ─────────────────
        StackPanel _raeumeOuterSp;   // held for apartment reorder rebuild

        StackPanel BuildRaeumePanel()
        {
            var sp = new StackPanel { Visibility = Visibility.Collapsed };
            sp.Children.Add(SectionBadge("Rooms — Circuit Parameters",
                MeToolsTheme.BrPetrol, $"{_vm.RaumZeilen.Count} room(s) loaded"));

            var info = new Border
            {
                Background = MeToolsTheme.BrSurface, BorderBrush = MeToolsTheme.BrBorder,
                BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(5),
                Padding = new Thickness(10, 6, 10, 6), Margin = new Thickness(0, 0, 0, 10),
            };
            var infoSp = new StackPanel();
            infoSp.Children.Add(new TextBlock
            {
                Text = _vm.SchemaInfo, FontWeight = FontWeights.SemiBold,
                FontSize = 11, Foreground = MeToolsTheme.BrPetrol, TextWrapping = TextWrapping.Wrap,
            });
            infoSp.Children.Add(new TextBlock
            {
                Text = "All 4 scheme values per room are editable. Defaults come from the panel's circuit prefix.",
                FontSize = 10, Foreground = MeToolsTheme.BrMuted, Margin = new Thickness(0, 2, 0, 0),
                TextWrapping = TextWrapping.Wrap,
            });
            info.Child = infoSp;
            sp.Children.Add(info);

            if (_vm.RaumZeilen.Count == 0)
            {
                sp.Children.Add(new TextBlock { Text = "No rooms found.", FontSize = 11,
                    Foreground = MeToolsTheme.BrMuted, Margin = new Thickness(0, 0, 0, 12) });
                return sp;
            }

            _raeumeOuterSp = new StackPanel();
            RebuildApartmentGroups();
            sp.Children.Add(_raeumeOuterSp);
            return sp;
        }

        void RebuildApartmentGroups()
        {
            if (_raeumeOuterSp == null) return;
            _raeumeOuterSp.Children.Clear();
            _allComboBoxes.Clear();
            _allSkInputs.Clear();

            // Group rooms by Verteiler (panel name = apartment identifier on RaumZeile)
            var byApt = _vm.RaumZeilen
                .GroupBy(z => z.Verteiler ?? "")
                .ToDictionary(g => g.Key, g => g.ToList());

            // Any ids not yet in order list get appended
            foreach (var id in byApt.Keys.Where(k => !_apartmentOrder.Contains(k)))
                _apartmentOrder.Add(id);

            int aptIdx = 0;
            foreach (var aptId in _apartmentOrder)
            {
                if (!byApt.ContainsKey(aptId)) continue;
                var rooms = byApt[aptId];
                int idx = aptIdx; // capture for lambdas

                // Group header
                var hdr = new Border
                {
                    Background = MeToolsTheme.BrSurface, BorderBrush = MeToolsTheme.BrBorder,
                    BorderThickness = new Thickness(0, 0, 0, 1),
                    Padding = new Thickness(8, 5, 8, 5), Margin = new Thickness(0, aptIdx == 0 ? 0 : 8, 0, 2),
                };
                var hdrSp = new StackPanel { Orientation = Orientation.Horizontal };
                var hdrLabel = new TextBlock
                {
                    Text = string.IsNullOrEmpty(aptId) ? "Unassigned rooms" : aptId,
                    FontWeight = FontWeights.SemiBold, FontSize = 11,
                    Foreground = MeToolsTheme.BrPetrol,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, 0, 8, 0),
                };
                var hdrCount = new TextBlock
                {
                    Text = rooms.Count.ToString() + " rooms",
                    FontSize = 10, Foreground = MeToolsTheme.BrMuted,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, 0, 14, 0),
                };
                hdrSp.Children.Add(hdrLabel);
                hdrSp.Children.Add(hdrCount);

                // Up/Down reorder buttons (only when more than one apartment)
                if (_apartmentOrder.Count(id => byApt.ContainsKey(id)) > 1)
                {
                    var btnUp = new System.Windows.Controls.Button
                    {
                        Content = "▲", Width = 22, Height = 20, FontSize = 9,
                        Margin = new Thickness(0, 0, 3, 0),
                        Background = System.Windows.Media.Brushes.Transparent,
                        BorderBrush = MeToolsTheme.BrBorder,
                        Foreground = MeToolsTheme.BrMuted,
                        ToolTip = "Move apartment up",
                        IsEnabled = idx > 0,
                    };
                    var capturedId = aptId;
                    btnUp.Click += (s, e) =>
                    {
                        int pos = _apartmentOrder.IndexOf(capturedId);
                        if (pos > 0) { _apartmentOrder.RemoveAt(pos); _apartmentOrder.Insert(pos - 1, capturedId); }
                        RebuildApartmentGroups();
                    };
                    var btnDown = new System.Windows.Controls.Button
                    {
                        Content = "▼", Width = 22, Height = 20, FontSize = 9,
                        Margin = new Thickness(0, 0, 0, 0),
                        Background = System.Windows.Media.Brushes.Transparent,
                        BorderBrush = MeToolsTheme.BrBorder,
                        Foreground = MeToolsTheme.BrMuted,
                        ToolTip = "Move apartment down",
                        IsEnabled = idx < _apartmentOrder.Count(id => byApt.ContainsKey(id)) - 1,
                    };
                    btnDown.Click += (s, e) =>
                    {
                        int pos = _apartmentOrder.IndexOf(capturedId);
                        if (pos < _apartmentOrder.Count - 1) { _apartmentOrder.RemoveAt(pos); _apartmentOrder.Insert(pos + 1, capturedId); }
                        RebuildApartmentGroups();
                    };
                    hdrSp.Children.Add(btnUp);
                    hdrSp.Children.Add(btnDown);
                }

                hdr.Child = hdrSp;
                // Only show group header when there are multiple distinct panels
                if (_apartmentOrder.Count(id => byApt.ContainsKey(id)) > 1)
                    _raeumeOuterSp.Children.Add(hdr);

                // Room rows for this apartment
                var container = GridContainer();
                GridHeader8(container, "Room #", "Name", "Panel", "Pre-fuse", "GFCI Circuit", "Circuit", "Device", "●");

                foreach (var z in rooms)
                {
                    var cb = VerteilerDropdown(z);
                    var vs = SmallInput(z.Vorsicherung, "Pre-fuse");
                    var fi = SmallInput(z.FIKreis,      "GFCI Circuit");
                    var si = SmallInput(z.Stromkreis,   "Circuit");
                    var ge = SmallInput(z.Geraet,       "Device");

                    _allComboBoxes.Add(cb);
                    _allSkInputs.Add(vs); _allSkInputs.Add(fi); _allSkInputs.Add(si); _allSkInputs.Add(ge);

                    var dot = StatusDotForRow(z);

                    vs.TextChanged += (sender, e) => { z.Vorsicherung = vs.Text; };
                    fi.TextChanged += (sender, e) => { z.FIKreis      = fi.Text; };
                    si.TextChanged += (sender, e) =>
                    {
                        z.Stromkreis = si.Text;
                        UpdateDotForRow(dot, z); _vm.AktualisiereStatistik(); RefreshFooter();
                    };
                    ge.TextChanged += (sender, e) => { z.Geraet = ge.Text; };
                    cb.SelectionChanged += (sender, e) =>
                    {
                        z.Verteiler = cb.Text ?? (cb.SelectedItem as string) ?? "";
                        UpdateDotForRow(dot, z); _vm.AktualisiereStatistik(); RefreshFooter();
                    };
                    cb.LostFocus += (sender, e) =>
                    {
                        z.Verteiler = cb.Text ?? "";
                        UpdateDotForRow(dot, z); _vm.AktualisiereStatistik(); RefreshFooter();
                    };

                    GridDataRow8(container,
                        TextCell(z.RaumNummer ?? "", true, mono: true),
                        TextCell(z.RaumName ?? "", false),
                        cb, vs, fi, si, ge, dot);
                }
                _raeumeOuterSp.Children.Add(container);
                aptIdx++;
            }
        }

        // ── Panel 4: Circuit Presets ──────────────────────────────────────
        // Stores room-type name → circuit/panel mappings. "Apply Presets" fills
        // the circuit fields of all matching rooms in the Rooms tab automatically.
        private readonly List<CircuitPresetEntry> _circuitPresets = new List<CircuitPresetEntry>();

        class CircuitPresetEntry
        {
            public string RoomTypeKeyword { get; set; } = "";  // e.g. "bedroom", "hallway"
            public string Panel           { get; set; } = "";
            public string Circuit         { get; set; } = "";
            public string PreFuse         { get; set; } = "";
            public string GFCICircuit     { get; set; } = "";
            public string Device          { get; set; } = "";
        }

        StackPanel BuildPresetsPanel()
        {
            var sp = new StackPanel { Visibility = Visibility.Collapsed };
            sp.Children.Add(SectionBadge("Circuit Presets — Auto-fill by room type",
                MeToolsTheme.BrPetrol, "match by keyword"));

            var desc = new TextBlock
            {
                Text = "Define circuit defaults per room type keyword. Click 'Apply Presets' to fill matching " +
                       "rooms in the Rooms tab. The keyword is matched against the room's canonical name " +
                       "(e.g. 'bedroom', 'bathroom', 'hallway', 'kitchen'). Case-insensitive.",
                FontSize = 10, Foreground = MeToolsTheme.BrMuted,
                Margin = new Thickness(0, 0, 0, 10), TextWrapping = TextWrapping.Wrap,
            };
            sp.Children.Add(desc);

            // Preset list container (rebuilt when entries change)
            var listSp = new StackPanel();
            sp.Children.Add(listSp);

            Action rebuildList = null;
            rebuildList = () =>
            {
                listSp.Children.Clear();
                // Header row
                var hdrGrid = new Grid { Margin = new Thickness(0, 0, 0, 2) };
                foreach (var w in new[] { 130.0, 120.0, 90.0, 70.0, 70.0, 90.0, 30.0 })
                    hdrGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(w) });
                var headers = new[] { "Room keyword", "Panel", "Circuit", "Pre-fuse", "GFCI", "Device", "" };
                for (int hi = 0; hi < headers.Length; hi++)
                {
                    var htb = new TextBlock { Text = headers[hi], FontSize = 10,
                        Foreground = MeToolsTheme.BrMuted, FontWeight = FontWeights.SemiBold,
                        Margin = new Thickness(4, 0, 4, 0) };
                    Grid.SetColumn(htb, hi); hdrGrid.Children.Add(htb);
                }
                listSp.Children.Add(hdrGrid);

                for (int pi = 0; pi < _circuitPresets.Count; pi++)
                {
                    var entry = _circuitPresets[pi];
                    int capturedIdx = pi;
                    var rowGrid = new Grid { Margin = new Thickness(0, 2, 0, 2) };
                    foreach (var w in new[] { 130.0, 120.0, 90.0, 70.0, 70.0, 90.0, 30.0 })
                        rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(w) });

                    var tbKeyword  = PresetInput(entry.RoomTypeKeyword, "e.g. bedroom");
                    var tbPanel    = PresetInput(entry.Panel,    "Panel name");
                    var tbCircuit  = PresetInput(entry.Circuit,  "Circuit");
                    var tbPreFuse  = PresetInput(entry.PreFuse,  "Pre-fuse");
                    var tbGFCI     = PresetInput(entry.GFCICircuit, "GFCI");
                    var tbDevice   = PresetInput(entry.Device,   "Device");

                    tbKeyword.TextChanged += (s, e) => entry.RoomTypeKeyword = tbKeyword.Text;
                    tbPanel.TextChanged   += (s, e) => entry.Panel           = tbPanel.Text;
                    tbCircuit.TextChanged += (s, e) => entry.Circuit         = tbCircuit.Text;
                    tbPreFuse.TextChanged += (s, e) => entry.PreFuse         = tbPreFuse.Text;
                    tbGFCI.TextChanged    += (s, e) => entry.GFCICircuit     = tbGFCI.Text;
                    tbDevice.TextChanged  += (s, e) => entry.Device          = tbDevice.Text;

                    var btnDel = new System.Windows.Controls.Button
                    {
                        Content = "×", Width = 22, Height = 22, FontSize = 12,
                        Background = System.Windows.Media.Brushes.Transparent,
                        BorderBrush = MeToolsTheme.BrBorder, Foreground = MeToolsTheme.BrMuted,
                        ToolTip = "Remove this preset",
                    };
                    btnDel.Click += (s, e) => { _circuitPresets.RemoveAt(capturedIdx); rebuildList(); };

                    Grid.SetColumn(tbKeyword, 0); Grid.SetColumn(tbPanel,   1);
                    Grid.SetColumn(tbCircuit, 2); Grid.SetColumn(tbPreFuse, 3);
                    Grid.SetColumn(tbGFCI,    4); Grid.SetColumn(tbDevice,  5);
                    Grid.SetColumn(btnDel,    6);
                    rowGrid.Children.Add(tbKeyword); rowGrid.Children.Add(tbPanel);
                    rowGrid.Children.Add(tbCircuit); rowGrid.Children.Add(tbPreFuse);
                    rowGrid.Children.Add(tbGFCI);    rowGrid.Children.Add(tbDevice);
                    rowGrid.Children.Add(btnDel);
                    listSp.Children.Add(rowGrid);
                }
            };

            rebuildList();

            // Add + Apply buttons
            var btnRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 10, 0, 0) };

            var btnAdd = FooterBtn("+ Add Preset", false, () =>
            {
                _circuitPresets.Add(new CircuitPresetEntry());
                rebuildList();
            });
            btnAdd.Margin = new Thickness(0, 0, 8, 0);
            btnRow.Children.Add(btnAdd);

            var btnApply = FooterBtn("Apply Presets to Rooms", true, () =>
            {
                int applied = 0;
                foreach (var z in _vm.RaumZeilen)
                {
                    var canonical = (z.KanonischerName ?? z.RaumName ?? "").ToLowerInvariant();
                    foreach (var preset in _circuitPresets)
                    {
                        if (string.IsNullOrWhiteSpace(preset.RoomTypeKeyword)) continue;
                        if (canonical.Contains(preset.RoomTypeKeyword.ToLowerInvariant()))
                        {
                            if (!string.IsNullOrWhiteSpace(preset.Panel))
                                z.Verteiler = preset.Panel;
                            if (!string.IsNullOrWhiteSpace(preset.Circuit))
                                z.Stromkreis = preset.Circuit;
                            if (!string.IsNullOrWhiteSpace(preset.PreFuse))
                                z.Vorsicherung = preset.PreFuse;
                            if (!string.IsNullOrWhiteSpace(preset.GFCICircuit))
                                z.FIKreis = preset.GFCICircuit;
                            if (!string.IsNullOrWhiteSpace(preset.Device))
                                z.Geraet = preset.Device;
                            applied++;
                            break; // first matching preset wins
                        }
                    }
                }
                _vm.AktualisiereStatistik(); RefreshFooter();
                // Rebuild rooms panel to reflect new values
                RebuildApartmentGroups();
                MessageBox.Show($"Applied presets to {applied} room(s).",
                    "Circuit Presets", MessageBoxButton.OK, MessageBoxImage.Information);
            });
            btnRow.Children.Add(btnApply);
            sp.Children.Add(btnRow);
            return sp;
        }

        TextBox PresetInput(string text, string tip) => new TextBox
        {
            Text = text ?? "", Height = 24, Margin = new Thickness(2, 0, 2, 0),
            Padding = new Thickness(4, 2, 4, 2), FontSize = 11,
            VerticalContentAlignment = System.Windows.VerticalAlignment.Center,
            Background = MeToolsTheme.BrInput, Foreground = MeToolsTheme.BrInputFg,
            BorderBrush = MeToolsTheme.BrBorder, ToolTip = tip,
        };

        // ── Number Rooms ──────────────────────────────────────────────────
        // Writes room numbers in format XYZ where X=Haus, Y=floor index, Z=apartment
        // to the ROOM_NUMBER parameter of each room in the model.
        void OnNumberRooms()
        {
            // Show a simple input dialog
            var dlg = new NumberRoomsDialog(_doc, _vm.RaumZeilen.ToList());
            dlg.Owner = this;
            if (dlg.ShowDialog() == true)
            {
                // Refresh the rooms panel after numbering
                _apartmentOrder = _vm.RaumZeilen
                    .Select(z => z.Verteiler ?? "")
                    .Distinct().OrderBy(id => id).ToList();
                RebuildApartmentGroups();
                RefreshFooter();
            }
        }

        ComboBox VerteilerDropdown(RaumZeile z)
        {
            var cb = new ComboBox
            {
                Width = 190, Height = 26, Margin = new Thickness(4, 0, 2, 0),
                VerticalAlignment = VerticalAlignment.Center,
                Background = MeToolsTheme.BrInput, Foreground = MeToolsTheme.BrInputFg,
                BorderBrush = MeToolsTheme.BrBorder, IsEditable = true,
                ItemsSource = _vm.VerfuegbareVerteiler, FontSize = 11,
                Padding = new Thickness(4, 0, 2, 0),
            };
            if (!string.IsNullOrEmpty(z.Verteiler))
            {
                if (_vm.VerfuegbareVerteiler.Contains(z.Verteiler))
                {
                    cb.SelectedItem = z.Verteiler;
                    cb.Text = z.Verteiler;
                }
                else
                {
                    // Toter Eintrag — rot hinterlegen, User soll das neu zuweisen
                    cb.Text = z.Verteiler + "  ⚠";
                    cb.BorderBrush = MeToolsTheme.BrOrange;
                    cb.ToolTip = "This panel no longer exists in the model. Please re-assign.";
                }
            }
            return cb;
        }

        TextBox SmallInput(string text, string tip)
        {
            return new TextBox
            {
                Text = text ?? "", Width = 75, Height = 24,
                Margin = new Thickness(2, 0, 2, 0), Padding = new Thickness(4, 2, 4, 2),
                FontSize = 11, VerticalAlignment = VerticalAlignment.Center,
                VerticalContentAlignment = System.Windows.VerticalAlignment.Center,
                Background = MeToolsTheme.BrInput, Foreground = MeToolsTheme.BrInputFg,
                BorderBrush = MeToolsTheme.BrBorder, ToolTip = tip,
            };
        }

        // ── Panel 2: Sonderanschlüsse ─────────────────────────────────────
        StackPanel BuildSonderPanel()
        {
            var sp = new StackPanel { Visibility = Visibility.Collapsed };

            sp.Children.Add(SectionBadge("Special Outlets → Stromkreis",
                MeToolsTheme.BrOrange, $"{_vm.SonderZeilen.Count} codes"));

            var container = GridContainer();
            GridHeader(container, "Code", "Device", "Circuit", "Status");

            foreach (var z in _vm.SonderZeilen)
            {
                var sk = SkInput(z.Stromkreis);
                _allSkInputs.Add(sk);
                var dot = StatusDot(z.Stromkreis);
                sk.TextChanged += (s, e) =>
                {
                    z.Stromkreis = sk.Text;
                    UpdateDot(dot, sk.Text);
                    _vm.AktualisiereStatistik();
                    RefreshFooter();
                };
                GridDataRow(container,
                    KuerzelBadge(z.Kuerzel ?? z.KanonischerName),
                    TextCell(z.GeraetName ?? z.KanonischerName, false),
                    sk, dot);
            }
            sp.Children.Add(container);
            return sp;
        }

        // ── Panel 3: Verteiler mit Prefix + Match ──────────────────────────
        StackPanel BuildVerteilerPanel()
        {
            var sp = new StackPanel { Visibility = Visibility.Collapsed };

            sp.Children.Add(SectionBadge("Panels — Circuit Prefix & Match",
                MeToolsTheme.BrBlue, $"{_vm.VerteilerZeilen.Count} aus Modell"));

            // Globaler Naming-Scheme-Selector
            var schemeBox = new Border
            {
                Background = MeToolsTheme.BrSurface, BorderBrush = MeToolsTheme.BrBorder,
                BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(5),
                Padding = new Thickness(12, 8, 12, 8), Margin = new Thickness(0, 0, 0, 12),
            };
            var schemeSp = new StackPanel();
            schemeSp.Children.Add(new TextBlock
            {
                Text = "Global Circuit Naming Scheme (Projekt-weit)",
                FontWeight = FontWeights.SemiBold, FontSize = 11,
                Foreground = MeToolsTheme.BrPetrol, Margin = new Thickness(0, 0, 0, 4),
            });

            _namingSchemeCombo = new ComboBox
            {
                Width = 320, Height = 26, HorizontalAlignment = HorizontalAlignment.Left,
                Background = MeToolsTheme.BrInput, Foreground = MeToolsTheme.BrInputFg,
                BorderBrush = MeToolsTheme.BrBorder, FontSize = 11,
                ItemsSource = _vm.VerfuegbareNamingSchemes,
                ToolTip = "Applied to all panels for which circuits are created.",
            };
            if (!string.IsNullOrWhiteSpace(_vm.GewaehltesNamingScheme))
                _namingSchemeCombo.SelectedItem = _vm.GewaehltesNamingScheme;
            _namingSchemeCombo.SelectionChanged += (s, e) =>
            {
                _vm.GewaehltesNamingScheme = _namingSchemeCombo.SelectedItem as string ?? "";
            };
            schemeSp.Children.Add(_namingSchemeCombo);
            schemeBox.Child = schemeSp;
            sp.Children.Add(schemeBox);

            // Auto-Match-Button
            var matchRow = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Margin = new Thickness(0, 0, 0, 10),
            };
            matchRow.Children.Add(new TextBlock
            {
                Text = "Auto-assign rooms to panels by match prefix:",
                FontSize = 11, Foreground = MeToolsTheme.BrMuted,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 10, 0),
            });
            var btnAutoMatch = new System.Windows.Controls.Button
            {
                Content = "Auto-Match", Height = 28, Padding = new Thickness(14, 0, 14, 0),
                Background = MeToolsTheme.BrPetrol, Foreground = System.Windows.Media.Brushes.White,
                BorderBrush = MeToolsTheme.BrPetrol, FontSize = 11, FontWeight = FontWeights.SemiBold,
                Cursor = System.Windows.Input.Cursors.Hand,
            };
            btnAutoMatch.Click += (s, e) =>
            {
                int n = _vm.AutoMatchRaeumeZuVerteilern();
                MessageBox.Show($"{n} Räume wurden Verteilern zugeordnet.\n\n" +
                    "Wechsle zum Rooms-Tab um die Zuordnungen zu sehen.",
                    "Auto-Match", MessageBoxButton.OK, MessageBoxImage.Information);
                ReloadRoomsPanel();
                RefreshFooter();
            };
            matchRow.Children.Add(btnAutoMatch);
            sp.Children.Add(matchRow);

            // ── Tote-Verteiler-Migrationsbox (erscheint nur wenn nötig) ──
            if (_vm.ToteVerteilerNamen.Count > 0)
            {
                var migBox = new Border
                {
                    Background = MeToolsTheme.BrOrange,
                    BorderBrush = MeToolsTheme.BrOrange,
                    BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(5),
                    Padding = new Thickness(12, 10, 12, 10), Margin = new Thickness(0, 0, 0, 12),
                };
                var migSp = new StackPanel();
                migSp.Children.Add(new TextBlock
                {
                    Text = "⚠ Tote Verteiler-Referenzen gefunden",
                    FontWeight = FontWeights.SemiBold, FontSize = 12,
                    Foreground = System.Windows.Media.Brushes.White,
                    Margin = new Thickness(0, 0, 0, 4),
                });
                migSp.Children.Add(new TextBlock
                {
                    Text = "These panels no longer exist in the model. " +
                           "Select a replacement and click 'Re-route' to migrate all rooms.",
                    FontSize = 11, TextWrapping = TextWrapping.Wrap,
                    Foreground = System.Windows.Media.Brushes.White,
                    Margin = new Thickness(0, 0, 0, 8),
                });

                foreach (var toter in _vm.ToteVerteilerNamen.ToList())
                {
                    var row = new Grid { Margin = new Thickness(0, 3, 0, 3) };
                    row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                    row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(24) });
                    row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(230) });
                    row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(90) });

                    var alt = new TextBlock
                    {
                        Text = toter, FontFamily = new System.Windows.Media.FontFamily("Consolas"),
                        FontSize = 11, Foreground = System.Windows.Media.Brushes.White,
                        VerticalAlignment = VerticalAlignment.Center,
                        TextTrimming = TextTrimming.CharacterEllipsis,
                    };
                    var pfeil = new TextBlock
                    {
                        Text = "→", FontSize = 12, FontWeight = FontWeights.Bold,
                        Foreground = System.Windows.Media.Brushes.White,
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center,
                    };
                    var combo = new ComboBox
                    {
                        Height = 24, FontSize = 11,
                        Background = MeToolsTheme.BrInput,
                        Foreground = MeToolsTheme.BrInputFg,
                        BorderBrush = MeToolsTheme.BrBorder,
                        ItemsSource = _vm.VerfuegbareVerteiler,
                        VerticalAlignment = VerticalAlignment.Center,
                    };
                    var btn = new System.Windows.Controls.Button
                    {
                        Content = "Re-route", Height = 24, FontSize = 11,
                        Margin = new Thickness(6, 0, 0, 0),
                        Background = MeToolsTheme.BrPetrol,
                        Foreground = System.Windows.Media.Brushes.White,
                        BorderBrush = MeToolsTheme.BrPetrol,
                        Cursor = System.Windows.Input.Cursors.Hand,
                    };
                    var capturedAlt = toter;
                    btn.Click += (s, e) =>
                    {
                        var neu = combo.SelectedItem as string;
                        if (string.IsNullOrWhiteSpace(neu))
                        {
                            MessageBox.Show("Please select a target panel first.",
                                "Re-route", MessageBoxButton.OK, MessageBoxImage.Warning);
                            return;
                        }
                        int n = _vm.MigriereVerteiler(capturedAlt, neu);
                        MessageBox.Show($"{n} Räume wurden von '{capturedAlt}' auf '{neu}' umgeleitet.\n\n" +
                            "Zum Speichern: 'Save' klicken.",
                            "Re-route", MessageBoxButton.OK, MessageBoxImage.Information);
                        ReloadRoomsPanel();
                        ReloadPanelsPanel();
                    };
                    Grid.SetColumn(alt, 0);   row.Children.Add(alt);
                    Grid.SetColumn(pfeil, 1); row.Children.Add(pfeil);
                    Grid.SetColumn(combo, 2); row.Children.Add(combo);
                    Grid.SetColumn(btn, 3);   row.Children.Add(btn);
                    migSp.Children.Add(row);
                }

                migBox.Child = migSp;
                sp.Children.Add(migBox);
            }

            if (!_vm.VerteilerZeilen.Any())
            {
                sp.Children.Add(new TextBlock
                {
                    Text = "No panels found in model.", FontSize = 11,
                    Foreground = MeToolsTheme.BrMuted, Margin = new Thickness(0, 0, 0, 12),
                });
                return sp;
            }

            var container = GridContainer();
            GridHeader5V(container, "Verteiler", "Circuit Prefix", "Aktuelles Scheme", "Match-Prefix", "●");

            foreach (var v in _vm.VerteilerZeilen)
            {
                var prefixField = new TextBox
                {
                    Text = v.MatchPrefix ?? "", Width = 110, Height = 24,
                    Padding = new Thickness(4, 2, 4, 2), FontSize = 11,
                    VerticalAlignment = VerticalAlignment.Center,
                    VerticalContentAlignment = System.Windows.VerticalAlignment.Center,
                    Background = MeToolsTheme.BrInput, Foreground = MeToolsTheme.BrInputFg,
                    BorderBrush = MeToolsTheme.BrBorder,
                    ToolTip = "Rooms whose number starts with this prefix will be assigned to this panel.",
                };
                prefixField.TextChanged += (s, e) => { v.MatchPrefix = prefixField.Text; };
                _allSkInputs.Add(prefixField);

                var dot = new Ellipse
                {
                    Width = 9, Height = 9,
                    Fill = v.AutomatischErkannt ? MeToolsTheme.BrBlue : MeToolsTheme.BrOrange,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    ToolTip = v.StatusTooltip,
                };

                GridDataRow5V(container,
                    TextCell(v.VerteilerName ?? "", true),
                    TextCell(string.IsNullOrWhiteSpace(v.CircuitPrefix) ? "(keiner)" : v.CircuitPrefix, false, mono: true),
                    TextCell(string.IsNullOrWhiteSpace(v.NamingSchemeName) ? "(default)" : v.NamingSchemeName, false, small: true),
                    prefixField,
                    dot);

                // ── Bestehende Stromkreise unter dem Verteiler anzeigen ──
                if (v.Stromkreise != null && v.Stromkreise.Count > 0)
                {
                    var kreiseSp = (StackPanel)container.Child;
                    var subHeader = new Border
                    {
                        Background = MeToolsTheme.BrActiveBg,
                        BorderBrush = MeToolsTheme.BrBorder,
                        BorderThickness = new Thickness(0, 0, 0, 1),
                        Padding = new Thickness(16, 4, 8, 4),
                    };
                    subHeader.Child = new TextBlock
                    {
                        Text = $"↳ {v.Stromkreise.Count} Stromkreis(e) zugeordnet",
                        FontSize = 10, FontWeight = FontWeights.SemiBold,
                        Foreground = MeToolsTheme.BrMuted,
                    };
                    kreiseSp.Children.Add(subHeader);

                    foreach (var sk in v.Stromkreise)
                    {
                        var grid = new Grid { MinHeight = 26 };
                        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(80) });
                        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(130) });
                        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(110) });
                        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(55) });

                        var col0 = new TextBlock
                        {
                            Text = sk.CircuitNumber, FontSize = 10,
                            FontFamily = new System.Windows.Media.FontFamily("Consolas"),
                            Foreground = MeToolsTheme.BrPetrol,
                            VerticalAlignment = VerticalAlignment.Center,
                            Margin = new Thickness(18, 0, 6, 0),
                        };
                        var col1 = new TextBlock
                        {
                            Text = string.IsNullOrWhiteSpace(sk.LoadName) ? "(kein Name)" : sk.LoadName,
                            FontSize = 10,
                            Foreground = MeToolsTheme.BrText,
                            VerticalAlignment = VerticalAlignment.Center,
                            Margin = new Thickness(6, 0, 6, 0),
                            TextTrimming = TextTrimming.CharacterEllipsis,
                        };
                        var col2 = new TextBlock
                        {
                            Text = sk.LoadClassification ?? "",
                            FontSize = 10,
                            Foreground = MeToolsTheme.BrMuted,
                            VerticalAlignment = VerticalAlignment.Center,
                            Margin = new Thickness(6, 0, 6, 0),
                        };
                        var col3 = new TextBlock
                        {
                            Text = sk.RaumAnzeige ?? "",
                            FontSize = 10,
                            FontFamily = new System.Windows.Media.FontFamily("Consolas"),
                            Foreground = MeToolsTheme.BrMuted,
                            VerticalAlignment = VerticalAlignment.Center,
                            Margin = new Thickness(6, 0, 6, 0),
                        };
                        var col4 = new TextBlock
                        {
                            Text = sk.AnzahlBauteile + " BT",
                            FontSize = 10,
                            Foreground = MeToolsTheme.BrMuted,
                            VerticalAlignment = VerticalAlignment.Center,
                            HorizontalAlignment = HorizontalAlignment.Center,
                        };
                        Grid.SetColumn(col0, 0); grid.Children.Add(col0);
                        Grid.SetColumn(col1, 1); grid.Children.Add(col1);
                        Grid.SetColumn(col2, 2); grid.Children.Add(col2);
                        Grid.SetColumn(col3, 3); grid.Children.Add(col3);
                        Grid.SetColumn(col4, 4); grid.Children.Add(col4);

                        var kreisRow = new Border
                        {
                            Background = MeToolsTheme.BrBg,
                            BorderBrush = MeToolsTheme.BrBorder,
                            BorderThickness = new Thickness(0, 0, 0, 1),
                            Child = grid,
                        };
                        kreiseSp.Children.Add(kreisRow);
                    }
                }
            }
            sp.Children.Add(container);
            return sp;
        }

        void ReloadRoomsPanel()
        {
            if (_panel1 == null) return;
            _allSkInputs.Clear(); _allComboBoxes.Clear();
            var neu = BuildRaeumePanel();
            neu.Visibility = _panel1.Visibility;
            var outerStack = _panel1.Parent as StackPanel;
            if (outerStack != null)
            {
                var idx = outerStack.Children.IndexOf(_panel1);
                if (idx >= 0)
                {
                    outerStack.Children.RemoveAt(idx);
                    outerStack.Children.Insert(idx, neu);
                }
            }
            _panel1 = neu;
            if (_activeTab == _tab1) _activePanel = neu;
        }

        void ReloadPanelsPanel()
        {
            if (_panel3 == null) return;
            var neu = BuildVerteilerPanel();
            neu.Visibility = _panel3.Visibility;
            var outerStack = _panel3.Parent as StackPanel;
            if (outerStack != null)
            {
                var idx = outerStack.Children.IndexOf(_panel3);
                if (idx >= 0)
                {
                    outerStack.Children.RemoveAt(idx);
                    outerStack.Children.Insert(idx, neu);
                }
            }
            _panel3 = neu;
            if (_activeTab == _tab3) _activePanel = neu;
        }

        // ── Footer ────────────────────────────────────────────────────────
        void BuildFooter()
        {
            _footerBorder = new Border
            {
                Background      = MeToolsTheme.BrFooter,
                BorderBrush     = MeToolsTheme.BrBorder,
                BorderThickness = new Thickness(0, 1, 0, 0),
                Padding         = new Thickness(14, 10, 14, 10),
            };

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            // Statistik
            var statSp = new StackPanel { Orientation = Orientation.Horizontal };
            statSp.Children.Add(Dot(MeToolsTheme.CGreen));
            _txtZugeordnet = StatText(); statSp.Children.Add(_txtZugeordnet);
            statSp.Children.Add(Dot(MeToolsTheme.COrange));
            _txtOffen = StatText(); statSp.Children.Add(_txtOffen);
            statSp.Children.Add(Dot(MeToolsTheme.CBlue));
            _txtAuto = StatText(); statSp.Children.Add(_txtAuto);
            Grid.SetColumn(statSp, 0);
            grid.Children.Add(statSp);

            // Buttons — 4 klar getrennte Aktionen + Settings-Zahnrad
            var btnSp = new StackPanel { Orientation = Orientation.Horizontal };

            var btnSettings = new System.Windows.Controls.Button
            {
                Content = "⚙",
                Width = 32, Height = 30, FontSize = 16,
                Margin = new Thickness(0, 0, 14, 0),
                Background = System.Windows.Media.Brushes.Transparent,
                Foreground = MeToolsTheme.BrMuted,
                BorderBrush = MeToolsTheme.BrBorder,
                Cursor = System.Windows.Input.Cursors.Hand,
                ToolTip = "Open ElecTriX settings (JSON file in editor)\n" +
                          "Templates, category labels, match-prefix patterns, parameter names",
            };
            btnSettings.Click += (s, e) =>
            {
                try
                {
                    ElecTriXSettingsStorage.OeffneInEditor();
                    MessageBox.Show(
                        "The file 'electrix-settings.json' was opened in the editor:\n\n" +
                        ElecTriXSettingsStorage.GetSettingsPath() + "\n\n" +
                        "After editing: save the file, close this dialog and reopen it " +
                        "damit die Änderungen wirksam werden.",
                        "ElecTriX Settings", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show("Could not open settings:\n" + ex.Message,
                        "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            };
            btnSp.Children.Add(btnSettings);

            var btnNumberRooms = FooterBtn("Number Rooms", false, OnNumberRooms);
            btnNumberRooms.Padding = new System.Windows.Thickness(10, 0, 10, 0);
            btnNumberRooms.Margin  = new Thickness(0, 0, 6, 0);
            btnNumberRooms.ToolTip = "Write room numbers in format HFS (Haus-Floor-Suite) to Revit room parameters";
            btnSp.Children.Add(btnNumberRooms);

            var btnCancel = FooterBtn("Cancel", false, () => OnCloseClicked());
            btnCancel.Margin = new Thickness(0, 0, 6, 0);
            btnSp.Children.Add(btnCancel);

            var btnSave = FooterBtn("Save", false, OnSaveOnly);
            btnSave.Padding = new System.Windows.Thickness(12, 0, 12, 0);
            btnSave.Margin  = new Thickness(0, 0, 6, 0);
            btnSp.Children.Add(btnSave);

            var btnSaveClose = FooterBtn("Save & Close", false, OnSaveAndClose);
            btnSaveClose.Padding = new System.Windows.Thickness(12, 0, 12, 0);
            btnSaveClose.Margin  = new Thickness(0, 0, 6, 0);
            btnSp.Children.Add(btnSaveClose);

            var btnCreate = FooterBtn("Create Circuits", true, OnCreateCircuits);
            btnCreate.Padding = new System.Windows.Thickness(14, 0, 14, 0);
            btnSp.Children.Add(btnCreate);

            Grid.SetColumn(btnSp, 1);
            grid.Children.Add(btnSp);

            _footerBorder.Child = grid;
            DockPanel.SetDock(_footerBorder, Dock.Bottom);
            RootDock.Children.Add(_footerBorder);

            RefreshFooter();
        }

        Ellipse Dot(Color c) => new Ellipse
        {
            Width = 7, Height = 7, Fill = new SolidColorBrush(c),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 5, 0),
        };
        TextBlock StatText() => new TextBlock
        {
            FontSize = 11, Foreground = MeToolsTheme.BrMuted,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 14, 0),
        };

        void RefreshFooter()
        {
            _vm.AktualisiereStatistik();
            int auto = _vm.VerteilerZeilen.Count(v => v.AutomatischErkannt);
            if (_txtZugeordnet != null) _txtZugeordnet.Text = $"{_vm.ZugeordnetGesamt} assigned";
            if (_txtOffen      != null) _txtOffen.Text      = $"{_vm.OffeneZuordnungen} pending";
            if (_txtAuto       != null) _txtAuto.Text       = $"{auto} automatic";
        }

        bool TrySave(bool silent)
        {
            _vm.SpeichernCommand.Execute(null);
            if (!_vm.GespeichertErfolgreich)
            {
                MessageBox.Show("Save failed:\n\n" + (_vm.FehlerMeldung ?? "(unknown)"),
                    "ElecTriX", MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }
            if (!silent)
                MessageBox.Show("Configuration saved.\n\n" + (_vm.SpeicherInfo ?? "") +
                    "\n\nFor permanent storage: save the Revit project with Ctrl+S.",
                    "ElecTriX", MessageBoxButton.OK, MessageBoxImage.Information);
            return true;
        }

        void OnSaveOnly() { _vm.AktualisiereStatistik(); TrySave(false); }

        void OnSaveAndClose()
        {
            _vm.AktualisiereStatistik();
            if (_vm.OffeneZuordnungen > 0)
            {
                var r = MessageBox.Show($"{_vm.OffeneZuordnungen} assignments pending. Save anyway?",
                    "Pending", MessageBoxButton.YesNo, MessageBoxImage.Question);
                if (r == MessageBoxResult.No) return;
            }
            if (!TrySave(true)) return;
            DialogResult = true; Close();
        }

        void OnCreateCircuits()
        {
            _vm.AktualisiereStatistik();
            if (_vm.OffeneZuordnungen > 0)
            {
                var r = MessageBox.Show($"{_vm.OffeneZuordnungen} Räume ohne vollständige Zuordnung werden übersprungen.\n\nFortfahren?",
                    "Pending", MessageBoxButton.YesNo, MessageBoxImage.Question);
                if (r == MessageBoxResult.No) return;
            }
            if (!TrySave(true)) return;

            var uiDoc = new Autodesk.Revit.UI.UIDocument(_doc);
            CircuitBuilderResult result;
            try { result = new CircuitBuilderHandler().Run(uiDoc); }
            catch (Exception ex)
            {
                MessageBox.Show("Error:\n\n" + ex.Message, "ElecTriX",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            var td = new Autodesk.Revit.UI.TaskDialog("ElecTriX — Circuits")
            {
                MainInstruction = result.Abgebrochen
                    ? "Cancelled"
                    : (result.KreiseErstellt > 0
                        ? result.KreiseErstellt + " circuits created"
                        : (result.BestehendeKreiseUpdated > 0
                            ? result.BestehendeKreiseUpdated + " circuits updated"
                            : "No circuits created")),
                MainContent   = result.BuildSummary(),
                CommonButtons = Autodesk.Revit.UI.TaskDialogCommonButtons.Close,
            };
            td.Show();
        }

        // ── Theme ─────────────────────────────────────────────────────────
        protected override void OnThemeChanged()
        {
            // Footer
            if (_footerBorder != null)
            {
                _footerBorder.Background  = MeToolsTheme.BrFooter;
                _footerBorder.BorderBrush = MeToolsTheme.BrBorder;
            }
            if (_txtZugeordnet != null) _txtZugeordnet.Foreground = MeToolsTheme.BrMuted;
            if (_txtOffen      != null) _txtOffen.Foreground      = MeToolsTheme.BrMuted;
            if (_txtAuto       != null) _txtAuto.Foreground        = MeToolsTheme.BrMuted;

            // SK Inputs
            foreach (var sk in _allSkInputs)
            {
                sk.Background  = MeToolsTheme.BrInput;
                sk.Foreground  = MeToolsTheme.BrInputFg;
                sk.BorderBrush = MeToolsTheme.BrBorder;
            }
            foreach (var cb in _allComboBoxes)
            {
                cb.Background  = MeToolsTheme.BrInput;
                cb.Foreground  = MeToolsTheme.BrInputFg;
                cb.BorderBrush = MeToolsTheme.BrBorder;
            }
            if (_namingSchemeCombo != null)
            {
                _namingSchemeCombo.Background  = MeToolsTheme.BrInput;
                _namingSchemeCombo.Foreground  = MeToolsTheme.BrInputFg;
                _namingSchemeCombo.BorderBrush = MeToolsTheme.BrBorder;
            }

            // Tabs
            if (_activeTab != null) ShowTab(_activeTab, _activePanel);
        }

        // ── UI-Helpers ────────────────────────────────────────────────────
        UIElement SectionBadge(string title, SolidColorBrush color, string badgeText)
        {
            var sp = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 10) };
            sp.Children.Add(new TextBlock
            {
                Text = title.ToUpper(), FontSize = 10, FontWeight = FontWeights.SemiBold,
                Foreground = MeToolsTheme.BrMuted, VerticalAlignment = VerticalAlignment.Center,
            });
            sp.Children.Add(new Border
            {
                CornerRadius = new CornerRadius(10), Padding = new Thickness(8, 2, 8, 2),
                Margin = new Thickness(10, 0, 0, 0), BorderThickness = new Thickness(1),
                BorderBrush = color, Background = MeToolsTheme.BrSurface,
                VerticalAlignment = VerticalAlignment.Center,
                Child = new TextBlock { Text = badgeText, FontSize = 11, Foreground = color },
            });
            return sp;
        }

        Border GridContainer() => new Border
        {
            BorderBrush = MeToolsTheme.BrBorder, BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(5), Margin = new Thickness(0, 0, 0, 14),
            ClipToBounds = true, Child = new StackPanel(),
        };

        void GridHeader(Border c, string col0, string col1, string col2, string col3)
        {
            var sp   = (StackPanel)c.Child;
            var grid = new Grid { Background = MeToolsTheme.BrHeader, MinHeight = 30 };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(55) });
            int i = 0;
            foreach (var t in new[] { col0, col1, col2, col3 })
            {
                var tb = new TextBlock
                {
                    Text = t, FontSize = 11, FontWeight = FontWeights.SemiBold,
                    Foreground = MeToolsTheme.BrMuted, VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(8, 0, 8, 0),
                };
                Grid.SetColumn(tb, i++);
                grid.Children.Add(tb);
            }
            sp.Children.Add(new Border { BorderBrush = MeToolsTheme.BrBorder, BorderThickness = new Thickness(0, 0, 0, 1), Child = grid });
        }

        void GridDataRow(Border c, UIElement col0, UIElement col1, UIElement col2, UIElement col3)
        {
            var sp   = (StackPanel)c.Child;
            var grid = new Grid { MinHeight = 40 };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(55) });
            Grid.SetColumn(col0, 0); grid.Children.Add(col0);
            Grid.SetColumn(col1, 1); grid.Children.Add(col1);
            Grid.SetColumn(col2, 2); grid.Children.Add(col2);
            Grid.SetColumn(col3, 3); grid.Children.Add(col3);
            var row = new Border
            {
                BorderBrush = MeToolsTheme.BrBorder, BorderThickness = new Thickness(0, 0, 0, 1),
                Background  = MeToolsTheme.BrRow, Child = grid,
            };
            row.MouseEnter += (s, e) => row.Background = MeToolsTheme.BrActiveBg;
            row.MouseLeave += (s, e) => row.Background = MeToolsTheme.BrRow;
            sp.Children.Add(row);
        }

        // ── 8-Spalten-Layout für Rooms-Tab ──
        void GridHeader8(Border c, string h0, string h1, string h2, string h3, string h4, string h5, string h6, string h7)
        {
            var sp   = (StackPanel)c.Child;
            var grid = new Grid { Background = MeToolsTheme.BrHeader, MinHeight = 30 };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(60) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(210) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(85) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(85) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(85) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(85) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(28) });
            int i = 0;
            foreach (var t in new[] { h0, h1, h2, h3, h4, h5, h6, h7 })
            {
                var tb = new TextBlock
                {
                    Text = t, FontSize = 10, FontWeight = FontWeights.SemiBold,
                    Foreground = MeToolsTheme.BrMuted, VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(6, 0, 6, 0),
                };
                Grid.SetColumn(tb, i++);
                grid.Children.Add(tb);
            }
            sp.Children.Add(new Border { BorderBrush = MeToolsTheme.BrBorder, BorderThickness = new Thickness(0, 0, 0, 1), Child = grid });
        }

        void GridDataRow8(Border c, UIElement col0, UIElement col1, UIElement col2,
            UIElement col3, UIElement col4, UIElement col5, UIElement col6, UIElement col7)
        {
            var sp   = (StackPanel)c.Child;
            var grid = new Grid { MinHeight = 36 };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(60) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(210) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(85) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(85) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(85) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(85) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(28) });
            Grid.SetColumn(col0, 0); grid.Children.Add(col0);
            Grid.SetColumn(col1, 1); grid.Children.Add(col1);
            Grid.SetColumn(col2, 2); grid.Children.Add(col2);
            Grid.SetColumn(col3, 3); grid.Children.Add(col3);
            Grid.SetColumn(col4, 4); grid.Children.Add(col4);
            Grid.SetColumn(col5, 5); grid.Children.Add(col5);
            Grid.SetColumn(col6, 6); grid.Children.Add(col6);
            Grid.SetColumn(col7, 7); grid.Children.Add(col7);
            var row = new Border
            {
                BorderBrush = MeToolsTheme.BrBorder, BorderThickness = new Thickness(0, 0, 0, 1),
                Background = MeToolsTheme.BrRow, Child = grid,
            };
            row.MouseEnter += (s, e) => row.Background = MeToolsTheme.BrActiveBg;
            row.MouseLeave += (s, e) => row.Background = MeToolsTheme.BrRow;
            sp.Children.Add(row);
        }

        // ── 5-Spalten-Layout für Verteiler-Tab ──
        void GridHeader5V(Border c, string h0, string h1, string h2, string h3, string h4)
        {
            var sp   = (StackPanel)c.Child;
            var grid = new Grid { Background = MeToolsTheme.BrHeader, MinHeight = 30 };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(160) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(180) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(130) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(32) });
            int i = 0;
            foreach (var t in new[] { h0, h1, h2, h3, h4 })
            {
                var tb = new TextBlock
                {
                    Text = t, FontSize = 11, FontWeight = FontWeights.SemiBold,
                    Foreground = MeToolsTheme.BrMuted, VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(8, 0, 8, 0),
                };
                Grid.SetColumn(tb, i++);
                grid.Children.Add(tb);
            }
            sp.Children.Add(new Border { BorderBrush = MeToolsTheme.BrBorder, BorderThickness = new Thickness(0, 0, 0, 1), Child = grid });
        }

        void GridDataRow5V(Border c, UIElement col0, UIElement col1, UIElement col2, UIElement col3, UIElement col4)
        {
            var sp   = (StackPanel)c.Child;
            var grid = new Grid { MinHeight = 38 };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(160) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(180) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(130) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(32) });
            Grid.SetColumn(col0, 0); grid.Children.Add(col0);
            Grid.SetColumn(col1, 1); grid.Children.Add(col1);
            Grid.SetColumn(col2, 2); grid.Children.Add(col2);
            Grid.SetColumn(col3, 3); grid.Children.Add(col3);
            Grid.SetColumn(col4, 4); grid.Children.Add(col4);
            var row = new Border
            {
                BorderBrush = MeToolsTheme.BrBorder, BorderThickness = new Thickness(0, 0, 0, 1),
                Background = MeToolsTheme.BrRow, Child = grid,
            };
            row.MouseEnter += (s, e) => row.Background = MeToolsTheme.BrActiveBg;
            row.MouseLeave += (s, e) => row.Background = MeToolsTheme.BrRow;
            sp.Children.Add(row);
        }

        Ellipse StatusDotForRow(RaumZeile z)
        {
            return new Ellipse
            {
                Width = 9, Height = 9,
                Fill = z.IstOffen ? MeToolsTheme.BrOrange : MeToolsTheme.BrGreen,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            };
        }

        void UpdateDotForRow(Ellipse dot, RaumZeile z)
        {
            dot.Fill = z.IstOffen ? MeToolsTheme.BrOrange : MeToolsTheme.BrGreen;
        }

        TextBlock TextCell(string text, bool bold, bool small = false, bool mono = false) => new TextBlock
        {
            Text = text, FontSize = small ? 11 : 13,
            FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal,
            FontFamily = mono ? new FontFamily("Consolas") : FontFamily,
            Foreground = MeToolsTheme.BrText, VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(8, 0, 6, 0), TextTrimming = TextTrimming.CharacterEllipsis,
        };

        UIElement KuerzelBadge(string k) => new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(0xFF, 0xF0, 0xD0)),
            CornerRadius = new CornerRadius(3), Padding = new Thickness(6, 2, 6, 2),
            Margin = new Thickness(6, 0, 0, 0), HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Center,
            Child = new TextBlock
            {
                Text = k, FontFamily = new FontFamily("Consolas"),
                FontWeight = FontWeights.Bold, FontSize = 11,
                Foreground = new SolidColorBrush(Color.FromRgb(0x7A, 0x45, 0x00)),
            },
        };

        UIElement VerteilerBadge(string name, bool auto) => new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(0xE0, 0xF0, 0xF0)),
            CornerRadius = new CornerRadius(3), Padding = new Thickness(6, 2, 6, 2),
            Margin = new Thickness(6, 0, 0, 0), HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Center,
            Child = new TextBlock
            {
                Text = name ?? "", FontFamily = new FontFamily("Consolas"),
                FontWeight = FontWeights.Bold, FontSize = 11,
                Foreground = MeToolsTheme.BrPetrol,
            },
        };

        Ellipse StatusDot(string stromkreis)
        {
            bool ok = !string.IsNullOrWhiteSpace(stromkreis) && stromkreis != "??";
            return new Ellipse
            {
                Width = 9, Height = 9,
                Fill  = ok ? MeToolsTheme.BrGreen : MeToolsTheme.BrOrange,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment   = VerticalAlignment.Center,
            };
        }

        void UpdateDot(Ellipse dot, string val)
        {
            bool ok = !string.IsNullOrWhiteSpace(val) && val != "??";
            dot.Fill = ok ? MeToolsTheme.BrGreen : MeToolsTheme.BrOrange;
        }
    }
}

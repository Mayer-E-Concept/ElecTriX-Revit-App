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
        private Border      _tab1, _tab2, _tab3;
        private StackPanel  _panel1, _panel2, _panel3;
        private Border      _activeTab;
        private StackPanel  _activePanel;

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
            BuildStatusBar($"{_vm.RaumZeilen.Count + _vm.SonderZeilen.Count} Einträge");

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

            _panel1 = BuildRaeumePanel();
            _panel2 = BuildSonderPanel();
            _panel3 = BuildVerteilerPanel();

            _tab1 = MakeTab("Rooms",           MeToolsTheme.CPetrol,  () => ShowTab(_tab1, _panel1));
            _tab2 = MakeTab("Special Outlets", MeToolsTheme.COrange,  () => ShowTab(_tab2, _panel2));
            _tab3 = MakeTab("Panels",         MeToolsTheme.CBlue,   () => ShowTab(_tab3, _panel3));

            sp.Children.Add(_tab1);
            sp.Children.Add(_tab2);
            sp.Children.Add(_tab3);
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
        StackPanel BuildRaeumePanel()
        {
            var sp = new StackPanel { Visibility = Visibility.Collapsed };
            sp.Children.Add(SectionBadge("Rooms — Circuit Parameters",
                MeToolsTheme.BrPetrol, $"{_vm.RaumZeilen.Count} Räume"));

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
                Text = "Alle 4 Scheme-Werte pro Raum editierbar. Defaults kommen aus dem Circuit Prefix des Verteilers.",
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

            var container = GridContainer();
            GridHeader8(container, "Room #", "Name", "Panel", "Vorsich.", "FI-Kreis", "Sicher.", "Gerät", "●");

            foreach (var z in _vm.RaumZeilen)
            {
                var cb = VerteilerDropdown(z);
                var vs = SmallInput(z.Vorsicherung, "Vorsicherung");
                var fi = SmallInput(z.FIKreis,      "FI-Kreis");
                var si = SmallInput(z.Stromkreis,   "Sicherung (Haupt-Kreis)");
                var ge = SmallInput(z.Geraet,       "Gerät");

                _allComboBoxes.Add(cb);
                _allSkInputs.Add(vs); _allSkInputs.Add(fi); _allSkInputs.Add(si); _allSkInputs.Add(ge);

                var dot = StatusDotForRow(z);

                vs.TextChanged += (s, e) => { z.Vorsicherung = vs.Text; };
                fi.TextChanged += (s, e) => { z.FIKreis      = fi.Text; };
                si.TextChanged += (s, e) =>
                {
                    z.Stromkreis = si.Text;
                    UpdateDotForRow(dot, z); _vm.AktualisiereStatistik(); RefreshFooter();
                };
                ge.TextChanged += (s, e) => { z.Geraet = ge.Text; };
                cb.SelectionChanged += (s, e) =>
                {
                    z.Verteiler = cb.Text ?? (cb.SelectedItem as string) ?? "";
                    UpdateDotForRow(dot, z); _vm.AktualisiereStatistik(); RefreshFooter();
                };
                cb.LostFocus += (s, e) =>
                {
                    z.Verteiler = cb.Text ?? "";
                    UpdateDotForRow(dot, z); _vm.AktualisiereStatistik(); RefreshFooter();
                };

                GridDataRow8(container,
                    TextCell(z.RaumNummer ?? "", true, mono: true),
                    TextCell(z.RaumName ?? "", false),
                    cb, vs, fi, si, ge, dot);
            }
            sp.Children.Add(container);
            return sp;
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
                    cb.ToolTip = "Dieser Verteiler existiert nicht mehr im Modell. Bitte neu zuweisen.";
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
                MeToolsTheme.BrOrange, $"{_vm.SonderZeilen.Count} Kürzel"));

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
                ToolTip = "Wird auf alle Verteiler angewendet, für die Kreise erstellt werden.",
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
                Text = "Räume automatisch zu Verteilern zuordnen anhand Match-Prefix:",
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
                    Text = "Diese Verteiler existieren im Modell nicht (mehr). " +
                           "Wähle einen Ersatz und klicke 'Umleiten' um alle Räume zu migrieren.",
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
                        Content = "Umleiten", Height = 24, FontSize = 11,
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
                            MessageBox.Show("Bitte zuerst einen Ziel-Verteiler auswählen.",
                                "Umleiten", MessageBoxButton.OK, MessageBoxImage.Warning);
                            return;
                        }
                        int n = _vm.MigriereVerteiler(capturedAlt, neu);
                        MessageBox.Show($"{n} Räume wurden von '{capturedAlt}' auf '{neu}' umgeleitet.\n\n" +
                            "Zum Speichern: 'Save' klicken.",
                            "Umleiten", MessageBoxButton.OK, MessageBoxImage.Information);
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
                    ToolTip = "Räume deren Nummer mit diesem Prefix beginnt werden diesem Verteiler zugeordnet.",
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
                ToolTip = "ElecTriX-Einstellungen öffnen (JSON-Datei in Editor)\n" +
                          "Templates, Kategorie-Labels, Match-Prefix-Patterns, Parameter-Namen",
            };
            btnSettings.Click += (s, e) =>
            {
                try
                {
                    ElecTriXSettingsStorage.OeffneInEditor();
                    MessageBox.Show(
                        "Die Datei 'electrix-settings.json' wurde im Editor geöffnet:\n\n" +
                        ElecTriXSettingsStorage.GetSettingsPath() + "\n\n" +
                        "Nach dem Editieren: Datei speichern, Dialog hier schließen und neu öffnen " +
                        "damit die Änderungen wirksam werden.",
                        "ElecTriX Settings", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show("Konnte Settings nicht öffnen:\n" + ex.Message,
                        "Fehler", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            };
            btnSp.Children.Add(btnSettings);

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
                MessageBox.Show("Speichern fehlgeschlagen:\n\n" + (_vm.FehlerMeldung ?? "(unbekannt)"),
                    "ElecTriX — Fehler", MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }
            if (!silent)
                MessageBox.Show("Konfiguration gespeichert.\n\n" + (_vm.SpeicherInfo ?? "") +
                    "\n\nFür dauerhafte Speicherung: Revit-Projekt mit Strg+S sichern.",
                    "ElecTriX", MessageBoxButton.OK, MessageBoxImage.Information);
            return true;
        }

        void OnSaveOnly() { _vm.AktualisiereStatistik(); TrySave(false); }

        void OnSaveAndClose()
        {
            _vm.AktualisiereStatistik();
            if (_vm.OffeneZuordnungen > 0)
            {
                var r = MessageBox.Show($"{_vm.OffeneZuordnungen} Zuordnungen pending. Trotzdem speichern?",
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
                MessageBox.Show("Fehler:\n\n" + ex.Message, "ElecTriX",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            var td = new Autodesk.Revit.UI.TaskDialog("ElecTriX — Stromkreise")
            {
                MainInstruction = result.Abgebrochen
                    ? "Abgebrochen"
                    : (result.KreiseErstellt > 0
                        ? result.KreiseErstellt + " Stromkreise erstellt"
                        : (result.BestehendeKreiseUpdated > 0
                            ? result.BestehendeKreiseUpdated + " Kreise aktualisiert"
                            : "Keine Kreise erstellt")),
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

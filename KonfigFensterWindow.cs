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
        private readonly List<TextBox> _allSkInputs = new List<TextBox>();

        public KonfigFensterWindow(Document doc)
        {
            _doc = doc;
            _vm  = new KonfigViewModel(doc);
            InitWindow("Circuit Configuration", 600, isDialog: true);
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

        // ── Panel 1: Räume ────────────────────────────────────────────────
        StackPanel BuildRaeumePanel()
        {
            var sp = new StackPanel { Visibility = Visibility.Collapsed };

            sp.Children.Add(SectionBadge("Rooms → Stromkreis",
                MeToolsTheme.BrPetrol, $"{_vm.RaumZeilen.Count} Raumtypen erkannt"));

            var container = GridContainer();
            GridHeader(container, "Room Type", "Synonyms", "Circuit", "Status");

            foreach (var z in _vm.RaumZeilen)
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
                    TextCell(z.KanonischerName, true),
                    TextCell(z.SynonymAnzeige, false, small: true),
                    sk, dot);
            }
            sp.Children.Add(container);
            return sp;
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

        // ── Panel 3: Verteiler ────────────────────────────────────────────
        StackPanel BuildVerteilerPanel()
        {
            var sp = new StackPanel { Visibility = Visibility.Collapsed };

            sp.Children.Add(SectionBadge("Panel Assignment",
                MeToolsTheme.BrBlue, $"{_vm.VerteilerZeilen.Count} aus Modell"));

            // Schema-Box
            var schemaBox = new Border
            {
                Background = MeToolsTheme.BrSurface, BorderBrush = MeToolsTheme.BrBorder,
                BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(5),
                Padding = new Thickness(12, 8, 12, 8), Margin = new Thickness(0, 0, 0, 12),
            };
            var schemaSp = new StackPanel();
            schemaSp.Children.Add(new TextBlock
            {
                Text = _vm.SchemaInfo, FontWeight = FontWeights.SemiBold,
                FontSize = 12, Foreground = MeToolsTheme.BrPetrol,
            });
            schemaSp.Children.Add(new TextBlock
            {
                Text = _vm.SchemaBeschreibung, FontSize = 11,
                Foreground = MeToolsTheme.BrMuted, Margin = new Thickness(0, 3, 0, 0),
            });
            schemaBox.Child = schemaSp;
            sp.Children.Add(schemaBox);

            if (!_vm.VerteilerZeilen.Any())
            {
                sp.Children.Add(new TextBlock
                {
                    Text = "No panels found in model.",
                    FontSize = 11, Foreground = MeToolsTheme.BrMuted,
                    Margin = new Thickness(0, 0, 0, 12),
                });
                return sp;
            }

            var container = GridContainer();
            GridHeader(container, "Unit", "Panels", "Rooms", "Status");

            foreach (var v in _vm.VerteilerZeilen)
            {
                var dot = new Ellipse
                {
                    Width = 9, Height = 9,
                    Fill  = v.AutomatischErkannt ? MeToolsTheme.BrBlue : MeToolsTheme.BrOrange,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment   = VerticalAlignment.Center,
                    ToolTip = v.StatusTooltip,
                };
                GridDataRow(container,
                    TextCell(v.WohnungsId, true, mono: true),
                    VerteilerBadge(v.VerteilerName, v.AutomatischErkannt),
                    TextCell(v.RaeumeAnzeige, false, small: true),
                    dot);
            }
            sp.Children.Add(container);
            return sp;
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

            // Buttons
            var btnSp = new StackPanel { Orientation = Orientation.Horizontal };
            var ab = FooterBtn("Cancel", false, () => OnCloseClicked());
            ab.Margin = new Thickness(0, 0, 8, 0);
            btnSp.Children.Add(ab);
            var btnSave = FooterBtn("Save & Close", true, OnSpeichern);
            btnSave.FontSize = 12;
            btnSave.Padding  = new System.Windows.Thickness(14, 0, 14, 0);
            btnSp.Children.Add(btnSave);
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

        void OnSpeichern()
        {
            _vm.AktualisiereStatistik();
            if (_vm.OffeneZuordnungen > 0)
            {
                var r = MessageBox.Show(
                    $"Es sind noch {_vm.OffeneZuordnungen} Zuordnungen pending.\n\nTrotzdem speichern?",
                    "Pending Assignments", MessageBoxButton.YesNo, MessageBoxImage.Question);
                if (r == MessageBoxResult.No) return;
            }
            DialogResult = true;
            Close();
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

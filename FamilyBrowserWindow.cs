// FamilyBrowserWindow.cs -- ME-Tools | Family Browser
// Shows loaded _E_CAx electrical families grouped by category.
// Allows loading new .rfa families from disk into the project.
// Pure WPF, no XAML. Mayer E-Concept SRL
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Microsoft.Win32;

namespace METools
{
    public class FamilyBrowserWindow : MeToolsWindowBase
    {
        protected override string AppKey => "FamilyBrowser";

        private readonly UIApplication _uiApp;
        private Document               _doc => _uiApp?.ActiveUIDocument?.Document;

        // Placement via ExternalEvent (Revit API requires a valid context)
        private ExternalEvent        _placeEv;
        private FamilyBrowserPlacer  _placer;

        // Family data
        private List<FamilyEntry>       _families    = new List<FamilyEntry>();
        private List<FamilyEntry>       _filtered    = new List<FamilyEntry>();
        private string                  _searchText  = "";
        private string                  _activeGroup = "";

        // UI references
        private StackPanel  _groupBar;
        private StackPanel  _listSp;
        private System.Windows.Controls.TextBox     _searchBox;
        private ScrollViewer _scroll;
        private TextBlock   _countLabel;

        private class FamilyEntry
        {
            public string    Name      { get; set; }
            public string    Group     { get; set; }
            public int       TypeCount { get; set; }
            public ElementId FamilyId  { get; set; }
            public List<string> TypeNames { get; set; } = new List<string>();
        }

        // Group definitions — order matters, first match wins.
        // Matching is case-INSENSITIVE: German compound words (e.g. "Wechselschalter",
        // "Serienschalter") don't capitalize their second half, so a case-sensitive
        // Contains("Schalter") silently misses them — that was the main reason so many
        // families fell through to "Other". A couple of short acronyms (GSM, RWA) use a
        // word-boundary match instead of plain Contains, since as plain substrings they
        // can appear by accident inside unrelated German compounds (e.g. "GSM" inside
        // "Bewegung**gsm**elder").
        private static bool Ci(string n, string s) => n.IndexOf(s, StringComparison.OrdinalIgnoreCase) >= 0;
        private static bool Word(string n, string s) =>
            System.Text.RegularExpressions.Regex.IsMatch(n, $@"\b{System.Text.RegularExpressions.Regex.Escape(s)}\b",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        private static readonly List<(string Label, Func<string, bool> Match)> Groups = new()
        {
            ("Sockets",     n => Ci(n,"Steckdose") || Ci(n,"socket") || Ci(n,"Herd") || Ci(n,"schaltbar")
                                || Ci(n,"Bodendose") || Ci(n,"Bodentank")),
            ("Switches",    n => Ci(n,"Schalter") || Ci(n,"switch") || Ci(n,"Taster") || Ci(n,"Jalousie")
                                || Ci(n,"pushers") || Ci(n,"shutter")),
            ("Fire Alarm",  n => Ci(n,"Druckknopf") || Ci(n,"Wärmemelder") || Ci(n,"Brandmelde") || Ci(n,"Feststellzen")
                                || Ci(n,"Blitzleuchte") || Ci(n,"Sirene") || Ci(n,"smoke") || Ci(n,"fire") || Ci(n,"alarm")
                                || Ci(n,"Rettungsweg") || Word(n,"RWA") || Ci(n,"Auslöse") || Ci(n,"Auswerteeinheit")
                                || Ci(n,"release button") || Ci(n,"ventilation") || Ci(n,"safety") || Ci(n,"security")
                                || Ci(n,"Lufter") || Ci(n,"Lüfter") || Ci(n,"sicherheit")),
            ("Lightning Protection", n => Ci(n,"Blitzschutz") || Ci(n,"Blitzleiter") || Ci(n,"Fangstange")
                                || Ci(n,"Erder") || Ci(n,"Potential") || Ci(n,"Potenzial")),
            ("Lighting",    n => Ci(n,"Beleuchtung") || Ci(n,"lighting") || Ci(n,"Ceiling") || Ci(n,"lamp")
                                || Ci(n,"Leuchte") || Ci(n,"licht") || Ci(n,"W-Auslass") || Ci(n,"light")),
            ("Panels",      n => Ci(n,"Verteiler") || Ci(n,"Panel") || Ci(n,"Schaltschrank") || Ci(n,"Distribution")
                                || Ci(n,"Anschlüsse") || Ci(n,"Anschlusskasten") || Ci(n,"connection")),
            ("Data / Comms",n => Ci(n,"EDV") || Ci(n,"Dose") || Ci(n,"RJ45") || Ci(n,"Datennetz") || Ci(n,"data") || Ci(n,"comms")
                                || Ci(n,"intercom") || Word(n,"GSM") || Ci(n,"Telefon") || Ci(n,"fibre") || Ci(n,"sprech")),
            ("Sensors",     n => Ci(n,"Temperature") || Ci(n,"Temperatur") || Ci(n,"sensor") || Ci(n,"Melder") || Ci(n,"Fühler")),
            ("Conduit & Fittings", n => Ci(n,"Bogen") || Ci(n,"Knie") || Ci(n,"Reduzierung") || Ci(n,"T-Stück")
                                || Ci(n,"Tstück") || Ci(n,"Z-Sprung") || Ci(n,"KreuzTstück")),
            ("Other",       n => true),
        };

        public FamilyBrowserWindow(UIApplication uiApp)
        {
            _uiApp  = uiApp;
            _placer = new FamilyBrowserPlacer();
            _placeEv = ExternalEvent.Create(_placer);
            S.SetLanguage(SettingsStore.Language ?? "en");
            InitWindow(S.Get("browser.title"), 520, false);
            // Fix window to a set size — don't auto-grow with content
            Height        = 720;
            MaxHeight     = 860;
            MinHeight     = 400;
            SizeToContent = System.Windows.SizeToContent.Manual;
            ResizeMode    = System.Windows.ResizeMode.CanResizeWithGrip;
            Build();
            LoadFamilies();
        }

        private void Build()
        {
            var root = new DockPanel { Background = MeToolsTheme.BrBg, LastChildFill = true };

            // ── Header ───────────────────────────────────────────────────────
            var hdr = new Border
            {
                Background = MeToolsTheme.BrSurface,
                BorderBrush = MeToolsTheme.BrBorder, BorderThickness = new Thickness(0, 0, 0, 1),
                Padding = new Thickness(14, 10, 14, 10),
            };
            var hdrSp = new StackPanel();
            var title = new TextBlock
            {
                Text = S.Get("browser.title"),
                FontSize = 15, FontWeight = FontWeights.SemiBold,
                Foreground = MeToolsTheme.BrPetrol,
            };
            var subtitle = new TextBlock
            {
                Text = S.Get("browser.subtitle"),
                FontSize = 10, Foreground = MeToolsTheme.BrMuted, Margin = new Thickness(0, 2, 0, 0),
            };
            hdrSp.Children.Add(title);
            hdrSp.Children.Add(subtitle);
            hdr.Child = hdrSp;
            DockPanel.SetDock(hdr, Dock.Top);
            root.Children.Add(hdr);

            // ── Footer (Load button + count) ─────────────────────────────────
            var ftr = new Border
            {
                Background = MeToolsTheme.BrSurface,
                BorderBrush = MeToolsTheme.BrBorder, BorderThickness = new Thickness(0, 1, 0, 0),
                Padding = new Thickness(14, 10, 14, 10),
            };
            var ftrSp = new StackPanel { Orientation = Orientation.Horizontal };
            _countLabel = new TextBlock
            {
                FontSize = 11, Foreground = MeToolsTheme.BrMuted,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 0, 0),
            };
            var btnLoad = FooterBtn(S.Get("browser.load_from_disk"), true, LoadFromDisk);
            btnLoad.Margin = new Thickness(0, 0, 10, 0);
            var btnRefresh = FooterBtn(S.Get("refresh"), false, () => { LoadFamilies(); });
            btnRefresh.Margin = new Thickness(0, 0, 0, 0);
            ftrSp.Children.Add(btnLoad);
            ftrSp.Children.Add(btnRefresh);

            // count on right
            var ftrDp = new DockPanel();
            DockPanel.SetDock(ftrSp, Dock.Left);
            ftrDp.Children.Add(ftrSp);
            ftrDp.Children.Add(_countLabel);
            _countLabel.HorizontalAlignment = HorizontalAlignment.Right;
            ftr.Child = ftrDp;
            DockPanel.SetDock(ftr, Dock.Bottom);
            root.Children.Add(ftr);

            // ── Search bar ───────────────────────────────────────────────────
            var searchBar = new Border
            {
                Background = MeToolsTheme.BrSurface,
                BorderBrush = MeToolsTheme.BrBorder, BorderThickness = new Thickness(0, 0, 0, 1),
                Padding = new Thickness(14, 8, 14, 8),
            };
            _searchBox = new System.Windows.Controls.TextBox
            {
                Height = 28, FontSize = 12,
                Background = MeToolsTheme.BrInput, Foreground = MeToolsTheme.BrInputFg,
                BorderBrush = MeToolsTheme.BrBorder, BorderThickness = new Thickness(1),
                Padding = new Thickness(8, 4, 8, 4),
                VerticalContentAlignment = VerticalAlignment.Center,
            };
            _searchBox.TextChanged += (s, e) => { _searchText = _searchBox.Text; Refilter(); };
            // Placeholder text
            var searchHint = new TextBlock
            {
                Text = S.Get("browser.search"), FontSize = 12, Foreground = MeToolsTheme.BrMuted,
                IsHitTestVisible = false, Margin = new Thickness(10, 6, 0, 0),
                VerticalAlignment = VerticalAlignment.Top,
            };
            _searchBox.GotFocus  += (s, e) => searchHint.Visibility = System.Windows.Visibility.Collapsed;
            _searchBox.LostFocus += (s, e) => { if (string.IsNullOrEmpty(_searchBox.Text)) searchHint.Visibility = System.Windows.Visibility.Visible; };
            var searchGrid = new System.Windows.Controls.Grid();
            searchGrid.Children.Add(_searchBox);
            searchGrid.Children.Add(searchHint);
            searchBar.Child = searchGrid;
            DockPanel.SetDock(searchBar, Dock.Top);
            root.Children.Add(searchBar);

            // ── Group filter bar ─────────────────────────────────────────────
            var groupBorder = new Border
            {
                Background = MeToolsTheme.BrSurface,
                BorderBrush = MeToolsTheme.BrBorder, BorderThickness = new Thickness(0, 0, 0, 1),
                Padding = new Thickness(8, 6, 8, 6),
            };
            _groupBar = new StackPanel { Orientation = Orientation.Horizontal };
            var groupScroll = new ScrollViewer
            {
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                VerticalScrollBarVisibility = ScrollBarVisibility.Hidden,
                Content = _groupBar,
            };
            groupBorder.Child = groupScroll;
            DockPanel.SetDock(groupBorder, Dock.Top);
            root.Children.Add(groupBorder);

            // ── Family list ──────────────────────────────────────────────────
            _scroll = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Background = MeToolsTheme.BrBg,
            };
            _listSp = new StackPanel { Margin = new Thickness(8, 4, 8, 4) };
            _scroll.Content = _listSp;
            root.Children.Add(_scroll);

            var _fbGrid = new System.Windows.Controls.Grid();
            _fbGrid.Children.Add(root);
            _fbGrid.Children.Add(Watermark());
            RootDock.Children.Add(_fbGrid);
            BuildStatusBar("", "Revit 2025");
        }

        private void LoadFamilies()
        {
            _families.Clear();
            var doc = _doc;
            if (doc == null) { if (StatusLeft != null) StatusLeft.Text = "No document open."; return; }

            try
            {
                // Collect all loaded families starting with _E_CAx
                var families = new FilteredElementCollector(doc)
                    .OfClass(typeof(Family))
                    .Cast<Family>()
                    .Where(f => f.Name.StartsWith("_E_CAx", StringComparison.OrdinalIgnoreCase) ||
                                f.Name.StartsWith("_E_CAx", StringComparison.OrdinalIgnoreCase))
                    .OrderBy(f => f.Name)
                    .ToList();

                foreach (var fam in families)
                {
                    try
                    {
                        var types = fam.GetFamilySymbolIds()
                            .Select(id => doc.GetElement(id) as FamilySymbol)
                            .Where(fs => fs != null)
                            .Select(fs => fs.Name)
                            .OrderBy(n => n)
                            .ToList();

                        string group = ClassifyFamily(fam.Name);
                        _families.Add(new FamilyEntry
                        {
                            Name = fam.Name, Group = group,
                            TypeCount = types.Count, FamilyId = fam.Id,
                            TypeNames = types,
                        });
                    }
                    catch { }
                }

                // Build group filter buttons
                BuildGroupBar();
                _activeGroup = "";
                Refilter();
                if (StatusLeft != null) StatusLeft.Text = $"{_families.Count} families loaded";
            }
            catch (Exception ex)
            {
                if (StatusLeft != null) StatusLeft.Text = "Error: " + ex.Message;
            }
        }

        private string ClassifyFamily(string name)
        {
            foreach (var (label, match) in Groups)
                if (match(name)) return label;
            return "Other";
        }

        private void BuildGroupBar()
        {
            _groupBar.Children.Clear();

            // "All" button
            AddGroupBtn(S.Get("browser.all"), "");

            // One button per group that has at least one family
            var presentGroups = _families.Select(f => f.Group).Distinct().OrderBy(g =>
            {
                int i = Groups.FindIndex(x => x.Label == g);
                return i < 0 ? 99 : i;
            }).ToList();

            foreach (var g in presentGroups)
                AddGroupBtn(GroupDisplay(g), g);
        }

        private void AddGroupBtn(string label, string group)
        {
            int count = group == "" ? _families.Count : _families.Count(f => f.Group == group);
            var btn = new Border
            {
                Background = MeToolsTheme.BrInput,
                BorderBrush = MeToolsTheme.BrBorder, BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(10, 4, 10, 4), Margin = new Thickness(0, 0, 6, 0),
                Cursor = Cursors.Hand,
            };
            var sp = new StackPanel { Orientation = Orientation.Horizontal };
            sp.Children.Add(new TextBlock
            {
                Text = label, FontSize = 10, FontWeight = FontWeights.SemiBold,
                Foreground = MeToolsTheme.BrText, VerticalAlignment = VerticalAlignment.Center,
            });
            sp.Children.Add(new Border
            {
                Background = MeToolsTheme.BrBorder, CornerRadius = new CornerRadius(8),
                Margin = new Thickness(5, 0, 0, 0), Padding = new Thickness(5, 1, 5, 1),
                Child = new TextBlock { Text = count.ToString(), FontSize = 9, Foreground = MeToolsTheme.BrMuted },
            });
            btn.Child = sp;
            btn.MouseLeftButtonUp += (s, e) =>
            {
                _activeGroup = group;
                // Update active styling
                foreach (var child in _groupBar.Children.OfType<Border>())
                {
                    child.Background = MeToolsTheme.BrInput;
                    child.BorderBrush = MeToolsTheme.BrBorder;
                    if (child.Tag is string t)
                        foreach (var tb in child.FindVisualChildren<TextBlock>())
                            tb.Foreground = MeToolsTheme.BrText;
                }
                btn.Background = MeToolsTheme.BrPetrol;
                btn.BorderBrush = MeToolsTheme.BrPetrolDark;
                foreach (var tb in btn.FindVisualChildren<TextBlock>())
                    tb.Foreground = System.Windows.Media.Brushes.White;
                Refilter();
            };
            btn.Tag = group;
            _groupBar.Children.Add(btn);
        }

        private void Refilter()
        {
            _filtered = _families
                .Where(f => (_activeGroup == "" || f.Group == _activeGroup) &&
                            (string.IsNullOrWhiteSpace(_searchText) ||
                             f.Name.IndexOf(_searchText, StringComparison.OrdinalIgnoreCase) >= 0))
                .ToList();
            RebuildList();
        }

        private void RebuildList()
        {
            _listSp.Children.Clear();
            if (_filtered.Count == 0)
            {
                _listSp.Children.Add(new TextBlock
                {
                    Text = "No families found.", FontSize = 12,
                    Foreground = MeToolsTheme.BrMuted,
                    Margin = new Thickness(8, 20, 8, 0),
                    HorizontalAlignment = HorizontalAlignment.Center,
                });
                _countLabel.Text = "0 " + S.Get("browser.families");
                return;
            }

            // Group by category if "All" is selected, otherwise flat list
            if (_activeGroup == "" && string.IsNullOrWhiteSpace(_searchText))
            {
                var byGroup = _filtered.GroupBy(f => f.Group).OrderBy(g =>
                {
                    int i = Groups.FindIndex(x => x.Label == g.Key);
                    return i < 0 ? 99 : i;
                });
                foreach (var grp in byGroup)
                {
                    // Group header
                    var hdr = new Border
                    {
                        Background = MeToolsTheme.BrSurface,
                        BorderBrush = MeToolsTheme.BrBorder, BorderThickness = new Thickness(0, 0, 0, 1),
                        Padding = new Thickness(10, 6, 10, 6), Margin = new Thickness(0, 8, 0, 2),
                    };
                    var hdrSp = new StackPanel { Orientation = Orientation.Horizontal };
                    hdrSp.Children.Add(new TextBlock
                    {
                        Text = GroupDisplay(grp.Key), FontWeight = FontWeights.SemiBold,
                        FontSize = 11, Foreground = MeToolsTheme.BrPetrol,
                    });
                    hdrSp.Children.Add(new TextBlock
                    {
                        Text = $"  {grp.Count()} " + S.Get("browser.families"),
                        FontSize = 10, Foreground = MeToolsTheme.BrMuted,
                        VerticalAlignment = VerticalAlignment.Center,
                    });
                    hdr.Child = hdrSp;
                    _listSp.Children.Add(hdr);
                    foreach (var fam in grp.OrderBy(f => f.Name))
                        _listSp.Children.Add(MakeFamilyRow(fam));
                }
            }
            else
            {
                foreach (var fam in _filtered.OrderBy(f => f.Name))
                    _listSp.Children.Add(MakeFamilyRow(fam));
            }

            _countLabel.Text = $"{_filtered.Count} / {_families.Count} " + S.Get("browser.families");
        }

        private UIElement MakeFamilyRow(FamilyEntry fam)
        {
            var row = new Border
            {
                Background = MeToolsTheme.BrSurface,
                BorderBrush = MeToolsTheme.BrBorder, BorderThickness = new Thickness(0, 0, 0, 1),
                Padding = new Thickness(10, 8, 10, 8), Margin = new Thickness(0, 0, 0, 0),
                Cursor = Cursors.Hand,
            };

            var grid = new System.Windows.Controls.Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });   // thumbnail
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });  // name
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });   // badge/btn

            // Thumbnail: colored category tile with abbreviation
            // (Revit's GetPreviewImage is not available on Family in 2025 API)
            var tileColor = GroupColor(fam.Group);
            var thumbBorder = new Border
            {
                Width = 44, Height = 44, Margin = new Thickness(0, 0, 10, 0),
                Background = tileColor,
                CornerRadius = new CornerRadius(4),
                VerticalAlignment = VerticalAlignment.Center,
            };
            thumbBorder.Child = new TextBlock
            {
                Text = GroupAbbrev(fam.Group),
                FontSize = 13, FontWeight = FontWeights.Bold,
                Foreground = System.Windows.Media.Brushes.White,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment   = VerticalAlignment.Center,
            };
            System.Windows.Controls.Grid.SetColumn(thumbBorder, 0);
            grid.Children.Add(thumbBorder);

            // Left: name + types
            var left = new StackPanel();
            left.Children.Add(new TextBlock
            {
                Text = fam.Name, FontSize = 11, FontWeight = FontWeights.SemiBold,
                Foreground = MeToolsTheme.BrText, TextWrapping = TextWrapping.Wrap,
            });

            if (fam.TypeNames.Count > 0)
            {
                left.Children.Add(new TextBlock
                {
                    Text = string.Join("  ·  ", fam.TypeNames.Take(5)) +
                           (fam.TypeNames.Count > 5 ? $"  +{fam.TypeNames.Count - 5} more" : ""),
                    FontSize = 10, Foreground = MeToolsTheme.BrMuted,
                    TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 0),
                });
            }

            System.Windows.Controls.Grid.SetColumn(left, 1);
            grid.Children.Add(left);

            // Right: type count badge
            var badge = new Border
            {
                Background = MeToolsTheme.BrBg, CornerRadius = new CornerRadius(10),
                BorderBrush = MeToolsTheme.BrBorder, BorderThickness = new Thickness(1),
                Padding = new Thickness(8, 3, 8, 3),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(8, 0, 0, 0),
            };
            badge.Child = new TextBlock
            {
                Text = fam.TypeCount == 1 ? "1 " + S.Get("browser.type") : $"{fam.TypeCount} " + S.Get("browser.types"),
                FontSize = 10, Foreground = MeToolsTheme.BrMuted,
            };
            System.Windows.Controls.Grid.SetColumn(badge, 2);
            grid.Children.Add(badge);

            row.Child = grid;

            // "Place" button — triggers Revit placement mode for the first type
            var btnPlace = new System.Windows.Controls.Button
            {
                Content    = "Place",
                Height     = 24, Padding = new Thickness(10, 0, 10, 0),
                FontSize   = 10,
                Background = MeToolsTheme.BrPetrol,
                Foreground = System.Windows.Media.Brushes.White,
                BorderBrush = MeToolsTheme.BrPetrol,
                BorderThickness = new Thickness(1),
                VerticalAlignment = VerticalAlignment.Center,
                Margin     = new Thickness(8, 0, 0, 0),
                ToolTip    = "Click to enter placement mode for this family (like drag-and-drop)",
                Cursor     = Cursors.Hand,
                Visibility = System.Windows.Visibility.Collapsed,
                Template   = RoundedBtnTemplate(),
            };
            btnPlace.MouseEnter += (s, e) => btnPlace.Background = MeToolsTheme.BrPetrolDark;
            btnPlace.MouseLeave += (s, e) => btnPlace.Background = MeToolsTheme.BrPetrol;
            var capturedFam = fam;
            btnPlace.Click += (s, e) =>
            {
                e.Handled = true;
                if (_doc == null || _placer == null) return;
                // Find the FamilySymbol Id for the first type
                var sym = new FilteredElementCollector(_doc)
                    .OfClass(typeof(FamilySymbol)).Cast<FamilySymbol>()
                    .FirstOrDefault(fs => fs.Family?.Name == capturedFam.Name);
                if (sym == null) { if (StatusLeft != null) StatusLeft.Text = "Family not found in project."; return; }
                _placer.SymbolId = sym.Id;
                _placer.OnDone   = msg => Dispatcher.Invoke(() => { if (StatusLeft != null) StatusLeft.Text = msg; });
                _placeEv.Raise();
                if (StatusLeft != null) StatusLeft.Text = $"Placement mode: {capturedFam.Name} — click in Revit to place.";
            };
            System.Windows.Controls.Grid.SetColumn(btnPlace, 2);
            grid.Children.Add(btnPlace);

            // Show Place button on hover
            row.MouseEnter += (s, e) => { row.Background = MeToolsTheme.BrActiveBg; btnPlace.Visibility = System.Windows.Visibility.Visible; };
            row.MouseLeave += (s, e) => { row.Background = MeToolsTheme.BrSurface;  btnPlace.Visibility = System.Windows.Visibility.Collapsed; };

            return row;
        }

        private static System.Windows.Media.Brush GroupColor(string group) => group switch
        {
            "Sockets"     => new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb( 52, 152, 219)),
            "Switches"    => new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb( 39, 174,  96)),
            "Lighting"    => new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(241, 196,  15)),
            "Panels"      => new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(155,  89, 182)),
            "Fire Alarm"  => new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(231,  76,  60)),
            "Data / Comms"=> new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb( 52, 152, 219)),
            "Sensors"     => new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb( 26, 188, 156)),
            "Lightning Protection" => new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(230, 126,  34)),
            "Conduit & Fittings"   => new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(127, 140, 141)),
            _             => new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(149, 165, 166)),
        };

        // Maps internal group key (English, used for matching/color) to display label.
        private static string GroupDisplay(string group) => group switch
        {
            "Sockets"      => S.Get("browser.grp.sockets"),
            "Switches"     => S.Get("browser.grp.switches"),
            "Lighting"     => S.Get("browser.grp.lighting"),
            "Panels"       => S.Get("browser.grp.panels"),
            "Fire Alarm"   => S.Get("browser.grp.fire"),
            "Data / Comms" => S.Get("browser.grp.data"),
            "Sensors"      => S.Get("browser.grp.sensors"),
            "Lightning Protection" => S.Get("browser.grp.lightning"),
            "Conduit & Fittings"   => S.Get("browser.grp.conduit"),
            "Other"        => S.Get("browser.grp.other"),
            _              => group,
        };

        private static string GroupAbbrev(string group) => group switch
        {
            "Sockets"      => "SO",
            "Switches"     => "SW",
            "Lighting"     => "LT",
            "Panels"       => "PL",
            "Fire Alarm"   => "FA",
            "Data / Comms" => "DT",
            "Sensors"      => "SN",
            "Lightning Protection" => "LP",
            "Conduit & Fittings"   => "CF",
            _              => "?",
        };

        private void LoadFromDisk()
        {
            var dlg = new OpenFileDialog
            {
                Title = "Select Revit Family to Load",
                Filter = "Revit Family (*.rfa)|*.rfa",
                Multiselect = true,
            };
            if (dlg.ShowDialog() != true) return;

            var doc = _doc;
            if (doc == null) { if (StatusLeft != null) StatusLeft.Text = "No document open."; return; }

            int loaded = 0, skipped = 0, failed = 0;
            foreach (var path in dlg.FileNames)
            {
                try
                {
                    string famName = Path.GetFileNameWithoutExtension(path);
                    // Check if already loaded
                    bool exists = new FilteredElementCollector(doc)
                        .OfClass(typeof(Family))
                        .Cast<Family>()
                        .Any(f => string.Equals(f.Name, famName, StringComparison.OrdinalIgnoreCase));

                    if (exists) { skipped++; continue; }

                    using (var tx = new Transaction(doc, $"ME-Tools: Load family {famName}"))
                    {
                        tx.Start();
                        Family fam;
                        bool ok = doc.LoadFamily(path, out fam);
                        if (ok) { tx.Commit(); loaded++; }
                        else    { tx.RollBack(); failed++; }
                    }
                }
                catch { failed++; }
            }

            if (StatusLeft != null) StatusLeft.Text = $"Loaded: {loaded}  |  Already in project: {skipped}  |  Failed: {failed}";
            if (loaded > 0) LoadFamilies();
        }
    }

    // Extension method for finding visual children
    internal static class VisualExtensions
    {
        public static IEnumerable<T> FindVisualChildren<T>(this DependencyObject parent) where T : DependencyObject
        {
            if (parent == null) yield break;
            int count = VisualTreeHelper.GetChildrenCount(parent);
            for (int i = 0; i < count; i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                if (child is T t) yield return t;
                foreach (var descendant in FindVisualChildren<T>(child))
                    yield return descendant;
            }
        }
    }

    // External event handler — runs family placement in Revit's API context.
    // When triggered, activates the family symbol and starts Revit's built-in
    // placement mode (PostCommand with PlaceComponent) so the user clicks to place.
    public class FamilyBrowserPlacer : IExternalEventHandler
    {
        public ElementId       SymbolId { get; set; }
        public Action<string>  OnDone   { get; set; }

        public void Execute(UIApplication app)
        {
            try
            {
                var uidoc = app.ActiveUIDocument;
                var doc   = uidoc?.Document;
                if (doc == null || SymbolId == null) return;

                var sym = doc.GetElement(SymbolId) as FamilySymbol;
                if (sym == null) { OnDone?.Invoke("Symbol not found."); return; }

                // Activate the symbol if not already active
                if (!sym.IsActive)
                    using (var tx = new Transaction(doc, "Activate family"))
                    { tx.Start(); sym.Activate(); doc.Regenerate(); tx.Commit(); }

                // PromptForFamilyInstancePlacement is the correct Revit API for
                // starting family placement from an add-in. Runs modally — Revit
                // stays in placement mode until the user presses ESC.
                try
                {
                    uidoc.PromptForFamilyInstancePlacement(sym);
                    OnDone?.Invoke("Placement finished.");
                }
                catch (Autodesk.Revit.Exceptions.OperationCanceledException)
                {
                    OnDone?.Invoke("Placement cancelled.");
                }
            }
            catch (Exception ex)
            {
                OnDone?.Invoke("Placement error: " + ex.Message);
            }
        }

        public string GetName() => "ME-Tools Family Browser Placer";
    }
}

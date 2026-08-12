// SettingsWindow.cs — ME-Tools Settings
// Mayer E-Concept SRL
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using Color      = System.Windows.Media.Color;
using Grid       = System.Windows.Controls.Grid;
using Ellipse    = System.Windows.Shapes.Ellipse;
using Path       = System.IO.Path;
using Visibility = System.Windows.Visibility;
// Revit types are fully qualified in OnApplyWorksets to avoid namespace conflicts

namespace METools
{
    public class SettingsWindow : MeToolsWindowBase
    {
        // ── Version ───────────────────────────────────────────────────────
        // Single source of truth: setup.iss (read via SplashGate).
        // Update #define AppVersion in setup.iss → rebuild → shown here.
        private static string AppVersion => $"v{SplashGate.GetVersion()}";

        // ── Panels ────────────────────────────────────────────────────────
        private StackPanel _panAppearance;
        private StackPanel _panLanguage;
        private StackPanel _panLicense;
        private StackPanel _panWorksets;
        private StackPanel _panHeights;
        private StackPanel _panImports;

        // ── Home-grid launcher ──────────────────────────────────────────────
        // Replaces the old horizontal tab strip (6 labels didn't fit the
        // window width without truncating -- "Imported Objects" was cut down
        // to "Imported (" in practice). This is a small grid of tiles instead,
        // one per section, with a Back button on whichever section is open.
        // -1 = home grid; 0-5 = which panel is open.
        private Grid       _homeGrid;
        private Border     _backBar;
        private TextBlock  _backBarTitle;
        private int        _activeTab = -1;

        // ── License controls ──────────────────────────────────────────────
        private TextBox   _tbKey;
        private TextBlock _lblStatus;
        private Button    _btnActivate, _btnDeactivate;

        // ── Theme controls ────────────────────────────────────────────────
        private Button _btnDark, _btnLight;

        // ── Language controls ─────────────────────────────────────────────
        private ComboBox _cbLanguage;

        // ── Worksets controls ─────────────────────────────────────────────
        private ListBox _lbWorksets;
        private ListBox _lbCurrentWorksets;
        private TextBox _tbNewWorkset;

        // ── Imported Objects controls ─────────────────────────────────────
        private StackPanel _importsList;
        private TextBlock  _importsStatus;
        private readonly List<ImportedCategoryRow> _importRows = new List<ImportedCategoryRow>();
        // Categories confirmed (by a real, verified delete attempt -- not
        // just a guess) to survive deletion, including Revit's own native
        // Purge Unused and the family-scan fix, mapped to a SPECIFIC,
        // human-readable reason where one is known (which family, nested
        // vs. not editable, etc.) rather than just a yes/no flag. Shown
        // directly on the row every time the list is scanned, not just
        // once in a message box -- a message box is easy to dismiss by
        // accident before reading it (exactly what happened here), and the
        // row itself doesn't go away.
        private readonly Dictionary<long, string> _stubbornCategoryNotes = new Dictionary<long, string>();

        // BUG FIXED HERE: this used to be a plain instance field, reset to
        // empty every time the Settings window was closed and reopened --
        // Revit creates a brand-new SettingsWindow each time the command
        // runs, so any in-memory-only "confirmed stubborn" tracking was
        // wiped the moment the dialog closed. That's exactly why the label
        // never showed up on a later visit even though a real, verified
        // delete attempt had already disproven "safe to remove" for those
        // specific categories. Persisting to a small per-project file
        // (same project-id mechanism CommentsStorage already provides,
        // reused here the same way TimeTrackerStorage/ActivityLogStorage
        // already reuse it) survives window close/reopen and Revit
        // restarts, since "does this category resist deletion" is a fact
        // about this specific project file, not about this one dialog session.
        private static string StubbornCategoriesPath(string projectId) =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "METools", "StubbornImports", $"{projectId}.json");

        private void LoadStubbornCategoryIds(Autodesk.Revit.DB.Document doc)
        {
            _stubbornCategoryNotes.Clear();
            try
            {
                var projectId = METools.Comments.CommentsStorage.GetOrCreateProjectId(doc);
                if (string.IsNullOrWhiteSpace(projectId)) return;
                var path = StubbornCategoriesPath(projectId);
                if (!File.Exists(path)) return;
                var json = File.ReadAllText(path, System.Text.Encoding.UTF8);
                // Read as Dictionary<string,string> -- JSON object keys are
                // always strings -- then convert back to long ids.
                var raw = JsonSerializer.Deserialize<Dictionary<string, string>>(json);
                if (raw != null)
                    foreach (var kv in raw)
                        if (long.TryParse(kv.Key, out var id))
                            _stubbornCategoryNotes[id] = kv.Value ?? "";
            }
            catch { }
        }

        private void SaveStubbornCategoryIds(Autodesk.Revit.DB.Document doc)
        {
            try
            {
                var projectId = METools.Comments.CommentsStorage.GetOrCreateProjectId(doc);
                if (string.IsNullOrWhiteSpace(projectId)) return;
                var path = StubbornCategoriesPath(projectId);
                var dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                var raw = _stubbornCategoryNotes.ToDictionary(kv => kv.Key.ToString(), kv => kv.Value);
                var json = JsonSerializer.Serialize(raw);
                File.WriteAllText(path, json, System.Text.Encoding.UTF8);
            }
            catch { }
        }

        // A single, minimal record of where a category name was found
        // while scanning families -- stored by NAME rather than by
        // element/Family object, since only strings/bools survive a trip
        // through JSON. The actual Family object gets re-resolved by name
        // (a single fast collector call, not another EditFamily) whenever
        // it's actually needed for a fix attempt.
        private class CachedFamilyMatch
        {
            public string Family   { get; set; } = "";
            public string Path     { get; set; } = "";
            public bool   Editable { get; set; }
        }

        private class FamilyIndexFile
        {
            public bool Complete { get; set; }
            public bool IncludesNested { get; set; } // true only if the scan that built this ALSO checked families nested inside others
            public List<long> VisitedFamilyIds { get; set; } = new List<long>();
            public Dictionary<string, List<CachedFamilyMatch>> Index { get; set; }
                = new Dictionary<string, List<CachedFamilyMatch>>(StringComparer.OrdinalIgnoreCase);
        }

        // BUG FIXED HERE (performance): "Find & Remove from Families" opens
        // EVERY loaded family via EditFamily to check its categories --
        // reported as taking 15-20 minutes for 7 selections on a real
        // project, which lines up with EditFamily/Close being genuinely
        // expensive per family, not something a smarter LINQ query can
        // avoid. What CAN be avoided is paying that cost again on every
        // repeated attempt (which is exactly what testing this feature
        // means doing over and over). A full scan now records EVERY
        // category name it sees along the way -- not just the ones
        // currently selected, since every family gets opened regardless --
        // into a small per-project cache. Once complete, that cache
        // answers "which family contains category X" instantly, without
        // opening a single family, for every future click, including ones
        // asking about categories that weren't even selected in the
        // original scan. A "Force Full Rescan" option exists for when the
        // cache might be stale (a family was added, removed, or edited
        // since it was built).
        //
        // _visitedFamilyIds is tracked separately from "complete" -- a scan
        // can stop early (see FindOwningFamilies) the moment every
        // currently-wanted category has at least one match, without
        // opening every remaining family. Every family actually opened
        // still gets permanently remembered here, so a LATER attempt for
        // different categories only opens families that are still unvisited,
        // rather than re-scanning everything from the start each time.
        private readonly Dictionary<string, List<CachedFamilyMatch>> _familyCategoryIndex
            = new Dictionary<string, List<CachedFamilyMatch>>(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<long> _visitedFamilyIds = new HashSet<long>();
        private bool _familyIndexComplete = false;
        private bool _familyIndexIncludesNested = false;

        private static string FamilyIndexPath(string projectId) =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "METools", "StubbornImports", $"{projectId}_familyindex.json");

        private void LoadFamilyIndex(Autodesk.Revit.DB.Document doc)
        {
            _familyCategoryIndex.Clear();
            _visitedFamilyIds.Clear();
            _familyIndexComplete = false;
            _familyIndexIncludesNested = false;
            try
            {
                var projectId = METools.Comments.CommentsStorage.GetOrCreateProjectId(doc);
                if (string.IsNullOrWhiteSpace(projectId)) return;
                var path = FamilyIndexPath(projectId);
                if (!File.Exists(path)) return;
                var json = File.ReadAllText(path, System.Text.Encoding.UTF8);
                var file = JsonSerializer.Deserialize<FamilyIndexFile>(json);
                if (file != null)
                {
                    _familyIndexComplete = file.Complete;
                    _familyIndexIncludesNested = file.IncludesNested;
                    if (file.VisitedFamilyIds != null)
                        foreach (var id in file.VisitedFamilyIds) _visitedFamilyIds.Add(id);
                    if (file.Index != null)
                        foreach (var kv in file.Index)
                            _familyCategoryIndex[kv.Key] = kv.Value ?? new List<CachedFamilyMatch>();
                }
            }
            catch { }
        }

        private void SaveFamilyIndex(Autodesk.Revit.DB.Document doc)
        {
            try
            {
                var projectId = METools.Comments.CommentsStorage.GetOrCreateProjectId(doc);
                if (string.IsNullOrWhiteSpace(projectId)) return;
                var path = FamilyIndexPath(projectId);
                var dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                var file = new FamilyIndexFile
                {
                    Complete         = _familyIndexComplete,
                    IncludesNested   = _familyIndexIncludesNested,
                    VisitedFamilyIds = _visitedFamilyIds.ToList(),
                    Index            = _familyCategoryIndex,
                };
                var json = JsonSerializer.Serialize(file);
                File.WriteAllText(path, json, System.Text.Encoding.UTF8);
            }
            catch { }
        }

        private static string WorksetsConfigPath =>
            Path.Combine(
                Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? "",
                "config", "standard_worksets.json");

        public SettingsWindow()
        {
            S.SetLanguage(SettingsStore.Language ?? "en");
            InitWindow(S._("settings.title"), width: 500, isDialog: false);
            BuildStatusBar(LicenseManager.StatusText, AppVersion);
            BuildContent();
        }

        // ── Build UI ──────────────────────────────────────────────────────
        private StackPanel _contentRoot;

        private void BuildContent()
        {
            _contentRoot = new StackPanel();
            RootDock.Children.Add(_contentRoot);
            PopulateContent();
        }

        // Rebuilds the whole content with the current theme (called on theme switch)
        private void PopulateContent()
        {
            _contentRoot.Children.Clear();

            _homeGrid = BuildHomeGrid();
            _contentRoot.Children.Add(_homeGrid);

            _backBar = BuildBackBar();
            _contentRoot.Children.Add(_backBar);

            var contentBorder = new Border
            {
                Padding    = new Thickness(24, 18, 24, 24),
                Background = MeToolsTheme.BrBg,
            };

            _panAppearance = BuildAppearancePanel();
            _panLanguage   = BuildLanguagePanel();
            _panLicense    = BuildLicensePanel();
            _panWorksets   = BuildWorksetsPanel();
            _panHeights    = BuildHeightsPanel();
            _panImports    = BuildImportsPanel();

            var contentStack = new StackPanel();
            contentStack.Children.Add(_panAppearance);
            contentStack.Children.Add(_panLanguage);
            contentStack.Children.Add(_panLicense);
            contentStack.Children.Add(_panWorksets);
            contentStack.Children.Add(_panHeights);
            contentStack.Children.Add(_panImports);
            contentBorder.Child = contentStack;
            _contentRoot.Children.Add(contentBorder);

            if (_activeTab < 0) ShowHome(); else ShowPanel(_activeTab);
        }

        // ── Home grid ("phone home screen" of section tiles) ────────────────
        private static readonly (string Key, string Glyph)[] _homeTiles =
        {
            ("settings.tab.appearance", "\uE790"), // Color
            ("settings.tab.language",   "\uE774"), // Globe
            ("settings.tab.license",    "\uE72E"), // Lock
            ("settings.tab.worksets",   "\uE8B7"), // Page2 (stacked pages)
            ("settings.tab.heights",    "\uE762"), // Line/ruler-ish
            ("settings.tab.imports",    "\uE8B5"), // Import
        };

        private Grid BuildHomeGrid()
        {
            var grid = new Grid { Margin = new Thickness(0, 4, 0, 8) };
            for (int c = 0; c < 3; c++)
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            for (int r = 0; r < 2; r++)
                grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            for (int i = 0; i < _homeTiles.Length; i++)
            {
                var tile = BuildHomeTile(S._(_homeTiles[i].Key), _homeTiles[i].Glyph, i);
                Grid.SetRow(tile, i / 3);
                Grid.SetColumn(tile, i % 3);
                grid.Children.Add(tile);
            }
            return grid;
        }

        private Border BuildHomeTile(string label, string glyph, int idx)
        {
            var iconTb = new TextBlock
            {
                Text = glyph, FontFamily = new FontFamily("Segoe MDL2 Assets"), FontSize = 26,
                Foreground = MeToolsTheme.BrAccent, HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 2, 0, 8),
            };
            var labelTb = new TextBlock
            {
                Text = label, FontSize = 12, FontWeight = FontWeights.SemiBold, Foreground = MeToolsTheme.BrText,
                HorizontalAlignment = HorizontalAlignment.Center, TextAlignment = TextAlignment.Center,
                TextWrapping = TextWrapping.Wrap,
            };
            var inner = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            inner.Children.Add(iconTb);
            inner.Children.Add(labelTb);

            var tile = new Border
            {
                Background      = MeToolsTheme.BrSurface,
                BorderBrush      = MeToolsTheme.BrBorder,
                BorderThickness  = new Thickness(1),
                CornerRadius     = new CornerRadius(10),
                Margin           = new Thickness(6),
                Padding          = new Thickness(8, 14, 8, 14),
                Height           = 92,
                Cursor           = Cursors.Hand,
                Child            = inner,
            };
            tile.MouseEnter += (s, e) => tile.Background = MeToolsTheme.BrActiveBg;
            tile.MouseLeave += (s, e) => tile.Background = MeToolsTheme.BrSurface;
            tile.MouseLeftButtonDown += (s, e) => ShowPanel(idx);
            return tile;
        }

        // ── Back bar (shown above whichever section is open) ────────────────
        private Border BuildBackBar()
        {
            var grid = new Grid { Margin = new Thickness(0, 0, 0, 14) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var backBtn = new Button
            {
                Content = "\u2190  " + S._("settings.back"), FontSize = 12, Height = 30,
                Padding = new Thickness(10, 0, 10, 0), Cursor = Cursors.Hand,
                Background = MeToolsTheme.BrBtnBg, Foreground = MeToolsTheme.BrText,
                BorderBrush = MeToolsTheme.BrBtnBorder, BorderThickness = new Thickness(1),
            };
            backBtn.Template = RoundedBtnTemplate();
            backBtn.Click += (s, e) => ShowHome();
            Grid.SetColumn(backBtn, 0);

            _backBarTitle = new TextBlock
            {
                FontSize = 13, FontWeight = FontWeights.SemiBold, Foreground = MeToolsTheme.BrAccent,
                VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0),
            };
            Grid.SetColumn(_backBarTitle, 1);

            grid.Children.Add(backBtn);
            grid.Children.Add(_backBarTitle);

            return new Border { Child = grid, Visibility = Visibility.Collapsed, Margin = new Thickness(0, 4, 0, 0) };
        }

        private string TabTitle(int idx) => idx >= 0 && idx < _homeTiles.Length ? S._(_homeTiles[idx].Key) : "";

        // ResizeToFitContent() measures against the CURRENT layout -- but a
        // Visibility change (or content being added to a Collapsed-until-now
        // panel) doesn't get reflected until WPF's next real layout pass,
        // not the instant the property changes. Calling ResizeToFitContent()
        // immediately after such a change measures the layout as it was
        // BEFORE the change, not after. Deferring to the next dispatcher
        // cycle measures after that real pass instead -- used everywhere a
        // resize follows a Visibility toggle or a panel repopulating itself.
        private void DeferredResize() =>
            Dispatcher.BeginInvoke(new Action(ResizeToFitContent), System.Windows.Threading.DispatcherPriority.Background);

        private void ShowHome()
        {
            _activeTab = -1;
            _homeGrid.Visibility = Visibility.Visible;
            _backBar.Visibility  = Visibility.Collapsed;
            _panAppearance.Visibility = Visibility.Collapsed;
            _panLanguage.Visibility   = Visibility.Collapsed;
            _panLicense.Visibility    = Visibility.Collapsed;
            _panWorksets.Visibility   = Visibility.Collapsed;
            _panHeights.Visibility    = Visibility.Collapsed;
            _panImports.Visibility    = Visibility.Collapsed;
            // Deferred (not called immediately) -- see the fix note on
            // ShowPanel below; the same reasoning applies here.
            DeferredResize();
        }

        private void ShowPanel(int idx)
        {
            _activeTab = idx;
            _homeGrid.Visibility = Visibility.Collapsed;
            _backBar.Visibility  = Visibility.Visible;
            _backBarTitle.Text   = TabTitle(idx);

            _panAppearance.Visibility = idx == 0 ? Visibility.Visible : Visibility.Collapsed;
            _panLanguage.Visibility   = idx == 1 ? Visibility.Visible : Visibility.Collapsed;
            _panLicense.Visibility    = idx == 2 ? Visibility.Visible : Visibility.Collapsed;
            _panWorksets.Visibility   = idx == 3 ? Visibility.Visible : Visibility.Collapsed;
            _panHeights.Visibility    = idx == 4 ? Visibility.Visible : Visibility.Collapsed;
            _panImports.Visibility    = idx == 5 ? Visibility.Visible : Visibility.Collapsed;

            if (idx == 3) { LoadWorksetsIntoList(); LoadCurrentProjectWorksets(); }
            if (idx == 4) LoadHeightsIntoList();
            if (idx == 5) LoadImportedCategories();

            // BUG FIXED HERE: calling ResizeToFitContent() immediately, in
            // the same synchronous pass that just flipped several panels'
            // Visibility, was measuring the window against a layout that
            // hadn't actually settled yet -- WPF processes a Visibility
            // change on its own next layout pass, not the instant the
            // property is set, and newly-visible content (Worksets' two
            // ListBoxes in particular, which virtualize their items) can
            // under-report their true height if measured before that real
            // pass happens. That produced exactly the reported symptom in
            // both directions: short panels (Appearance, Language) stuck
            // at whatever taller height a previous panel had left frozen,
            // and tall panels (Worksets, Imported Objects) never getting
            // measured tall enough to show everything, cutting off buttons
            // at the bottom. Deferring to the next dispatcher cycle --
            // exactly the same technique LoadHeightsIntoList already uses,
            // for the same underlying reason -- measures AFTER that real
            // layout pass instead of before it.
            DeferredResize();
        }

        // InitWindow's Loaded handler (see MeToolsWindowBase.cs) measures the
        // window once via SizeToContent, then freezes it to a fixed Height so
        // the resize grip doesn't fight WPF's auto-sizing (that fix is what
        // solved the earlier resize-glitch/snap-to-right-edge bug). The
        // tradeoff: that freeze happens while whichever view is shown FIRST is
        // visible -- so a later section with more content (License, Worksets)
        // never gets to grow the window, and looks cut off until the user
        // manually drags it bigger. This re-measures on every navigation:

        // ── TAB 0: Appearance ─────────────────────────────────────────────
        private StackPanel BuildAppearancePanel()
        {
            var p = new StackPanel();
            p.Children.Add(Sec(S._("settings.appearance.theme")));
            p.Children.Add(InfoBox(S._("settings.appearance.theme_hint")));

            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 20) };
            _btnDark  = ToggleBtn(S._("settings.appearance.dark"),  MeToolsTheme.Current == MeTheme.Dark,  () => ApplyTheme(MeTheme.Dark));
            _btnLight = ToggleBtn(S._("settings.appearance.light"), MeToolsTheme.Current == MeTheme.Light, () => ApplyTheme(MeTheme.Light));
            _btnDark.Width  = 150;
            _btnLight.Width = 150;
            _btnLight.Margin = new Thickness(10, 0, 0, 0);
            row.Children.Add(_btnDark);
            row.Children.Add(_btnLight);
            p.Children.Add(row);
            return p;
        }

        private void ApplyTheme(MeTheme theme)
        {
            if (MeToolsTheme.Current == theme) return;
            MeToolsTheme.Toggle();
            UpdateToggle(_btnDark,  MeToolsTheme.Current == MeTheme.Dark);
            UpdateToggle(_btnLight, MeToolsTheme.Current == MeTheme.Light);
        }

        // ── TAB 1: Language ───────────────────────────────────────────────
        private StackPanel BuildLanguagePanel()
        {
            var p = new StackPanel { Visibility = Visibility.Collapsed };
            p.Children.Add(Sec(S._("settings.language.title")));
            p.Children.Add(InfoBox(S._("settings.language.hint")));

            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 20), VerticalAlignment = VerticalAlignment.Center };
            row.Children.Add(new TextBlock { Text = S._("settings.language.label"), FontSize = 12, Foreground = MeToolsTheme.BrText, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0) });
            _cbLanguage = StyledCombo(30, 12); _cbLanguage.Width = 180;
            _cbLanguage.Items.Add("English"); _cbLanguage.Items.Add("Deutsch"); _cbLanguage.Items.Add("Română");
            _cbLanguage.SelectedItem = SettingsStore.Language == "de" ? "Deutsch"
                                     : SettingsStore.Language == "ro" ? "Română"
                                     : "English";
            _cbLanguage.SelectionChanged += (s, e) =>
                SettingsStore.Language = _cbLanguage.SelectedItem?.ToString() == "Deutsch" ? "de"
                                        : _cbLanguage.SelectedItem?.ToString() == "Română" ? "ro"
                                        : "en";
            row.Children.Add(_cbLanguage);
            p.Children.Add(row);
            p.Children.Add(new TextBlock { Text = S._("settings.language.restart"), FontSize = 10, Foreground = MeToolsTheme.BrMuted, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8) });
            return p;
        }

        // ── TAB 2: License ────────────────────────────────────────────────
        private StackPanel BuildLicensePanel()
        {
            var p = new StackPanel { Visibility = Visibility.Collapsed };
            p.Children.Add(Sec(S._("settings.license.title")));
            p.Children.Add(BuildStatusBadge());
            p.Children.Add(new Border { Height = 16 });
            p.Children.Add(Sec(S._("settings.license.key")));
            p.Children.Add(new TextBlock { Text = S._("settings.license.key_hint"), FontSize = 11, Foreground = MeToolsTheme.BrMuted, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8) });

            var keyRow = new Grid { Margin = new Thickness(0, 0, 0, 10) };
            keyRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            keyRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8) });
            keyRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            _tbKey = new TextBox
            {
                Text = LicenseManager.SavedKey, Height = 34, FontSize = 13,
                FontFamily = new FontFamily("Consolas"),
                Background = MeToolsTheme.BrInput, Foreground = MeToolsTheme.BrInputFg,
                BorderBrush = MeToolsTheme.BrBorder, BorderThickness = new Thickness(1),
                Padding = new Thickness(8, 0, 8, 0), CaretBrush = MeToolsTheme.BrText,
                VerticalContentAlignment = VerticalAlignment.Center, CharacterCasing = CharacterCasing.Upper,
            };
            _tbKey.TextChanged += (s, e) => UpdateActivateButton();
            _btnActivate = FooterBtn(S._("settings.license.activate"), primary: true, onClick: OnActivate);
            _btnActivate.Height = 34; _btnActivate.Padding = new Thickness(16, 0, 16, 0);
            Grid.SetColumn(_tbKey, 0); Grid.SetColumn(_btnActivate, 2);
            keyRow.Children.Add(_tbKey); keyRow.Children.Add(_btnActivate);
            p.Children.Add(keyRow);

            _btnDeactivate = FooterBtn(S._("settings.license.remove"), primary: false, onClick: OnDeactivate);
            _btnDeactivate.Margin = new Thickness(0, 0, 0, 16);
            _btnDeactivate.Visibility = LicenseManager.IsLicensed() ? Visibility.Visible : Visibility.Collapsed;
            p.Children.Add(_btnDeactivate);

            // -- Machine ID section ─────────────────────────────────────────
            p.Children.Add(new Border { Height = 1, Background = MeToolsTheme.BrBorder, Margin = new Thickness(0, 16, 0, 16) });
            p.Children.Add(new TextBlock
            {
                Text = S._("settings.license.machine_id"), FontSize = 10, FontWeight = FontWeights.SemiBold,
                Foreground = MeToolsTheme.BrMuted, Margin = new Thickness(0, 0, 0, 6),
            });
            p.Children.Add(new TextBlock
            {
                Text = S._("settings.license.machine_id_hint"),
                FontSize = 10, Foreground = MeToolsTheme.BrMuted, TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 8),
            });
            var machineIdRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
            var machineIdTb = new TextBox
            {
                Text            = LicenseManager.GetMachineId(),
                IsReadOnly      = true,
                FontFamily      = new FontFamily("Consolas"),
                FontSize        = 14,
                FontWeight      = FontWeights.Bold,
                Background      = MeToolsTheme.BrSurface,
                Foreground      = MeToolsTheme.BrAccent,
                BorderBrush     = MeToolsTheme.BrBorder,
                BorderThickness = new Thickness(1),
                Padding         = new Thickness(10, 6, 10, 6),
                MinWidth        = 180,
                VerticalContentAlignment = VerticalAlignment.Center,
            };
            var copyBtn = FooterBtn(S._("settings.license.copy"), false, () =>
            {
                try
                {
                    System.Windows.Clipboard.SetText(machineIdTb.Text);
                    MessageBox.Show(S._("settings.license.copied_msg"), S._("settings.license.copied_title"), MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch { }
            });
            copyBtn.Height = 34; copyBtn.Margin = new Thickness(8, 0, 0, 0);
            machineIdRow.Children.Add(machineIdTb);
            machineIdRow.Children.Add(copyBtn);
            p.Children.Add(machineIdRow);

            var contactRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 0) };
            contactRow.Children.Add(new TextBlock { Text = S._("settings.license.need"), FontSize = 10, Foreground = MeToolsTheme.BrMuted, VerticalAlignment = VerticalAlignment.Center });
            var mailLink = new TextBlock { Text = "office@mayer-econcept.ro", FontSize = 10, Foreground = MeToolsTheme.BrAccent, Cursor = Cursors.Hand, TextDecorations = TextDecorations.Underline, VerticalAlignment = VerticalAlignment.Center };
            mailLink.MouseLeftButtonDown += (s, e) => { try { System.Diagnostics.Process.Start("mailto:office@mayer-econcept.ro"); } catch { } };
            contactRow.Children.Add(mailLink);
            p.Children.Add(contactRow);
            UpdateActivateButton();
            return p;
        }

        private UIElement BuildStatusBadge()
        {
            _lblStatus = new TextBlock { FontSize = 13, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center };
            RefreshStatusLabel();
            var badge = new Border { CornerRadius = new CornerRadius(6), Padding = new Thickness(16, 10, 16, 10), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 4, 0, 0) };
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            var dot = new Ellipse { Width = 10, Height = 10, Margin = new Thickness(0, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center };
            bool licensed = LicenseManager.IsLicensed(), expired = LicenseManager.IsTrialExpired;
            if (licensed)      { badge.Background = new SolidColorBrush(Color.FromRgb(0x1D, 0x6A, 0x40)); dot.Fill = new SolidColorBrush(Color.FromRgb(0x5D, 0xCA, 0xA5)); }
            else if (expired)  { badge.Background = new SolidColorBrush(Color.FromRgb(0x80, 0x20, 0x20)); dot.Fill = new SolidColorBrush(Color.FromRgb(0xFF, 0x70, 0x70)); }
            else               { badge.Background = new SolidColorBrush(Color.FromRgb(0x7A, 0x50, 0x10)); dot.Fill = new SolidColorBrush(Color.FromRgb(0xFF, 0xC0, 0x50)); }
            _lblStatus.Foreground = Brushes.White;
            row.Children.Add(dot); row.Children.Add(_lblStatus);
            badge.Child = row;
            return badge;
        }

        private void RefreshStatusLabel()
        {
            if (_lblStatus != null) _lblStatus.Text = LicenseManager.StatusText;
            if (StatusLeft  != null) StatusLeft.Text  = LicenseManager.StatusText;
        }

        private void UpdateActivateButton()
        {
            if (_btnActivate == null) return;
            bool hasText = !string.IsNullOrWhiteSpace(_tbKey?.Text);
            _btnActivate.IsEnabled = hasText && !LicenseManager.IsLicensed();
            _btnActivate.Opacity   = _btnActivate.IsEnabled ? 1.0 : 0.5;
        }

        private void OnActivate()
        {
            string key = _tbKey?.Text?.Trim().ToUpperInvariant() ?? "";
            if (string.IsNullOrEmpty(key)) return;
            bool ok = LicenseManager.TryActivate(key);
            if (ok) { MessageBox.Show(S._("settings.license.act_ok_msg"), S._("settings.license.act_ok_title"), MessageBoxButton.OK, MessageBoxImage.None); RefreshStatusLabel(); if (_btnDeactivate != null) _btnDeactivate.Visibility = Visibility.Visible; UpdateActivateButton(); }
            else    { MessageBox.Show(S._("settings.license.act_fail_msg"), S._("settings.license.act_fail_title"), MessageBoxButton.OK, MessageBoxImage.Warning); }
        }

        private void OnDeactivate()
        {
            if (MessageBox.Show(S._("settings.license.remove_confirm"), S._("settings.license.remove_title"), MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
            { LicenseManager.Deactivate(); if (_tbKey != null) _tbKey.Text = ""; if (_btnDeactivate != null) _btnDeactivate.Visibility = Visibility.Collapsed; RefreshStatusLabel(); UpdateActivateButton(); }
        }

        // ── TAB 3: Worksets ───────────────────────────────────────────────
        private StackPanel BuildWorksetsPanel()
        {
            var p = new StackPanel { Visibility = Visibility.Collapsed };

            // Everything below goes inside a bounded, internally-scrolling
            // area instead of directly in p -- four sections stacked
            // together (standard worksets editor, apply-to-project,
            // current-project readonly list, share config) add up to
            // noticeably more height than fits on a normal screen. This
            // way the window itself stays a reasonable, fixed size and the
            // panel scrolls internally for whatever doesn't fit, rather
            // than the window trying (and failing) to grow tall enough for
            // all four sections at once.
            var inner = new StackPanel();

            inner.Children.Add(Sec(S._("settings.worksets.title")));
            inner.Children.Add(InfoBox(S._("settings.worksets.hint")));

            // List
            _lbWorksets = new ListBox
            {
                Height = 180, Margin = new Thickness(0, 8, 0, 8),
                Background = MeToolsTheme.BrInput, Foreground = MeToolsTheme.BrText,
                BorderBrush = MeToolsTheme.BrBorder, BorderThickness = new Thickness(1),
                FontSize = 12, Padding = new Thickness(2),
            };
            inner.Children.Add(_lbWorksets);

            // Add row
            var addGrid = new Grid { Margin = new Thickness(0, 0, 0, 6) };
            addGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            addGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8) });
            addGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            _tbNewWorkset = new TextBox
            {
                Height = 32, FontSize = 12,
                Background = MeToolsTheme.BrInput, Foreground = MeToolsTheme.BrInputFg,
                BorderBrush = MeToolsTheme.BrBorder, BorderThickness = new Thickness(1),
                Padding = new Thickness(8, 0, 8, 0), CaretBrush = MeToolsTheme.BrText,
                VerticalContentAlignment = VerticalAlignment.Center,
            };
            _tbNewWorkset.KeyDown += (s, e) => { if (e.Key == Key.Enter) OnAddWorkset(); };
            var btnAdd = FooterBtn(S._("settings.worksets.add"), primary: true, onClick: OnAddWorkset);
            btnAdd.Height = 32; btnAdd.Padding = new Thickness(16, 0, 16, 0);
            Grid.SetColumn(_tbNewWorkset, 0); Grid.SetColumn(btnAdd, 2);
            addGrid.Children.Add(_tbNewWorkset); addGrid.Children.Add(btnAdd);
            inner.Children.Add(addGrid);

            // Edit buttons
            var editRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 16) };
            var btnRemove = FooterBtn(S._("settings.worksets.remove"), primary: false, onClick: OnRemoveWorkset);
            var btnSave   = FooterBtn(S._("settings.worksets.save"),   primary: true,  onClick: OnSaveWorksets);
            btnRemove.Margin = new Thickness(0, 0, 8, 0);
            editRow.Children.Add(btnRemove); editRow.Children.Add(btnSave);
            inner.Children.Add(editRow);

            // Apply to project button
            inner.Children.Add(new Separator { Margin = new Thickness(0, 0, 0, 16), Background = MeToolsTheme.BrBorder });
            inner.Children.Add(Sec(S._("settings.worksets.apply_title")));
            inner.Children.Add(InfoBox(S._("settings.worksets.apply_hint")));
            var btnApply = ActionBtn(S._("settings.worksets.create_btn"), true, OnApplyWorksets);
            btnApply.Margin = new Thickness(0, 8, 0, 0);
            inner.Children.Add(btnApply);

            // -- Current project's actual worksets (read-only, live from the open document) --
            inner.Children.Add(new Separator { Margin = new Thickness(0, 20, 0, 16), Background = MeToolsTheme.BrBorder });
            var curHdrRow = new Grid { Margin = new Thickness(0, 0, 0, 6) };
            curHdrRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            curHdrRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var curHdrLbl = Sec(S._("settings.worksets.project"));
            Grid.SetColumn(curHdrLbl, 0); curHdrRow.Children.Add(curHdrLbl);
            var btnRefreshCur = FooterBtn(S._("settings.worksets.refresh"), false, LoadCurrentProjectWorksets);
            btnRefreshCur.Height = 26; btnRefreshCur.Padding = new Thickness(10, 0, 10, 0); btnRefreshCur.FontSize = 11;
            Grid.SetColumn(btnRefreshCur, 1); curHdrRow.Children.Add(btnRefreshCur);
            inner.Children.Add(curHdrRow);

            inner.Children.Add(InfoBox(S._("settings.worksets.current_hint")));

            _lbCurrentWorksets = new ListBox
            {
                Height = 140, Margin = new Thickness(0, 8, 0, 0),
                Background = MeToolsTheme.BrInput, Foreground = MeToolsTheme.BrText,
                BorderBrush = MeToolsTheme.BrBorder, BorderThickness = new Thickness(1),
                FontSize = 12, Padding = new Thickness(2),
                IsHitTestVisible = true, // allow scrolling; selection has no effect (read-only)
            };
            inner.Children.Add(_lbCurrentWorksets);

            // -- Share your whole ME-Tools configuration with a colleague --
            inner.Children.Add(new Separator { Margin = new Thickness(0, 20, 0, 16), Background = MeToolsTheme.BrBorder });
            inner.Children.Add(Sec(S._("settings.config.title")));
            inner.Children.Add(InfoBox(S._("settings.config.hint")));
            var configBtnRow = new StackPanel { Orientation = Orientation.Horizontal };
            var exportBtn = FooterBtn(S._("settings.config.export"), true, OnExportConfig);
            exportBtn.Margin = new Thickness(0, 0, 8, 0);
            var importBtn = FooterBtn(S._("settings.config.import"), false, OnImportConfig);
            configBtnRow.Children.Add(exportBtn);
            configBtnRow.Children.Add(importBtn);
            inner.Children.Add(configBtnRow);

            var scroll = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                MaxHeight = 480,
                Content   = inner,
            };
            p.Children.Add(scroll);

            return p;
        }

        // -- Combined configuration export/import: worksets + default heights
        // + Circuit Tagger's tag style, as one shareable file. Each piece is
        // read/written through its own store's existing Load/Save methods
        // (CircuitTaggerSettings, FamilyHeightStore) rather than duplicating
        // their file formats here -- only the worksets list has no dedicated
        // store class, so that one reuses the exact same JSON shape
        // LoadWorksetsIntoList()/OnSaveWorksets() already read and write.
        private class MeToolsConfigExport
        {
            public string ExportedAtUtc { get; set; } = DateTime.UtcNow.ToString("o");
            public string ExportedFrom  { get; set; } = Environment.MachineName;
            public List<string> Worksets { get; set; } = new List<string>();
            public Dictionary<string, double> FamilyHeights { get; set; } = new Dictionary<string, double>();
            public METools.FamilyPlacer.CircuitTaggerSettingsData CircuitTagger { get; set; } = new METools.FamilyPlacer.CircuitTaggerSettingsData();
        }

        private List<string> ReadWorksetsList()
        {
            var result = new List<string>();
            try
            {
                var path = WorksetsConfigPath;
                if (!File.Exists(path)) return result;
                var json = File.ReadAllText(path, System.Text.Encoding.UTF8);
                using var doc = JsonDocument.Parse(json);
                if (!doc.RootElement.TryGetProperty("worksets", out var arr)) return result;
                foreach (var el in arr.EnumerateArray())
                {
                    var name = el.GetString()?.Trim();
                    if (!string.IsNullOrEmpty(name)) result.Add(name);
                }
            }
            catch { }
            return result;
        }

        private void WriteWorksetsList(List<string> worksets)
        {
            var path = WorksetsConfigPath;
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            using var stream = File.Open(path, FileMode.Create, FileAccess.Write);
            using var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true });
            writer.WriteStartObject(); writer.WriteStartArray("worksets");
            foreach (var ws in worksets ?? new List<string>()) writer.WriteStringValue(ws);
            writer.WriteEndArray(); writer.WriteEndObject();
        }

        private void OnExportConfig()
        {
            try
            {
                var export = new MeToolsConfigExport
                {
                    Worksets      = ReadWorksetsList(),
                    FamilyHeights = new Dictionary<string, double>(FamilyHeightStore.All()),
                    CircuitTagger = METools.FamilyPlacer.CircuitTaggerSettings.Load(),
                };

                var dlg = new Microsoft.Win32.SaveFileDialog
                {
                    Title = S._("settings.config.export_dialog_title"),
                    Filter = "ME-Tools config (*.json)|*.json",
                    FileName = "ME-Tools-config-" + DateTime.Now.ToString("yyyyMMdd") + ".json",
                };
                if (dlg.ShowDialog() != true) return;

                var json = JsonSerializer.Serialize(export, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(dlg.FileName, json, System.Text.Encoding.UTF8);
                MessageBox.Show(string.Format(S._("settings.config.exported"), Path.GetFileName(dlg.FileName)),
                    S._("settings.config.title"), MessageBoxButton.OK, MessageBoxImage.None);
            }
            catch (Exception ex)
            {
                MessageBox.Show(string.Format(S._("settings.save_failed"), ex.Message), S._("settings.save_failed_title"), MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void OnImportConfig()
        {
            try
            {
                var dlg = new Microsoft.Win32.OpenFileDialog
                {
                    Title = S._("settings.config.import_dialog_title"),
                    Filter = "ME-Tools config (*.json)|*.json",
                    CheckFileExists = true,
                };
                if (dlg.ShowDialog() != true) return;

                var json = File.ReadAllText(dlg.FileName, System.Text.Encoding.UTF8);
                var import = JsonSerializer.Deserialize<MeToolsConfigExport>(json);
                if (import == null)
                { MessageBox.Show(S._("settings.config.import_failed_bad_file"), S._("settings.config.title"), MessageBoxButton.OK, MessageBoxImage.Warning); return; }

                var result = MessageBox.Show(
                    string.Format(S._("settings.config.import_confirm"), import.ExportedFrom ?? "?", import.ExportedAtUtc ?? "?"),
                    S._("settings.config.title"), MessageBoxButton.YesNo, MessageBoxImage.Question);
                if (result != MessageBoxResult.Yes) return;

                WriteWorksetsList(import.Worksets ?? new List<string>());
                FamilyHeightStore.SaveAll(import.FamilyHeights ?? new Dictionary<string, double>());
                METools.FamilyPlacer.CircuitTaggerSettings.Save(import.CircuitTagger ?? new METools.FamilyPlacer.CircuitTaggerSettingsData());

                // Reflect the freshly-imported worksets list immediately if
                // this tab is what's currently on screen.
                LoadWorksetsIntoList();

                MessageBox.Show(
                    string.Format(S._("settings.config.imported"), (import.Worksets?.Count ?? 0), (import.FamilyHeights?.Count ?? 0)),
                    S._("settings.config.title"), MessageBoxButton.OK, MessageBoxImage.None);
            }
            catch (Exception ex)
            {
                MessageBox.Show(string.Format(S._("settings.save_failed"), ex.Message), S._("settings.save_failed_title"), MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        // Reads the ACTUAL worksets that exist in the currently open Revit document
        // (not the saved standard-list template above). Read-only -- no transaction
        // needed since this is a pure read of document metadata.
        private void LoadCurrentProjectWorksets()
        {
            if (_lbCurrentWorksets == null) return;
            _lbCurrentWorksets.Items.Clear();

            var doc = SettingsCommand.CurrentDocument;
            if (doc == null)
            {
                _lbCurrentWorksets.Items.Add(S._("settings.worksets.no_doc"));
                return;
            }
            if (!doc.IsWorkshared)
            {
                _lbCurrentWorksets.Items.Add(S._("settings.worksets.no_sharing"));
                return;
            }

            try
            {
                var worksets = new Autodesk.Revit.DB.FilteredWorksetCollector(doc)
                    .OfKind(Autodesk.Revit.DB.WorksetKind.UserWorkset)
                    .ToWorksets()
                    .OrderBy(w => w.Name, StringComparer.OrdinalIgnoreCase)
                    .ToList();

                if (worksets.Count == 0)
                    _lbCurrentWorksets.Items.Add(S._("settings.worksets.none_found"));
                else
                    foreach (var w in worksets)
                        _lbCurrentWorksets.Items.Add(w.Name);
            }
            catch (Exception ex)
            {
                _lbCurrentWorksets.Items.Add(string.Format(S._("settings.worksets.error_reading"), ex.Message));
            }
        }

        private void LoadWorksetsIntoList()
        {
            if (_lbWorksets == null) return;
            _lbWorksets.Items.Clear();
            try
            {
                var path = WorksetsConfigPath;
                if (!File.Exists(path)) return;
                var json = File.ReadAllText(path, System.Text.Encoding.UTF8);
                using var doc = JsonDocument.Parse(json);
                if (!doc.RootElement.TryGetProperty("worksets", out var arr)) return;
                foreach (var el in arr.EnumerateArray())
                {
                    var name = el.GetString()?.Trim();
                    if (!string.IsNullOrEmpty(name)) _lbWorksets.Items.Add(name);
                }
            }
            catch { }
        }

        private void OnAddWorkset()
        {
            var name = _tbNewWorkset?.Text?.Trim() ?? "";
            if (string.IsNullOrEmpty(name)) return;
            foreach (var item in _lbWorksets.Items)
                if (string.Equals(item?.ToString(), name, StringComparison.OrdinalIgnoreCase))
                { _tbNewWorkset.Clear(); return; }
            _lbWorksets.Items.Add(name);
            _tbNewWorkset.Clear();
            _lbWorksets.ScrollIntoView(_lbWorksets.Items[_lbWorksets.Items.Count - 1]);
        }

        private void OnRemoveWorkset()
        {
            if (_lbWorksets.SelectedItem != null) _lbWorksets.Items.Remove(_lbWorksets.SelectedItem);
        }

        private void OnSaveWorksets()
        {
            try
            {
                var worksets = _lbWorksets.Items.Cast<object>().Select(o => o?.ToString() ?? "").Where(s => !string.IsNullOrEmpty(s)).ToList();
                var path = WorksetsConfigPath;
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                using var stream = File.Open(path, FileMode.Create, FileAccess.Write);
                using var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true });
                writer.WriteStartObject(); writer.WriteStartArray("worksets");
                foreach (var ws in worksets) writer.WriteStringValue(ws);
                writer.WriteEndArray(); writer.WriteEndObject();
                MessageBox.Show(string.Format(S._("settings.worksets.saved_msg"), worksets.Count), S._("settings.worksets.saved"), MessageBoxButton.OK, MessageBoxImage.None);
            }
            catch (Exception ex) { MessageBox.Show(string.Format(S._("settings.save_failed"), ex.Message), S._("settings.save_failed_title"), MessageBoxButton.OK, MessageBoxImage.Warning); }
        }

        private void OnApplyWorksets()
        {
            var doc = SettingsCommand.CurrentDocument;
            if (doc == null)
            { MessageBox.Show(S._("settings.worksets.no_project"), S._("settings.worksets.title"), MessageBoxButton.OK, MessageBoxImage.Warning); return; }

            if (!doc.IsWorkshared)
            { MessageBox.Show(S._("settings.worksets.enable_hint"), S._("settings.worksets.title"), MessageBoxButton.OK, MessageBoxImage.Warning); return; }

            var worksets = _lbWorksets.Items.Cast<object>()
                .Select(o => o?.ToString() ?? "").Where(s => !string.IsNullOrEmpty(s)).ToList();
            if (worksets.Count == 0)
            { MessageBox.Show(S._("settings.worksets.empty"), S._("settings.worksets.title"), MessageBoxButton.OK, MessageBoxImage.Information); return; }

            var existingNames = new Autodesk.Revit.DB.FilteredWorksetCollector(doc)
                .OfKind(Autodesk.Revit.DB.WorksetKind.UserWorkset)
                .ToWorksets()
                .Select(w => w.Name)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            var toCreate = worksets.Where(n => !existingNames.Contains(n)).ToList();
            int skipped  = worksets.Count - toCreate.Count;
            int created  = 0;
            var failed   = new List<string>();

            if (toCreate.Count > 0)
            {
                using var tx = new Autodesk.Revit.DB.Transaction(doc, "Standard Worksets");
                tx.Start();
                foreach (var name in toCreate)
                    try { Autodesk.Revit.DB.Workset.Create(doc, name); created++; }
                    catch { failed.Add(name); }
                tx.Commit();
            }

            var sb = new System.Text.StringBuilder();
            sb.AppendLine(string.Format(S._("settings.worksets.created_line"), created));
            sb.AppendLine(string.Format(S._("settings.worksets.skipped_line"), skipped));
            if (failed.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine(string.Format(S._("settings.worksets.failed_line"), failed.Count));
                foreach (var f in failed) sb.AppendLine($"   • {f}");
            }
            MessageBox.Show(sb.ToString(), S._("settings.worksets.done_title"), MessageBoxButton.OK, MessageBoxImage.None);
        }

        // ── Theme change ──────────────────────────────────────────────────
        // -- TAB 4: Default Heights ----------------------------------------
        private StackPanel _heightsHost;
        private readonly List<KeyValuePair<string, TextBox>> _heightRows = new List<KeyValuePair<string, TextBox>>();
        private bool _heightsLoaded;

        private StackPanel BuildHeightsPanel()
        {
            _heightsLoaded = false;   // fresh panel (e.g. after theme switch) -> repopulate on next show
            _heightRows.Clear();

            var p = new StackPanel { Visibility = Visibility.Collapsed };
            p.Children.Add(Sec(S._("settings.heights.title")));
            p.Children.Add(InfoBox(S._("settings.heights.hint")));

            _heightsHost = new StackPanel { Margin = new Thickness(0, 8, 0, 8) };
            var scroll = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                MaxHeight = 320,
                Content   = _heightsHost,
            };
            p.Children.Add(scroll);

            var btnRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0) };
            var btnRescan = FooterBtn(S._("settings.heights.rescan"), primary: false, onClick: () => { _heightsLoaded = false; LoadHeightsIntoList(); });
            var btnSave   = FooterBtn(S._("settings.heights.save"),   primary: true, onClick: OnSaveHeights);
            btnRescan.Margin = new Thickness(0, 0, 8, 0);
            btnRow.Children.Add(btnRescan);
            btnRow.Children.Add(btnSave);
            p.Children.Add(btnRow);

            return p;
        }

        private void LoadHeightsIntoList()
        {
            if (_heightsHost == null || _heightsLoaded) return;
            _heightsHost.Children.Clear();
            _heightRows.Clear();

            var doc = SettingsCommand.CurrentDocument;
            if (doc == null)
            {
                _heightsHost.Children.Add(InfoBox(S._("settings.heights.no_document")));
                DeferredResize();
                return;
            }

            _heightsHost.Children.Add(new TextBlock
            {
                Text       = S._("settings.heights.scanning"),
                Foreground = MeToolsTheme.BrMuted,
                FontSize   = 12,
                Margin     = new Thickness(2, 6, 0, 6),
            });

            // Let the message render before the (blocking) scan runs.
            Dispatcher.BeginInvoke(new Action(() =>
            {
                List<METools.FamilyPlacer.FamilyHeightEntry> entries;
                try { entries = METools.FamilyPlacer.FamilyHeightScanner.Scan(doc); }
                catch { entries = new List<METools.FamilyPlacer.FamilyHeightEntry>(); }

                var overrides = FamilyHeightStore.All();
                _heightsHost.Children.Clear();

                if (entries.Count == 0)
                {
                    _heightsHost.Children.Add(InfoBox(S._("settings.heights.none_found")));
                    _heightsLoaded = true;
                    ResizeToFitContent();
                    return;
                }

                string lastGroup = null;
                foreach (var en in entries)
                {
                    if (en.Group != lastGroup)
                    {
                        _heightsHost.Children.Add(new TextBlock
                        {
                            Text       = en.Group,
                            FontSize   = 11,
                            FontWeight = FontWeights.Bold,
                            Foreground = MeToolsTheme.BrAccent,
                            Margin     = new Thickness(2, 8, 0, 2),
                        });
                        lastGroup = en.Group;
                    }
                    _heightsHost.Children.Add(BuildHeightRow(en, overrides));
                }
                _heightsLoaded = true;
                ResizeToFitContent();
            }), System.Windows.Threading.DispatcherPriority.Background);
        }

        // -- TAB 5: Imported Objects ----------------------------------------
        //
        // Revit's own Object Styles dialog lets you delete a top-level
        // imported category's SUBcategories, but not the top-level category
        // itself -- confirmed as a real, long-standing Revit limitation
        // (not specific to this app) against multiple independent Revit API
        // forum threads reporting the exact same symptom. The reliable fix,
        // per those same threads: a category still actively referenced by a
        // live ImportInstance can't be deleted directly, but deleting that
        // ImportInstance FIRST makes the category itself genuinely
        // deletable afterward. A category with no live instance at all
        // (the file was removed from the model at some point, but Revit
        // left the category behind -- the common "orphaned .dwg category"
        // case) should already be directly deletable.
        //
        // Deleting a category doesn't always throw when Revit refuses --
        // it can silently do nothing -- so this re-scans afterward and
        // reports exactly what actually got removed, rather than assuming
        // success from the absence of an exception.
        private class ImportedCategoryRow
        {
            public Autodesk.Revit.DB.ElementId CategoryId;
            public string   Name;
            public int      SubCategoryCount;
            public int      LiveInstanceCount;
            public CheckBox Checkbox;
        }

        private StackPanel BuildImportsPanel()
        {
            var p = new StackPanel { Visibility = Visibility.Collapsed };
            p.Children.Add(Sec(S._("settings.imports.title")));
            p.Children.Add(InfoBox(S._("settings.imports.hint")));

            var btnRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 8) };
            var btnAll  = FooterBtn(S._("settings.imports.select_all"),  primary: false, onClick: OnImportsSelectAll);
            var btnNone = FooterBtn(S._("settings.imports.select_none"), primary: false, onClick: OnImportsSelectNone);
            var btnRescan = FooterBtn(S._("settings.imports.rescan"), primary: false, onClick: () =>
            {
                // BUG FIXED HERE: Rescan gave literally no visible feedback --
                // the list silently cleared and repopulated, which (especially
                // when the result looks identical to before, e.g. nothing was
                // actually removable) reads as "did that even do anything?"
                // Every other ME-Tools window shows this kind of feedback in
                // its own bottom-left status bar; this one just never did.
                // Deferred so "Rescanning..." gets a real chance to render
                // before the (potentially slow, per Stefan's own report)
                // scan work runs, rather than both messages landing in the
                // same paint and only the second one ever being visible.
                StatusLeft.Text = S._("settings.imports.rescanning");
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    LoadImportedCategories();
                    StatusLeft.Text = string.Format(S._("settings.imports.rescan_done"), _importRows.Count);
                }), System.Windows.Threading.DispatcherPriority.Background);
            });
            btnAll.Margin = new Thickness(0, 0, 8, 0);
            btnNone.Margin = new Thickness(0, 0, 8, 0);
            btnRow.Children.Add(btnAll);
            btnRow.Children.Add(btnNone);
            btnRow.Children.Add(btnRescan);
            p.Children.Add(btnRow);

            _importsList = new StackPanel { Margin = new Thickness(0, 0, 0, 8) };
            var scroll = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                MaxHeight = 320,
                Content   = _importsList,
            };
            p.Children.Add(scroll);

            _importsStatus = new TextBlock
            {
                FontSize = 11, Foreground = MeToolsTheme.BrMuted, Margin = new Thickness(2, 0, 0, 8),
            };
            p.Children.Add(_importsStatus);

            var btnDelete = new Button
            {
                Content = S._("settings.imports.delete_selected"), Height = 36, FontSize = 13, FontWeight = FontWeights.SemiBold,
                Padding = new Thickness(16, 0, 16, 0),
                Background = new SolidColorBrush(MeToolsTheme.CRed), BorderBrush = new SolidColorBrush(MeToolsTheme.CRed),
                BorderThickness = new Thickness(1.5), Foreground = Brushes.White, Cursor = Cursors.Hand,
            };
            btnDelete.Template = RoundedBtnTemplate();
            btnDelete.Click += (s, e) => OnDeleteImportsClicked();
            p.Children.Add(btnDelete);

            // Everything that survives Delete Selected AND Revit's own
            // native Purge Unused is almost certainly an "Import in
            // Families" case -- CAD content embedded inside a loaded
            // family rather than the project itself, which neither
            // mechanism can touch because the real owner isn't the
            // project at all. This is a genuinely more invasive operation
            // than the button above: it opens matching families, edits
            // them, and reloads them into the project -- affecting every
            // place that family is used, not just the one entry in this
            // list. Kept as a separate, deliberate action rather than
            // folded into Delete Selected, since it can be slow (every
            // editable family in the project has to be opened to check)
            // and carries real project-wide side effects a simple delete
            // attempt doesn't.
            var btnFixFamilies = ActionBtn(S._("settings.imports.fix_in_families"), true, () => OnFindInFamiliesClicked());
            btnFixFamilies.Margin = new Thickness(0, 8, 0, 0);
            p.Children.Add(btnFixFamilies);

            var btnForceRescan = FooterBtn(S._("settings.imports.force_rescan_families"), false, () => OnFindInFamiliesClicked(forceFullRescan: true));
            btnForceRescan.Margin = new Thickness(0, 6, 0, 0);
            p.Children.Add(btnForceRescan);

            return p;
        }

        private void LoadImportedCategories()
        {
            if (_importsList == null) return;
            _importsList.Children.Clear();
            _importRows.Clear();

            var doc = SettingsCommand.CurrentDocument;
            if (doc == null)
            {
                _importsList.Children.Add(InfoBox(S._("settings.imports.no_document")));
                UpdateImportsStatus();
                DeferredResize();
                return;
            }
            LoadStubbornCategoryIds(doc);

            List<Autodesk.Revit.DB.Category> topLevel;
            var liveInstanceCounts = new Dictionary<long, int>();
            try
            {
                topLevel = doc.Settings.Categories.Cast<Autodesk.Revit.DB.Category>()
                    .Where(c => c != null && c.Parent == null && c.Id != null && c.Id.IntegerValue > 0)
                    .OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase)
                    .ToList();

                foreach (var ii in new Autodesk.Revit.DB.FilteredElementCollector(doc)
                    .OfClass(typeof(Autodesk.Revit.DB.ImportInstance))
                    .Cast<Autodesk.Revit.DB.ImportInstance>())
                {
                    var cid = ii.Category?.Id;
                    if (cid == null) continue;
                    liveInstanceCounts[cid.IntegerValue] = liveInstanceCounts.TryGetValue(cid.IntegerValue, out var n) ? n + 1 : 1;
                }
            }
            catch (Exception ex)
            {
                _importsList.Children.Add(InfoBox(string.Format(S._("settings.imports.scan_failed"), ex.Message)));
                UpdateImportsStatus();
                DeferredResize();
                return;
            }

            if (topLevel.Count == 0)
            {
                _importsList.Children.Add(new TextBlock
                {
                    Text = S._("settings.imports.none_found"), FontSize = 12, Foreground = MeToolsTheme.BrMuted,
                    Margin = new Thickness(2, 8, 0, 8),
                });
                UpdateImportsStatus();
                DeferredResize();
                return;
            }

            foreach (var cat in topLevel)
            {
                int subCount = 0;
                try { subCount = cat.SubCategories?.Size ?? 0; } catch { }
                liveInstanceCounts.TryGetValue(cat.Id.IntegerValue, out var liveCount);

                var row = new ImportedCategoryRow
                {
                    CategoryId       = cat.Id,
                    Name             = cat.Name,
                    SubCategoryCount = subCount,
                    LiveInstanceCount = liveCount,
                };
                _importRows.Add(row);
                _importsList.Children.Add(BuildImportRow(row));
            }
            UpdateImportsStatus();
            // Deferred for the same reason as ShowHome/ShowPanel -- this
            // runs synchronously right after ShowPanel just changed several
            // panels' Visibility, before WPF's next real layout pass has
            // happened. Also reached directly from the Rescan button, where
            // the same "measure before layout settles" risk applies if the
            // list's length changes significantly between scans.
            DeferredResize();
        }

        private UIElement BuildImportRow(ImportedCategoryRow row)
        {
            var grid = new Grid { Margin = new Thickness(2, 4, 2, 4) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var cb = new CheckBox { VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 3, 8, 0) };
            cb.Checked   += (s, e) => UpdateImportsStatus();
            cb.Unchecked += (s, e) => UpdateImportsStatus();
            row.Checkbox = cb;
            Grid.SetColumn(cb, 0);

            var textStack = new StackPanel();
            textStack.Children.Add(new TextBlock
            {
                Text = row.Name, FontSize = 12, FontWeight = FontWeights.SemiBold, Foreground = MeToolsTheme.BrText,
            });

            bool hasNote = _stubbornCategoryNotes.TryGetValue(row.CategoryId.IntegerValue, out var stubbornNote)
                           && !string.IsNullOrWhiteSpace(stubbornNote);
            string statusText = row.LiveInstanceCount > 0
                ? string.Format(S._("settings.imports.row_in_use"), row.SubCategoryCount, row.LiveInstanceCount)
                : hasNote
                    ? string.Format(S._("settings.imports.row_stubborn_note"), row.SubCategoryCount, stubbornNote)
                    : _stubbornCategoryNotes.ContainsKey(row.CategoryId.IntegerValue)
                        ? string.Format(S._("settings.imports.row_stubborn"), row.SubCategoryCount)
                        : string.Format(S._("settings.imports.row_orphaned"), row.SubCategoryCount);
            bool isStubborn = _stubbornCategoryNotes.ContainsKey(row.CategoryId.IntegerValue);
            textStack.Children.Add(new TextBlock
            {
                Text = statusText, FontSize = 10.5, TextWrapping = TextWrapping.Wrap,
                Foreground = row.LiveInstanceCount > 0 ? MeToolsTheme.BrOrange
                           : isStubborn                ? MeToolsTheme.Br(MeToolsTheme.CRed)
                                                         : MeToolsTheme.BrMuted,
            });
            Grid.SetColumn(textStack, 1);

            grid.Children.Add(cb);
            grid.Children.Add(textStack);

            return new Border
            {
                BorderBrush = MeToolsTheme.BrBorder, BorderThickness = new Thickness(0, 0, 0, 1),
                Padding = new Thickness(0, 4, 0, 4), Child = grid,
            };
        }

        private void UpdateImportsStatus()
        {
            if (_importsStatus == null) return;
            int total = _importRows.Count;
            int selected = _importRows.Count(r => r.Checkbox?.IsChecked == true);
            // Includes the total count on purpose -- Rescan used to give no
            // visible feedback at all beyond the list silently repopulating,
            // which looked like it might not have done anything. Seeing the
            // total change (or confirm it's the same) is direct evidence a
            // rescan actually ran.
            _importsStatus.Text = selected > 0
                ? string.Format(S._("settings.imports.n_selected"), total, selected)
                : string.Format(S._("settings.imports.none_selected"), total);
        }

        private void OnImportsSelectAll()
        {
            foreach (var r in _importRows) if (r.Checkbox != null) r.Checkbox.IsChecked = true;
            UpdateImportsStatus();
        }

        private void OnImportsSelectNone()
        {
            foreach (var r in _importRows) if (r.Checkbox != null) r.Checkbox.IsChecked = false;
            UpdateImportsStatus();
        }

        private void OnDeleteImportsClicked()
        {
            var selected = _importRows.Where(r => r.Checkbox?.IsChecked == true).ToList();
            if (selected.Count == 0)
            {
                MessageBox.Show(S._("settings.imports.select_first"), S._("settings.imports.title"),
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var doc = SettingsCommand.CurrentDocument;
            if (doc == null)
            {
                MessageBox.Show(S._("settings.imports.no_document"), S._("settings.imports.title"),
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            int inUseCount = selected.Count(r => r.LiveInstanceCount > 0);
            string confirmMsg = string.Format(S._("settings.imports.confirm_delete"), selected.Count);
            if (inUseCount > 0)
                confirmMsg += "\n\n" + string.Format(S._("settings.imports.confirm_delete_inuse_warning"), inUseCount);

            var result = MessageBox.Show(confirmMsg, S._("settings.imports.confirm_title"),
                MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (result != MessageBoxResult.Yes) return;

            StatusLeft.Text = S._("settings.imports.deleting");
            try
            {
                using (var tx = new Autodesk.Revit.DB.Transaction(doc, "ME-Tools: Remove Imported Categories"))
                {
                    tx.Start();

                    // Delete live instances FIRST -- a category still
                    // actively referenced by one can't be deleted directly.
                    //
                    // BUG FIXED HERE: allImportInstances is collected once,
                    // but re-iterated once per selected category below --
                    // an instance already deleted while processing an
                    // earlier category was still being touched again on a
                    // later category's pass. Accessing ii.Category on that
                    // now-stale reference throws "the referenced object is
                    // not valid... deleted from the database", and since
                    // that property access sat OUTSIDE any try/catch (only
                    // the Delete call itself was protected), it escaped all
                    // the way to the outer catch and aborted the entire
                    // operation -- exactly the error dialog reported.
                    // Tracking already-deleted ids (and wrapping the whole
                    // per-instance check, not just the delete, in try/catch
                    // as a safety net) fixes both the cause and the symptom.
                    var allImportInstances = new Autodesk.Revit.DB.FilteredElementCollector(doc)
                        .OfClass(typeof(Autodesk.Revit.DB.ImportInstance))
                        .Cast<Autodesk.Revit.DB.ImportInstance>()
                        .ToList();
                    var deletedInstanceIds = new HashSet<long>();

                    foreach (var row in selected)
                    {
                        foreach (var ii in allImportInstances)
                        {
                            try
                            {
                                if (deletedInstanceIds.Contains(ii.Id.IntegerValue)) continue;
                                if (ii.Category?.Id?.IntegerValue == row.CategoryId.IntegerValue)
                                {
                                    doc.Delete(ii.Id);
                                    deletedInstanceIds.Add(ii.Id.IntegerValue);
                                }
                            }
                            catch { }
                        }
                    }

                    doc.Regenerate();

                    foreach (var row in selected)
                    {
                        try
                        {
                            var cat = doc.Settings.Categories.Cast<Autodesk.Revit.DB.Category>()
                                .FirstOrDefault(c => c.Id.IntegerValue == row.CategoryId.IntegerValue);
                            if (cat == null) continue;

                            try
                            {
                                foreach (Autodesk.Revit.DB.Category sub in cat.SubCategories)
                                    try { doc.Delete(sub.Id); } catch { }
                            }
                            catch { }

                            try { doc.Delete(cat.Id); } catch { }
                        }
                        catch { }
                    }

                    doc.Regenerate();
                    tx.Commit();
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(string.Format(S._("settings.imports.delete_error"), ex.Message),
                    S._("settings.imports.title"), MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // Verify -- doc.Delete() on a still-protected category doesn't
            // throw, it just silently does nothing, so trust a fresh scan,
            // not the absence of an exception.
            HashSet<long> stillExisting;
            try
            {
                stillExisting = doc.Settings.Categories.Cast<Autodesk.Revit.DB.Category>()
                    .Where(c => c.Parent == null)
                    .Select(c => (long)c.Id.IntegerValue)
                    .ToHashSet();
            }
            catch { stillExisting = new HashSet<long>(); }

            int removed = 0, stillPresent = 0;
            var stillPresentNames = new List<string>();
            foreach (var row in selected)
            {
                if (stillExisting.Contains(row.CategoryId.IntegerValue))
                {
                    stillPresent++;
                    stillPresentNames.Add(row.Name);
                    // Empty note = "known stubborn, no specific reason yet"
                    // -- only set if nothing more specific is already on
                    // file (e.g. from a previous Find & Remove from
                    // Families attempt), so this doesn't erase a more
                    // useful note with a blank one.
                    if (!_stubbornCategoryNotes.ContainsKey(row.CategoryId.IntegerValue))
                        _stubbornCategoryNotes[row.CategoryId.IntegerValue] = "";
                }
                else
                {
                    removed++;
                    // Actually gone this time (e.g. after a Purge Unused or
                    // family edit since a previous attempt) -- stop
                    // remembering it as stubborn, or a later re-import
                    // reusing the same id would wrongly inherit the old label.
                    _stubbornCategoryNotes.Remove(row.CategoryId.IntegerValue);
                }
            }
            SaveStubbornCategoryIds(doc);

            var sb = new System.Text.StringBuilder();
            sb.AppendLine(string.Format(S._("settings.imports.removed_line"), removed));
            if (stillPresent > 0)
            {
                sb.AppendLine(string.Format(S._("settings.imports.still_present_line"), stillPresent));
                sb.AppendLine(S._("settings.imports.still_present_hint"));
                foreach (var n in stillPresentNames.Distinct().Take(10))
                    sb.AppendLine("   • " + n);
            }
            MessageBox.Show(sb.ToString(), S._("settings.imports.done_title"), MessageBoxButton.OK, MessageBoxImage.None);

            LoadImportedCategories();
        }

        // ── "Find & Remove from Families" ────────────────────────────────
        //
        // Standard IFamilyLoadOptions implementation for reloading a family
        // back into the project it came from -- always overwrite, since
        // we're the ones who just edited it and want that edit to take
        // effect. Same shape as the pattern Autodesk's own docs and every
        // Revit API reference show for exactly this "reload after editing"
        // scenario.
        private class OverwriteFamilyLoadOptions : Autodesk.Revit.DB.IFamilyLoadOptions
        {
            public bool OnFamilyFound(bool familyInUse, out bool overwriteParameterValues)
            {
                overwriteParameterValues = true;
                return true;
            }

            public bool OnSharedFamilyFound(Autodesk.Revit.DB.Family sharedFamily, bool familyInUse,
                out Autodesk.Revit.DB.FamilySource source, out bool overwriteParameterValues)
            {
                source = Autodesk.Revit.DB.FamilySource.Family;
                overwriteParameterValues = true;
                return true;
            }
        }

        // Editing/regenerating a family can trigger routine, informational
        // Revit warnings that have nothing to do with the actual operation
        // -- e.g. "constraints between geometry in the family can behave
        // unpredictably", which just means the family's own geometry isn't
        // fully constrained to reference planes, a common and mostly
        // harmless modeling habit, not something this specific operation
        // caused. Without this, that dialog blocks and needs a manual OK
        // click -- for every family this scan happens to touch, which
        // could mean many clicks in a row for something that isn't
        // actually a problem. Only WARNING-severity messages are
        // dismissed; actual errors are left completely untouched and
        // handled normally, exactly the pattern Autodesk's own official
        // SDK samples use for this.
        private class SuppressWarningsPreprocessor : Autodesk.Revit.DB.IFailuresPreprocessor
        {
            public Autodesk.Revit.DB.FailureProcessingResult PreprocessFailures(Autodesk.Revit.DB.FailuresAccessor failuresAccessor)
            {
                foreach (var fma in failuresAccessor.GetFailureMessages())
                {
                    try
                    {
                        if (fma.GetSeverity() == Autodesk.Revit.DB.FailureSeverity.Warning)
                            failuresAccessor.DeleteWarning(fma);
                    }
                    catch { }
                }
                return Autodesk.Revit.DB.FailureProcessingResult.Continue;
            }
        }

        // A single place a wanted category name turned up. TopLevelFamily
        // is always the one directly reachable from the project via
        // EditFamily -- for a nested match that's the OUTER family, since
        // that's what the fix would eventually need to open first, even
        // though the category actually lives one level deeper inside it.
        private class FamilyMatch
        {
            public Autodesk.Revit.DB.Family TopLevelFamily;
            public string FamilyName; // captured as a plain string while TopLevelFamily is known-fresh -- see RemoveImportFromFamily's caller for why this matters
            public string Path;       // e.g. "MyFamily" or "MyFamily > NestedSubFamily"
            public bool   IsEditable; // editability of the OUTER family -- that's what actually gates whether a fix is even possible
            public bool   IsNested;   // found inside a family nested within another family, not directly at the top level
        }

        // Opens every loaded family in the project -- editable or not, since
        // editability only matters for whether a fix is possible, not for
        // whether we should even look -- and checks each one's own
        // top-level categories for a name match against the given set.
        //
        // includeNested also recurses into families NESTED inside other
        // families (up to a sane depth limit). Off by default: every real,
        // verified fix so far has been a top-level family, and nested
        // recursion roughly doubles or triples the EditFamily/Close cost
        // per family (opening every family AND everything nested inside
        // it) for a case that hasn't actually been confirmed to happen yet
        // on a real project. "Force Full Rescan" turns this on.
        //
        // forceFullRescan clears the cache AND the visited-families record
        // entirely and starts completely fresh -- for when the cache might
        // be stale (a family was added, removed, or edited since it was
        // built). Without forceFullRescan, families already visited in an
        // EARLIER attempt are skipped outright, and the scan stops the
        // moment every currently-wanted category already has at least one
        // match -- there's no reason to keep opening the rest of the
        // project's families once the specific answer needed right now has
        // already been found. _familyIndexComplete only becomes true once
        // every family has actually been visited (possibly across several
        // separate attempts, not necessarily all in one run), which is
        // what makes it safe to trust the cache outright on a later call
        // for different categories.
        private Dictionary<string, List<FamilyMatch>> FindOwningFamilies(
            Autodesk.Revit.DB.Document doc, IEnumerable<string> categoryNames,
            bool forceFullRescan = false, bool includeNested = false)
        {
            var wanted = new HashSet<string>(categoryNames, StringComparer.OrdinalIgnoreCase);
            var result = wanted.ToDictionary(n => n, n => new List<FamilyMatch>(), StringComparer.OrdinalIgnoreCase);

            LoadFamilyIndex(doc);

            if (forceFullRescan)
            {
                _familyCategoryIndex.Clear();
                _visitedFamilyIds.Clear();
                _familyIndexComplete = false;
            }
            else if (_familyIndexComplete)
            {
                ResolveMatchesFromIndex(doc, wanted, result);
                return result;
            }
            else if (wanted.All(w => _familyCategoryIndex.TryGetValue(w, out var m) && m.Count > 0))
            {
                // Partial cache from an earlier attempt already has
                // everything currently wanted -- no need to open even one
                // more family to confirm that.
                ResolveMatchesFromIndex(doc, wanted, result);
                return result;
            }

            List<Autodesk.Revit.DB.Family> families;
            try
            {
                families = new Autodesk.Revit.DB.FilteredElementCollector(doc)
                    .OfClass(typeof(Autodesk.Revit.DB.Family))
                    .Cast<Autodesk.Revit.DB.Family>()
                    .Where(f => f != null)
                    .ToList();
            }
            catch { families = new List<Autodesk.Revit.DB.Family>(); }

            var visitedThisPass = new HashSet<long>(); // separate from _visitedFamilyIds -- this one only guards against infinite loops on circular nesting WITHIN this single call
            foreach (var fam in families)
            {
                if (_visitedFamilyIds.Contains(fam.Id.IntegerValue)) continue; // already checked in an earlier attempt -- never reopened

                Autodesk.Revit.DB.Document famDoc = null;
                try
                {
                    famDoc = doc.EditFamily(fam);
                    if (famDoc != null && famDoc.IsFamilyDocument)
                        ScanFamilyDocForCategories(famDoc, fam, fam.Name, 0, _familyCategoryIndex, visitedThisPass, includeNested);
                }
                catch { }
                finally { try { famDoc?.Close(false); } catch { } }

                _visitedFamilyIds.Add(fam.Id.IntegerValue);

                if (wanted.All(w => _familyCategoryIndex.TryGetValue(w, out var m) && m.Count > 0))
                    break; // everything currently being asked about is already found -- stop here
            }

            _familyIndexComplete       = _visitedFamilyIds.Count >= families.Count;
            _familyIndexIncludesNested = includeNested || _familyIndexIncludesNested; // once true from a fuller scan, a later shallow one shouldn't downgrade it
            SaveFamilyIndex(doc);

            ResolveMatchesFromIndex(doc, wanted, result);
            return result;
        }

        // Turns the (possibly cached) name-based index back into live
        // FamilyMatch objects -- a single collector call to resolve
        // families by name, not another round of EditFamily.
        private void ResolveMatchesFromIndex(Autodesk.Revit.DB.Document doc, HashSet<string> wanted, Dictionary<string, List<FamilyMatch>> result)
        {
            List<Autodesk.Revit.DB.Family> liveFamilies;
            try
            {
                liveFamilies = new Autodesk.Revit.DB.FilteredElementCollector(doc)
                    .OfClass(typeof(Autodesk.Revit.DB.Family))
                    .Cast<Autodesk.Revit.DB.Family>()
                    .ToList();
            }
            catch { liveFamilies = new List<Autodesk.Revit.DB.Family>(); }

            foreach (var name in wanted)
            {
                if (!_familyCategoryIndex.TryGetValue(name, out var cached)) continue;
                foreach (var c in cached)
                {
                    var fam = liveFamilies.FirstOrDefault(f => string.Equals(f.Name, c.Family, StringComparison.OrdinalIgnoreCase));
                    if (fam == null) continue; // renamed or removed since the cache was built
                    result[name].Add(new FamilyMatch
                    {
                        TopLevelFamily = fam,
                        FamilyName     = fam.Name,
                        Path           = c.Path,
                        IsEditable     = fam.IsEditable, // re-check live rather than trust a possibly-stale cached flag
                        IsNested       = c.Path.Contains(">"),
                    });
                }
            }
        }

        // depth 0 = this IS the top-level family; depth > 0 = nested one
        // level or more inside it. Depth-limited (3) purely as a safety net
        // against unusual/circular nesting -- real-world nesting rarely
        // goes more than one or two levels deep. Records into `index` for
        // every category name it finds, unconditionally -- not filtered to
        // any particular wanted set, since the whole point is building a
        // reusable cache from a cost that's already being paid regardless.
        // includeNested=false skips even collecting nested families at all
        // (not just skipping the recursion into them) -- saves a collector
        // call per family for the common case where nested checking isn't
        // needed.
        private void ScanFamilyDocForCategories(
            Autodesk.Revit.DB.Document famDocToScan, Autodesk.Revit.DB.Family topLevelFamily, string pathSoFar, int depth,
            Dictionary<string, List<CachedFamilyMatch>> index, HashSet<long> visitedFamilyIds, bool includeNested)
        {
            if (depth > 3) return;
            try
            {
                var famCatNames = famDocToScan.Settings.Categories.Cast<Autodesk.Revit.DB.Category>()
                    .Where(c => c != null && c.Parent == null && c.Id != null && c.Id.IntegerValue > 0)
                    .Select(c => c.Name)
                    .ToList();

                foreach (var name in famCatNames)
                {
                    if (!index.TryGetValue(name, out var list))
                    {
                        list = new List<CachedFamilyMatch>();
                        index[name] = list;
                    }
                    list.Add(new CachedFamilyMatch
                    {
                        Family   = topLevelFamily.Name,
                        Path     = pathSoFar,
                        Editable = topLevelFamily.IsEditable,
                    });
                }

                if (!includeNested) return;

                var nested = new Autodesk.Revit.DB.FilteredElementCollector(famDocToScan)
                    .OfClass(typeof(Autodesk.Revit.DB.Family))
                    .Cast<Autodesk.Revit.DB.Family>()
                    .Where(f => f != null)
                    .ToList();

                foreach (var nf in nested)
                {
                    if (!visitedFamilyIds.Add(nf.Id.IntegerValue)) continue;
                    Autodesk.Revit.DB.Document nestedDoc = null;
                    try
                    {
                        nestedDoc = famDocToScan.EditFamily(nf);
                        if (nestedDoc != null && nestedDoc.IsFamilyDocument)
                            ScanFamilyDocForCategories(nestedDoc, topLevelFamily, pathSoFar + " > " + nf.Name,
                                depth + 1, index, visitedFamilyIds, includeNested);
                    }
                    catch { }
                    finally { try { nestedDoc?.Close(false); } catch { } }
                }
            }
            catch { }
        }

        // Opens the given family, removes the import instances + category
        // matching categoryName from WITHIN the family itself, then reloads
        // the family back into the project. This is the operation that
        // actually has a chance of working where project-level deletion and
        // Purge Unused both can't -- the category's real owner is the
        // family, not the project, so the fix has to happen there too.
        private (bool Success, string Error) RemoveImportFromFamily(Autodesk.Revit.DB.Document doc, Autodesk.Revit.DB.Family fam, string categoryName)
        {
            Autodesk.Revit.DB.Document famDoc = null;
            try
            {
                try { famDoc = doc.EditFamily(fam); }
                catch (Exception ex) { return (false, "EditFamily: " + ex.Message); }
                if (famDoc == null) return (false, "EditFamily returned null");
                if (!famDoc.IsFamilyDocument) return (false, "EditFamily result isn't a family document");

                try
                {
                    using (var tx = new Autodesk.Revit.DB.Transaction(famDoc, "ME-Tools: Remove Imported Category"))
                    {
                        tx.Start();

                        var failOpts = tx.GetFailureHandlingOptions();
                        failOpts.SetFailuresPreprocessor(new SuppressWarningsPreprocessor());
                        tx.SetFailureHandlingOptions(failOpts);

                        foreach (var ii in new Autodesk.Revit.DB.FilteredElementCollector(famDoc)
                            .OfClass(typeof(Autodesk.Revit.DB.ImportInstance))
                            .Cast<Autodesk.Revit.DB.ImportInstance>()
                            .ToList())
                        {
                            try
                            {
                                if (string.Equals(ii.Category?.Name, categoryName, StringComparison.OrdinalIgnoreCase))
                                    famDoc.Delete(ii.Id);
                            }
                            catch { }
                        }

                        famDoc.Regenerate();

                        try
                        {
                            var cat = famDoc.Settings.Categories.Cast<Autodesk.Revit.DB.Category>()
                                .FirstOrDefault(c => string.Equals(c.Name, categoryName, StringComparison.OrdinalIgnoreCase) && c.Parent == null);
                            if (cat != null)
                            {
                                try
                                {
                                    foreach (Autodesk.Revit.DB.Category sub in cat.SubCategories)
                                        try { famDoc.Delete(sub.Id); } catch { }
                                }
                                catch { }
                                try { famDoc.Delete(cat.Id); } catch { }
                            }
                        }
                        catch { }

                        famDoc.Regenerate();
                        tx.Commit();
                    }
                }
                catch (Exception ex) { return (false, "Editing/committing inside the family: " + ex.Message); }

                // LoadFamily must run with no active transaction on the
                // family document -- confirmed via Autodesk's own docs and
                // API reference examples for this exact "reload after
                // editing" pattern. The transaction above is already
                // committed by this point, so this is safe.
                try
                {
                    var loaded = famDoc.LoadFamily(doc, new OverwriteFamilyLoadOptions());
                    return loaded != null
                        ? (true, (string)null)
                        : (false, "LoadFamily returned null -- the edit inside the family likely succeeded, but reloading it into the project was silently refused");
                }
                catch (Exception ex) { return (false, "LoadFamily: " + ex.Message); }
            }
            catch (Exception ex) { return (false, "Unexpected: " + ex.Message); }
            finally { try { famDoc?.Close(false); } catch { } }
        }

        private void OnFindInFamiliesClicked(bool forceFullRescan = false, bool skipConfirm = false)
        {
            var selected = _importRows.Where(r => r.Checkbox?.IsChecked == true).ToList();
            if (selected.Count == 0)
            {
                MessageBox.Show(S._("settings.imports.select_first"), S._("settings.imports.title"),
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var doc = SettingsCommand.CurrentDocument;
            if (doc == null)
            {
                MessageBox.Show(S._("settings.imports.no_document"), S._("settings.imports.title"),
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (!skipConfirm)
            {
                var confirmResult = MessageBox.Show(S._("settings.imports.fix_families_confirm"),
                    S._("settings.imports.confirm_title"), MessageBoxButton.YesNo, MessageBoxImage.Warning);
                if (confirmResult != MessageBoxResult.Yes) return;
            }

            StatusLeft.Text = S._("settings.imports.scanning_families");
            Dispatcher.BeginInvoke(new Action(() =>
            {
                var names = selected.Select(r => r.Name).ToList();
                Dictionary<string, List<FamilyMatch>> owners;
                try
                {
                    owners = FindOwningFamilies(doc, names, forceFullRescan, includeNested: forceFullRescan);
                }
                catch (Exception ex)
                {
                    StatusLeft.Text = string.Format(S._("settings.imports.delete_error"), ex.Message);
                    return;
                }

                int fixedCount = 0, needsAttentionCount = 0, notFoundCount = 0;
                var needsAttentionByCategory = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
                var notFoundNames = new List<string>();

                foreach (var name in names)
                {
                    var matches = owners.TryGetValue(name, out var list) ? list : new List<FamilyMatch>();
                    if (matches.Count == 0)
                    {
                        notFoundCount++;
                        notFoundNames.Add(name);
                        continue;
                    }

                    // Only attempt the automatic fix for a match that's both
                    // directly at the top level AND editable -- anything
                    // nested or not editable gets reported honestly instead
                    // of a risky, unproven multi-level fix attempt.
                    var fixable = matches.Where(m => !m.IsNested && m.IsEditable).ToList();
                    bool anySucceeded = false;
                    var failureReasons = new List<string>();
                    foreach (var m in fixable)
                    {
                        try
                        {
                            // Re-resolve fresh by name right before this
                            // specific attempt -- do NOT reuse m.TopLevelFamily
                            // directly. A successful LoadFamily call for an
                            // EARLIER match in this same loop can invalidate
                            // .NET wrapper objects for every OTHER family
                            // collected before it, even ones that were never
                            // touched -- a real, documented Revit API
                            // behavior, not a bug in the matching logic. This
                            // is exactly why the first fix in a batch would
                            // succeed and every one after it would fail with
                            // "EditFamily: the referenced object is not
                            // valid" regardless of which family it was.
                            var freshFam = new Autodesk.Revit.DB.FilteredElementCollector(doc)
                                .OfClass(typeof(Autodesk.Revit.DB.Family))
                                .Cast<Autodesk.Revit.DB.Family>()
                                .FirstOrDefault(f => string.Equals(f.Name, m.FamilyName, StringComparison.OrdinalIgnoreCase));
                            if (freshFam == null)
                            {
                                failureReasons.Add($"{m.Path}: family '{m.FamilyName}' could not be re-resolved");
                                continue;
                            }

                            var (success, error) = RemoveImportFromFamily(doc, freshFam, name);
                            if (success) anySucceeded = true;
                            else failureReasons.Add($"{m.Path}: {error}");
                        }
                        catch (Exception ex) { failureReasons.Add($"{m.Path}: {ex.Message}"); }
                    }

                    if (anySucceeded)
                    {
                        fixedCount++;
                        var row = _importRows.FirstOrDefault(r => string.Equals(r.Name, name, StringComparison.OrdinalIgnoreCase));
                        if (row != null) _stubbornCategoryNotes.Remove(row.CategoryId.IntegerValue);
                    }
                    else
                    {
                        // Found somewhere, but not somewhere this code can
                        // safely fix on its own -- nested inside another
                        // family, not editable right now (often means
                        // someone else has it checked out in a workshared
                        // model), or the fix attempt itself failed, now with
                        // the ACTUAL error captured instead of a generic
                        // phrase. Persisted directly onto the row's own
                        // note, not just this one message box -- a message
                        // box is easy to close by accident before reading
                        // it, which is exactly what happened here; the row
                        // itself keeps showing the real reason every time
                        // this list is scanned from now on.
                        needsAttentionCount++;
                        var noteParts = new List<string>();
                        int reasonIdx = 0;
                        foreach (var m in matches)
                        {
                            string why = m.IsNested ? S._("settings.imports.match_nested")
                                       : !m.IsEditable ? S._("settings.imports.match_not_editable")
                                       : reasonIdx < failureReasons.Count ? failureReasons[reasonIdx++]
                                       : S._("settings.imports.match_fix_failed");
                            noteParts.Add($"{m.Path} ({why})");
                        }

                        if (!needsAttentionByCategory.TryGetValue(name, out var catLines))
                        {
                            catLines = new List<string>();
                            needsAttentionByCategory[name] = catLines;
                        }
                        catLines.AddRange(noteParts);

                        var row = _importRows.FirstOrDefault(r => string.Equals(r.Name, name, StringComparison.OrdinalIgnoreCase));
                        if (row != null)
                            _stubbornCategoryNotes[row.CategoryId.IntegerValue] =
                                string.Format(S._("settings.imports.note_found_in"), string.Join("; ", noteParts));
                    }
                }
                SaveStubbornCategoryIds(doc);

                var sb = new System.Text.StringBuilder();
                sb.AppendLine(string.Format(S._("settings.imports.fix_families_fixed_line"), fixedCount));
                if (needsAttentionCount > 0)
                {
                    sb.AppendLine(string.Format(S._("settings.imports.fix_families_attention_line"), needsAttentionCount));
                    // BUG FIXED HERE: this used to be one flat, global list
                    // capped at 10 lines total -- a single category with 10
                    // family matches (a shared block embedded in many
                    // family types, which is exactly what happened here)
                    // could consume the entire cap on its own, silently
                    // hiding every OTHER category's own reason from the
                    // summary entirely. Capping per-category instead
                    // guarantees every category gets at least some
                    // representation.
                    foreach (var kv in needsAttentionByCategory.Take(10))
                    {
                        sb.AppendLine("   • " + kv.Key);
                        foreach (var line in kv.Value.Take(2))
                            sb.AppendLine("      - " + line);
                        if (kv.Value.Count > 2)
                            sb.AppendLine(string.Format(S._("settings.imports.n_more_locations"), kv.Value.Count - 2));
                    }
                }
                bool offerNestedRescan = notFoundCount > 0 && !_familyIndexIncludesNested;
                if (notFoundCount > 0)
                {
                    sb.AppendLine(string.Format(S._("settings.imports.fix_families_notfound_line"), notFoundCount));
                    sb.AppendLine(_familyIndexIncludesNested
                        ? S._("settings.imports.fix_families_notfound_hint")
                        : S._("settings.imports.fix_families_notfound_hint_shallow"));
                    foreach (var n in notFoundNames.Distinct().Take(10))
                        sb.AppendLine("   • " + n);
                }

                if (offerNestedRescan)
                {
                    sb.AppendLine();
                    sb.AppendLine(S._("settings.imports.offer_nested_now"));
                    var goDeeper = MessageBox.Show(sb.ToString(), S._("settings.imports.done_title"),
                        MessageBoxButton.YesNo, MessageBoxImage.None);
                    StatusLeft.Text = string.Format(S._("settings.imports.rescan_done"), _importRows.Count);
                    LoadImportedCategories();
                    if (goDeeper == MessageBoxResult.Yes)
                        OnFindInFamiliesClicked(forceFullRescan: true, skipConfirm: true);
                    return;
                }

                MessageBox.Show(sb.ToString(), S._("settings.imports.done_title"), MessageBoxButton.OK, MessageBoxImage.None);

                StatusLeft.Text = string.Format(S._("settings.imports.rescan_done"), _importRows.Count);
                LoadImportedCategories();
            }), System.Windows.Threading.DispatcherPriority.Background);
        }

        private UIElement BuildHeightRow(METools.FamilyPlacer.FamilyHeightEntry en,
                                         IReadOnlyDictionary<string, double> overrides)
        {
            var g = new Grid { Margin = new Thickness(0, 2, 0, 2) };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(90) });

            var name = new TextBlock
            {
                Text              = en.Family,
                FontSize          = 12,
                Foreground        = MeToolsTheme.BrText,
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming      = TextTrimming.CharacterEllipsis,
            };

            bool   hasOv    = overrides.TryGetValue(en.Family, out double ov);
            double? shownVal = hasOv ? (double?)ov : en.DefaultMm;
            string  txt      = shownVal.HasValue ? shownVal.Value.ToString("0.###") : "";

            var box = new TextBox
            {
                Text                     = txt,
                Height                   = 28,
                FontSize                 = 12,
                TextAlignment            = TextAlignment.Center,
                VerticalContentAlignment = VerticalAlignment.Center,
                Background                = MeToolsTheme.BrInput,
                Foreground                = MeToolsTheme.BrInputFg,
                BorderBrush               = MeToolsTheme.BrBorder,
                BorderThickness           = new Thickness(1),
                CaretBrush                = MeToolsTheme.BrText,
                ToolTip = en.DefaultMm.HasValue
                    ? S._("settings.heights.family_default") + en.DefaultMm.Value.ToString("0.###") + " mm"
                    : S._("settings.heights.no_default"),
            };

            Grid.SetColumn(name, 0);
            Grid.SetColumn(box, 2);
            g.Children.Add(name);
            g.Children.Add(box);
            _heightRows.Add(new KeyValuePair<string, TextBox>(en.Family, box));
            return g;
        }

        private void OnSaveHeights()
        {
            // Merge into existing overrides so values for families not currently listed are preserved.
            var map = new Dictionary<string, double>();
            foreach (var kv in FamilyHeightStore.All()) map[kv.Key] = kv.Value;

            foreach (var row in _heightRows)
            {
                var fam = row.Key;
                if (string.IsNullOrEmpty(fam)) continue;
                var t = row.Value?.Text?.Trim() ?? "";
                if (t.Length == 0) { map.Remove(fam); continue; } // blank -> track family default
                if (double.TryParse(t, out double mm)) map[fam] = mm;
            }

            try
            {
                FamilyHeightStore.SaveAll(map);
                MessageBox.Show(string.Format(S._("settings.heights.saved_msg"), map.Count),
                                S._("settings.heights.saved_title"), MessageBoxButton.OK, MessageBoxImage.None);
            }
            catch (Exception ex)
            {
                MessageBox.Show(string.Format(S._("settings.save_failed"), ex.Message),
                                S._("settings.save_failed_title"), MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        protected override void OnThemeChanged()
        {
            base.OnThemeChanged();
            PopulateContent();   // full rebuild -> every element repainted in the new theme
        }
    }

    // ── Settings store ────────────────────────────────────────────────────
    internal static class SettingsStore
    {
        private static readonly string File = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "METools", "settings.ini");
        private static string _language;
        public static string Language
        {
            get
            {
                if (_language != null) return _language;
                try { if (System.IO.File.Exists(File)) foreach (var line in System.IO.File.ReadAllLines(File)) if (line.StartsWith("language=")) return _language = line.Substring(9).Trim(); }
                catch { }
                return _language = "en";
            }
            set
            {
                _language = value;
                try { var dir = Path.GetDirectoryName(File); Directory.CreateDirectory(dir); System.IO.File.WriteAllText(File, $"language={value}\n"); }
                catch { }
            }
        }
    }
}

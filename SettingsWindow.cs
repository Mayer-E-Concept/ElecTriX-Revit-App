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
                MinHeight  = 280,
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
                Foreground = MeToolsTheme.BrPetrol, HorizontalAlignment = HorizontalAlignment.Center,
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
                FontSize = 13, FontWeight = FontWeights.SemiBold, Foreground = MeToolsTheme.BrPetrol,
                VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0),
            };
            Grid.SetColumn(_backBarTitle, 1);

            grid.Children.Add(backBtn);
            grid.Children.Add(_backBarTitle);

            return new Border { Child = grid, Visibility = Visibility.Collapsed, Margin = new Thickness(0, 4, 0, 0) };
        }

        private string TabTitle(int idx) => idx >= 0 && idx < _homeTiles.Length ? S._(_homeTiles[idx].Key) : "";

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
            ResizeToFitContent();
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

            ResizeToFitContent();
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
                Foreground      = MeToolsTheme.BrPetrol,
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
            var mailLink = new TextBlock { Text = "office@mayer-econcept.ro", FontSize = 10, Foreground = MeToolsTheme.BrPetrol, Cursor = Cursors.Hand, TextDecorations = TextDecorations.Underline, VerticalAlignment = VerticalAlignment.Center };
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
            p.Children.Add(Sec(S._("settings.worksets.title")));
            p.Children.Add(InfoBox(S._("settings.worksets.hint")));

            // List
            _lbWorksets = new ListBox
            {
                Height = 180, Margin = new Thickness(0, 8, 0, 8),
                Background = MeToolsTheme.BrInput, Foreground = MeToolsTheme.BrText,
                BorderBrush = MeToolsTheme.BrBorder, BorderThickness = new Thickness(1),
                FontSize = 12, Padding = new Thickness(2),
            };
            p.Children.Add(_lbWorksets);

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
            p.Children.Add(addGrid);

            // Edit buttons
            var editRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 16) };
            var btnRemove = FooterBtn(S._("settings.worksets.remove"), primary: false, onClick: OnRemoveWorkset);
            var btnSave   = FooterBtn(S._("settings.worksets.save"),   primary: true,  onClick: OnSaveWorksets);
            btnRemove.Margin = new Thickness(0, 0, 8, 0);
            editRow.Children.Add(btnRemove); editRow.Children.Add(btnSave);
            p.Children.Add(editRow);

            // Apply to project button
            p.Children.Add(new Separator { Margin = new Thickness(0, 0, 0, 16), Background = MeToolsTheme.BrBorder });
            p.Children.Add(Sec(S._("settings.worksets.apply_title")));
            p.Children.Add(InfoBox(S._("settings.worksets.apply_hint")));
            var btnApply = ActionBtn(S._("settings.worksets.create_btn"), true, OnApplyWorksets);
            btnApply.Margin = new Thickness(0, 8, 0, 0);
            p.Children.Add(btnApply);

            // -- Current project's actual worksets (read-only, live from the open document) --
            p.Children.Add(new Separator { Margin = new Thickness(0, 20, 0, 16), Background = MeToolsTheme.BrBorder });
            var curHdrRow = new Grid { Margin = new Thickness(0, 0, 0, 6) };
            curHdrRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            curHdrRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var curHdrLbl = Sec(S._("settings.worksets.project"));
            Grid.SetColumn(curHdrLbl, 0); curHdrRow.Children.Add(curHdrLbl);
            var btnRefreshCur = FooterBtn(S._("settings.worksets.refresh"), false, LoadCurrentProjectWorksets);
            btnRefreshCur.Height = 26; btnRefreshCur.Padding = new Thickness(10, 0, 10, 0); btnRefreshCur.FontSize = 11;
            Grid.SetColumn(btnRefreshCur, 1); curHdrRow.Children.Add(btnRefreshCur);
            p.Children.Add(curHdrRow);

            p.Children.Add(InfoBox(S._("settings.worksets.current_hint")));

            _lbCurrentWorksets = new ListBox
            {
                Height = 140, Margin = new Thickness(0, 8, 0, 0),
                Background = MeToolsTheme.BrInput, Foreground = MeToolsTheme.BrText,
                BorderBrush = MeToolsTheme.BrBorder, BorderThickness = new Thickness(1),
                FontSize = 12, Padding = new Thickness(2),
                IsHitTestVisible = true, // allow scrolling; selection has no effect (read-only)
            };
            p.Children.Add(_lbCurrentWorksets);

            // -- Share your whole ME-Tools configuration with a colleague --
            p.Children.Add(new Separator { Margin = new Thickness(0, 20, 0, 16), Background = MeToolsTheme.BrBorder });
            p.Children.Add(Sec(S._("settings.config.title")));
            p.Children.Add(InfoBox(S._("settings.config.hint")));
            var configBtnRow = new StackPanel { Orientation = Orientation.Horizontal };
            var exportBtn = FooterBtn(S._("settings.config.export"), true, OnExportConfig);
            exportBtn.Margin = new Thickness(0, 0, 8, 0);
            var importBtn = FooterBtn(S._("settings.config.import"), false, OnImportConfig);
            configBtnRow.Children.Add(exportBtn);
            configBtnRow.Children.Add(importBtn);
            p.Children.Add(configBtnRow);

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
                            Foreground = MeToolsTheme.BrPetrol,
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
            var btnRescan = FooterBtn(S._("settings.imports.rescan"), primary: false, onClick: LoadImportedCategories);
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
                return;
            }

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
            ResizeToFitContent();
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

            string statusText = row.LiveInstanceCount > 0
                ? string.Format(S._("settings.imports.row_in_use"), row.SubCategoryCount, row.LiveInstanceCount)
                : string.Format(S._("settings.imports.row_orphaned"), row.SubCategoryCount);
            textStack.Children.Add(new TextBlock
            {
                Text = statusText, FontSize = 10.5,
                Foreground = row.LiveInstanceCount > 0 ? MeToolsTheme.BrOrange : MeToolsTheme.BrMuted,
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
            int selected = _importRows.Count(r => r.Checkbox?.IsChecked == true);
            _importsStatus.Text = selected > 0
                ? string.Format(S._("settings.imports.n_selected"), selected)
                : S._("settings.imports.none_selected");
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
                { stillPresent++; stillPresentNames.Add(row.Name); }
                else removed++;
            }

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

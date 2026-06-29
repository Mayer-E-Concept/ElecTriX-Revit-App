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

        // ── Tab panels ────────────────────────────────────────────────────
        private StackPanel _panAppearance;
        private StackPanel _panLanguage;
        private StackPanel _panLicense;
        private StackPanel _panWorksets;
        private StackPanel _panHeights;

        // ── Tab buttons ───────────────────────────────────────────────────
        private Button _tabAppearance, _tabLanguage, _tabLicense, _tabWorksets, _tabHeights;
        private int    _activeTab = 0;

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
        private ListBox _lbProjectWorksets;
        private TextBox _tbNewWorkset;

        private static string WorksetsConfigPath =>
            Path.Combine(
                Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? "",
                "config", "standard_worksets.json");

        public SettingsWindow()
        {
            S.SetLanguage(SettingsStore.Language ?? "en");
            InitWindow(S.Get("settings.title"), width: 500, isDialog: false);
            // Fix to explicit height so it doesn't grow off-screen on tall tabs (Worksets)
            Height        = 680;
            MaxHeight     = 820;
            MinHeight     = 480;
            SizeToContent = System.Windows.SizeToContent.Manual;
            BuildStatusBar(LicenseManager.StatusText, AppVersion);
            BuildContent();
        }

        // ── Build UI ──────────────────────────────────────────────────────
        private StackPanel _contentRoot;

        private void BuildContent()
        {
            _contentRoot = new StackPanel();

            // Wrap everything below the tab bar in a scroll viewer so tall
            // tabs (Worksets) don't push the window off-screen.
            var scroll = new ScrollViewer
            {
                VerticalScrollBarVisibility   = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            };
            scroll.Content = _contentRoot;
            var _swGrid = new System.Windows.Controls.Grid();
            _swGrid.Children.Add(scroll);
            _swGrid.Children.Add(Watermark());
            RootDock.Children.Add(_swGrid);
            PopulateContent();
        }

        // Rebuilds the whole content with the current theme (called on theme switch)
        private void PopulateContent()
        {
            _contentRoot.Children.Clear();
            _contentRoot.Children.Add(BuildTabBar());

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

            var contentStack = new StackPanel();
            contentStack.Children.Add(_panAppearance);
            contentStack.Children.Add(_panLanguage);
            contentStack.Children.Add(_panLicense);
            contentStack.Children.Add(_panWorksets);
            contentStack.Children.Add(_panHeights);
            contentBorder.Child = contentStack;
            _contentRoot.Children.Add(contentBorder);

            ShowTab(_activeTab);
        }

        // ── Tab bar ───────────────────────────────────────────────────────
        private UIElement BuildTabBar()
        {
            var bar = new Grid { Height = 42, Background = MeToolsTheme.BrPetrolDark };
            for (int i = 0; i < 5; i++)
                bar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            _tabAppearance = MakeTabBtn(S.Get("settings.tab.appearance"), 0);
            _tabLanguage   = MakeTabBtn(S.Get("settings.tab.language"), 1);
            _tabLicense    = MakeTabBtn(S.Get("settings.tab.license"), 2);
            _tabWorksets   = MakeTabBtn(S.Get("settings.tab.worksets"), 3);
            _tabHeights    = MakeTabBtn(S.Get("settings.tab.heights"), 4);

            Grid.SetColumn(_tabAppearance, 0);
            Grid.SetColumn(_tabLanguage,   1);
            Grid.SetColumn(_tabLicense,    2);
            Grid.SetColumn(_tabWorksets,   3);
            Grid.SetColumn(_tabHeights,    4);

            bar.Children.Add(_tabAppearance);
            bar.Children.Add(_tabLanguage);
            bar.Children.Add(_tabLicense);
            bar.Children.Add(_tabWorksets);
            bar.Children.Add(_tabHeights);
            return bar;
        }

        private Button MakeTabBtn(string label, int idx)
        {
            var b = new Button
            {
                Content         = label,
                FontSize        = 12,
                BorderThickness = new Thickness(0),
                Background      = Brushes.Transparent,
                Foreground      = new SolidColorBrush(Color.FromArgb(160, 255, 255, 255)),
                Cursor          = Cursors.Hand,
                Height          = 42,
            };
            b.Template = RoundedBtnTemplate();
            b.Click += (s, e) => ShowTab(idx);
            return b;
        }

        private void ShowTab(int idx)
        {
            _activeTab = idx;
            _panAppearance.Visibility = idx == 0 ? Visibility.Visible : Visibility.Collapsed;
            _panLanguage.Visibility   = idx == 1 ? Visibility.Visible : Visibility.Collapsed;
            _panLicense.Visibility    = idx == 2 ? Visibility.Visible : Visibility.Collapsed;
            _panWorksets.Visibility   = idx == 3 ? Visibility.Visible : Visibility.Collapsed;
            _panHeights.Visibility    = idx == 4 ? Visibility.Visible : Visibility.Collapsed;

            StyleTabBtn(_tabAppearance, idx == 0);
            StyleTabBtn(_tabLanguage,   idx == 1);
            StyleTabBtn(_tabLicense,    idx == 2);
            StyleTabBtn(_tabWorksets,   idx == 3);
            StyleTabBtn(_tabHeights,    idx == 4);

            if (idx == 3) { LoadWorksetsIntoList(); LoadProjectWorksets(); }
            if (idx == 4) LoadHeightsIntoList();
        }

        private void StyleTabBtn(Button b, bool active)
        {
            if (b == null) return;
            b.Background = active ? new SolidColorBrush(MeToolsTheme.CBg) : Brushes.Transparent;
            b.Foreground = active ? MeToolsTheme.BrPetrol : new SolidColorBrush(Color.FromArgb(180, 255, 255, 255));
            b.FontWeight = active ? FontWeights.SemiBold : FontWeights.Normal;
        }

        // ── TAB 0: Appearance ─────────────────────────────────────────────
        private StackPanel BuildAppearancePanel()
        {
            var p = new StackPanel();
            p.Children.Add(Sec(S.Get("settings.appearance.theme_label")));
            p.Children.Add(InfoBox(S.Get("settings.appearance.theme_hint")));

            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 20) };
            _btnDark  = ToggleBtn(S.Get("settings.appearance.dark"),  MeToolsTheme.Current == MeTheme.Dark,  () => ApplyTheme(MeTheme.Dark));
            _btnLight = ToggleBtn(S.Get("settings.appearance.light"), MeToolsTheme.Current == MeTheme.Light, () => ApplyTheme(MeTheme.Light));
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
            ThemeToggleCommand.UpdateButton();
            UpdateToggle(_btnDark,  MeToolsTheme.Current == MeTheme.Dark);
            UpdateToggle(_btnLight, MeToolsTheme.Current == MeTheme.Light);
        }

        // ── TAB 1: Language ───────────────────────────────────────────────
        private StackPanel BuildLanguagePanel()
        {
            var p = new StackPanel { Visibility = Visibility.Collapsed };
            p.Children.Add(Sec(S.Get("settings.language.title")));
            p.Children.Add(InfoBox("Select the display language for all ME-Tools windows."));

            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 20), VerticalAlignment = VerticalAlignment.Center };
            row.Children.Add(new TextBlock { Text = S.Get("settings.language.label"), FontSize = 12, Foreground = MeToolsTheme.BrText, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0) });
            _cbLanguage = StyledCombo(30, 12); _cbLanguage.Width = 180;
            _cbLanguage.Items.Add("English");
            _cbLanguage.Items.Add("Deutsch");
            _cbLanguage.Items.Add("Romanian");
            var savedLang = SettingsStore.Language ?? "en";
            _cbLanguage.SelectedItem = savedLang == "de" ? "Deutsch" : savedLang == "ro" ? "Romanian" : "English";
            _cbLanguage.SelectionChanged += (s, e) =>
            {
                var sel = _cbLanguage.SelectedItem?.ToString();
                string code = sel == "Deutsch" ? "de" : sel == "Romanian" ? "ro" : "en";
                SettingsStore.Language = code;
                S.SetLanguage(code);
            };
            row.Children.Add(_cbLanguage);
            p.Children.Add(row);
            p.Children.Add(new TextBlock { Text = S.Get("settings.language.restart"), FontSize = 10, Foreground = MeToolsTheme.BrMuted, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8) });
            return p;
        }

        // ── TAB 2: License ────────────────────────────────────────────────
        private StackPanel BuildLicensePanel()
        {
            var p = new StackPanel { Visibility = Visibility.Collapsed };
            p.Children.Add(Sec(S.Get("settings.license.title")));
            p.Children.Add(BuildStatusBadge());
            p.Children.Add(new Border { Height = 16 });
            p.Children.Add(Sec(S.Get("settings.license.key")));
            p.Children.Add(new TextBlock { Text = "Enter your license key (format: METL-XXXX-XXXX-XXXX):", FontSize = 11, Foreground = MeToolsTheme.BrMuted, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8) });

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
            _btnActivate = FooterBtn(S.Get("settings.license.activate"), primary: true, onClick: OnActivate);
            _btnActivate.Height = 34; _btnActivate.Padding = new Thickness(16, 0, 16, 0);
            Grid.SetColumn(_tbKey, 0); Grid.SetColumn(_btnActivate, 2);
            keyRow.Children.Add(_tbKey); keyRow.Children.Add(_btnActivate);
            p.Children.Add(keyRow);

            _btnDeactivate = FooterBtn(S.Get("settings.license.remove"), primary: false, onClick: OnDeactivate);
            _btnDeactivate.Margin = new Thickness(0, 0, 0, 16);
            _btnDeactivate.Visibility = LicenseManager.IsLicensed() ? Visibility.Visible : Visibility.Collapsed;
            p.Children.Add(_btnDeactivate);

            var contactRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 0) };
            contactRow.Children.Add(new TextBlock { Text = S.Get("settings.license.need"), FontSize = 10, Foreground = MeToolsTheme.BrMuted, VerticalAlignment = VerticalAlignment.Center });
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
            if (ok) { MessageBox.Show("License activated successfully!\n\nThank you for using ME-Tools.", "Activation Successful", MessageBoxButton.OK, MessageBoxImage.None); RefreshStatusLabel(); if (_btnDeactivate != null) _btnDeactivate.Visibility = Visibility.Visible; UpdateActivateButton(); }
            else    { MessageBox.Show("The license key could not be validated.\n\nPlease check the key or contact office@mayer-econcept.ro.", "Activation Failed", MessageBoxButton.OK, MessageBoxImage.Warning); }
        }

        private void OnDeactivate()
        {
            if (MessageBox.Show("Remove the saved license key?\nYou can re-enter it at any time.", "Remove License", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
            { LicenseManager.Deactivate(); if (_tbKey != null) _tbKey.Text = ""; if (_btnDeactivate != null) _btnDeactivate.Visibility = Visibility.Collapsed; RefreshStatusLabel(); UpdateActivateButton(); }
        }

        // ── TAB 3: Worksets ───────────────────────────────────────────────
        private StackPanel BuildWorksetsPanel()
        {
            var p = new StackPanel { Visibility = Visibility.Collapsed };
            p.Children.Add(Sec(S.Get("settings.worksets.title")));
            p.Children.Add(InfoBox("Define which worksets are created in workshared projects.\nEdit the list and click Save. Changes take effect immediately."));

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
            var btnAdd = FooterBtn("Add", primary: true, onClick: OnAddWorkset);
            btnAdd.Height = 32; btnAdd.Padding = new Thickness(16, 0, 16, 0);
            Grid.SetColumn(_tbNewWorkset, 0); Grid.SetColumn(btnAdd, 2);
            addGrid.Children.Add(_tbNewWorkset); addGrid.Children.Add(btnAdd);
            p.Children.Add(addGrid);

            // Edit buttons
            var editRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 16) };
            var btnRemove = FooterBtn(S.Get("settings.worksets.remove"), primary: false, onClick: OnRemoveWorkset);
            var btnSave   = FooterBtn(S.Get("settings.worksets.save"),       primary: true,  onClick: OnSaveWorksets);
            btnRemove.Margin = new Thickness(0, 0, 8, 0);
            editRow.Children.Add(btnRemove); editRow.Children.Add(btnSave);
            p.Children.Add(editRow);

            // Apply to project button
            p.Children.Add(new Separator { Margin = new Thickness(0, 0, 0, 16), Background = MeToolsTheme.BrBorder });
            p.Children.Add(Sec("Apply to Current Project"));
            p.Children.Add(InfoBox("Creates all worksets from the list above in the active Revit project.\nExisting worksets are skipped automatically. Worksharing must be active."));
            var btnApply = ActionBtn(S.Get("settings.worksets.create_btn"), true, OnApplyWorksets);
            btnApply.Margin = new Thickness(0, 8, 0, 0);
            p.Children.Add(btnApply);

            // ── Project worksets (read-only view of what exists in the model) ──
            p.Children.Add(new Separator { Margin = new Thickness(0, 16, 0, 16), Background = MeToolsTheme.BrBorder });
            p.Children.Add(Sec(S.Get("settings.worksets.project")));
            p.Children.Add(InfoBox(S.Get("settings.worksets.project_hint")));

            _lbProjectWorksets = new ListBox
            {
                Height = 140, Margin = new Thickness(0, 8, 0, 0),
                Background = MeToolsTheme.BrInput, Foreground = MeToolsTheme.BrText,
                BorderBrush = MeToolsTheme.BrBorder, BorderThickness = new Thickness(1),
                FontSize = 11, Padding = new Thickness(2),
                IsEnabled = false,
            };
            p.Children.Add(_lbProjectWorksets);

            return p;
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

        private void LoadProjectWorksets()
        {
            if (_lbProjectWorksets == null) return;
            _lbProjectWorksets.Items.Clear();
            try
            {
                var doc = SettingsCommand.CurrentDocument;
                if (doc == null || !doc.IsWorkshared)
                {
                    _lbProjectWorksets.Items.Add("(no workshared project open)");
                    return;
                }
                var worksets = new Autodesk.Revit.DB.FilteredWorksetCollector(doc)
                    .OfKind(Autodesk.Revit.DB.WorksetKind.UserWorkset)
                    .ToWorksets()
                    .OrderBy(w => w.Name)
                    .ToList();
                if (worksets.Count == 0)
                { _lbProjectWorksets.Items.Add("(no user worksets found)"); return; }
                foreach (var w in worksets)
                    _lbProjectWorksets.Items.Add(w.Name);
            }
            catch (Exception ex)
            {
                _lbProjectWorksets.Items.Add("Error: " + ex.Message);
            }
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
                MessageBox.Show($"Saved {worksets.Count} workset(s).", "Worksets Saved", MessageBoxButton.OK, MessageBoxImage.None);
            }
            catch (Exception ex) { MessageBox.Show($"Could not save:\n{ex.Message}", "Save Failed", MessageBoxButton.OK, MessageBoxImage.Warning); }
        }

        private void OnApplyWorksets()
        {
            var doc = SettingsCommand.CurrentDocument;
            if (doc == null)
            { MessageBox.Show("No active Revit project found.", "Standard Worksets", MessageBoxButton.OK, MessageBoxImage.Warning); return; }

            if (!doc.IsWorkshared)
            { MessageBox.Show("Worksharing is not active in this project.\n\nEnable worksharing first:\nCollaborate → Enable Worksharing.", "Standard Worksets", MessageBoxButton.OK, MessageBoxImage.Warning); return; }

            var worksets = _lbWorksets.Items.Cast<object>()
                .Select(o => o?.ToString() ?? "").Where(s => !string.IsNullOrEmpty(s)).ToList();
            if (worksets.Count == 0)
            { MessageBox.Show("The workset list is empty. Add worksets first.", "Standard Worksets", MessageBoxButton.OK, MessageBoxImage.Information); return; }

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
            sb.AppendLine($"✓  {created} workset(s) created");
            sb.AppendLine($"–  {skipped} already present (skipped)");
            if (failed.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine($"⚠  {failed.Count} failed:");
                foreach (var f in failed) sb.AppendLine($"   • {f}");
            }
            MessageBox.Show(sb.ToString(), "Standard Worksets — Done", MessageBoxButton.OK, MessageBoxImage.None);
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
            p.Children.Add(Sec(S.Get("settings.heights.title")));
            p.Children.Add(InfoBox(
                "Per-family default height (Niveau, in mm) used by Family Placer.\n" +
                "Values are read from each family; edit a value to override it.\n" +
                "Leave a field blank to track the family's own default.\n" +
                "Overrides are saved for ME-Tools only -- the project is never changed."));

            _heightsHost = new StackPanel { Margin = new Thickness(0, 8, 0, 8) };
            var scroll = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                MaxHeight = 320,
                Content   = _heightsHost,
            };
            p.Children.Add(scroll);

            var btnRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0) };
            var btnRescan = FooterBtn(S.Get("settings.heights.rescan"), primary: false, onClick: () => { _heightsLoaded = false; LoadHeightsIntoList(); });
            var btnSave   = FooterBtn(S.Get("settings.heights.save"), primary: true, onClick: OnSaveHeights);
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
                _heightsHost.Children.Add(InfoBox("No active Revit document."));
                return;
            }

            _heightsHost.Children.Add(new TextBlock
            {
                Text       = "Scanning families, please wait...",
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
                    _heightsHost.Children.Add(InfoBox("No families with a height (Niveau) parameter were found."));
                    _heightsLoaded = true;
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
                    ? "Family default: " + en.DefaultMm.Value.ToString("0.###") + " mm"
                    : "No family default",
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
                MessageBox.Show("Saved " + map.Count + " height override(s).",
                                "Default Heights", MessageBoxButton.OK, MessageBoxImage.None);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Could not save:\n" + ex.Message,
                                "Save Failed", MessageBoxButton.OK, MessageBoxImage.Warning);
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

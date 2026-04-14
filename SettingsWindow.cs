// SettingsWindow.cs — ME-Tools Settings
// Mayer E-Concept SRL
using System;
using METools.Licensing;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using Color = System.Windows.Media.Color;

namespace METools
{
    /// <summary>
    /// Full settings dialog — accessible via the Settings button in the ribbon.
    /// Contains:
    ///   • Appearance  — Light / Dark theme toggle
    ///   • Language    — UI language selection (foundation for localisation)
    ///   • License     — Status display, key input, activation
    /// </summary>
    public class SettingsWindow : MeToolsWindowBase
    {
        // ── Section panels (shown/hidden by tabs) ────────────────────────
        private StackPanel _panAppearance;
        private StackPanel _panLanguage;
        private StackPanel _panLicense;

        // ── Tab buttons ──────────────────────────────────────────────────
        private Button _tabAppearance;
        private Button _tabLanguage;
        private Button _tabLicense;
        private int    _activeTab = 0;

        // ── License section controls ─────────────────────────────────────
        private TextBox   _tbKey;
        private TextBlock _lblStatus;
        private Button    _btnActivate;
        private Button    _btnDeactivate;

        // ── Theme section controls ────────────────────────────────────────
        private Button _btnDark;
        private Button _btnLight;

        // ── Language section ─────────────────────────────────────────────
        private ComboBox _cbLanguage;

        public SettingsWindow()
        {
            InitWindow("Settings", width: 420, isDialog: false);
            BuildContent();
            BuildStatusBar(LicenseManager.StatusText, "v1.0.0 beta");
        }

        // ── Build UI ─────────────────────────────────────────────────────
        private void BuildContent()
        {
            var outer = new StackPanel { Margin = new Thickness(0, 0, 0, 0) };
            DockPanel.SetDock(outer, Dock.Top);
            RootDock.Children.Insert(RootDock.Children.Count - 1, outer);

            // ── Tab bar ──────────────────────────────────────────────────
            outer.Children.Add(BuildTabBar());

            // ── Content area ─────────────────────────────────────────────
            var contentBorder = new Border
            {
                Padding    = new Thickness(20, 16, 20, 20),
                Background = MeToolsTheme.BrBg,
            };
            _panAppearance = BuildAppearancePanel();
            _panLanguage   = BuildLanguagePanel();
            _panLicense    = BuildLicensePanel();

            // Wrap all panels in a shared container
            var stack = new StackPanel();
            stack.Children.Add(_panAppearance);
            stack.Children.Add(_panLanguage);
            stack.Children.Add(_panLicense);
            contentBorder.Child = stack;
            outer.Children.Add(contentBorder);

            ShowTab(0);
        }

        // ── Tab bar ───────────────────────────────────────────────────────
        private UIElement BuildTabBar()
        {
            var bar = new Grid { Height = 40, Background = MeToolsTheme.BrPetrolDark };
            bar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            bar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            bar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            _tabAppearance = MakeTabBtn("Appearance", 0);
            _tabLanguage   = MakeTabBtn("Language",   1);
            _tabLicense    = MakeTabBtn("License",    2);

            Grid.SetColumn(_tabAppearance, 0);
            Grid.SetColumn(_tabLanguage,   1);
            Grid.SetColumn(_tabLicense,    2);

            bar.Children.Add(_tabAppearance);
            bar.Children.Add(_tabLanguage);
            bar.Children.Add(_tabLicense);
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
                Height          = 40,
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

            StyleTabBtn(_tabAppearance, idx == 0);
            StyleTabBtn(_tabLanguage,   idx == 1);
            StyleTabBtn(_tabLicense,    idx == 2);
        }

        private void StyleTabBtn(Button b, bool active)
        {
            if (b == null) return;
            b.Background = active
                ? new SolidColorBrush(MeToolsTheme.CBg)
                : Brushes.Transparent;
            b.Foreground = active
                ? MeToolsTheme.BrPetrol
                : new SolidColorBrush(Color.FromArgb(180, 255, 255, 255));
            b.FontWeight = active ? FontWeights.SemiBold : FontWeights.Normal;
        }

        // ── TAB 0: Appearance ─────────────────────────────────────────────
        private StackPanel BuildAppearancePanel()
        {
            var p = new StackPanel();

            p.Children.Add(Sec("Theme"));
            p.Children.Add(InfoBox("Switch between dark and light mode for all ME-Tools windows simultaneously."));

            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 16) };

            _btnDark  = ToggleBtn("🌙  Dark Mode",
                MeToolsTheme.Current == MeTheme.Dark, () => ApplyTheme(MeTheme.Dark));
            _btnLight = ToggleBtn("☀  Light Mode",
                MeToolsTheme.Current == MeTheme.Light, () => ApplyTheme(MeTheme.Light));

            _btnDark.Width  = 140;
            _btnLight.Width = 140;
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

            p.Children.Add(Sec("Language / Sprache"));
            p.Children.Add(InfoBox(
                "Set the display language for ME-Tools.\n" +
                "Full localisation will be implemented in a future update.\n" +
                "Stellt die Anzeigesprache für ME-Tools ein."));

            var row = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Margin      = new Thickness(0, 0, 0, 16),
                VerticalAlignment = VerticalAlignment.Center,
            };

            row.Children.Add(new TextBlock
            {
                Text              = "Language:",
                FontSize          = 12,
                Foreground        = MeToolsTheme.BrText,
                VerticalAlignment = VerticalAlignment.Center,
                Margin            = new Thickness(0, 0, 10, 0),
            });

            _cbLanguage = StyledCombo(28, 12);
            _cbLanguage.Width = 180;
            _cbLanguage.Items.Add("English");
            _cbLanguage.Items.Add("Deutsch");
            _cbLanguage.SelectedItem = SettingsStore.Language == "de" ? "Deutsch" : "English";
            _cbLanguage.SelectionChanged += (s, e) =>
            {
                SettingsStore.Language = _cbLanguage.SelectedItem?.ToString() == "Deutsch" ? "de" : "en";
            };

            row.Children.Add(_cbLanguage);
            p.Children.Add(row);

            p.Children.Add(new TextBlock
            {
                Text         = "Note: Language change takes effect after restarting Revit.",
                FontSize     = 10,
                Foreground   = MeToolsTheme.BrMuted,
                TextWrapping = TextWrapping.Wrap,
                Margin       = new Thickness(0, 0, 0, 8),
            });

            return p;
        }

        // ── TAB 2: License ────────────────────────────────────────────────
        private StackPanel BuildLicensePanel()
        {
            var p = new StackPanel { Visibility = Visibility.Collapsed };

            p.Children.Add(Sec("License Status"));
            p.Children.Add(BuildStatusBadge());

            p.Children.Add(new Border { Height = 12 });
            p.Children.Add(Sec("Activation"));

            p.Children.Add(new TextBlock
            {
                Text         = "Enter your license key below (format: METL-XXXX-XXXX-XXXX):",
                FontSize     = 11,
                Foreground   = MeToolsTheme.BrMuted,
                TextWrapping = TextWrapping.Wrap,
                Margin       = new Thickness(0, 0, 0, 8),
            });

            // Key input row
            var keyRow = new Grid { Margin = new Thickness(0, 0, 0, 10) };
            keyRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            keyRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8) });
            keyRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            _tbKey = new TextBox
            {
                Text       = LicenseManager.SavedKey,
                Height     = 32,
                FontSize   = 13,
                FontFamily = new FontFamily("Consolas"),
                Background = MeToolsTheme.BrInput,
                Foreground = MeToolsTheme.BrInputFg,
                BorderBrush      = MeToolsTheme.BrBorder,
                BorderThickness  = new Thickness(1),
                Padding          = new Thickness(8, 0, 8, 0),
                CaretBrush       = MeToolsTheme.BrText,
                VerticalContentAlignment = VerticalAlignment.Center,
                CharacterCasing  = CharacterCasing.Upper,
            };
            _tbKey.TextChanged += (s, e) => UpdateActivateButton();

            _btnActivate = FooterBtn("Activate", primary: true, onClick: OnActivate);
            _btnActivate.Height  = 32;
            _btnActivate.Padding = new Thickness(14, 0, 14, 0);

            Grid.SetColumn(_tbKey,       0);
            Grid.SetColumn(_btnActivate, 2);
            keyRow.Children.Add(_tbKey);
            keyRow.Children.Add(_btnActivate);
            p.Children.Add(keyRow);

            // Deactivate button
            _btnDeactivate = FooterBtn("Remove Key", primary: false, onClick: OnDeactivate);
            _btnDeactivate.Margin     = new Thickness(0, 0, 0, 16);
            _btnDeactivate.Visibility = LicenseManager.IsLicensed()
                ? Visibility.Visible : Visibility.Collapsed;
            p.Children.Add(_btnDeactivate);

            // Contact
            var contactRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 0) };
            contactRow.Children.Add(new TextBlock
            {
                Text       = "Need a license?  Contact: ",
                FontSize   = 10,
                Foreground = MeToolsTheme.BrMuted,
                VerticalAlignment = VerticalAlignment.Center,
            });
            var mailLink = new TextBlock
            {
                Text                = "office@mayer-econcept.ro",
                FontSize            = 10,
                Foreground          = MeToolsTheme.BrPetrol,
                Cursor              = Cursors.Hand,
                TextDecorations     = TextDecorations.Underline,
                VerticalAlignment   = VerticalAlignment.Center,
            };
            mailLink.MouseLeftButtonDown += (s, e) =>
            {
                try { System.Diagnostics.Process.Start("mailto:office@mayer-econcept.ro"); } catch { }
            };
            contactRow.Children.Add(mailLink);
            p.Children.Add(contactRow);

            UpdateActivateButton();
            return p;
        }

        private UIElement BuildStatusBadge()
        {
            _lblStatus = new TextBlock
            {
                FontSize   = 13,
                FontWeight = FontWeights.SemiBold,
                Margin     = new Thickness(0, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center,
            };
            RefreshStatusLabel();

            var badge = new Border
            {
                CornerRadius = new CornerRadius(6),
                Padding      = new Thickness(14, 8, 14, 8),
                Margin       = new Thickness(0, 0, 0, 0),
                HorizontalAlignment = HorizontalAlignment.Left,
            };

            var row = new StackPanel { Orientation = Orientation.Horizontal };
            var dot = new Ellipse { Width = 10, Height = 10, Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center };

            bool licensed = LicenseManager.IsLicensed();
            bool expired  = LicenseManager.IsTrialExpired;

            if (licensed)
            {
                badge.Background = new SolidColorBrush(Color.FromRgb(0x1D, 0x6A, 0x40));
                dot.Fill = new SolidColorBrush(Color.FromRgb(0x5D, 0xCA, 0xA5));
                _lblStatus.Foreground = Brushes.White;
            }
            else if (expired)
            {
                badge.Background = new SolidColorBrush(Color.FromRgb(0x80, 0x20, 0x20));
                dot.Fill = new SolidColorBrush(Color.FromRgb(0xFF, 0x70, 0x70));
                _lblStatus.Foreground = Brushes.White;
            }
            else
            {
                badge.Background = new SolidColorBrush(Color.FromRgb(0x7A, 0x50, 0x10));
                dot.Fill = new SolidColorBrush(Color.FromRgb(0xFF, 0xC0, 0x50));
                _lblStatus.Foreground = Brushes.White;
            }

            row.Children.Add(dot);
            row.Children.Add(_lblStatus);
            badge.Child = row;
            return badge;
        }

        private void RefreshStatusLabel()
        {
            if (_lblStatus == null) return;
            _lblStatus.Text = LicenseManager.StatusText;
            if (StatusLeft != null)
                StatusLeft.Text = LicenseManager.StatusText;
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
            if (ok)
            {
                MessageBox.Show(
                    "License activated successfully!\n\nThank you for using ME-Tools.",
                    "Activation Successful",
                    MessageBoxButton.OK, MessageBoxImage.None);
                RefreshStatusLabel();
                if (_btnDeactivate != null)
                    _btnDeactivate.Visibility = Visibility.Visible;
                UpdateActivateButton();
            }
            else
            {
                MessageBox.Show(
                    "The license key could not be validated.\n\n" +
                    "Please check the key and try again, or contact\n" +
                    "office@mayer-econcept.ro for assistance.",
                    "Activation Failed",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void OnDeactivate()
        {
            var result = MessageBox.Show(
                "Remove the saved license key?\nYou can re-enter it at any time.",
                "Remove License",
                MessageBoxButton.YesNo, MessageBoxImage.Question);

            if (result == MessageBoxResult.Yes)
            {
                LicenseManager.Deactivate();
                if (_tbKey != null) _tbKey.Text = "";
                if (_btnDeactivate != null)
                    _btnDeactivate.Visibility = Visibility.Collapsed;
                RefreshStatusLabel();
                UpdateActivateButton();
            }
        }

        // ── Theme change callback ─────────────────────────────────────────
        protected override void OnThemeChanged()
        {
            base.OnThemeChanged();

            // Re-style tab bar background
            ShowTab(_activeTab);

            // Update combo style
            if (_cbLanguage != null) ApplyComboStyle(_cbLanguage);

            // Update key input
            if (_tbKey != null)
            {
                _tbKey.Background  = MeToolsTheme.BrInput;
                _tbKey.Foreground  = MeToolsTheme.BrInputFg;
                _tbKey.BorderBrush = MeToolsTheme.BrBorder;
                _tbKey.CaretBrush  = MeToolsTheme.BrText;
            }
        }
    }

    // ── Simple settings store ─────────────────────────────────────────────

    internal static class SettingsStore
    {
        private static readonly string File = System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "METools", "settings.ini");

        private static string _language;

        public static string Language
        {
            get
            {
                if (_language != null) return _language;
                try
                {
                    if (System.IO.File.Exists(File))
                    {
                        foreach (var line in System.IO.File.ReadAllLines(File))
                        {
                            if (line.StartsWith("language="))
                                return _language = line.Substring(9).Trim();
                        }
                    }
                }
                catch { }
                return _language = "en";
            }
            set
            {
                _language = value;
                try
                {
                    var dir = System.IO.Path.GetDirectoryName(File);
                    System.IO.Directory.CreateDirectory(dir);
                    System.IO.File.WriteAllText(File, $"language={value}\n");
                }
                catch { }
            }
        }
    }
}

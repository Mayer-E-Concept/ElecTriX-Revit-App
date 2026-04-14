// LicenseWindow.cs — ME-Tools | License Activation
// Mayer E-Concept SRL
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using METools.Licensing;

using Grid       = System.Windows.Controls.Grid;
using TextBox    = System.Windows.Controls.TextBox;
using Visibility = System.Windows.Visibility;

namespace METools
{
    public class LicenseWindow : MeToolsWindowBase
    {
        TextBox   _codeTb;
        TextBlock _statusTb;
        Button    _activateBtn;

        public bool Activated { get; private set; } = false;

        public LicenseWindow()
        {
            InitWindow("ME-Tools — Activation Required", 460);
            Build();
        }

        void Build()
        {
            var status = LicenseManager.GetStatus();
            string headerText = status == LicenseStatus.LicenseExpired
                ? "Your ME-Tools license has expired."
                : "Your ME-Tools beta period has ended.";

            BuildStatusBar("License required to continue");

            var body = new StackPanel { Margin = new Thickness(24, 20, 24, 20) };

            // Header
            body.Children.Add(new TextBlock
            {
                Text         = headerText,
                FontSize     = 15,
                FontWeight   = FontWeights.SemiBold,
                Foreground   = MeToolsTheme.BrText,
                Margin       = new Thickness(0, 0, 0, 6),
                TextWrapping = TextWrapping.Wrap
            });

            body.Children.Add(new TextBlock
            {
                Text         = "To continue using ME-Tools, choose a license option below or enter an activation code received from Mayer E-Concept SRL.",
                FontSize     = 12,
                Foreground   = MeToolsTheme.BrMuted,
                TextWrapping = TextWrapping.Wrap,
                Margin       = new Thickness(0, 0, 0, 20)
            });

            // ── License options ──────────────────────────────────────────────
            body.Children.Add(new TextBlock
            {
                Text       = "LICENSE OPTIONS",
                FontSize   = 10,
                FontWeight = FontWeights.SemiBold,
                Foreground = MeToolsTheme.BrMuted,
                Margin     = new Thickness(0, 0, 0, 8)
            });

            // Two option cards side by side
            var optGrid = new Grid { Margin = new Thickness(0, 0, 0, 20) };
            optGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            optGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(10) });
            optGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var card30  = BuildOptionCard("30-Day Extension",
                "30 Tage verlängern",
                "Ideal for short-term projects.\nActivated immediately with code.",
                false);
            var card1y  = BuildOptionCard("1-Year License",
                "1 Jahr Lizenz",
                "Full access for 12 months.\nBest value for regular users.",
                true);

            Grid.SetColumn(card30, 0);
            Grid.SetColumn(card1y, 2);
            optGrid.Children.Add(card30);
            optGrid.Children.Add(card1y);
            body.Children.Add(optGrid);

            // Contact info
            body.Children.Add(new Border
            {
                Background      = new SolidColorBrush(Color.FromRgb(20, 35, 65)),
                BorderBrush     = MeToolsTheme.BrBorder,
                BorderThickness = new Thickness(1),
                CornerRadius    = new CornerRadius(8),
                Padding         = new Thickness(14, 10, 14, 10),
                Margin          = new Thickness(0, 0, 0, 20),
                Child           = new TextBlock
                {
                    Text         = "Contact: info@mayer-econcept.ro  ·  Mayer E-Concept SRL\nSend your Machine ID and desired license option.",
                    FontSize     = 11,
                    Foreground   = MeToolsTheme.BrMuted,
                    TextWrapping = TextWrapping.Wrap
                }
            });

            // ── Machine ID ───────────────────────────────────────────────────
            body.Children.Add(new TextBlock
            {
                Text   = "YOUR MACHINE ID",
                FontSize   = 10,
                FontWeight = FontWeights.SemiBold,
                Foreground = MeToolsTheme.BrMuted,
                Margin = new Thickness(0, 0, 0, 6)
            });

            var idRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 20) };
            var idTb  = new TextBox
            {
                Text            = LicenseManager.GetMachineId(),
                IsReadOnly      = true,
                FontFamily      = new FontFamily("Consolas"),
                FontSize        = 14,
                FontWeight      = FontWeights.Bold,
                Background      = MeToolsTheme.BrSurface,
                Foreground      = MeToolsTheme.BrText,
                BorderBrush     = MeToolsTheme.BrBorder,
                BorderThickness = new Thickness(1),
                Padding         = new Thickness(10, 6, 10, 6),
                Width           = 240
            };
            var copyBtn = new Button
            {
                Content         = "Copy",
                Margin          = new Thickness(8, 0, 0, 0),
                Padding         = new Thickness(14, 6, 14, 6),
                Background      = MeToolsTheme.BrSurface,
                Foreground      = MeToolsTheme.BrText,
                BorderBrush     = MeToolsTheme.BrBorder,
                FontSize        = 12,
                VerticalAlignment = VerticalAlignment.Center
            };
            copyBtn.Click += (s, e) =>
            {
                System.Windows.Clipboard.SetText(idTb.Text);
                copyBtn.Content = "✓ Copied";
            };
            idRow.Children.Add(idTb);
            idRow.Children.Add(copyBtn);
            body.Children.Add(idRow);

            // ── Activation code entry ────────────────────────────────────────
            body.Children.Add(new TextBlock
            {
                Text       = "ACTIVATION CODE",
                FontSize   = 10,
                FontWeight = FontWeights.SemiBold,
                Foreground = MeToolsTheme.BrMuted,
                Margin     = new Thickness(0, 0, 0, 6)
            });

            _codeTb = new TextBox
            {
                FontFamily      = new FontFamily("Consolas"),
                FontSize        = 13,
                Background      = MeToolsTheme.BrInput,
                Foreground      = MeToolsTheme.BrText,
                BorderBrush     = MeToolsTheme.BrBorder,
                BorderThickness = new Thickness(1),
                Padding         = new Thickness(10, 8, 10, 8),
                Margin          = new Thickness(0, 0, 0, 8),
                Height          = 38
            };
            body.Children.Add(_codeTb);

            _statusTb = new TextBlock
            {
                FontSize     = 11,
                Margin       = new Thickness(0, 0, 0, 10),
                TextWrapping = TextWrapping.Wrap,
                Visibility   = Visibility.Collapsed
            };
            body.Children.Add(_statusTb);

            _activateBtn = ActionBtn("Activate ME-Tools", true, Activate);
            _activateBtn.HorizontalAlignment = HorizontalAlignment.Stretch;
            _activateBtn.Height = 40;
            body.Children.Add(_activateBtn);

            RootDock.Children.Add(body);
        }

        private Border BuildOptionCard(string title, string titleDe, string desc, bool highlight)
        {
            var accent = highlight
                ? Color.FromRgb(74, 158, 255)
                : Color.FromRgb(80, 160, 120);

            var card = new Border
            {
                Background      = new SolidColorBrush(Color.FromRgb(16, 28, 55)),
                BorderBrush     = new SolidColorBrush(highlight ? accent : Color.FromRgb(30, 50, 90)),
                BorderThickness = new Thickness(highlight ? 2 : 1),
                CornerRadius    = new CornerRadius(10),
                Padding         = new Thickness(16, 14, 16, 14)
            };

            var inner = new StackPanel();

            if (highlight)
            {
                inner.Children.Add(new Border
                {
                    Background      = new SolidColorBrush(Color.FromArgb(60, accent.R, accent.G, accent.B)),
                    BorderBrush     = new SolidColorBrush(accent),
                    BorderThickness = new Thickness(1),
                    CornerRadius    = new CornerRadius(4),
                    Padding         = new Thickness(6, 2, 6, 2),
                    HorizontalAlignment = HorizontalAlignment.Left,
                    Margin          = new Thickness(0, 0, 0, 8),
                    Child           = new TextBlock
                    {
                        Text       = "RECOMMENDED",
                        FontSize   = 9,
                        FontWeight = FontWeights.Bold,
                        Foreground = new SolidColorBrush(accent)
                    }
                });
            }

            inner.Children.Add(new TextBlock
            {
                Text       = title,
                FontSize   = 14,
                FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Color.FromRgb(255, 255, 255)),
                Margin     = new Thickness(0, 0, 0, 2)
            });

            inner.Children.Add(new TextBlock
            {
                Text       = titleDe,
                FontSize   = 11,
                Foreground = new SolidColorBrush(accent),
                Margin     = new Thickness(0, 0, 0, 8)
            });

            inner.Children.Add(new TextBlock
            {
                Text         = desc,
                FontSize     = 11,
                Foreground   = new SolidColorBrush(Color.FromRgb(140, 160, 200)),
                TextWrapping = TextWrapping.Wrap
            });

            card.Child = inner;
            return card;
        }

        void Activate()
        {
            string code = _codeTb.Text?.Trim() ?? "";
            if (string.IsNullOrEmpty(code))
            {
                ShowStatus("Please enter the activation code.", false);
                return;
            }

            var licType = LicenseManager.Activate(code);
            if (licType != LicenseType.None)
            {
                Activated = true;
                string msg = licType switch
                {
                    LicenseType.Permanent => "✓  Activated — Full permanent license.",
                    LicenseType.Year1     => "✓  Activated — 1-year license.",
                    LicenseType.Extend30  => "✓  Activated — 30-day extension.",
                    _                     => "✓  Activation successful."
                };
                ShowStatus(msg, true);
                _activateBtn.IsEnabled = false;
                _codeTb.IsReadOnly     = true;

                var timer = new System.Windows.Threading.DispatcherTimer
                    { Interval = System.TimeSpan.FromSeconds(2) };
                timer.Tick += (s, e) => { timer.Stop(); Close(); };
                timer.Start();
            }
            else
            {
                ShowStatus("✗  Invalid activation code. Please check and try again.", false);
            }
        }

        void ShowStatus(string message, bool success)
        {
            _statusTb.Text       = message;
            _statusTb.Foreground = success
                ? new SolidColorBrush(Color.FromRgb(80, 200, 120))
                : new SolidColorBrush(Color.FromRgb(220, 80, 80));
            _statusTb.Visibility = Visibility.Visible;
        }

        protected override void OnThemeChanged() { }
    }
}

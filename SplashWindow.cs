// SplashWindow.cs - ME-Tools Startup Splash
// Mayer E-Concept SRL
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using Color = System.Windows.Media.Color;

namespace METools
{
    /// <summary>
    /// Startup splash shown by SplashGate on first install, when
    /// trial drops to ≤ 5 days, or when trial has expired.
    /// Version is resolved live from the embedded setup.iss — never hardcoded.
    /// </summary>
    public class SplashWindow : MeToolsWindowBase
    {
        public SplashWindow()
        {
            InitWindow("ME-Tools", width: 360, isDialog: true);
            ResizeMode = ResizeMode.NoResize;
            BuildContent();
        }

        private void BuildContent()
        {
            var panel = new StackPanel
            {
                Margin = new Thickness(30, 20, 30, 24),
                HorizontalAlignment = HorizontalAlignment.Center,
            };
            RootDock.Children.Add(panel);

            // Logo
            panel.Children.Add(new System.Windows.Controls.Image
            {
                Source = MeToolsTheme.LoadLogo(),
                Width  = 64, Height = 64,
                Margin = new Thickness(0, 8, 0, 12),
                HorizontalAlignment = HorizontalAlignment.Center,
            });

            // Title
            panel.Children.Add(new TextBlock
            {
                Text       = "ME-Tools",
                FontSize   = 24, FontWeight = FontWeights.Bold, FontStyle = FontStyles.Italic,
                Foreground = MeToolsTheme.BrPetrol,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin     = new Thickness(0, 0, 0, 2),
            });
            panel.Children.Add(new TextBlock
            {
                Text       = "for Autodesk Revit 2025",
                FontSize   = 12, Foreground = MeToolsTheme.BrMuted,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin     = new Thickness(0, 0, 0, 20),
            });

            // Divider
            panel.Children.Add(new Border
            {
                Height = 1, Background = MeToolsTheme.BrSecLine,
                Margin = new Thickness(0, 0, 0, 16),
            });

            // License badge
            panel.Children.Add(BuildLicenseBadge());

            // Version — single source of truth: setup.iss (via SplashGate)
            panel.Children.Add(new TextBlock
            {
                Text = $"v{SplashGate.GetVersion()}  ·  Mayer E-Concept SRL",
                FontSize = 10, Foreground = MeToolsTheme.BrMuted,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 12, 0, 0),
            });

            // Divider
            panel.Children.Add(new Border
            {
                Height = 1, Background = MeToolsTheme.BrSecLine,
                Margin = new Thickness(0, 16, 0, 16),
            });

            // OK button
            var okBtn = FooterBtn("OK  ·  Continue", primary: true, onClick: () =>
            {
                try { DialogResult = true; } catch { }
                Close();
            });
            okBtn.Height = 38;
            panel.Children.Add(okBtn);

            // Settings hint for trial users
            if (!LicenseManager.IsLicensed())
            {
                panel.Children.Add(new TextBlock
                {
                    Text = "To enter a license key → Settings in the ribbon",
                    FontSize = 10, Foreground = MeToolsTheme.BrMuted,
                    TextWrapping = TextWrapping.Wrap,
                    TextAlignment = TextAlignment.Center,
                    Margin = new Thickness(0, 8, 0, 0),
                });
            }
        }

        private UIElement BuildLicenseBadge()
        {
            bool licensed = LicenseManager.IsLicensed();
            bool expired  = LicenseManager.IsTrialExpired;
            int  days     = LicenseManager.DaysRemaining;

            Color bg, dot; string label;

            if (licensed)
            {
                bg    = Color.FromRgb(0x1D, 0x6A, 0x40);
                dot   = Color.FromRgb(0x5D, 0xCA, 0xA5);
                label = "[v]  Licensed";
            }
            else if (expired)
            {
                bg    = Color.FromRgb(0x80, 0x20, 0x20);
                dot   = Color.FromRgb(0xFF, 0x70, 0x70);
                label = "Trial expired — please activate";
            }
            else
            {
                bg    = Color.FromRgb(0x7A, 0x50, 0x10);
                dot   = Color.FromRgb(0xFF, 0xC0, 0x50);
                label = $"Beta access — {days} day{(days == 1 ? "" : "s")} remaining";
            }

            var badge = new Border
            {
                Background  = new SolidColorBrush(bg),
                CornerRadius = new CornerRadius(20),
                Padding      = new Thickness(18, 8, 18, 8),
                HorizontalAlignment = HorizontalAlignment.Center,
            };
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            row.Children.Add(new Ellipse
            {
                Width = 8, Height = 8,
                Fill  = new SolidColorBrush(dot),
                Margin = new Thickness(0, 0, 8, 0),
                VerticalAlignment = VerticalAlignment.Center,
            });
            row.Children.Add(new TextBlock
            {
                Text       = label, FontSize = 12, FontWeight = FontWeights.SemiBold,
                Foreground = Brushes.White,
                VerticalAlignment = VerticalAlignment.Center,
            });
            badge.Child = row;
            return badge;
        }
    }
}

// SplashWindow.cs — ME-Tools | Startup Splash Screen
// Mayer E-Concept SRL
// Shown once per Revit session at startup.
// Displays logo, product name, beta status, company info.
// Auto-closes after 4 seconds or on click.

using System;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using METools.Licensing;

namespace METools
{
    public class SplashWindow : Window
    {
        private DispatcherTimer _timer;

        public SplashWindow()
        {
            // Window properties — no chrome, centered
            WindowStyle        = WindowStyle.None;
            AllowsTransparency = true;
            Background         = Brushes.Transparent;
            ResizeMode         = ResizeMode.NoResize;
            Topmost            = true;
            SizeToContent      = SizeToContent.WidthAndHeight;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            ShowInTaskbar      = false;

            MouseDown += (s, e) => Close();

            BuildContent();
            StartTimer();
        }

        private void BuildContent()
        {
            // Outer container with rounded corners and shadow
            var border = new Border
            {
                Width           = 420,
                Height          = 320,
                Background      = new SolidColorBrush(Color.FromRgb(14, 25, 50)),
                CornerRadius    = new CornerRadius(16),
                Effect          = new System.Windows.Media.Effects.DropShadowEffect
                {
                    Color     = Colors.Black,
                    BlurRadius = 30,
                    ShadowDepth = 6,
                    Opacity   = 0.7
                }
            };

            var grid = new Grid();
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            // ── Main content ─────────────────────────────────────────────────
            var stack = new StackPanel
            {
                VerticalAlignment   = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin              = new Thickness(40, 36, 40, 20)
            };

            // Logo image
            var logoImg = LoadLogoImage();
            if (logoImg != null)
            {
                var img = new System.Windows.Controls.Image
                {
                    Source  = logoImg,
                    Width   = 80,
                    Height  = 80,
                    Margin  = new Thickness(0, 0, 0, 16),
                    HorizontalAlignment = HorizontalAlignment.Center
                };
                stack.Children.Add(img);
            }

            // Product name
            stack.Children.Add(new TextBlock
            {
                Text                = "ME-Tools",
                FontSize            = 32,
                FontWeight          = FontWeights.Bold,
                Foreground          = new SolidColorBrush(Color.FromRgb(255, 255, 255)),
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin              = new Thickness(0, 0, 0, 4)
            });

            // Subtitle
            stack.Children.Add(new TextBlock
            {
                Text                = "for Autodesk Revit",
                FontSize            = 13,
                Foreground          = new SolidColorBrush(Color.FromRgb(120, 160, 220)),
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin              = new Thickness(0, 0, 0, 20)
            });

            // Divider
            stack.Children.Add(new Border
            {
                Height     = 1,
                Background = new SolidColorBrush(Color.FromRgb(40, 60, 100)),
                Margin     = new Thickness(0, 0, 0, 20)
            });

            // Beta / license status badge
            var status   = LicenseManager.GetStatus();
            var licType  = LicenseManager.CurrentLicenseType();
            string badgeText;
            Color  badgeColor;

            if (status == LicenseStatus.Licensed && licType == LicenseType.Permanent)
            {
                badgeText  = "✓  Licensed — Full Version";
                badgeColor = Color.FromRgb(60, 180, 100);
            }
            else if (status == LicenseStatus.Licensed && licType == LicenseType.Year1)
            {
                int days   = LicenseManager.LicenseDaysRemaining();
                badgeText  = $"✓  1-Year License — {days} days remaining";
                badgeColor = Color.FromRgb(60, 180, 100);
            }
            else if (status == LicenseStatus.Licensed && licType == LicenseType.Extend30)
            {
                int days   = LicenseManager.LicenseDaysRemaining();
                badgeText  = $"✓  Extended — {days} days remaining";
                badgeColor = Color.FromRgb(80, 160, 220);
            }
            else if (status == LicenseStatus.TrialActive)
            {
                int days   = LicenseManager.TrialDaysRemaining();
                badgeText  = $"Beta-Zugang  ·  {days} Tage verbleibend";
                badgeColor = Color.FromRgb(240, 160, 40);
            }
            else
            {
                badgeText  = "Beta abgelaufen — Lizenz erforderlich";
                badgeColor = Color.FromRgb(220, 80, 60);
            }

            var badgeBorder = new Border
            {
                Background          = new SolidColorBrush(Color.FromArgb(40,
                    badgeColor.R, badgeColor.G, badgeColor.B)),
                BorderBrush         = new SolidColorBrush(badgeColor),
                BorderThickness     = new Thickness(1),
                CornerRadius        = new CornerRadius(6),
                Padding             = new Thickness(14, 7, 14, 7),
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin              = new Thickness(0, 0, 0, 0)
            };
            badgeBorder.Child = new TextBlock
            {
                Text       = badgeText,
                FontSize   = 12,
                FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(badgeColor)
            };
            stack.Children.Add(badgeBorder);

            Grid.SetRow(stack, 0);
            grid.Children.Add(stack);

            // ── Footer ───────────────────────────────────────────────────────
            var footer = new Border
            {
                Background      = new SolidColorBrush(Color.FromRgb(10, 18, 38)),
                CornerRadius    = new CornerRadius(0, 0, 16, 16),
                Padding         = new Thickness(24, 10, 24, 10)
            };

            var footerRow = new Grid();
            footerRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            footerRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            footerRow.Children.Add(new TextBlock
            {
                Text       = "Mayer E-Concept SRL",
                FontSize   = 11,
                Foreground = new SolidColorBrush(Color.FromRgb(80, 110, 160)),
                VerticalAlignment = VerticalAlignment.Center
            });

            var versionTb = new TextBlock
            {
                Text       = "v1.0.0 beta",
                FontSize   = 11,
                Foreground = new SolidColorBrush(Color.FromRgb(60, 80, 120)),
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(versionTb, 1);
            footerRow.Children.Add(versionTb);

            footer.Child = footerRow;
            Grid.SetRow(footer, 1);
            grid.Children.Add(footer);

            border.Child = grid;
            Content = border;

            // Fade-in animation
            Opacity = 0;
            Loaded += (s, e) =>
            {
                var fadeIn = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(300));
                BeginAnimation(OpacityProperty, fadeIn);
            };
        }

        private void StartTimer()
        {
            // Auto-close after 4 seconds with fade-out
            _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(4) };
            _timer.Tick += (s, e) =>
            {
                _timer.Stop();
                FadeOutAndClose();
            };
            _timer.Start();
        }

        private void FadeOutAndClose()
        {
            var fadeOut = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(400));
            fadeOut.Completed += (s, e) => Close();
            BeginAnimation(OpacityProperty, fadeOut);
        }

        private ImageSource LoadLogoImage()
        {
            try
            {
                // Try base icon first, then lamp icon
                foreach (var name in new[] { "base_icon_transparent_background.png", "icon_lamp_32.png" })
                {
                    var stream = Assembly.GetExecutingAssembly()
                        .GetManifestResourceStream($"METools.Icons.{name}");
                    if (stream == null) continue;
                    var bmp = new BitmapImage();
                    bmp.BeginInit();
                    bmp.StreamSource  = stream;
                    bmp.CacheOption   = BitmapCacheOption.OnLoad;
                    bmp.DecodePixelWidth = 80;
                    bmp.EndInit();
                    bmp.Freeze();
                    return bmp;
                }
            }
            catch { }
            return null;
        }

        // Click anywhere to close immediately
        protected override void OnMouseDown(MouseButtonEventArgs e)
        {
            _timer?.Stop();
            FadeOutAndClose();
        }
    }
}

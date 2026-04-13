// LicenseWindow.cs — ME-Tools | License Activation Window
// Mayer E-Concept SRL
// Shown when beta period expires. Customer enters activation code here.

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
            InitWindow("ME-Tools — Activation", 420);
            Build();
        }

        void Build()
        {
            BuildStatusBar("License required");

            var body = new StackPanel { Margin = new Thickness(20, 16, 20, 16) };

            // Header
            body.Children.Add(new TextBlock
            {
                Text       = "ME-Tools — Beta Expired",
                FontSize   = 16,
                FontWeight = FontWeights.SemiBold,
                Foreground = MeToolsTheme.BrText,
                Margin     = new Thickness(0, 0, 0, 8)
            });

            body.Children.Add(new TextBlock
            {
                Text         = "Your beta period has ended. To continue using ME-Tools, " +
                               "please send your Machine ID to Mayer E-Concept and enter " +
                               "the activation code you receive.",
                FontSize     = 12,
                Foreground   = MeToolsTheme.BrMuted,
                TextWrapping = TextWrapping.Wrap,
                Margin       = new Thickness(0, 0, 0, 20)
            });

            // Machine ID section
            body.Children.Add(Lbl("Your Machine ID  (send this to Mayer E-Concept):"));
            var machineRow = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Margin      = new Thickness(0, 4, 0, 20)
            };
            var machineIdTb = new TextBox
            {
                Text              = LicenseManager.GetMachineId(),
                IsReadOnly        = true,
                FontFamily        = new FontFamily("Consolas"),
                FontSize          = 14,
                FontWeight        = FontWeights.Bold,
                Background        = MeToolsTheme.BrSurface,
                Foreground        = MeToolsTheme.BrText,
                BorderBrush       = MeToolsTheme.BrBorder,
                BorderThickness   = new Thickness(1),
                Padding           = new Thickness(8, 4, 8, 4),
                Width             = 230,
                VerticalAlignment = VerticalAlignment.Center
            };
            var copyBtn = new Button
            {
                Content           = "Copy",
                Margin            = new Thickness(8, 0, 0, 0),
                Padding           = new Thickness(12, 4, 12, 4),
                VerticalAlignment = VerticalAlignment.Center,
                Background        = MeToolsTheme.BrSurface,
                Foreground        = MeToolsTheme.BrText,
                BorderBrush       = MeToolsTheme.BrBorder,
                FontSize          = 12
            };
            copyBtn.Click += (s, e) =>
            {
                System.Windows.Clipboard.SetText(machineIdTb.Text);
                copyBtn.Content = "✓ Copied";
            };
            machineRow.Children.Add(machineIdTb);
            machineRow.Children.Add(copyBtn);
            body.Children.Add(machineRow);

            // Activation code entry
            body.Children.Add(Lbl("Activation Code:"));
            _codeTb = new TextBox
            {
                FontFamily      = new FontFamily("Consolas"),
                FontSize        = 13,
                Background      = MeToolsTheme.BrInput,
                Foreground      = MeToolsTheme.BrText,
                BorderBrush     = MeToolsTheme.BrBorder,
                BorderThickness = new Thickness(1),
                Padding         = new Thickness(8, 6, 8, 6),
                Margin          = new Thickness(0, 4, 0, 8),
                Height          = 36
            };
            body.Children.Add(_codeTb);

            // Status text
            _statusTb = new TextBlock
            {
                FontSize     = 11,
                Margin       = new Thickness(0, 0, 0, 12),
                TextWrapping = TextWrapping.Wrap,
                Visibility   = Visibility.Collapsed
            };
            body.Children.Add(_statusTb);

            // Activate button
            _activateBtn = ActionBtn("Activate ME-Tools", true, Activate);
            _activateBtn.HorizontalAlignment = HorizontalAlignment.Stretch;
            _activateBtn.Height = 38;
            body.Children.Add(_activateBtn);

            // Contact info
            body.Children.Add(new TextBlock
            {
                Text         = "\nContact: info@mayer-econcept.ro\nMayer E-Concept SRL",
                FontSize     = 10,
                Foreground   = MeToolsTheme.BrMuted,
                TextWrapping = TextWrapping.Wrap,
                Margin       = new Thickness(0, 12, 0, 0)
            });

            RootDock.Children.Add(body);
        }

        void Activate()
        {
            string code = _codeTb.Text?.Trim() ?? "";
            if (string.IsNullOrEmpty(code))
            {
                ShowStatus("Please enter the activation code.", false);
                return;
            }

            if (LicenseManager.Activate(code))
            {
                Activated = true;
                ShowStatus("✓  Activation successful! ME-Tools is now licensed.", true);
                _activateBtn.IsEnabled = false;
                _codeTb.IsReadOnly = true;

                // Close after short delay
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
                ? new SolidColorBrush(System.Windows.Media.Color.FromRgb(80, 200, 120))
                : new SolidColorBrush(System.Windows.Media.Color.FromRgb(220, 80, 80));
            _statusTb.Visibility = Visibility.Visible;
        }

        TextBlock Lbl(string text) => new TextBlock
        {
            Text       = text,
            FontSize   = 11,
            Foreground = MeToolsTheme.BrText
        };

        protected override void OnThemeChanged() { }
    }
}

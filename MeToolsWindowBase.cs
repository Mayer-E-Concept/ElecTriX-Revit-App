// MeToolsWindowBase.cs — EINZIGE Datei für alle Fenster-Darstellung
// Mayer E-Concept SRL — Hier ändern = überall gleich
using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using Color = System.Windows.Media.Color;
using Image = System.Windows.Controls.Image;

namespace METools
{
    public class MeToolsWindowBase : Window
    {
        // ── Öffentliche UI-Refs ───────────────────────────────────────────
        protected DockPanel  RootDock;
        protected Grid       StatusBarGrid;
        protected TextBlock  StatusLeft;
        protected TextBlock  StatusRight;

        // Private
        private Action      _themeHandler;
        private bool        _isDialog;
        private Border      _outerBorder;

        // Revit main window handle (set by commands) -> keeps windows above Revit.
        public static System.IntPtr RevitHandle = System.IntPtr.Zero;

        // ── Fenster initialisieren ────────────────────────────────────────
        protected void InitWindow(string title, double width = 480, bool isDialog = false)
        {
            _isDialog             = isDialog;
            Width                 = width;
            SizeToContent         = SizeToContent.Height;
            WindowStyle           = WindowStyle.None;
            AllowsTransparency    = false;
            ResizeMode            = ResizeMode.CanResizeWithGrip;
            WindowStartupLocation = WindowStartupLocation.Manual;
            var _wa = System.Windows.SystemParameters.WorkArea;
            Left = _wa.Right - Width - 24;
            Top  = _wa.Top + 40;
            Loaded += (s, e) =>
            {
                var wa = System.Windows.SystemParameters.WorkArea;
                Left = wa.Right - ActualWidth - 24;
                Top  = wa.Top + System.Math.Max(0, (wa.Height - ActualHeight) / 2);

                // Freeze the content-derived height and hand full control back to the
                // user. Leaving SizeToContent=Height active together with
                // ResizeMode.CanResizeWithGrip is what caused the resize-grip glitch /
                // snap-to-right-edge bug: WPF's auto-size engine kept re-solving the
                // window bounds against this Loaded-time right-anchor math on every
                // resize pass. Locking Height + switching to Manual stops that fight.
                Height        = ActualHeight;
                SizeToContent = SizeToContent.Manual;
            };
            FontFamily            = new FontFamily("Segoe UI");
            FontSize              = 12;

            // Kein weißer Rand: WindowChrome entfernt + Background = Titelleiste
            Background = new SolidColorBrush(MeToolsTheme.CPetrolDark);
            var chrome = new System.Windows.Shell.WindowChrome
            {
                CaptionHeight         = 0,
                ResizeBorderThickness = new Thickness(4),
                GlassFrameThickness   = new Thickness(0),
                UseAeroCaptionButtons = false,
            };
            System.Windows.Shell.WindowChrome.SetWindowChrome(this, chrome);

            // Äußerer Container — abgerundete Ecken
            _outerBorder = new Border
            {
                CornerRadius = new CornerRadius(8),
                ClipToBounds = true,
                Background   = MeToolsTheme.BrBg,
            };
            RootDock = new DockPanel { LastChildFill = true };
            _outerBorder.Child = RootDock;
            Content = _outerBorder;

            // Titelleiste
            BuildTitleBar(title);

            // Theme-Event: alle offenen Fenster gleichzeitig umschalten
            _themeHandler = () => Dispatcher.Invoke(() =>
            {
                Background = new SolidColorBrush(MeToolsTheme.CPetrolDark);
                _outerBorder.Background = MeToolsTheme.BrBg;
                if (StatusBarGrid != null)
                    StatusBarGrid.Background = MeToolsTheme.BrStatusBar;
                OnThemeChanged();
            });
            MeToolsTheme.ThemeChanged += _themeHandler;
            Closed += (s, e) => MeToolsTheme.ThemeChanged -= _themeHandler;

            // Glue to Revit: stays above the Revit window, minimizes/restores with it,
            // but remains a separate, movable window.
            if (RevitHandle != System.IntPtr.Zero)
                try { new System.Windows.Interop.WindowInteropHelper(this).Owner = RevitHandle; } catch { }
        }

        // ── Titelleiste (immer gleich für ALLE Fenster) ───────────────────
        private void BuildTitleBar(string title)
        {
            var bar = new Grid
            {
                Height     = 38,
                Background = MeToolsTheme.BrPetrolDark,
            };
            bar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            bar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            bar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            // Drag
            bar.MouseLeftButtonDown += (s, e) =>
            { if (e.ButtonState == MouseButtonState.Pressed) DragMove(); };

            // Logo
            var logo = new Image
            {
                Source = MeToolsTheme.LoadLogo(),
                Width = 20, Height = 20,
                Margin = new Thickness(12, 0, 8, 0),
                VerticalAlignment = VerticalAlignment.Center,
            };
            Grid.SetColumn(logo, 0);
            bar.Children.Add(logo);

            // Titel
            var tp = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            tp.Children.Add(new TextBlock
            {
                Text = title, FontSize = 14,
                FontWeight = FontWeights.Bold, FontStyle = FontStyles.Italic,
                Foreground = Brushes.White, VerticalAlignment = VerticalAlignment.Center,
            });
            tp.Children.Add(new TextBlock
            {
                Text = "  by Mayer E-Concept SRL", FontSize = 10,
                Foreground = new SolidColorBrush(Color.FromArgb(160, 255, 255, 255)),
                VerticalAlignment = VerticalAlignment.Center,
            });
            if (AppKey != null)
            {
                var caret = new Button
                {
                    Content = "\u25BE", FontSize = 13, FontWeight = FontWeights.Bold,
                    Width = 34, Height = 26, Padding = new Thickness(0),
                    Margin = new Thickness(8, 1, 0, 0),
                    Background = new SolidColorBrush(Color.FromArgb(36, 255, 255, 255)),
                    BorderThickness = new Thickness(0),
                    Foreground = Brushes.White,
                    Cursor = Cursors.Hand, VerticalAlignment = VerticalAlignment.Center,
                    ToolTip = "Switch app",
                };
                caret.Template = RoundedBtnTemplate();
                var caretBg = caret.Background;
                caret.MouseEnter += (s, e) => caret.Background = new SolidColorBrush(Color.FromArgb(80, 255, 255, 255));
                caret.MouseLeave += (s, e) => caret.Background = caretBg;
                caret.Click += (s, e) => ShowAppMenu(caret);
                tp.Children.Add(caret);
            }
            Grid.SetColumn(tp, 1);
            bar.Children.Add(tp);

            // Fenster-Buttons: Theme | Minimize | Close
            var btns = new StackPanel { Orientation = Orientation.Horizontal };

            var minBtn = TitleBtn(new TextBlock
            {
                Text = "─", FontSize = 14, FontWeight = FontWeights.Bold,
                Foreground = Brushes.White,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment   = VerticalAlignment.Center,
            }, false);
            minBtn.Click += (s, e) => WindowState = WindowState.Minimized;
            btns.Children.Add(minBtn);

            var closeBtn = TitleBtn(new TextBlock
            {
                Text = "✕", FontSize = 12, FontWeight = FontWeights.Bold,
                Foreground = Brushes.White,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment   = VerticalAlignment.Center,
            }, true);
            closeBtn.Click += (s, e) => OnCloseClicked();
            btns.Children.Add(closeBtn);

            Grid.SetColumn(btns, 2);
            bar.Children.Add(btns);

            DockPanel.SetDock(bar, Dock.Top);
            RootDock.Children.Add(bar);
        }

        // ── Schließen-Logik (für Dialog- und normale Fenster) ─────────────
        protected virtual void OnCloseClicked()
        {
            if (_isDialog)
            {
                // Nur setzen wenn Fenster noch offen ist
                if (IsLoaded && IsVisible)
                {
                    try { DialogResult = false; } catch { }
                }
            }
            Close();
        }

        // ── StatusBar (immer gleich) ───────────────────────────────────────
        protected void BuildStatusBar(string left = "", string right = "Revit 2025")
        {
            StatusBarGrid = new Grid
            {
                Height = 26,
                Background = MeToolsTheme.BrStatusBar,
            };
            StatusBarGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            StatusBarGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            StatusLeft = new TextBlock
            {
                Text = left, FontSize = 11,
                Foreground = new SolidColorBrush(Color.FromArgb(220, 255, 255, 255)),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(12, 0, 0, 0),
            };
            StatusRight = new TextBlock
            {
                Text = right, FontSize = 10,
                Foreground = new SolidColorBrush(Color.FromArgb(140, 255, 255, 255)),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 10, 0),
            };
            Grid.SetColumn(StatusLeft,  0);
            Grid.SetColumn(StatusRight, 1);
            StatusBarGrid.Children.Add(StatusLeft);
            StatusBarGrid.Children.Add(StatusRight);
            DockPanel.SetDock(StatusBarGrid, Dock.Bottom);
            RootDock.Children.Add(StatusBarGrid);
        }

        // ── Theme-Hook für Unterklassen ───────────────────────────────────
        protected virtual void OnThemeChanged() { }

        // ── App-Switcher (title dropdown) ─────────────────────────
        // Override in a window to enable the title dropdown (null = no switcher).
        protected virtual string AppKey => null;

        private System.Windows.Controls.Primitives.Popup _appPopup;

        private void ShowAppMenu(UIElement anchor)
        {
            var panel = new StackPanel();
            foreach (var app in AppSwitcher.Apps)
            {
                var key = app.Key;
                bool current = key == AppKey;
                var row = new Border
                {
                    Height = 34, CornerRadius = new CornerRadius(4),
                    Padding = new Thickness(14, 0, 18, 0),
                    Background = current ? MeToolsTheme.BrActiveBg : Brushes.Transparent,
                    Cursor = current ? Cursors.Arrow : Cursors.Hand,
                    Child = new TextBlock
                    {
                        Text = app.Label, FontSize = 12,
                        Foreground = current ? MeToolsTheme.BrActiveFg : MeToolsTheme.BrText,
                        FontWeight = current ? FontWeights.SemiBold : FontWeights.Normal,
                        VerticalAlignment   = VerticalAlignment.Center,
                        HorizontalAlignment = HorizontalAlignment.Left,
                    },
                };
                if (!current)
                {
                    row.MouseEnter += (s, e) => row.Background = MeToolsTheme.BrActiveBg;
                    row.MouseLeave += (s, e) => row.Background = Brushes.Transparent;
                    row.MouseLeftButtonUp += (s, e) =>
                    {
                        if (_appPopup != null) _appPopup.IsOpen = false;
                        AppSwitcher.SwitchTo(key);
                        Close();
                    };
                }
                panel.Children.Add(row);
            }

            var shell = new Border
            {
                Background = MeToolsTheme.BrSurface,
                BorderBrush = MeToolsTheme.BrBorder,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(6),
                MinWidth = 180,
                Padding = new Thickness(4),
                Child = panel,
                Effect = new System.Windows.Media.Effects.DropShadowEffect
                {
                    BlurRadius = 12, ShadowDepth = 2, Opacity = 0.3, Color = Colors.Black,
                },
            };

            _appPopup = new System.Windows.Controls.Primitives.Popup
            {
                PlacementTarget = anchor,
                Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom,
                StaysOpen = false,
                AllowsTransparency = true,
                Child = shell,
            };
            _appPopup.IsOpen = true;
        }


        // ── Styled ComboBox via XAML-String (einzig zuverlässige Methode) ──────
        public static System.Windows.Controls.ComboBox StyledCombo(int height = 28, int fontSize = 12)
        {
            var cb = new System.Windows.Controls.ComboBox
            {
                Height   = height,
                FontSize = fontSize,
            };
            ApplyComboStyle(cb);
            return cb;
        }

        public static void ApplyComboStyle(System.Windows.Controls.ComboBox cb)
        {
            if (cb == null) return;

            bool dark   = MeToolsTheme.Current == MeTheme.Dark;
            string bg   = dark ? "#FF282828" : "#FFFFFFFF";
            string fg   = dark ? "#FFE8E8E8" : "#FF1E2528";
            string bdr  = dark ? "#FF444444" : "#FFD0D5D9";
            string hov  = dark ? "#FF0F3535" : "#FFE0F0F0";
            string hfg  = dark ? "#FF5DCAA5" : "#FF0D3D3D";
            string pbg  = dark ? "#FF2A2A2A" : "#FFFFFFFF";

            string xaml = $@"
<Style xmlns=""http://schemas.microsoft.com/winfx/2006/xaml/presentation""
       xmlns:x=""http://schemas.microsoft.com/winfx/2006/xaml""
       TargetType=""ComboBox"">
    <Setter Property=""Background"" Value=""{bg}""/>
    <Setter Property=""Foreground"" Value=""{fg}""/>
    <Setter Property=""BorderBrush"" Value=""{bdr}""/>
    <Setter Property=""BorderThickness"" Value=""1""/>
    <Setter Property=""Padding"" Value=""6,2,0,2""/>
    <Setter Property=""Template"">
        <Setter.Value>
            <ControlTemplate TargetType=""ComboBox"">
                <Border Background=""{{TemplateBinding Background}}""
                        BorderBrush=""{{TemplateBinding BorderBrush}}""
                        BorderThickness=""{{TemplateBinding BorderThickness}}""
                        CornerRadius=""4"">
                    <Grid>
                        <ToggleButton Focusable=""False"" Opacity=""0""
                            IsChecked=""{{Binding IsDropDownOpen, Mode=TwoWay, RelativeSource={{RelativeSource TemplatedParent}}}}""
                            HorizontalAlignment=""Stretch"" VerticalAlignment=""Stretch""/>
                        <ContentPresenter Margin=""8,0,24,0"" IsHitTestVisible=""False""
                            VerticalAlignment=""Center""
                            Content=""{{Binding SelectionBoxItem, RelativeSource={{RelativeSource TemplatedParent}}}}""
                            ContentTemplate=""{{Binding SelectionBoxItemTemplate, RelativeSource={{RelativeSource TemplatedParent}}}}""/>
                        <Path Data=""M 0 0 L 4 4 L 8 0 Z"" Fill=""{fg}""
                              HorizontalAlignment=""Right"" VerticalAlignment=""Center""
                              Margin=""0,0,8,0"" IsHitTestVisible=""False""/>
                        <Popup IsOpen=""{{TemplateBinding IsDropDownOpen}}""
                               Placement=""Bottom"" AllowsTransparency=""True""
                               Focusable=""False"" StaysOpen=""False""
                               Width=""{{Binding ActualWidth, RelativeSource={{RelativeSource TemplatedParent}}}}"">
                            <Border Background=""{pbg}"" BorderBrush=""{bdr}""
                                    BorderThickness=""1"" CornerRadius=""0,0,4,4"">
                                <ScrollViewer MaxHeight=""200"" VerticalScrollBarVisibility=""Auto"">
                                    <ItemsPresenter/>
                                </ScrollViewer>
                            </Border>
                        </Popup>
                    </Grid>
                </Border>
            </ControlTemplate>
        </Setter.Value>
    </Setter>
    <Setter Property=""ItemContainerStyle"">
        <Setter.Value>
            <Style TargetType=""ComboBoxItem"">
                <Setter Property=""Background"" Value=""{pbg}""/>
                <Setter Property=""Foreground"" Value=""{fg}""/>
                <Setter Property=""Padding""    Value=""8,4,8,4""/>
                <Style.Triggers>
                    <Trigger Property=""IsHighlighted"" Value=""True"">
                        <Setter Property=""Background"" Value=""{hov}""/>
                        <Setter Property=""Foreground"" Value=""{hfg}""/>
                    </Trigger>
                </Style.Triggers>
            </Style>
        </Setter.Value>
    </Setter>
</Style>";

            try
            {
                var style = (System.Windows.Style)System.Windows.Markup.XamlReader.Parse(xaml);
                cb.Style = style;
            }
            catch { }
        }

        public static System.Windows.Controls.ControlTemplate MakeComboBoxTemplate()
        {
            var cb = new System.Windows.Controls.ComboBox();
            ApplyComboStyle(cb);
            return cb.Template;
        }





        // ═════════════════════════════════════════════════════════════════
        // GEMEINSAME UI-HELPERS (alle Fenster benutzen genau diese Methoden)
        // ═════════════════════════════════════════════════════════════════

        // Titelleisten-Button
        private Button TitleBtn(UIElement content, bool isClose)
        {
            var hover = isClose
                ? new SolidColorBrush(MeToolsTheme.CRed)
                : new SolidColorBrush(Color.FromRgb(0x2A, 0x7A, 0x7A));
            var b = new Button
            {
                Width = 34, Height = 38, Content = content,
                Background = Brushes.Transparent, BorderThickness = new Thickness(0),
                Cursor = Cursors.Hand,
            };
            b.MouseEnter += (s, e) => b.Background = hover;
            b.MouseLeave += (s, e) => b.Background = Brushes.Transparent;
            return b;
        }

        // Section-Label "── TEXT ──────────"
        protected FrameworkElement Sec(string text)
        {
            var sp = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 6) };
            sp.Children.Add(new Border { Height = 1, Width = 10, Background = MeToolsTheme.BrSecLine, VerticalAlignment = VerticalAlignment.Center });
            sp.Children.Add(new TextBlock
            {
                Text = $"  {text.ToUpper()}  ", FontSize = 9.5, FontWeight = FontWeights.Bold,
                Foreground = MeToolsTheme.BrSecText, VerticalAlignment = VerticalAlignment.Center,
            });
            sp.Children.Add(new Border { Height = 1, MinWidth = 80, Background = MeToolsTheme.BrSecLine, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Stretch });
            return sp;
        }

        // Zahlen-Eingabe
        protected TextBox Num(string val) => new TextBox
        {
            Text = val, Height = 28, FontSize = 12, TextAlignment = TextAlignment.Center,
            Background = MeToolsTheme.BrInput, Foreground = MeToolsTheme.BrText,
            BorderBrush = MeToolsTheme.BrBorder, BorderThickness = new Thickness(1),
            Padding = new Thickness(4, 0, 4, 0), CaretBrush = MeToolsTheme.BrText,
            VerticalContentAlignment = VerticalAlignment.Center,
        };

        // Stromkreis-Eingabe (Consolas, petrol)
        protected TextBox SkInput(string val = "??") => new TextBox
        {
            Text = val, Width = 100, Height = 26,
            FontFamily = new FontFamily("Consolas"), FontWeight = FontWeights.Bold, FontSize = 13,
            TextAlignment = TextAlignment.Center,
            Background = MeToolsTheme.BrInput, Foreground = MeToolsTheme.BrInputFg,
            BorderBrush = MeToolsTheme.BrBorder, BorderThickness = new Thickness(1),
            Padding = new Thickness(4, 0, 4, 0),
        };

        // Toggle-Button (Mode/Rotation, aktiv/inaktiv)
        protected Button ToggleBtn(string label, bool active, Action onClick)
        {
            var b = new Button
            {
                Content = label, Height = 30, MinWidth = 80, FontSize = 12,
                Padding = new Thickness(14, 0, 14, 0),
                Background  = active ? MeToolsTheme.BrActiveBg : MeToolsTheme.BrBtnBg,
                BorderBrush = active ? MeToolsTheme.BrPetrol    : MeToolsTheme.BrBtnBorder,
                BorderThickness = new Thickness(1),
                Foreground  = active ? MeToolsTheme.BrActiveFg  : MeToolsTheme.BrMuted,
                Cursor = Cursors.Hand,
            };
            b.Template = RoundedBtnTemplate();
            b.Click += (s, e) => onClick();
            return b;
        }

        internal static System.Windows.Controls.ControlTemplate RoundedBtnTemplate()
        {
            var f = new System.Windows.FrameworkElementFactory(typeof(Border));
            f.SetBinding(Border.BackgroundProperty, new System.Windows.Data.Binding("Background")
                { RelativeSource = new System.Windows.Data.RelativeSource(System.Windows.Data.RelativeSourceMode.TemplatedParent) });
            f.SetBinding(Border.BorderBrushProperty, new System.Windows.Data.Binding("BorderBrush")
                { RelativeSource = new System.Windows.Data.RelativeSource(System.Windows.Data.RelativeSourceMode.TemplatedParent) });
            f.SetBinding(Border.BorderThicknessProperty, new System.Windows.Data.Binding("BorderThickness")
                { RelativeSource = new System.Windows.Data.RelativeSource(System.Windows.Data.RelativeSourceMode.TemplatedParent) });
            f.SetValue(Border.CornerRadiusProperty, new CornerRadius(4));
            var cp = new System.Windows.FrameworkElementFactory(typeof(ContentPresenter));
            cp.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            cp.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
            // This was missing entirely -- without it, every button's own
            // Padding setting (there are over a hundred across the suite)
            // was silently ignored, since nothing ever told this template's
            // ContentPresenter to respect it. That's the actual root cause
            // of "text touching the edges" -- not a per-button styling
            // mistake, a single missing binding in the one shared template
            // almost every button in ME-Tools uses.
            cp.SetBinding(ContentPresenter.MarginProperty, new System.Windows.Data.Binding("Padding")
                { RelativeSource = new System.Windows.Data.RelativeSource(System.Windows.Data.RelativeSourceMode.TemplatedParent) });
            f.AppendChild(cp);
            return new System.Windows.Controls.ControlTemplate(typeof(Button)) { VisualTree = f };
        }

        // Toggle-Button Update (ohne neu erstellen)
        protected void UpdateToggle(Button b, bool active)
        {
            if (b == null) return;
            b.Background  = active ? MeToolsTheme.BrActiveBg : MeToolsTheme.BrBtnBg;
            b.BorderBrush = active ? MeToolsTheme.BrPetrol    : MeToolsTheme.BrBtnBorder;
            b.Foreground  = active ? MeToolsTheme.BrActiveFg  : MeToolsTheme.BrMuted;
            if (b.Template == null) b.Template = RoundedBtnTemplate();
        }

        // Aktions-Button (Place / Multi-Place)
        protected Button ActionBtn(string label, bool outline, Action onClick)
        {
            var bgNorm = outline ? MeToolsTheme.BrBtnBg       : MeToolsTheme.BrPetrol;
            var bgHov  = outline ? MeToolsTheme.BrActiveBg    : MeToolsTheme.BrPetrolDark;
            var fg     = outline ? (MeToolsTheme.Current == MeTheme.Dark ? Brushes.White : MeToolsTheme.BrPetrol) : Brushes.White;
            var b = new Button
            {
                Content = label, Height = 36, FontSize = 13, FontWeight = FontWeights.SemiBold,
                Padding = new Thickness(16, 0, 16, 0),
                Background = bgNorm, BorderBrush = MeToolsTheme.BrPetrol,
                BorderThickness = new Thickness(1.5), Foreground = fg, Cursor = Cursors.Hand,
            };
            b.Template = RoundedBtnTemplate();
            b.MouseEnter += (s, e) => b.Background = bgHov;
            b.MouseLeave += (s, e) => b.Background = bgNorm;
            b.Click += (s, e) => onClick();
            return b;
        }

        // Footer-Button (Abbrechen / Speichern)
        protected Button FooterBtn(string label, bool primary, Action onClick)
        {
            var bgNorm = primary ? MeToolsTheme.BrPetrol    : MeToolsTheme.BrBtnBg;
            var bgHov  = primary ? MeToolsTheme.BrPetrolDark : MeToolsTheme.BrActiveBg;
            var fg     = primary ? Brushes.White              : MeToolsTheme.BrText;
            var b = new Button
            {
                Content = label, Height = 32, Padding = new Thickness(16, 0, 16, 0),
                FontSize = 12, FontWeight = primary ? FontWeights.SemiBold : FontWeights.Normal,
                Background = bgNorm,
                BorderBrush = primary ? MeToolsTheme.BrPetrol : MeToolsTheme.BrBtnBorder,
                BorderThickness = new Thickness(1), Foreground = fg, Cursor = Cursors.Hand,
            };
            b.MouseEnter += (s, e) => b.Background = bgHov;
            b.MouseLeave += (s, e) => b.Background = bgNorm;
            b.Template = RoundedBtnTemplate();
            b.Click += (s, e) => onClick();
            return b;
        }

        // Info-Box
        protected Border InfoBox(string text) => new Border
        {
            Background = MeToolsTheme.BrInfoBox, CornerRadius = new CornerRadius(5),
            Padding = new Thickness(10, 8, 10, 8), Margin = new Thickness(0, 0, 0, 12),
            Child = new TextBlock
            {
                Text = text, FontSize = 11, Foreground = MeToolsTheme.BrInfoText,
                TextWrapping = TextWrapping.Wrap,
            },
        };

        // Wasserzeichen (Logo, transparent, rechts unten)
        protected Image Watermark() => new Image
        {
            Source = MeToolsTheme.LoadLogo(),
            Width = 150, Height = 150, Opacity = 0.05,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment   = VerticalAlignment.Bottom,
            Margin = new Thickness(0, 0, 8, 26),
            IsHitTestVisible = false,
        };
    }
}

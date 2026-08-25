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
        private Grid        _titleBar;
        private Border      _titleWash;
        private Border      _footerWash;
        private TextBlock   _titleTextBlock;
        private TextBlock   _bylineTextBlock;
        private Button      _caretBtn;
        private TextBlock   _minGlyph;
        private TextBlock   _closeGlyph;

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
                //
                // Clamped to the actual available screen height (minus a small
                // margin) rather than frozen at whatever raw content wanted --
                // on a smaller or heavily-scaled display, a window's natural
                // content height can genuinely exceed the screen's usable area,
                // which otherwise pushes the window's own footer/buttons off
                // the bottom edge with no way to reach them. Every window here
                // uses a DockPanel with an inner ScrollViewer as the "fill"
                // element, so clamping the outer window doesn't hide anything --
                // it just leaves that scroller less room, and it absorbs the
                // difference as internal scrolling instead.
                Height        = System.Math.Min(ActualHeight, wa.Height - 40);
                SizeToContent = SizeToContent.Manual;
            };
            FontFamily            = new FontFamily("Segoe UI");
            FontSize              = 12;

            // Kein weißer Rand: WindowChrome entfernt + Background = Titelleiste
            // Flat CBg, not the grid brush -- this only ever shows in the
            // tiny sliver outside _outerBorder's rounded corners. Also
            // fixes a real pre-existing bug: this statement used to be on
            // the SAME physical line as the comment above it, so the `//`
            // silently commented out the entire assignment -- it never
            // actually ran. Barely noticeable while hardcoded to
            // CPetrolDark next to an also-solid-petrol title bar; would
            // have been obviously wrong once the header went light in
            // Light mode, which is what surfaced it.
            Background = new SolidColorBrush(MeToolsTheme.CBg);
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
                CornerRadius = new CornerRadius(20),
                ClipToBounds = true,
                Background   = MeToolsTheme.BrBg,
            };
            RootDock = new DockPanel { LastChildFill = true };
            _outerBorder.Child = RootDock;
            Content = _outerBorder;

            // Titelleiste
            BuildTitleBar(title);

            // Implicit style, not an explicit call like ApplyComboStyle --
            // checkboxes get constructed ad-hoc in individual tool windows
            // (e.g. Collision Checker's "Also check imported CAD/IFC..."),
            // not through a shared helper here, so there's no one place to
            // intercept them all. An implicit Style keyed by typeof(CheckBox)
            // on THIS window's own Resources applies automatically to every
            // CheckBox anywhere in this window's visual tree -- current and
            // future -- without touching a single tool's own file.
            ApplyCheckBoxStyle(this);
            ApplyScrollBarStyle(this); // same implicit-style reasoning -- scrollbars live inside whatever ScrollViewer a tool uses, not a shared helper

            // Theme-Event: alle offenen Fenster gleichzeitig umschalten
            _themeHandler = () => Dispatcher.Invoke(() =>
            {
                // Flat CBg, not the grid brush -- this only ever shows in
                // the tiny sliver outside _outerBorder's rounded corners,
                // where a tiled pattern would be pointless. Was hardcoded
                // to CPetrolDark regardless of theme before, which barely
                // mattered while the title bar was ALSO solid petrol (it
                // just blended in) -- now that the header is light in
                // Light mode, a dark corner speck would actually be
                // visible and look like a mistake.
                Background = new SolidColorBrush(MeToolsTheme.CBg);
                _outerBorder.Background = MeToolsTheme.BrBg;
                if (_titleBar != null) _titleBar.Background = MeToolsTheme.BrBg;
                if (_titleWash != null) _titleWash.Background = MeToolsTheme.HeaderWashBrush();
                if (_titleTextBlock != null) _titleTextBlock.Foreground = MeToolsTheme.BrTitleText;
                if (_bylineTextBlock != null) _bylineTextBlock.Foreground = MeToolsTheme.BrTitleTextMuted;
                if (_caretBtn != null)
                {
                    _caretBtn.Background = MeToolsTheme.BrTitleOverlay;
                    _caretBtn.Foreground = MeToolsTheme.BrTitleText;
                }
                if (_minGlyph != null) _minGlyph.Foreground = MeToolsTheme.BrTitleText;
                if (_closeGlyph != null) _closeGlyph.Foreground = MeToolsTheme.BrTitleText;
                if (StatusBarGrid != null) StatusBarGrid.Background = MeToolsTheme.BrBg;
                if (_footerWash != null) _footerWash.Background = MeToolsTheme.FooterWashBrush();
                if (StatusLeft != null) StatusLeft.Foreground = MeToolsTheme.BrTitleTextMuted;
                if (StatusRight != null)
                {
                    var mc = MeToolsTheme.CTitleTextMuted;
                    StatusRight.Foreground = new SolidColorBrush(Color.FromArgb(170, mc.R, mc.G, mc.B));
                }
                ApplyCheckBoxStyle(this);
                ApplyScrollBarStyle(this);
                OnThemeChanged();
            });
            MeToolsTheme.ThemeChanged += _themeHandler;
            Closed += (s, e) => MeToolsTheme.ThemeChanged -= _themeHandler;

            // Glue to Revit: stays above the Revit window, minimizes/restores with it,
            // but remains a separate, movable window.
            if (RevitHandle != System.IntPtr.Zero)
                try { new System.Windows.Interop.WindowInteropHelper(this).Owner = RevitHandle; } catch { }
        }

        // InitWindow's Loaded handler freezes Height and switches SizeToContent
        // to Manual after the very first layout pass, specifically to avoid a
        // resize-grip glitch (see the comment there). The side effect: if a
        // window's own content later grows substantially -- switching to a
        // tab with much more content, or a results panel that only appears
        // once data loads -- the window never grows to fit it, silently
        // cutting off whatever's at the bottom (e.g. an action button).
        // Any window whose content can change size after its first paint
        // should call this after that content changes, to briefly re-enable
        // auto-sizing, force an immediate layout pass, and re-freeze at the
        // new height -- same technique as the original startup fix, just
        // re-applied on demand instead of once.
        protected void ResizeToFitContent()
        {
            if (!IsLoaded) return; // constructor-time call runs before the window has a
                                    // screen presence -- UpdateLayout/ActualHeight are
                                    // unreliable then, and locking in whatever they
                                    // produce is what caused a tiny-sliver-on-open bug.
                                    // The Loaded handler above does the correct first
                                    // measure once the window is genuinely shown; this
                                    // only needs to run for real content changes after that.
            try
            {
                SizeToContent = SizeToContent.Height;
                UpdateLayout();

                // Same clamp as InitWindow's own Loaded handler, and for the
                // same reason: content that grew past the screen's usable
                // height shouldn't be allowed to push the window itself past
                // it too -- the inner ScrollViewer (DockPanel "fill" element)
                // absorbs the difference as internal scrolling instead.
                var wa = System.Windows.SystemParameters.WorkArea;
                Height        = System.Math.Min(ActualHeight, wa.Height - 40);
                SizeToContent = SizeToContent.Manual;

                // The window's vertical position was set once, centered on
                // whatever content loaded first (usually short). Growing
                // taller only extends the bottom edge, since Top never moves --
                // so a sufficiently tall result can run off the bottom of the
                // screen. Pull Top up to compensate whenever that would
                // happen, clamped so it never goes above the screen's own
                // top edge either.
                double bottom = Top + Height;
                if (bottom > wa.Bottom)
                    Top = System.Math.Max(wa.Top, Top - (bottom - wa.Bottom));
            }
            catch { }
        }

        // ── Titelleiste (immer gleich für ALLE Fenster) ───────────────────
        private void BuildTitleBar(string title)
        {
            var bar = new Grid
            {
                Height     = 38,
                Background = MeToolsTheme.BrBg,
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
            _titleTextBlock = new TextBlock
            {
                Text = title, FontSize = 14,
                FontWeight = FontWeights.SemiBold,
                Foreground = MeToolsTheme.BrTitleText, VerticalAlignment = VerticalAlignment.Center,
            };
            tp.Children.Add(_titleTextBlock);
            _bylineTextBlock = new TextBlock
            {
                Text = "  by Mayer E-Concept SRL", FontSize = 10,
                Foreground = MeToolsTheme.BrTitleTextMuted,
                VerticalAlignment = VerticalAlignment.Center,
            };
            tp.Children.Add(_bylineTextBlock);
            
            if (AppKey != null)
            {
                _caretBtn = new Button
                {
                    Content = "\u25BE", FontSize = 13, FontWeight = FontWeights.Bold,
                    Width = 34, Height = 26, Padding = new Thickness(0),
                    Margin = new Thickness(8, 1, 0, 0),
                    Background = MeToolsTheme.BrTitleOverlay,
                    BorderThickness = new Thickness(0),
                    Foreground = MeToolsTheme.BrTitleText,
                    Cursor = Cursors.Hand, VerticalAlignment = VerticalAlignment.Center,
                    ToolTip = "Switch app",
                };
                _caretBtn.Template = RoundedBtnTemplate();
                _caretBtn.MouseEnter += (s, e) => _caretBtn.Background = MeToolsTheme.BrTitleOverlayHover;
                _caretBtn.MouseLeave += (s, e) => _caretBtn.Background = MeToolsTheme.BrTitleOverlay;
                _caretBtn.Click += (s, e) => ShowAppMenu(_caretBtn);
                tp.Children.Add(_caretBtn);
            }
            Grid.SetColumn(tp, 1);
            bar.Children.Add(tp);

            // Fenster-Buttons: Minimize | Close
            var btns = new StackPanel { Orientation = Orientation.Horizontal };

            _minGlyph = new TextBlock
            {
                Text = "─", FontSize = 14, FontWeight = FontWeights.Bold,
                Foreground = MeToolsTheme.BrTitleText,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment   = VerticalAlignment.Center,
            };
            var minBtn = TitleBtn(_minGlyph, false);
            minBtn.Click += (s, e) => WindowState = WindowState.Minimized;
            btns.Children.Add(minBtn);

            _closeGlyph = new TextBlock
            {
                Text = "✕", FontSize = 12, FontWeight = FontWeights.Bold,
                Foreground = MeToolsTheme.BrTitleText,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment   = VerticalAlignment.Center,
            };
            var closeBtn = TitleBtn(_closeGlyph, true);
            closeBtn.Click += (s, e) => OnCloseClicked();
            btns.Children.Add(closeBtn);

            Grid.SetColumn(btns, 2);
            bar.Children.Add(btns);

            // The actual redesign: no more solid petrol fill -- a soft
            // wash, strongest at the bottom (the seam with the body),
            // fading to nothing at the window's own top edge. Added last
            // so it sits visually on top of the logo/title/buttons (same
            // as the approved preview, where the wash tints everything
            // semi-transparently rather than sitting behind it), spans
            // all 3 columns, and is hit-test-transparent so it doesn't
            // swallow clicks meant for the buttons or the drag behavior
            // above.
            var wash = new Border { Background = MeToolsTheme.HeaderWashBrush(), IsHitTestVisible = false };
            Grid.SetColumnSpan(wash, 3);
            bar.Children.Add(wash);

            _titleBar  = bar;
            _titleWash = wash;
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
                Background = MeToolsTheme.BrBg,
            };
            StatusBarGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            StatusBarGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            StatusLeft = new TextBlock
            {
                Text = left, FontSize = 11,
                Foreground = MeToolsTheme.BrTitleTextMuted,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(12, 0, 0, 0),
            };
            var mutedC = MeToolsTheme.CTitleTextMuted;
            StatusRight = new TextBlock
            {
                Text = right, FontSize = 10,
                Foreground = new SolidColorBrush(Color.FromArgb(170, mutedC.R, mutedC.G, mutedC.B)),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 10, 0),
            };
            Grid.SetColumn(StatusLeft,  0);
            Grid.SetColumn(StatusRight, 1);
            StatusBarGrid.Children.Add(StatusLeft);
            StatusBarGrid.Children.Add(StatusRight);

            // Mirror of the header wash -- strongest at the TOP of the bar
            // (the seam with the body above it), fading to nothing at the
            // window's own bottom edge. Added last, spans both columns,
            // hit-test-transparent so it doesn't block anything.
            _footerWash = new Border { Background = MeToolsTheme.FooterWashBrush(), IsHitTestVisible = false };
            Grid.SetColumnSpan(_footerWash, 2);
            StatusBarGrid.Children.Add(_footerWash);

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
                    Height = 34, CornerRadius = new CornerRadius(8),
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
                CornerRadius = new CornerRadius(12),
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
            string bg   = dark ? "#FF0F1E1E" : "#FFFFFFFF";
            string fg   = dark ? "#FFE9F7F5" : "#FF23292B";
            string bdr  = dark ? "#22FFFFFF" : "#FFE3E5E4";
            string hov  = dark ? "#1F54DBD3" : "#FFE8F2F0";
            string hfg  = dark ? "#FF6FE9E0" : "#FF14524C";
            string pbg  = dark ? "#FF112222" : "#FFFFFFFF";

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
                        CornerRadius=""10"">
                    <Grid>
                        <ToggleButton Focusable=""False"" Opacity=""0""
                            IsChecked=""{{Binding IsDropDownOpen, Mode=TwoWay, RelativeSource={{RelativeSource TemplatedParent}}}}""
                            HorizontalAlignment=""Stretch"" VerticalAlignment=""Stretch""/>
                        <ContentPresenter x:Name=""ContentSite"" Margin=""8,0,24,0"" IsHitTestVisible=""False""
                            VerticalAlignment=""Center""
                            Content=""{{Binding SelectionBoxItem, RelativeSource={{RelativeSource TemplatedParent}}}}""
                            ContentTemplate=""{{Binding SelectionBoxItemTemplate, RelativeSource={{RelativeSource TemplatedParent}}}}""/>
                        <TextBox x:Name=""PART_EditableTextBox"" Visibility=""Collapsed""
                            Margin=""8,0,24,0"" Background=""Transparent"" BorderThickness=""0""
                            Foreground=""{fg}"" CaretBrush=""{fg}""
                            VerticalAlignment=""Center"" VerticalContentAlignment=""Center""
                            Focusable=""True""/>
                        <Path Data=""M 0 0 L 4 4 L 8 0 Z"" Fill=""{fg}""
                              HorizontalAlignment=""Right"" VerticalAlignment=""Center""
                              Margin=""0,0,8,0"" IsHitTestVisible=""False""/>
                        <Popup IsOpen=""{{TemplateBinding IsDropDownOpen}}""
                               Placement=""Bottom"" AllowsTransparency=""True""
                               Focusable=""False"" StaysOpen=""False""
                               Width=""{{Binding ActualWidth, RelativeSource={{RelativeSource TemplatedParent}}}}"">
                            <Border Background=""{pbg}"" BorderBrush=""{bdr}""
                                    BorderThickness=""1"" CornerRadius=""0,0,10,10"">
                                <ScrollViewer MaxHeight=""200"" VerticalScrollBarVisibility=""Auto"">
                                    <ItemsPresenter/>
                                </ScrollViewer>
                            </Border>
                        </Popup>
                    </Grid>
                </Border>
                <ControlTemplate.Triggers>
                    <Trigger Property=""IsEditable"" Value=""true"">
                        <Setter TargetName=""ContentSite"" Property=""Visibility"" Value=""Collapsed""/>
                        <Setter TargetName=""PART_EditableTextBox"" Property=""Visibility"" Value=""Visible""/>
                    </Trigger>
                </ControlTemplate.Triggers>
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

        // Implicit style for every CheckBox in this window (see the call
        // site in InitWindow for why implicit rather than explicit-per-
        // control). Rounded box instead of WPF's default square one,
        // solid accent fill + a checkmark glyph when checked instead of
        // the default's plain tick-in-a-square -- same visual family as
        // the rest of this redesign's rounded, accent-filled controls.
        // Re-called from the theme-toggle handler too, replacing this
        // window's Resources entry with a freshly-themed Style -- WPF's
        // implicit style lookup is resource-reference-based under the
        // hood, so existing checkboxes already in the tree pick up the
        // replacement the same way a DynamicResource would.
        public static void ApplyCheckBoxStyle(Window window)
        {
            if (window == null) return;
            bool dark = MeToolsTheme.Current == MeTheme.Dark;
            string fg        = dark ? "#FFCFE6E3" : "#FF23292B";
            string boxBg     = dark ? "#2654DBD3" : "#FFFFFFFF";
            string boxBdr    = dark ? "#FF54DBD3" : "#FFC7CBCA";
            string checkedBg = dark ? "#FF54DBD3" : "#FF0F6E5E";
            string checkFg   = dark ? "#FF06201D" : "#FFFFFFFF";

            string xaml = $@"
<Style xmlns=""http://schemas.microsoft.com/winfx/2006/xaml/presentation""
       xmlns:x=""http://schemas.microsoft.com/winfx/2006/xaml""
       TargetType=""CheckBox"">
    <Setter Property=""Foreground"" Value=""{fg}""/>
    <Setter Property=""Template"">
        <Setter.Value>
            <ControlTemplate TargetType=""CheckBox"">
                <StackPanel Orientation=""Horizontal"">
                    <Border x:Name=""Box"" Width=""16"" Height=""16"" CornerRadius=""5""
                            Background=""{boxBg}"" BorderBrush=""{boxBdr}"" BorderThickness=""1.3""
                            VerticalAlignment=""Center"">
                        <Path x:Name=""Check"" Data=""M 2 6 L 6 10 L 12 2"" Stroke=""{checkFg}""
                              StrokeThickness=""1.8"" StrokeStartLineCap=""Round"" StrokeEndLineCap=""Round""
                              StrokeLineJoin=""Round"" Visibility=""Collapsed""
                              Margin=""1"" Stretch=""Uniform""/>
                    </Border>
                    <ContentPresenter Margin=""8,0,0,0"" VerticalAlignment=""Center""/>
                </StackPanel>
                <ControlTemplate.Triggers>
                    <Trigger Property=""IsChecked"" Value=""True"">
                        <Setter TargetName=""Box"" Property=""Background"" Value=""{checkedBg}""/>
                        <Setter TargetName=""Box"" Property=""BorderBrush"" Value=""{checkedBg}""/>
                        <Setter TargetName=""Check"" Property=""Visibility"" Value=""Visible""/>
                    </Trigger>
                </ControlTemplate.Triggers>
            </ControlTemplate>
        </Setter.Value>
    </Setter>
</Style>";

            try
            {
                var style = (System.Windows.Style)System.Windows.Markup.XamlReader.Parse(xaml);
                window.Resources[typeof(System.Windows.Controls.CheckBox)] = style;
            }
            catch { }
        }

        // Implicit style for every ScrollBar in this window -- same
        // reasoning as ApplyCheckBoxStyle: scrollbars show up inside
        // whatever ScrollViewer a given tool happens to use, not through a
        // shared helper, so an implicit Style is the only way to reach all
        // of them without touching every tool's own file. WPF's stock
        // scrollbar is plain OS-themed light gray and doesn't adapt to an
        // app's own theme at all -- exactly what stood out against a dark
        // background.
        //
        // Deliberately doesn't touch the scrollbar's own thickness/width --
        // that's actually controlled by the ScrollViewer's template, a
        // different control this doesn't touch, so changing it here
        // wouldn't do anything anyway. This only recolors what's already
        // there: a transparent track and a rounded, theme-tinted thumb,
        // with the up/down and page arrow buttons kept functional but
        // invisible (Opacity 0) for a cleaner, more minimal look, matching
        // the rest of this redesign.
        public static void ApplyScrollBarStyle(Window window)
        {
            if (window == null) return;
            bool dark = MeToolsTheme.Current == MeTheme.Dark;
            string thumb      = dark ? "#3854DBD3" : "#48124D4D";
            string thumbHover = dark ? "#7054DBD3" : "#80124D4D";

            string xaml = $@"
<Style xmlns=""http://schemas.microsoft.com/winfx/2006/xaml/presentation""
       xmlns:x=""http://schemas.microsoft.com/winfx/2006/xaml""
       TargetType=""ScrollBar"">
    <Setter Property=""Background"" Value=""Transparent""/>
    <Setter Property=""Template"">
        <Setter.Value>
            <ControlTemplate TargetType=""ScrollBar"">
                <Grid Background=""Transparent"">
                    <Track x:Name=""PART_Track"" IsDirectionReversed=""True"" Focusable=""False"">
                        <Track.DecreaseRepeatButton>
                            <RepeatButton Command=""ScrollBar.PageUpCommand"" Opacity=""0"" Focusable=""False""/>
                        </Track.DecreaseRepeatButton>
                        <Track.IncreaseRepeatButton>
                            <RepeatButton Command=""ScrollBar.PageDownCommand"" Opacity=""0"" Focusable=""False""/>
                        </Track.IncreaseRepeatButton>
                        <Track.Thumb>
                            <Thumb MinHeight=""30"" MinWidth=""30"">
                                <Thumb.Template>
                                    <ControlTemplate TargetType=""Thumb"">
                                        <Border x:Name=""ThumbBorder"" Background=""{thumb}"" CornerRadius=""4"" Margin=""4,2,2,2"" MinHeight=""24""/>
                                        <ControlTemplate.Triggers>
                                            <Trigger Property=""IsMouseOver"" Value=""True"">
                                                <Setter TargetName=""ThumbBorder"" Property=""Background"" Value=""{thumbHover}""/>
                                            </Trigger>
                                        </ControlTemplate.Triggers>
                                    </ControlTemplate>
                                </Thumb.Template>
                            </Thumb>
                        </Track.Thumb>
                    </Track>
                </Grid>
                <ControlTemplate.Triggers>
                    <Trigger Property=""Orientation"" Value=""Horizontal"">
                        <Setter TargetName=""PART_Track"" Property=""IsDirectionReversed"" Value=""False""/>
                    </Trigger>
                </ControlTemplate.Triggers>
            </ControlTemplate>
        </Setter.Value>
    </Setter>
</Style>";

            try
            {
                var style = (System.Windows.Style)System.Windows.Markup.XamlReader.Parse(xaml);
                window.Resources[typeof(System.Windows.Controls.Primitives.ScrollBar)] = style;
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
                Text = $"  {text}  ", FontSize = 11, FontWeight = FontWeights.Medium,
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
                // No border at all in either state -- calmer, closer to
                // how macOS segmented controls read (background-tint
                // differentiation only), instead of the previous
                // bordered-outline treatment.
                Background  = active ? MeToolsTheme.BrActiveBg : MeToolsTheme.BrSoftFill,
                BorderBrush = Brushes.Transparent,
                BorderThickness = new Thickness(0),
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
            f.SetValue(Border.CornerRadiusProperty, new CornerRadius(10));
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
            b.Background  = active ? MeToolsTheme.BrActiveBg : MeToolsTheme.BrSoftFill;
            b.BorderBrush = Brushes.Transparent;
            b.BorderThickness = new Thickness(0);
            b.Foreground  = active ? MeToolsTheme.BrActiveFg  : MeToolsTheme.BrMuted;
            if (b.Template == null) b.Template = RoundedBtnTemplate();
        }

        // Aktions-Button (Place / Multi-Place)
        protected Button ActionBtn(string label, bool outline, Action onClick)
        {
            bool dark = MeToolsTheme.Current == MeTheme.Dark;
            var bgNorm = outline ? MeToolsTheme.BrSoftFill : MeToolsTheme.BrPrimaryFill;
            var bgHov  = outline ? MeToolsTheme.BrSoftFillHover : (dark ? MeToolsTheme.BrAccentHover : MeToolsTheme.BrPetrolDark);
            // Primary (non-outline) buttons need theme-aware text: white on
            // the light-mode petrol gradient, but the dark COnAccent tone on
            // dark mode's bright cyan fill -- white text there would barely
            // be legible against that background. Secondary/outline
            // buttons now use muted text to match their new soft-fill,
            // no-border look -- a calmer button reads better with calmer
            // text than the previous strong petrol tone.
            var fg     = outline ? MeToolsTheme.BrMuted : MeToolsTheme.BrPrimaryFg;
            var b = new Button
            {
                Content = label, Height = 36, FontSize = 13, FontWeight = FontWeights.SemiBold,
                Padding = new Thickness(16, 0, 16, 0),
                Background = bgNorm, BorderBrush = Brushes.Transparent,
                BorderThickness = new Thickness(0), Foreground = fg, Cursor = Cursors.Hand,
            };
            b.Template = RoundedBtnTemplate();
            if (!outline) b.Effect = MeToolsTheme.PrimaryButtonGlow(); // contained shadow/glow -- see remarks on PrimaryButtonGlow itself for why this is safe here but wouldn't be on the whole window
            b.MouseEnter += (s, e) => b.Background = bgHov;
            b.MouseLeave += (s, e) => b.Background = bgNorm;
            b.Click += (s, e) => onClick();
            return b;
        }

        // Footer-Button (Abbrechen / Speichern)
        protected Button FooterBtn(string label, bool primary, Action onClick)
        {
            bool dark = MeToolsTheme.Current == MeTheme.Dark;
            var bgNorm = primary ? MeToolsTheme.BrPrimaryFill : MeToolsTheme.BrSoftFill;
            var bgHov  = primary ? (dark ? MeToolsTheme.BrAccentHover : MeToolsTheme.BrPetrolDark) : MeToolsTheme.BrSoftFillHover;
            var fg     = primary ? MeToolsTheme.BrPrimaryFg : MeToolsTheme.BrMuted;
            var b = new Button
            {
                Content = label, Height = 32, Padding = new Thickness(16, 0, 16, 0),
                FontSize = 12, FontWeight = primary ? FontWeights.SemiBold : FontWeights.Normal,
                Background = bgNorm,
                BorderBrush = Brushes.Transparent,
                BorderThickness = new Thickness(0), Foreground = fg, Cursor = Cursors.Hand,
            };
            if (primary) b.Effect = MeToolsTheme.PrimaryButtonGlow();
            b.MouseEnter += (s, e) => b.Background = bgHov;
            b.MouseLeave += (s, e) => b.Background = bgNorm;
            b.Template = RoundedBtnTemplate();
            b.Click += (s, e) => onClick();
            return b;
        }

        // Info-Box
        protected Border InfoBox(string text) => new Border
        {
            Background = MeToolsTheme.BrInfoBox, CornerRadius = new CornerRadius(12),
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

        // ═════════════════════════════════════════════════════════════════
        // SHARED SMALL UI HELPERS
        // Previously each tool window (CircuitTaggerWindow, BatchParamsWindow)
        // kept its own private copy of these -- which meant fixes only landed
        // wherever they were first written. Two real bugs came from exactly
        // that: BatchParamsWindow shipped without the `using Visibility =
        // System.Windows.Visibility;` alias CircuitTaggerWindow already had
        // (CS0176, since Window.Visibility is an instance property that
        // shadows the enum type), and its LabeledField had no tooltip
        // parameter at all even though CircuitTagger's equivalent did. Living
        // here once means every future tool gets both fixes automatically.

        // Updates the window's status bar text (bottom-left).
        protected void UpdateStatusBar(string msg) { if (StatusLeft != null) StatusLeft.Text = msg; }

        // Small muted section-header label, e.g. "CIRCUIT PARAMETERS".
        protected static TextBlock SecH(string text) => new TextBlock
        {
            Text = text, FontSize = 11.5, FontWeight = FontWeights.Medium,
            Foreground = MeToolsTheme.BrMuted, Margin = new Thickness(0, 0, 0, 6),
        };

        // Thin horizontal divider between sections.
        protected Border Div(double vmargin = 10) => new Border
        {
            Height = 1, Background = MeToolsTheme.BrBorder, Margin = new Thickness(0, vmargin, 0, vmargin),
        };

        // Compact label-above-narrow-input field, sized to what actually
        // goes in it rather than stretching to fill its column. hint becomes
        // a hover tooltip; defaultText is optional (most fields start empty).
        protected StackPanel CompactField(string label, string hint, double width, out TextBox tb, string defaultText = "")
        {
            var sp = new StackPanel { Margin = new Thickness(0, 0, 14, 8) };
            sp.Children.Add(new TextBlock { Text = label, FontSize = 9.5, FontWeight = FontWeights.Medium,
                Foreground = MeToolsTheme.BrMuted, Margin = new Thickness(1, 0, 0, 3) });
            var box = new TextBox
            {
                Text = defaultText, Width = width, Height = 26, FontSize = 12,
                FontFamily = new FontFamily("Consolas"), FontWeight = FontWeights.SemiBold,
                Background = MeToolsTheme.BrInput, Foreground = MeToolsTheme.BrInputFg,
                BorderBrush = MeToolsTheme.BrBorder, BorderThickness = new Thickness(1),
                Padding = new Thickness(5, 0, 5, 0), VerticalContentAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Left,
                ToolTip = hint,
            };
            sp.Children.Add(box);
            tb = box;
            return sp;
        }

        // Bare non-editable combo (no card, no label) sized to a fixed
        // width instead of stretching full-width for a short value.
        protected ComboBox CompactComboStrict(string hint, double width)
        {
            var cb = new ComboBox
            {
                Width = width, Height = 26, FontSize = 12, IsEditable = false,
                FontFamily = new FontFamily("Consolas"), FontWeight = FontWeights.SemiBold,
                ToolTip = hint,
                DisplayMemberPath = "DisplayName",
            };
            // Was missing entirely -- this constructed a plain ComboBox
            // with a few properties set directly, but never applied the
            // themed Style/Template that actually controls how a ComboBox
            // renders (the dropdown button chrome, the popup background,
            // etc. all come from the Template, not from loose property
            // values on the outer control). Result: it always looked like
            // WPF's stock light-themed combo regardless of app theme --
            // exactly what showed up as "doesn't match dark mode" in
            // Circuit Tagger's Tag Family picker, which uses this helper.
            ApplyComboStyle(cb);
            return cb;
        }

        // Bordered card: label above, full-width input below, hint below
        // that. Used for settings-style fields with room to spare.
        protected Border InlineCard(string label, string hint, out TextBox tb)
        {
            var card = new Border
            {
                Background = MeToolsTheme.BrSurface, BorderBrush = MeToolsTheme.BrBorder,
                BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(12),
                Padding = new Thickness(12, 10, 12, 10),
            };
            var sp = new StackPanel();
            sp.Children.Add(new TextBlock { Text = label, FontSize = 10.5, FontWeight = FontWeights.Medium,
                Foreground = MeToolsTheme.BrMuted, Margin = new Thickness(0, 0, 0, 5) });
            var box = new TextBox
            {
                Height = 32, FontSize = 13, FontFamily = new FontFamily("Consolas"), FontWeight = FontWeights.SemiBold,
                Background = MeToolsTheme.BrInput, Foreground = MeToolsTheme.BrInputFg,
                BorderBrush = MeToolsTheme.BrBorder, BorderThickness = new Thickness(1),
                Padding = new Thickness(6, 0, 6, 0), VerticalContentAlignment = VerticalAlignment.Center,
                ToolTip = hint,
            };
            sp.Children.Add(box);
            sp.Children.Add(new TextBlock { Text = hint, FontSize = 10,
                Foreground = MeToolsTheme.BrMuted, TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 4, 0, 0) });
            card.Child = sp; tb = box;
            return card;
        }

        // Bordered card: label above, editable (free-text-with-suggestions)
        // combo below. MaxWidth keeps it from stretching to fill a whole
        // star-sized grid column for what's usually a short value.
        protected Border ComboCard(string label, string hint, out ComboBox cb)
        {
            var card = new Border
            {
                Background = MeToolsTheme.BrSurface, BorderBrush = MeToolsTheme.BrBorder,
                BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(12),
                Padding = new Thickness(12, 8, 12, 8), MaxWidth = 220, HorizontalAlignment = HorizontalAlignment.Left,
            };
            var sp = new StackPanel();
            sp.Children.Add(new TextBlock { Text = label, FontSize = 10.5, FontWeight = FontWeights.Medium,
                Foreground = MeToolsTheme.BrMuted, Margin = new Thickness(0, 0, 0, 5) });
            var combo = new ComboBox
            {
                Height = 28, FontSize = 12, IsEditable = true,
                FontFamily = new FontFamily("Consolas"), FontWeight = FontWeights.SemiBold,
                ToolTip = hint,
            };
            // Same fix as CompactComboStrict above -- was setting a few
            // properties directly but never applying the Style/Template
            // that actually renders a ComboBox, so it always looked like
            // stock WPF regardless of theme (Circuit Tagger's Apartment/
            // Building fields use this helper). IsEditable=true here
            // specifically needed the shared template to gain a real
            // PART_EditableTextBox first (see ApplyComboStyle) -- applying
            // the old template to an editable combo would have silently
            // removed the ability to type into it.
            ApplyComboStyle(combo);
            sp.Children.Add(combo);
            card.Child = sp; cb = combo;
            return card;
        }
    }
}

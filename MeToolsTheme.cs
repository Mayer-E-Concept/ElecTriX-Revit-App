// MeToolsTheme.cs — Gemeinsame Farben & Theme-Event für alle ME-Tools Fenster
// Mayer E-Concept SRL
using System;
using System.Reflection;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Color = System.Windows.Media.Color;

namespace METools
{
    public enum MeTheme { Light, Dark }

    public static class MeToolsTheme
    {
        public static MeTheme Current { get; private set; } = MeTheme.Light;

        // ── Event: alle Fenster gleichzeitig umschalten ───────────────────
        public static event Action ThemeChanged;

        public static void Toggle()
        {
            Current = Current == MeTheme.Light ? MeTheme.Dark : MeTheme.Light;
            ThemeChanged?.Invoke();
        }

        public static string ThemeIcon => Current == MeTheme.Light ? ")" : "O";
        public static string ThemeTip  => Current == MeTheme.Light ? "Dark Mode" : "Light Mode";

        // ── Immer gleich ──────────────────────────────────────────────────
        // Brand palette, aligned with me-concept.ro: deep petrol/teal + a bright
        // cyan accent (circuit-trace cyan) instead of a generic neutral grey UI.
        public static readonly Color CPetrol     = Color.FromRgb(0x18, 0x5f, 0x5f);
        public static readonly Color CPetrolDark = Color.FromRgb(0x12, 0x4d, 0x4d);
        public static readonly Color CPetrolLite = Color.FromRgb(0x1c, 0x6c, 0x6c);
        public static readonly Color CStatusBar  = Color.FromRgb(0x12, 0x4d, 0x4d);
        public static readonly Color COrange     = Color.FromRgb(0xEF, 0x9F, 0x27);
        public static readonly Color CGreen      = Color.FromRgb(0x1D, 0x9E, 0x75);
        public static readonly Color CBlue       = Color.FromRgb(0x37, 0x8A, 0xDD);
        public static readonly Color CRed        = Color.FromRgb(0xA0, 0x30, 0x30);

        // Signature accent — the bright cyan used for circuit traces, stats and
        // primary buttons on the website. Darker/deeper in Light mode so it still
        // reads on a white background; bright/electric in Dark mode.
        public static Color CAccent      => Current == MeTheme.Dark ? Color.FromRgb(0x54,0xDB,0xD3) : Color.FromRgb(0x0F,0x6E,0x5E);
        public static Color CAccentHover => Current == MeTheme.Dark ? Color.FromRgb(0x6F,0xE9,0xE0) : Color.FromRgb(0x0C,0x5A,0x4D);
        // Foreground to put ON TOP of an accent-filled surface (button, badge…)
        public static Color COnAccent    => Current == MeTheme.Dark ? Color.FromRgb(0x06,0x20,0x1D) : Colors.White;

        // ── Theme-abhängig ────────────────────────────────────────────────
        // Dark mode is tinted teal/near-black (like the site's background),
        // not a generic neutral charcoal.
        public static Color CBg        => Current == MeTheme.Dark ? Color.FromRgb(0x0B,0x16,0x16) : Color.FromRgb(0xFB,0xFA,0xF8);
        public static Color CSurface   => Current == MeTheme.Dark ? Color.FromRgb(0x0E,0x1C,0x1C) : Colors.White;
        public static Color CRow       => Current == MeTheme.Dark ? Color.FromRgb(0x11,0x22,0x22) : Colors.White;
        public static Color CRowHov    => Current == MeTheme.Dark ? Color.FromRgb(0x16,0x2C,0x2C) : Color.FromRgb(0xF0,0xF8,0xF8);
        public static Color CBorder    => Current == MeTheme.Dark ? Color.FromArgb(0x18,0xFF,0xFF,0xFF) : Color.FromRgb(0xE3,0xE5,0xE4);
        public static Color CText      => Current == MeTheme.Dark ? Color.FromRgb(0xE9,0xF7,0xF5) : Color.FromRgb(0x23,0x29,0x2B);
        public static Color CMuted     => Current == MeTheme.Dark ? Color.FromRgb(0x6C,0x8B,0x89) : Color.FromRgb(0x76,0x82,0x84);
        public static Color CInput     => Current == MeTheme.Dark ? Color.FromRgb(0x0F,0x1E,0x1E) : Colors.White;
        public static Color CInputFg   => CAccent;
        public static Color CFooter    => Current == MeTheme.Dark ? Color.FromRgb(0x0E,0x1C,0x1C) : Color.FromRgb(0xF0,0xF0,0xF0);
        public static Color CHeader    => Current == MeTheme.Dark ? Color.FromRgb(0x0E,0x1C,0x1C) : Color.FromRgb(0xF8,0xF9,0xFA);
        public static Color CInfoBox   => Current == MeTheme.Dark ? Color.FromRgb(0x0F,0x24,0x22) : Color.FromRgb(0xEA,0xF5,0xF3);
        public static Color CInfoText  => Current == MeTheme.Dark ? Color.FromRgb(0xA9,0xD8,0xD3) : Color.FromRgb(0x14,0x52,0x4C);
        public static Color CActiveBg  => Current == MeTheme.Dark ? Color.FromArgb(0x1F,0x54,0xDB,0xD3) : Color.FromRgb(0xE8,0xF2,0xF0);
        public static Color CActiveFg  => CAccent;
        public static Color CBtnBg     => Current == MeTheme.Dark ? Color.FromRgb(0x11,0x22,0x22) : Colors.White;
        public static Color CBtnBorder => Current == MeTheme.Dark ? Color.FromArgb(0x22,0xFF,0xFF,0xFF) : Color.FromRgb(0xE3,0xE5,0xE4);
        public static Color CSecLine   => Current == MeTheme.Dark ? Color.FromArgb(0x14,0xFF,0xFF,0xFF) : Color.FromRgb(0xEC,0xED,0xEC);
        public static Color CSecText   => Current == MeTheme.Dark ? Color.FromRgb(0x5C,0x82,0x80) : Color.FromRgb(0x8C,0x97,0x96);
        public static Color CTabActive => Current == MeTheme.Dark ? Color.FromRgb(0x0E,0x1C,0x1C) : Colors.White;
        public static Color CTabInact  => Current == MeTheme.Dark ? Color.FromRgb(0x0B,0x16,0x16) : Color.FromRgb(0xF0,0xF0,0xF0);

        // ── Brushes ───────────────────────────────────────────────────────
        public static SolidColorBrush Br(Color c)  => new SolidColorBrush(c);
        public static SolidColorBrush BrPetrol      => Br(CPetrol);
        public static SolidColorBrush BrPetrolDark  => Br(CPetrolDark);
        public static SolidColorBrush BrStatusBar   => Br(CStatusBar);
        // Subtle blueprint-style grid instead of a flat fill -- approved as
        // "Option B" from the preview (24px tile, ~3% petrol/cyan tint,
        // applied everywhere BrBg is used, not just the outer window).
        // Kept as the SAME property name/call sites everywhere -- Background
        // properties on WPF controls are typed as the base Brush class, so
        // widening this from SolidColorBrush to DrawingBrush needed zero
        // changes anywhere else in the suite; confirmed no call site reads
        // a SolidColorBrush-only member (like .Color) off it before making
        // this change.
        public static Brush BrBg => BuildGridBrush();

        private static Brush BuildGridBrush()
        {
            try
            {
                // A 96x96 tile (4x4 of the actual 24px grid cell) rather
                // than tiling the 24px cell directly -- the grain specks
                // below are what make this read as textured paper instead
                // of a machine-stamped grid, and grain baked into a tile
                // no bigger than one grid cell would just repeat every
                // single cell, which defeats the point (it would look like
                // a deliberate DOT PATTERN, not an imperfection). Repeating
                // every 96px instead of every 24px is far less noticeable
                // at normal viewing distance/window size.
                const double cell = 24.0;
                const int cellsPerTile = 4;
                const double tile = cell * cellsPerTile;

                // ~2% -- down from the original ~2.7-3.9%, per direct
                // Bumped back up from ~2% -- direct feedback after seeing
                // it rendered was that it went too faint the other way.
                var lineColor = Current == MeTheme.Dark
                    ? Color.FromArgb(11, 0x54, 0xDB, 0xD3)
                    : Color.FromArgb(9, 0x0F, 0x6E, 0x5E);
                var grainColor = Current == MeTheme.Dark
                    ? Color.FromArgb(15, 0x54, 0xDB, 0xD3)
                    : Color.FromArgb(12, 0x0F, 0x6E, 0x5E);

                var group = new DrawingGroup();

                group.Children.Add(new GeometryDrawing
                {
                    Geometry = new RectangleGeometry(new System.Windows.Rect(0, 0, tile, tile)),
                    Brush = Br(CBg),
                });

                var gridLines = new GeometryGroup();
                for (int i = 1; i <= cellsPerTile; i++)
                {
                    double pos = i * cell;
                    gridLines.Children.Add(new LineGeometry(new System.Windows.Point(pos, 0), new System.Windows.Point(pos, tile)));
                    gridLines.Children.Add(new LineGeometry(new System.Windows.Point(0, pos), new System.Windows.Point(tile, pos)));
                }
                group.Children.Add(new GeometryDrawing
                {
                    Geometry = gridLines,
                    Pen = new Pen(new SolidColorBrush(lineColor), 1),
                });

                // Grain -- a handful of tiny flecks at FIXED positions (a
                // seeded Random, not a fresh one each time), so the texture
                // is identical every time the app runs rather than
                // reshuffling on every window open. This is the actual
                // "make it look less perfect" fix: the grid lines
                // themselves stay perfectly straight (a wobbly/hand-drawn
                // line risks looking like a rendering bug, which isn't
                // something to gamble on without being able to preview it
                // live) -- the imperfection comes from these irregular
                // specks breaking up the otherwise-uniform tile instead.
                var rnd = new Random(20260812);
                var grain = new GeometryGroup();
                for (int i = 0; i < 10; i++)
                {
                    double gx = rnd.NextDouble() * tile;
                    double gy = rnd.NextDouble() * tile;
                    double r  = 0.4 + rnd.NextDouble() * 0.5;
                    grain.Children.Add(new EllipseGeometry(new System.Windows.Point(gx, gy), r, r));
                }
                group.Children.Add(new GeometryDrawing
                {
                    Geometry = grain,
                    Brush = new SolidColorBrush(grainColor),
                });

                return new DrawingBrush
                {
                    Drawing = group,
                    Viewport = new System.Windows.Rect(0, 0, tile, tile),
                    ViewportUnits = BrushMappingMode.Absolute,
                    TileMode = TileMode.Tile,
                };
            }
            catch { return Br(CBg); } // fall back to a flat fill rather than a missing/blank background if anything above ever throws
        }
        public static SolidColorBrush BrSurface     => Br(CSurface);
        public static SolidColorBrush BrRow         => Br(CRow);
        public static SolidColorBrush BrRowHov      => Br(CRowHov);
        public static SolidColorBrush BrBorder      => Br(CBorder);
        public static SolidColorBrush BrText        => Br(CText);
        public static SolidColorBrush BrMuted       => Br(CMuted);
        public static SolidColorBrush BrInput       => Br(CInput);
        public static SolidColorBrush BrInputFg     => Br(CInputFg);
        public static SolidColorBrush BrFooter      => Br(CFooter);
        public static SolidColorBrush BrHeader      => Br(CHeader);
        public static SolidColorBrush BrInfoBox     => Br(CInfoBox);
        public static SolidColorBrush BrInfoText    => Br(CInfoText);
        public static SolidColorBrush BrOrange      => Br(COrange);
        public static SolidColorBrush BrGreen       => Br(CGreen);
        public static SolidColorBrush BrBlue        => Br(CBlue);
        public static SolidColorBrush BrAccent      => Br(CAccent);
        public static SolidColorBrush BrAccentHover => Br(CAccentHover);
        public static SolidColorBrush BrOnAccent    => Br(COnAccent);
        public static SolidColorBrush BrActiveBg    => Br(CActiveBg);
        public static SolidColorBrush BrActiveFg    => Br(CActiveFg);
        public static SolidColorBrush BrBtnBg       => Br(CBtnBg);
        public static SolidColorBrush BrBtnBorder   => Br(CBtnBorder);
        public static SolidColorBrush BrSecLine     => Br(CSecLine);
        public static SolidColorBrush BrSecText     => Br(CSecText);

        // ── Elevated-teal / midnight-cyan redesign additions ────────────────
        // Primary action button fill -- same gradient as the header in light
        // mode (one consistent "petrol surface" language across the window);
        // solid accent cyan in dark mode, matching CActiveFg/COnAccent so a
        // primary button and an active toggle read as the same family of
        // "this is the emphasized thing" surface.
        public static Brush BrPrimaryFill => Current == MeTheme.Dark
            ? (Brush)Br(CAccent)
            : new LinearGradientBrush(CPetrolLite, CPetrolDark, new System.Windows.Point(0, 0), new System.Windows.Point(0, 1));

        public static Color CPrimaryFg => Current == MeTheme.Dark ? COnAccent : Colors.White;
        public static SolidColorBrush BrPrimaryFg => Br(CPrimaryFg);

        // Header/footer now share the body's own background (BrBg) instead
        // of a solid petrol fill, so the text/icons that used to be a flat
        // white (which only worked against a dark solid fill) need their
        // own theme-aware colors instead.
        public static Color CTitleText      => Current == MeTheme.Dark ? CText  : CPetrolDark;
        public static Color CTitleTextMuted => Current == MeTheme.Dark ? CMuted : Color.FromRgb(0x6B, 0x85, 0x84);
        // Caret/window-control hover overlay -- a dark tint in Light mode
        // (the background is light there, so hover needs to darken), a
        // light tint in Dark mode (the reverse).
        public static Color CTitleOverlay      => Current == MeTheme.Dark ? Color.FromArgb(24, 255, 255, 255) : Color.FromArgb(18, 0x12, 0x4D, 0x4D);
        public static Color CTitleOverlayHover => Current == MeTheme.Dark ? Color.FromArgb(46, 255, 255, 255) : Color.FromArgb(34, 0x12, 0x4D, 0x4D);
        public static SolidColorBrush BrTitleText          => Br(CTitleText);
        public static SolidColorBrush BrTitleTextMuted     => Br(CTitleTextMuted);
        public static SolidColorBrush BrTitleOverlay       => Br(CTitleOverlay);
        public static SolidColorBrush BrTitleOverlayHover  => Br(CTitleOverlayHover);

        // The actual "no more solid bar" idea: a soft petrol/cyan wash
        // that's strongest right at the seam between the bar and the body,
        // fading to fully transparent by the window's own outer edge --
        // approved directly from a preview showing exactly this. Light
        // mode washes with petrol (the brand's own color); Dark mode uses
        // the cyan accent instead, since petrol is barely distinguishable
        // from Dark mode's own near-black background -- extending the
        // same petrol-in-Light/cyan-in-Dark pairing already used
        // everywhere else in this theme, not a literal "use petrol in
        // both modes" reading of the request.
        //
        // Peak alpha is deliberately DIFFERENT per theme, not the same
        // number reused with a different base color -- confirmed after
        // seeing it rendered that the identical ~16% alpha read as clearly
        // visible in Dark mode (bright cyan against near-black) but nearly
        // invisible in Light mode (a dark color at low alpha over a light
        // background has much less perceptual contrast than the reverse).
        // Left at a moderate bump rather than a large one: since the
        // gradient fades continuously and the title/status text sits
        // roughly mid-way through that fade rather than right at the
        // strongest edge, the text itself only ever sees roughly half of
        // this peak value either way.
        public static Brush HeaderWashBrush()
        {
            bool dark = Current == MeTheme.Dark;
            var c = dark ? CAccent : CPetrolDark;
            byte peak = dark ? (byte)41 : (byte)85;
            return new LinearGradientBrush(
                Color.FromArgb(peak, c.R, c.G, c.B), // strongest at the bottom seam
                Color.FromArgb(0, c.R, c.G, c.B),    // fully transparent at the top edge
                new System.Windows.Point(0, 1),
                new System.Windows.Point(0, 0));
        }

        public static Brush FooterWashBrush()
        {
            bool dark = Current == MeTheme.Dark;
            var c = dark ? CAccent : CPetrolDark;
            byte peak = dark ? (byte)41 : (byte)85;
            return new LinearGradientBrush(
                Color.FromArgb(peak, c.R, c.G, c.B), // strongest at the top seam
                Color.FromArgb(0, c.R, c.G, c.B),    // fully transparent at the bottom edge
                new System.Windows.Point(0, 0),
                new System.Windows.Point(0, 1));
        }

        // Soft tinted fill for secondary/"ghost" buttons and inactive
        // segmented-toggle options -- no border at all, just a very subtle
        // background tint, closer to how macOS toolbar buttons sit quietly
        // until interacted with instead of outlining everything.
        public static Color CSoftFill      => Current == MeTheme.Dark ? Color.FromArgb(14, 255, 255, 255) : Color.FromArgb(14, 0x12, 0x4D, 0x4D);
        public static Color CSoftFillHover => Current == MeTheme.Dark ? Color.FromArgb(24, 255, 255, 255) : Color.FromArgb(24, 0x12, 0x4D, 0x4D);
        public static SolidColorBrush BrSoftFill      => Br(CSoftFill);
        public static SolidColorBrush BrSoftFillHover => Br(CSoftFillHover);

        // A small, contained shadow/glow for the primary action button only
        // -- not the whole window. Unlike a window-edge shadow (which this
        // codebase can't do safely: AllowsTransparency is off, so anything
        // rendered past the window's own rectangle gets hard-clipped, not
        // softly shown), a button sitting well inside the window's content
        // area has plenty of margin for this to render normally. Light
        // mode gets a soft dark shadow (the classic "raised button" cue);
        // dark mode gets a subtle accent-tinted glow instead, since a dark
        // shadow is invisible against a near-black background anyway.
        public static System.Windows.Media.Effects.DropShadowEffect PrimaryButtonGlow()
        {
            return new System.Windows.Media.Effects.DropShadowEffect
            {
                Direction = 270,
                ShadowDepth = Current == MeTheme.Dark ? 0 : 3,
                BlurRadius = Current == MeTheme.Dark ? 14 : 10,
                Opacity = Current == MeTheme.Dark ? 0.45 : 0.30,
                Color = Current == MeTheme.Dark ? CAccent : CPetrolDark,
                RenderingBias = System.Windows.Media.Effects.RenderingBias.Performance,
            };
        }

        // ── Logo (gecacht) ────────────────────────────────────────────────
        private static BitmapImage _logo;
        public static BitmapImage LoadLogo()
        {
            if (_logo != null) return _logo;
            try
            {
                var asm    = Assembly.GetExecutingAssembly();
                var stream = asm.GetManifestResourceStream(
                    "METools.Icons.base_icon_transparent_background.png");
                if (stream == null) return null;
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.StreamSource = stream;
                bmp.CacheOption  = BitmapCacheOption.OnLoad;
                bmp.EndInit(); bmp.Freeze();
                _logo = bmp;
            }
            catch { }
            return _logo;
        }
    }
}

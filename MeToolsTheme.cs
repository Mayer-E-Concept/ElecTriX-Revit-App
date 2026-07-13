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
        public static readonly Color CStatusBar  = Color.FromRgb(0x15, 0x58, 0x58);
        public static readonly Color COrange     = Color.FromRgb(0xEF, 0x9F, 0x27);
        public static readonly Color CGreen      = Color.FromRgb(0x1D, 0x9E, 0x75);
        public static readonly Color CBlue       = Color.FromRgb(0x37, 0x8A, 0xDD);
        public static readonly Color CRed        = Color.FromRgb(0xA0, 0x30, 0x30);

        // Signature accent — the bright cyan used for circuit traces, stats and
        // primary buttons on the website. Darker/deeper in Light mode so it still
        // reads on a white background; bright/electric in Dark mode.
        public static Color CAccent      => Current == MeTheme.Dark ? Color.FromRgb(0x54,0xDB,0xD3) : Color.FromRgb(0x0F,0x8F,0x87);
        public static Color CAccentHover => Current == MeTheme.Dark ? Color.FromRgb(0x3C,0xB8,0xB1) : Color.FromRgb(0x0B,0x6F,0x68);
        // Foreground to put ON TOP of an accent-filled surface (button, badge…)
        public static Color COnAccent    => Current == MeTheme.Dark ? Color.FromRgb(0x06,0x1E,0x1C) : Colors.White;

        // ── Theme-abhängig ────────────────────────────────────────────────
        // Dark mode is tinted teal/near-black (like the site's background),
        // not a generic neutral charcoal.
        public static Color CBg        => Current == MeTheme.Dark ? Color.FromRgb(0x0A,0x1E,0x1E) : Color.FromRgb(0xF4,0xF5,0xF6);
        public static Color CSurface   => Current == MeTheme.Dark ? Color.FromRgb(0x10,0x2B,0x2B) : Colors.White;
        public static Color CRow       => Current == MeTheme.Dark ? Color.FromRgb(0x13,0x30,0x30) : Colors.White;
        public static Color CRowHov    => Current == MeTheme.Dark ? Color.FromRgb(0x1A,0x3D,0x3D) : Color.FromRgb(0xF0,0xF8,0xF8);
        public static Color CBorder    => Current == MeTheme.Dark ? Color.FromRgb(0x24,0x4A,0x49) : Color.FromRgb(0xD0,0xD5,0xD9);
        public static Color CText      => Current == MeTheme.Dark ? Color.FromRgb(0xE9,0xF4,0xF3) : Color.FromRgb(0x1E,0x25,0x28);
        public static Color CMuted     => Current == MeTheme.Dark ? Color.FromRgb(0x86,0xA8,0xA6) : Color.FromRgb(0x6B,0x78,0x80);
        public static Color CInput     => Current == MeTheme.Dark ? Color.FromRgb(0x0D,0x26,0x26) : Colors.White;
        public static Color CInputFg   => CAccent;
        public static Color CFooter    => Current == MeTheme.Dark ? Color.FromRgb(0x0D,0x24,0x24) : Color.FromRgb(0xF0,0xF0,0xF0);
        public static Color CHeader    => Current == MeTheme.Dark ? Color.FromRgb(0x0F,0x28,0x28) : Color.FromRgb(0xF8,0xF9,0xFA);
        public static Color CInfoBox   => Current == MeTheme.Dark ? Color.FromRgb(0x0A,0x2E,0x2D) : Color.FromRgb(0xE0,0xF0,0xF0);
        public static Color CInfoText  => Current == MeTheme.Dark ? CAccent : Color.FromRgb(0x0D,0x3D,0x3D);
        public static Color CActiveBg  => Current == MeTheme.Dark ? Color.FromRgb(0x0A,0x35,0x33) : Color.FromRgb(0xE0,0xF0,0xF0);
        public static Color CActiveFg  => CAccent;
        public static Color CBtnBg     => Current == MeTheme.Dark ? Color.FromRgb(0x13,0x30,0x30) : Colors.White;
        public static Color CBtnBorder => Current == MeTheme.Dark ? Color.FromRgb(0x2A,0x55,0x54) : Color.FromRgb(0xD0,0xD5,0xD9);
        public static Color CSecLine   => Current == MeTheme.Dark ? Color.FromRgb(0x24,0x4A,0x49) : Color.FromRgb(0xD0,0xD5,0xD9);
        public static Color CSecText   => Current == MeTheme.Dark ? Color.FromRgb(0x5C,0x82,0x80) : Color.FromRgb(0x80,0x90,0x90);
        public static Color CTabActive => Current == MeTheme.Dark ? Color.FromRgb(0x10,0x2B,0x2B) : Colors.White;
        public static Color CTabInact  => Current == MeTheme.Dark ? Color.FromRgb(0x0A,0x1E,0x1E) : Color.FromRgb(0xF0,0xF0,0xF0);

        // ── Brushes ───────────────────────────────────────────────────────
        public static SolidColorBrush Br(Color c)  => new SolidColorBrush(c);
        public static SolidColorBrush BrPetrol      => Br(CPetrol);
        public static SolidColorBrush BrPetrolDark  => Br(CPetrolDark);
        public static SolidColorBrush BrStatusBar   => Br(CStatusBar);
        public static SolidColorBrush BrBg          => Br(CBg);
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

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
        public static readonly Color CPetrol     = Color.FromRgb(0x18, 0x5f, 0x5f);
        public static readonly Color CPetrolDark = Color.FromRgb(0x12, 0x4d, 0x4d);
        public static readonly Color CStatusBar  = Color.FromRgb(0x15, 0x58, 0x58);
        public static readonly Color COrange     = Color.FromRgb(0xEF, 0x9F, 0x27);
        public static readonly Color CGreen      = Color.FromRgb(0x1D, 0x9E, 0x75);
        public static readonly Color CBlue       = Color.FromRgb(0x37, 0x8A, 0xDD);
        public static readonly Color CRed        = Color.FromRgb(0xA0, 0x30, 0x30);

        // ── Theme-abhängig ────────────────────────────────────────────────
        public static Color CBg        => Current == MeTheme.Dark ? Color.FromRgb(0x1E,0x1E,0x1E) : Color.FromRgb(0xF4,0xF5,0xF6);
        public static Color CSurface   => Current == MeTheme.Dark ? Color.FromRgb(0x2A,0x2A,0x2A) : Colors.White;
        public static Color CRow       => Current == MeTheme.Dark ? Color.FromRgb(0x2F,0x2F,0x2F) : Colors.White;
        public static Color CRowHov    => Current == MeTheme.Dark ? Color.FromRgb(0x38,0x38,0x38) : Color.FromRgb(0xF0,0xF8,0xF8);
        public static Color CBorder    => Current == MeTheme.Dark ? Color.FromRgb(0x44,0x44,0x44) : Color.FromRgb(0xD0,0xD5,0xD9);
        public static Color CText      => Current == MeTheme.Dark ? Color.FromRgb(0xE8,0xE8,0xE8) : Color.FromRgb(0x1E,0x25,0x28);
        public static Color CMuted     => Current == MeTheme.Dark ? Color.FromRgb(0x88,0x88,0x88) : Color.FromRgb(0x6B,0x78,0x80);
        public static Color CInput     => Current == MeTheme.Dark ? Color.FromRgb(0x28,0x28,0x28) : Colors.White;
        public static Color CInputFg   => Current == MeTheme.Dark ? Color.FromRgb(0x5D,0xCA,0xA5) : Color.FromRgb(0x18,0x5F,0x5F);
        public static Color CFooter    => Current == MeTheme.Dark ? Color.FromRgb(0x22,0x22,0x22) : Color.FromRgb(0xF0,0xF0,0xF0);
        public static Color CHeader    => Current == MeTheme.Dark ? Color.FromRgb(0x25,0x25,0x25) : Color.FromRgb(0xF8,0xF9,0xFA);
        public static Color CInfoBox   => Current == MeTheme.Dark ? Color.FromRgb(0x0F,0x2A,0x2A) : Color.FromRgb(0xE0,0xF0,0xF0);
        public static Color CInfoText  => Current == MeTheme.Dark ? Color.FromRgb(0x5D,0xCA,0xA5) : Color.FromRgb(0x0D,0x3D,0x3D);
        public static Color CActiveBg  => Current == MeTheme.Dark ? Color.FromRgb(0x0F,0x35,0x35) : Color.FromRgb(0xE0,0xF0,0xF0);
        public static Color CActiveFg  => Current == MeTheme.Dark ? Color.FromRgb(0x5D,0xCA,0xA5) : Color.FromRgb(0x0D,0x3D,0x3D);
        public static Color CBtnBg     => Current == MeTheme.Dark ? Color.FromRgb(0x33,0x33,0x33) : Colors.White;
        public static Color CBtnBorder => Current == MeTheme.Dark ? Color.FromRgb(0x55,0x55,0x55) : Color.FromRgb(0xD0,0xD5,0xD9);
        public static Color CSecLine   => Current == MeTheme.Dark ? Color.FromRgb(0x44,0x44,0x44) : Color.FromRgb(0xD0,0xD5,0xD9);
        public static Color CSecText   => Current == MeTheme.Dark ? Color.FromRgb(0x66,0x66,0x66) : Color.FromRgb(0x80,0x90,0x90);
        public static Color CTabActive => Current == MeTheme.Dark ? Color.FromRgb(0x2A,0x2A,0x2A) : Colors.White;
        public static Color CTabInact  => Current == MeTheme.Dark ? Color.FromRgb(0x22,0x22,0x22) : Color.FromRgb(0xF0,0xF0,0xF0);

        // ── Brushes ───────────────────────────────────────────────────────
        public static SolidColorBrush Br(Color c)  => new SolidColorBrush(c);
        public static SolidColorBrush BrPetrol      => Br(CPetrol);
        public static SolidColorBrush BrPetrolDark  => Br(CPetrolDark);
        public static SolidColorBrush BrStatusBar   => Br(CStatusBar);
        public static SolidColorBrush BrBg          => Br(CBg);
        public static SolidColorBrush BrSurface     => Br(CSurface);
        public static SolidColorBrush BrRow         => Br(CRow);
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

// RibbonLicenseWatcher.cs -- ME-Tools | Ribbon license-state sync
// Mayer E-Concept SRL
//
// Visually greys out the ribbon buttons for tools that require a full
// license (everything except the free tier: Family Placer, Family Browser,
// Lamp Placer, Statistics, Activity & Time, Fix Level), so someone on the
// trial can see upfront which tools are locked without having to click each
// one first.
//
// Buttons stay Enabled = true on purpose, even when greyed out -- clicking
// one still runs its Command normally, which calls
// LicenseManager.CheckFullAccessOrExplain() and shows the "needs a license"
// prompt itself. Disabling the button instead would make it unclickable,
// which would make that prompt unreachable.
//
// BUG FIXED HERE: this used to capture each button's "normal" icon at
// Register() time, immediately after the button was created -- but
// RibbonThemeWatcher.Init() (which applies the correct light/dark-theme
// icon) runs LATER, after all buttons are registered. That meant the
// "normal" icon cached here was the stale, pre-theme-correction one, and
// RefreshAll() kept reapplying that stale icon even for a fully licensed
// user -- confirmed live as exactly this symptom (icons showing as solid
// black rectangles regardless of actual license state). Fixed by capturing
// the normal icon lazily, the first time RefreshAll() actually runs, which
// in practice is always after RibbonThemeWatcher.Init() has already set the
// theme-correct icon (see the call order in App.OnStartup).
//
// Also switched from a generic WPF greyscale conversion (FormatConvertedBitmap
// with a Gray destination format) to manual pixel manipulation: flattening
// every visible pixel to one fixed dark grey, alpha untouched. A generic
// greyscale conversion keeps each pixel's original brightness, which for an
// icon built mostly from one accent color (this app's teal/petrol) barely
// changes how it reads at a glance -- not the flat, uniform "dark grey,
// accent included" look actually asked for. It's also a more standard,
// widely-supported pixel format (Bgra32) than the Gray formats, which may
// have been part of the black-icon symptom above as well.
using System;
using System.Collections.Generic;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Autodesk.Revit.UI;

namespace METools
{
    public static class RibbonLicenseWatcher
    {
        // The uniform grey every visible pixel gets flattened to (RGB, all
        // channels equal). ~33% brightness -- dark enough to clearly read
        // as "unavailable" against this ribbon's dark background, without
        // going all the way to black (indistinguishable from a rendering
        // failure, which is exactly what prompted this fix).
        private const byte GreyLevel = 85;

        private class Entry
        {
            public PushButton   Button;
            public ImageSource  Normal16, Normal32;   // captured lazily -- see remarks above
            public ImageSource  Grey16, Grey32;       // computed lazily from whichever Normal was captured
            public bool         Captured;
        }

        private static readonly List<Entry> _entries = new List<Entry>();

        /// <summary>
        /// Register a full-license-tool ribbon button. Call once per button,
        /// right after it's added to its panel in App.cs -- order relative to
        /// RibbonThemeWatcher.Register/Init doesn't matter, since the actual
        /// icon capture is deferred to first use (see remarks above).
        /// </summary>
        public static void Register(PushButton button)
        {
            if (button == null) return;
            _entries.Add(new Entry { Button = button });
        }

        /// <summary>
        /// Re-applies the correct (normal or greyed) icon to every registered
        /// button based on the CURRENT license state. Call once at startup
        /// (after RibbonThemeWatcher.Init(), so the icon captured on first
        /// use here is already theme-correct) and again any time license
        /// state could have changed -- activation or deactivation.
        /// </summary>
        public static void RefreshAll()
        {
            bool licensed = LicenseManager.IsLicensed();
            foreach (var e in _entries)
            {
                try
                {
                    if (!e.Captured)
                    {
                        e.Normal16 = SafeGet(() => e.Button.Image);
                        e.Normal32 = SafeGet(() => e.Button.LargeImage);
                        e.Captured = true;
                    }

                    if (e.Normal16 != null)
                        e.Button.Image = licensed ? e.Normal16 : (e.Grey16 ??= ToGrey(e.Normal16));
                    if (e.Normal32 != null)
                        e.Button.LargeImage = licensed ? e.Normal32 : (e.Grey32 ??= ToGrey(e.Normal32));

                    // Tooltip note appended/removed rather than replacing
                    // the whole tooltip, so the tool's own description
                    // (set once in App.cs) survives every refresh.
                    var baseToolTip = StripLicenseNote(e.Button.ToolTip);
                    e.Button.ToolTip = licensed ? baseToolTip : baseToolTip + LicenseNoteSuffix;
                }
                catch { }
            }
        }

        private const string LicenseNoteSuffix = "\n\n\u26A0 Requires a license (not part of the free tier).";

        private static string StripLicenseNote(string toolTip)
        {
            if (string.IsNullOrEmpty(toolTip)) return toolTip ?? "";
            int idx = toolTip.IndexOf(LicenseNoteSuffix, StringComparison.Ordinal);
            return idx >= 0 ? toolTip.Substring(0, idx) : toolTip;
        }

        private static T SafeGet<T>(Func<T> get)
        {
            try { return get(); } catch { return default; }
        }

        // Flattens every pixel with any visible alpha to a single fixed
        // dark grey (GreyLevel on all three channels), leaving alpha
        // untouched so transparent/antialiased edges stay exactly as
        // crisp as the original icon. Converts through Bgra32 first --
        // a standard, universally-supported 4-bytes-per-pixel format --
        // regardless of whatever format the source PNG decoded to, so the
        // byte layout below (B, G, R, A per pixel) is always guaranteed.
        private static ImageSource ToGrey(ImageSource src)
        {
            try
            {
                if (!(src is BitmapSource bmp)) return src;
                var converted = new FormatConvertedBitmap(bmp, PixelFormats.Bgra32, null, 0);

                int width = converted.PixelWidth, height = converted.PixelHeight;
                int stride = width * 4;
                var pixels = new byte[stride * height];
                converted.CopyPixels(pixels, stride, 0);

                for (int i = 0; i < pixels.Length; i += 4)
                {
                    byte alpha = pixels[i + 3];
                    if (alpha == 0) continue; // fully transparent -- nothing to flatten
                    pixels[i]     = GreyLevel; // B
                    pixels[i + 1] = GreyLevel; // G
                    pixels[i + 2] = GreyLevel; // R
                    // alpha (pixels[i + 3]) left exactly as it was
                }

                var result = BitmapSource.Create(width, height, converted.DpiX, converted.DpiY,
                    PixelFormats.Bgra32, null, pixels, stride);
                result.Freeze();
                return result;
            }
            catch { return src; }
        }
    }
}

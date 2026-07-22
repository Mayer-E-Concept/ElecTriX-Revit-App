// RibbonThemeWatcher.cs -- ME-Tools | Ribbon icon theme sync
// Mayer E-Concept SRL
//
// Detects Revit's active UI theme (Light / Dark) and swaps ribbon button
// icons to match, using Autodesk.Windows.ComponentManager.CurrentTheme.
// Requires a reference to AdWindows.dll (already shipped with Revit, found in
// the Revit install folder, e.g. C:\Program Files\Autodesk\Revit 2025\AdWindows.dll).
//
// Usage in App.cs, after creating each PushButton:
//     RibbonThemeWatcher.Register(myPushButton, "icon_fp");
// The registered key must match the icon file naming convention:
//     icon_fp_light_16.png / icon_fp_light_32.png   (Revit Light theme)
//     icon_fp_dark_16.png  / icon_fp_dark_32.png    (Revit Dark theme)
//
// Call RibbonThemeWatcher.Init() once in App.OnStartup (after all buttons
// are registered) to apply the correct icons immediately and subscribe to
// Revit's theme-changed event so icons update live if the user switches
// Revit's theme without restarting.
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Windows.Media.Imaging;
using Autodesk.Revit.UI;

namespace METools
{
    public static class RibbonThemeWatcher
    {
        // One entry per registered ribbon button.
        private class Entry
        {
            public PushButton Button;
            public string     IconKey; // e.g. "icon_fp" -> resolves to icon_fp_light_16.png etc.
        }

        private static readonly List<Entry> _entries = new List<Entry>();
        private static bool _subscribed;

        /// <summary>
        /// Register a ribbon button so its icon follows Revit's active theme.
        /// Call once per button, right after adding it to the panel.
        /// </summary>
        public static void Register(PushButton button, string iconKey)
        {
            if (button == null || string.IsNullOrEmpty(iconKey)) return;
            _entries.Add(new Entry { Button = button, IconKey = iconKey });
        }

        /// <summary>
        /// Applies the correct icon set immediately and subscribes to Revit's
        /// UIThemeChanged event (if available) so icons update live.
        /// Call once from App.OnStartup after all Register() calls.
        /// </summary>
        public static void Init()
        {
            ApplyCurrentTheme();

            if (_subscribed) return;
            try
            {
                // The exact live theme-changed event name varies between Revit
                // versions/patches (UIThemeChanged / UIThemeUpdated / ThemeChanged
                // have all appeared). Look it up via reflection instead of a
                // hard compile-time reference, so this never breaks the build
                // regardless of which AdWindows.dll ships with the installed
                // Revit version. If nothing matches, icons are still correct
                // at startup -- they just won't live-update mid-session.
                var cmType = typeof(Autodesk.Windows.ComponentManager);
                System.Reflection.EventInfo evt =
                       cmType.GetEvent("UIThemeChanged")
                    ?? cmType.GetEvent("UIThemeUpdated")
                    ?? cmType.GetEvent("ThemeChanged");

                if (evt != null)
                {
                    var method = typeof(RibbonThemeWatcher).GetMethod(
                        nameof(OnUIThemeChanged),
                        System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
                    var del = Delegate.CreateDelegate(evt.EventHandlerType, method);
                    evt.AddEventHandler(null, del);
                    _subscribed = true;
                }
            }
            catch
            {
                // Live updates unavailable on this Revit version -- startup
                // icons are still correct via ApplyCurrentTheme() above.
            }
        }

        private static void OnUIThemeChanged(object sender, EventArgs e) => ApplyCurrentTheme();

        private static bool IsDarkTheme()
        {
            try
            {
                var theme = Autodesk.Windows.ComponentManager.CurrentTheme;
                if (theme == null) return false; // default to light icons if theme can't be detected

                var type = theme.GetType();

                // "Name"/"DisplayName"/"ThemeName"/"Id" usually say "Dark"/"Light"
                // directly even when ToString() itself isn't overridden.
                foreach (var propName in new[] { "Name", "DisplayName", "ThemeName", "Id" })
                {
                    var p = type.GetProperty(propName);
                    if (p == null) continue;
                    var val = p.GetValue(theme)?.ToString() ?? "";
                    if (val.IndexOf("Dark", StringComparison.OrdinalIgnoreCase) >= 0) return true;
                    if (val.IndexOf("Light", StringComparison.OrdinalIgnoreCase) >= 0) return false;
                }

                return false; // no match -- default to light icons
            }
            catch
            {
                return false; // default to light icons if theme can't be detected
            }
        }

        private static void ApplyCurrentTheme()
        {
            bool dark = IsDarkTheme();
            string variant = dark ? "dark" : "light";

            foreach (var entry in _entries)
            {
                try
                {
                    var img16 = LoadIcon($"{entry.IconKey}_{variant}_16.png");
                    var img32 = LoadIcon($"{entry.IconKey}_{variant}_32.png");
                    entry.Button.Image      = img16;
                    entry.Button.LargeImage = img32;
                }
                catch { }
            }
        }

        private static System.Windows.Media.ImageSource LoadIcon(string fileName)
        {
            try
            {
                var stream = Assembly.GetExecutingAssembly()
                    .GetManifestResourceStream($"METools.Icons.{fileName}");
                if (stream == null) return null;
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.StreamSource = stream;
                bmp.CacheOption  = BitmapCacheOption.OnLoad;
                bmp.EndInit();
                bmp.Freeze();
                return bmp;
            }
            catch { return null; }
        }
    }
}

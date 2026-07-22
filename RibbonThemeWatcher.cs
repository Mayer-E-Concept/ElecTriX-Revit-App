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
            public System.Windows.Media.Color? GroupTint; // optional accent bar color for panel grouping
        }

        private static readonly List<Entry> _entries = new List<Entry>();
        private static bool _subscribed;

        /// <summary>
        /// Register a ribbon button so its icon follows Revit's active theme.
        /// Call once per button, right after adding it to the panel.
        /// groupTint (optional): draws a thin colored accent bar under the icon,
        /// so buttons belonging to the same ribbon panel group are recognizable
        /// by color at a glance -- since Revit itself doesn't support coloring
        /// the panel title bars.
        /// </summary>
        public static void Register(PushButton button, string iconKey, System.Windows.Media.Color? groupTint = null)
        {
            if (button == null || string.IsNullOrEmpty(iconKey)) return;
            _entries.Add(new Entry { Button = button, IconKey = iconKey, GroupTint = groupTint });
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
                if (theme == null)
                {
                    LogDiag("Theme object is NULL -- defaulting to light icons");
                    return false;
                }

                var type = theme.GetType();
                LogDiag($"Theme runtime type: {type.FullName}");

                // Dump every readable public property/field so we can see what's
                // actually available (Name, DisplayName, a background Color, etc.)
                // instead of guessing again.
                foreach (var prop in type.GetProperties(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance))
                {
                    try
                    {
                        var val = prop.GetValue(theme);
                        LogDiag($"  prop {prop.Name} ({prop.PropertyType.Name}) = {val}");
                    }
                    catch (Exception ex) { LogDiag($"  prop {prop.Name}: <error reading: {ex.Message}>"); }
                }
                foreach (var fld in type.GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance))
                {
                    try
                    {
                        var val = fld.GetValue(theme);
                        LogDiag($"  field {fld.Name} ({fld.FieldType.Name}) = {val}");
                    }
                    catch (Exception ex) { LogDiag($"  field {fld.Name}: <error reading: {ex.Message}>"); }
                }

                // Best-effort heuristics using whatever we find above. Try a "Name"
                // or similar string property first; these usually say "Dark"/"Light"
                // directly even when ToString() itself isn't overridden.
                foreach (var propName in new[] { "Name", "DisplayName", "ThemeName", "Id" })
                {
                    var p = type.GetProperty(propName);
                    if (p == null) continue;
                    var val = p.GetValue(theme)?.ToString() ?? "";
                    if (val.IndexOf("Dark", StringComparison.OrdinalIgnoreCase) >= 0)
                    { LogDiag($"Matched dark via property '{propName}'='{val}'"); return true; }
                    if (val.IndexOf("Light", StringComparison.OrdinalIgnoreCase) >= 0)
                    { LogDiag($"Matched light via property '{propName}'='{val}'"); return false; }
                }

                LogDiag("No Name/DisplayName/ThemeName/Id match found -- defaulting to light icons. See property dump above.");
                return false;
            }
            catch (Exception ex)
            {
                LogDiag($"Theme detection FAILED: {ex.Message} -- defaulting to light icons");
                return false; // default to light icons if theme can't be detected
            }
        }

        // Writes a one-line diagnostic to %APPDATA%\METools\theme-debug.log so we
        // can see exactly what's happening without needing to attach a debugger.
        // Safe to delete this method (and its two call sites) once icons are
        // confirmed working correctly.
        private static void LogDiag(string msg)
        {
            try
            {
                var dir = System.IO.Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "METools");
                System.IO.Directory.CreateDirectory(dir);
                var path = System.IO.Path.Combine(dir, "theme-debug.log");
                System.IO.File.AppendAllText(path, $"{DateTime.Now:HH:mm:ss}  {msg}\r\n");
            }
            catch { }
        }

        private static void ApplyCurrentTheme()
        {
            bool dark = IsDarkTheme();
            string variant = dark ? "dark" : "light";
            LogDiag($"Applying variant='{variant}' to {_entries.Count} button(s)");

            foreach (var entry in _entries)
            {
                try
                {
                    var img16 = LoadIcon($"{entry.IconKey}_{variant}_16.png");
                    var img32 = LoadIcon($"{entry.IconKey}_{variant}_32.png");
                    if (img16 == null || img32 == null)
                        LogDiag($"  '{entry.IconKey}': MISSING resource(s) for variant '{variant}' (16={(img16==null?"NULL":"ok")}, 32={(img32==null?"NULL":"ok")})");
                    if (entry.GroupTint.HasValue)
                    {
                        img16 = AddAccentBar(img16, entry.GroupTint.Value, 16);
                        img32 = AddAccentBar(img32, entry.GroupTint.Value, 32);
                    }
                    entry.Button.Image      = img16;
                    entry.Button.LargeImage = img32;
                }
                catch (Exception ex) { LogDiag($"  '{entry.IconKey}': EXCEPTION {ex.Message}"); }
            }
        }

        // Draws the icon as-is, then a thin rounded accent bar near the bottom
        // edge in the group's color -- a fully-supported way (plain WPF drawing,
        // no ribbon internals) to make panel groups recognizable by color, since
        // Revit itself doesn't expose a way to color panel title bars.
        private static System.Windows.Media.ImageSource AddAccentBar(
            System.Windows.Media.ImageSource baseIcon, System.Windows.Media.Color color, int size)
        {
            if (baseIcon == null) return null;
            try
            {
                var visual = new System.Windows.Media.DrawingVisual();
                using (var dc = visual.RenderOpen())
                {
                    dc.DrawImage(baseIcon, new System.Windows.Rect(0, 0, size, size));

                    double barHeight = size <= 16 ? 2.0 : 3.5;
                    double barMargin = size <= 16 ? 1.0 : 2.0;
                    var barRect = new System.Windows.Rect(
                        barMargin, size - barHeight - barMargin,
                        size - barMargin * 2, barHeight);
                    var brush = new System.Windows.Media.SolidColorBrush(color);
                    dc.DrawRoundedRectangle(brush, null, barRect, barHeight / 2, barHeight / 2);
                }
                var rtb = new System.Windows.Media.Imaging.RenderTargetBitmap(
                    size, size, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
                rtb.Render(visual);
                rtb.Freeze();
                return rtb;
            }
            catch { return baseIcon; }
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

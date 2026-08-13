// RibbonLanguageWatcher.cs -- ME-Tools | Ribbon label language sync
// Mayer E-Concept SRL
//
// Every ribbon button's display text used to be a hardcoded English
// string baked into its PushButtonData at startup, with no path back to
// Strings.cs at all -- so changing the language in Settings updated
// every tool WINDOW correctly (each one calls S.SetLanguage/S._ fresh
// when it opens) but left the ribbon itself stuck in English, since
// nothing ever told the already-created PushButton objects to re-read
// their label. Confirmed live: this was a real, reported gap, not a
// hypothetical.
//
// Modeled directly on RibbonThemeWatcher's own registration pattern
// (register once per button at startup, keep an internal list, expose a
// method that reapplies every registered value) -- PushButton.ItemText
// is a writable property after creation, the same way .Image/.LargeImage
// are, so refreshing labels live works the same way icon refresh already
// does.
//
// Usage in App.cs, after creating each PushButton:
//     RibbonLanguageWatcher.Register(myPushButton, "ribbon.family_placer");
// Then call RibbonLanguageWatcher.Refresh() once from wherever the
// language actually changes (SettingsWindow's language dropdown handler).
using System.Collections.Generic;
using Autodesk.Revit.UI;

namespace METools
{
    public static class RibbonLanguageWatcher
    {
        private class Entry
        {
            public PushButton Button;
            public string     TextKey; // Strings.cs key, e.g. "ribbon.family_placer"
        }

        private static readonly List<Entry> _entries = new List<Entry>();

        /// <summary>
        /// Register a ribbon button so its displayed text follows the
        /// current language. Call once per button, right after adding it
        /// to the panel -- mirrors RibbonThemeWatcher.Register.
        /// </summary>
        public static void Register(PushButton button, string textKey)
        {
            if (button == null || string.IsNullOrEmpty(textKey)) return;
            _entries.Add(new Entry { Button = button, TextKey = textKey });
        }

        /// <summary>
        /// Re-applies every registered button's text using the CURRENT
        /// language (S._ reads whatever S.SetLanguage was last called
        /// with) -- call this immediately after S.SetLanguage, not just
        /// after persisting the new language to SettingsStore, since
        /// S._ itself won't reflect the change until SetLanguage runs.
        /// </summary>
        public static void Refresh()
        {
            foreach (var entry in _entries)
            {
                try { entry.Button.ItemText = S._(entry.TextKey); }
                catch { }
            }
        }
    }
}

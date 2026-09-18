// RibbonLanguageWatcher.cs -- ME-Tools | Ribbon label + tooltip language sync
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
// Tooltips had the exact same problem, one level deeper: they weren't
// even routed through S._() in the first place, just hardcoded English
// strings straight in PushButtonData -- so there was nothing for this
// watcher to refresh even if it tried. TooltipKey is optional specifically
// so a button that hasn't been given a tooltip translation key yet still
// compiles and keeps working; only its label refreshes until it's caught
// up too.
//
// Modeled directly on RibbonThemeWatcher's own registration pattern
// (register once per button at startup, keep an internal list, expose a
// method that reapplies every registered value) -- PushButton.ItemText
// and .ToolTip are both writable properties after creation, the same way
// .Image/.LargeImage are, so refreshing them live works the same way
// icon refresh already does.
//
// Usage in App.cs, after creating each PushButton:
//     RibbonLanguageWatcher.Register(myPushButton, "ribbon.family_placer", "tooltip.family_placer");
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
            public string     TextKey;    // Strings.cs key, e.g. "ribbon.family_placer"
            public string     TooltipKey; // Strings.cs key, e.g. "tooltip.family_placer" -- optional
        }

        private static readonly List<Entry> _entries = new List<Entry>();

        /// <summary>
        /// Register a ribbon button so its displayed text (and, if
        /// tooltipKey is given, its tooltip) follows the current
        /// language. Call once per button, right after adding it to the
        /// panel -- mirrors RibbonThemeWatcher.Register. tooltipKey is
        /// optional so existing call sites keep compiling as-is.
        /// </summary>
        public static void Register(PushButton button, string textKey, string tooltipKey = null)
        {
            if (button == null || string.IsNullOrEmpty(textKey)) return;
            _entries.Add(new Entry { Button = button, TextKey = textKey, TooltipKey = tooltipKey });
        }

        /// <summary>
        /// Re-applies every registered button's text and tooltip using
        /// the CURRENT language (S._ reads whatever S.SetLanguage was
        /// last called with) -- call this immediately after
        /// S.SetLanguage, not just after persisting the new language to
        /// SettingsStore, since S._ itself won't reflect the change
        /// until SetLanguage runs.
        /// </summary>
        public static void Refresh()
        {
            foreach (var entry in _entries)
            {
                try { entry.Button.ItemText = S._(entry.TextKey); }
                catch { }

                if (!string.IsNullOrEmpty(entry.TooltipKey))
                {
                    try { entry.Button.ToolTip = S._(entry.TooltipKey); }
                    catch { }
                }
            }
        }
    }
}

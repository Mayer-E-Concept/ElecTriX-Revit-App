// RibbonPanelColorizer.cs -- ME-Tools | EXPERIMENTAL per-panel ribbon coloring
// Mayer E-Concept SRL
//
// *** This reaches into an undocumented, unsupported internal Revit API. ***
// Autodesk's own developer advocates have confirmed on the Revit API forum
// that panel/tab coloring is not supported by the public Revit API or UI:
// https://forums.autodesk.com/t5/revit-api-forum/how-to-change-revit-ribbon-tab-color/td-p/9761734
//
// CONFIRMED via a full member dump of the internal Autodesk.Windows.
// RibbonPanel object: it exposes two SEPARATE background properties --
//   CustomPanelBackground          -- tints the icon/button area
//   CustomPanelTitleBarBackground  -- tints just the bottom title strip
// Only the second one is set here, so the icon area stays Revit's normal
// look and just the title strip picks up the group color.
//
// Log: %APPDATA%\METools\ribbon-color-debug.log
using System;
using System.Reflection;
using System.Windows.Media;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Events;

namespace METools
{
    public static class RibbonPanelColorizer
    {
        private class PendingPanel
        {
            public Autodesk.Revit.UI.RibbonPanel Panel;
            public Color Color;
        }

        private static readonly System.Collections.Generic.List<PendingPanel> _pending
            = new System.Collections.Generic.List<PendingPanel>();
        private static bool _subscribed;
        private static bool _ran;

        /// <summary>
        /// Queue a panel for coloring. Call once per panel from App.OnStartup,
        /// right after CreateRibbonPanel.
        /// </summary>
        public static void TryColor(Autodesk.Revit.UI.RibbonPanel panel, Color color)
        {
            if (panel == null) return;
            _pending.Add(new PendingPanel { Panel = panel, Color = color });
        }

        /// <summary>
        /// Call once from App.OnStartup, after all TryColor(...) calls, passing
        /// the UIControlledApplication so we can hook Idling.
        /// </summary>
        public static void Init(UIControlledApplication app)
        {
            if (_subscribed || app == null) return;
            try
            {
                app.Idling += OnIdling;
                _subscribed = true;
            }
            catch (Exception ex) { Log("Init EXCEPTION: " + ex.Message); }
        }

        private static void OnIdling(object sender, IdlingEventArgs e)
        {
            if (_ran) return;
            _ran = true;
            var uiapp = sender as UIApplication;
            if (uiapp != null)
            {
                try { uiapp.Idling -= OnIdling; } catch { }
            }

            foreach (var p in _pending)
                ColorOne(p.Panel, p.Color);
        }

        // Candidate names for a title-text-color override, tried on both the
        // panel object itself and its Source (RibbonPanelSource) sub-object --
        // the earlier full member dump only covered the panel object itself,
        // never recursed into Source, so if the real property lives there
        // this is the first attempt to actually find it.
        private static readonly string[] TitleTextCandidates =
        {
            "CustomPanelTitleBarForeground", "CustomTitleBarForeground", "CustomTitleForeground",
            "TitleBarForeground", "TitleForeground", "CustomTextColor", "TitleTextColor",
        };

        private static void ColorOne(Autodesk.Revit.UI.RibbonPanel panel, Color color)
        {
            string name = "?";
            try { name = panel.Name; } catch { }

            try
            {
                var internalPanel = GetInternalPanel(panel);
                if (internalPanel == null)
                {
                    Log($"'{name}': could not reach the internal Autodesk.Windows.RibbonPanel.");
                    return;
                }

                var brush = new SolidColorBrush(color);
                brush.Freeze();
                var white = new SolidColorBrush(Colors.White);
                white.Freeze();

                // Title strip only -- CustomPanelBackground intentionally NOT
                // set, so the icon/button area keeps Revit's normal look.
                TrySetProperty(internalPanel, "CustomPanelTitleBarBackground", brush, name);

                // Text color: try the panel object first, then its Source
                // sub-object. Log a full dump either way so if none of these
                // hit, the real property name (if any) is visible without
                // guessing a third time.
                bool textHit = false;
                foreach (var candidate in TitleTextCandidates)
                    if (TrySetProperty(internalPanel, candidate, white, name + " (panel text)")) { textHit = true; break; }

                if (!textHit)
                {
                    object source = null;
                    try
                    {
                        var sourceProp = internalPanel.GetType().GetProperty("Source", BindingFlags.Public | BindingFlags.Instance);
                        source = sourceProp?.GetValue(internalPanel);
                    }
                    catch (Exception ex) { Log($"'{name}': reading Source failed: {ex.Message}"); }

                    if (source != null)
                    {
                        foreach (var candidate in TitleTextCandidates)
                            if (TrySetProperty(source, candidate, white, name + " (Source text)")) { textHit = true; break; }

                        if (!textHit)
                        {
                            Log($"'{name}': no text-color candidate matched on panel or Source -- dumping Source's members below.");
                            DumpMembers(source, name + "/Source");
                        }
                    }
                    else
                    {
                        Log($"'{name}': Source was null -- can't check it for a text-color property.");
                    }
                }
            }
            catch (Exception ex)
            {
                Log($"'{name}': EXCEPTION {ex.Message}");
            }
        }

        // Dumps every public property (name, type, current value, writability)
        // so the real member names are visible directly in the log instead of
        // guessing a third time.
        private static void DumpMembers(object obj, string label)
        {
            try
            {
                var type = obj.GetType();
                Log($"'{label}': ---- member dump for {type.FullName} ----");
                foreach (var p in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
                {
                    try
                    {
                        var val = p.CanRead ? (p.GetValue(obj)?.ToString() ?? "null") : "<write-only>";
                        Log($"  {p.Name} ({p.PropertyType.Name})  CanWrite={p.CanWrite}  value={val}");
                    }
                    catch (Exception ex) { Log($"  {p.Name}: <error reading: {ex.Message}>"); }
                }
                Log($"'{label}': ---- end dump ----");
            }
            catch (Exception ex) { Log($"DumpMembers EXCEPTION: {ex.Message}"); }
        }

        private static bool TrySetProperty(object obj, string propName, object value, string label)
        {
            try
            {
                var type = obj.GetType();
                var prop = type.GetProperty(propName, BindingFlags.Public | BindingFlags.Instance);
                if (prop == null || !prop.CanWrite) return false;
                prop.SetValue(obj, value);
                Log($"'{label}': OK -- set '{propName}' on {type.FullName}");
                return true;
            }
            catch (Exception ex)
            {
                Log($"'{label}': setting '{propName}' failed: {ex.Message}");
                return false;
            }
        }

        // Retrieves the internal Autodesk.Windows.RibbonPanel behind a public
        // Autodesk.Revit.UI.RibbonPanel, via the private "m_RibbonPanel" field.
        // Documented technique (Jeremy Tammik / Autodesk, "Pimp my Ribbon"):
        // https://jeremytammik.github.io/tbc/a/0542_pimp_my_ribbon.htm
        private static object GetInternalPanel(Autodesk.Revit.UI.RibbonPanel panel)
        {
            try
            {
                var fi = panel.GetType().GetField("m_RibbonPanel", BindingFlags.Instance | BindingFlags.NonPublic);
                return fi?.GetValue(panel);
            }
            catch (Exception ex)
            {
                Log($"GetInternalPanel EXCEPTION: {ex.Message}");
                return null;
            }
        }

        private static void Log(string msg)
        {
            try
            {
                var dir = System.IO.Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "METools");
                System.IO.Directory.CreateDirectory(dir);
                var path = System.IO.Path.Combine(dir, "ribbon-color-debug.log");
                System.IO.File.AppendAllText(path, $"{DateTime.Now:HH:mm:ss}  {msg}\r\n");
            }
            catch { }
        }
    }
}


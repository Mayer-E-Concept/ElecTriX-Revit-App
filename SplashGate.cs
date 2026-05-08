// SplashGate.cs — ME-Tools Startup Gate
// Mayer E-Concept SRL
// -----------------------------------------------------------------
// Central place for all startup behaviour that should NOT live in
// App.cs (to keep App.cs ribbon setup untouched):
//
//   • Subscribes to DocumentOpened and shows the splash ONLY when
//     the trial needs the user's attention:
//       a) First run ever after install              (marker missing)
//       b) Trial drops to ≤ 5 days remaining         (once)
//       c) Trial has expired                         (once)
//     All other document opens → silent, no popup.
//
//   • Provides the single authoritative app-version string,
//     read live from the embedded setup.iss. Update setup.iss and
//     the next build picks the version up automatically everywhere.
// -----------------------------------------------------------------
using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Windows.Interop;
using Autodesk.Revit.DB.Events;
using Autodesk.Revit.UI;

namespace METools
{
    internal static class SplashGate
    {
        // ── Persistence ────────────────────────────────────────────────────
        private static readonly string DataDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "METools");

        // Marker stores the last trigger-threshold that we already showed to
        // the user, so we don't keep popping the splash on every doc open.
        //   99 = first-run splash was shown, trial > 5 days
        //    5 = ≤ 5 days reminder was shown
        //    0 = expired notice was shown
        private static readonly string SplashMarker = Path.Combine(DataDir, "splash_seen.marker");

        private static readonly object _lock = new object();
        private static bool   _subscribed    = false;
        private static string _cachedVersion = null;

        // ── App wiring (called from App.OnStartup) ─────────────────────────
        public static void Register(UIControlledApplication app)
        {
            if (_subscribed || app == null) return;
            try
            {
                app.ControlledApplication.DocumentOpened += OnDocumentOpened;
                _subscribed = true;
            }
            catch
            {
                // Never let the gate break Revit startup.
            }
        }

        // ── Document-opened handler ────────────────────────────────────────
        private static void OnDocumentOpened(object sender, DocumentOpenedEventArgs e)
        {
            try
            {
                if (!ShouldShow()) return;
                MarkSeen();
                ShowSplash();
            }
            catch
            {
                // Swallow — a broken gate must not take Revit down with it.
            }
        }

        // Show policy — returns true only when the user hasn't seen
        // the splash for the current state yet.
        private static bool ShouldShow()
        {
            lock (_lock)
            {
                // a) First-ever run after install
                if (!File.Exists(SplashMarker)) return true;

                // Activated users stay quiet
                if (LicenseManager.IsLicensed()) return false;

                int days          = LicenseManager.DaysRemaining;
                int lastThreshold = ReadLastThreshold();

                // c) Expired — remind once
                if (days <= 0 && lastThreshold != 0) return true;

                // b) ≤5 days — remind once
                if (days <= 5 && days > 0 && lastThreshold > 5) return true;

                return false;
            }
        }

        private static int ReadLastThreshold()
        {
            try
            {
                string raw = File.ReadAllText(SplashMarker).Trim();
                return int.TryParse(raw, out int v) ? v : 99;
            }
            catch { return 99; }
        }

        private static void MarkSeen()
        {
            try
            {
                Directory.CreateDirectory(DataDir);
                int d = LicenseManager.DaysRemaining;
                int threshold = d <= 0 ? 0 : d <= 5 ? 5 : 99;
                File.WriteAllText(SplashMarker, threshold.ToString());
            }
            catch { }
        }

        private static void ShowSplash()
        {
            try
            {
                var w = new SplashWindow();
                var helper = new WindowInteropHelper(w);
                helper.Owner = Process.GetCurrentProcess().MainWindowHandle;
                w.ShowDialog();
            }
            catch { }
        }

        // ── Version resolution ─────────────────────────────────────────────
        /// <summary>
        /// Returns the app version, live-parsed from the embedded setup.iss
        /// on first call, cached thereafter. Falls back to assembly version.
        /// </summary>
        public static string GetVersion()
        {
            if (_cachedVersion != null) return _cachedVersion;
            _cachedVersion = ReadIssVersion() ?? ReadAssemblyVersion() ?? "1.0";
            return _cachedVersion;
        }

        private static string ReadIssVersion()
        {
            try
            {
                var asm = Assembly.GetExecutingAssembly();
                string resourceName = null;
                foreach (var r in asm.GetManifestResourceNames())
                {
                    if (r.EndsWith("setup.iss", StringComparison.OrdinalIgnoreCase))
                    { resourceName = r; break; }
                }
                if (resourceName == null) return null;

                using (var s = asm.GetManifestResourceStream(resourceName))
                {
                    if (s == null) return null;
                    using (var sr = new StreamReader(s))
                    {
                        string content = sr.ReadToEnd();
                        // #define AppVersion "1.0.5-beta"
                        var m = Regex.Match(content,
                            "#define\\s+AppVersion\\s+\"([^\"]+)\"",
                            RegexOptions.IgnoreCase);
                        if (m.Success) return m.Groups[1].Value.Trim();
                    }
                }
            }
            catch { }
            return null;
        }

        private static string ReadAssemblyVersion()
        {
            try
            {
                var v = Assembly.GetExecutingAssembly().GetName().Version;
                return v != null ? $"{v.Major}.{v.Minor}.{v.Build}" : null;
            }
            catch { return null; }
        }
    }
}

// App.cs — ME-Tools Ribbon Setup
// Mayer E-Concept SRL
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Events;
using Autodesk.Revit.UI;
using System;
using System.Reflection;
using System.Windows.Media.Imaging;

namespace METools
{
    public class App : IExternalApplication
    {
        private const string TAB    = "ME-Tools";
        private const string PANEL  = "ME-Tools";
        private const string VENDOR = "Mayer E-Concept SRL";

        public Result OnStartup(UIControlledApplication app)
        {
            // Show license/beta splash on every document open (until licensed)
            app.ControlledApplication.DocumentOpened += OnDocumentOpened;

            try { app.CreateRibbonTab(TAB); } catch { }

            var panel  = app.CreateRibbonPanel(TAB, PANEL);
            string dll = Assembly.GetExecutingAssembly().Location;

            // ── Settings — FIRST button (theme + language + license + worksets)
            var settingsBtn = new PushButtonData(
                "Settings", "Settings", dll,
                "METools.SettingsCommand")
            {
                ToolTip         = "Open ME-Tools Settings: theme, language, license and worksets.",
                LongDescription = $"Settings — {VENDOR}\n\nAppearance · Language · License · Worksets\n\nLicense status: {LicenseManager.StatusText}",
                Image           = LoadIcon("icon_settings_16.png") ?? LoadIcon("icon_cfg_16.png"),
                LargeImage      = LoadIcon("icon_settings_32.png") ?? LoadIcon("icon_cfg_32.png"),
            };
            var settingsButton = panel.AddItem(settingsBtn) as PushButton;
            if (settingsButton != null)
                SettingsCommand.RibbonButton = settingsButton;
            panel.AddSeparator();

            // ── Family Placer ─────────────────────────────────────────────
            var fpBtn = new PushButtonData(
                "FamilyPlacer", "Family\nPlacer", dll,
                "METools.FamilyPlacer.FamilyPlacerCommand")
            {
                ToolTip         = "Place stacked combinations of electrical families with configurable height and offset.",
                LongDescription = $"Family Placer — {VENDOR}\n\nBuild a stack of electrical families, set their mounting height (Niveau) and offset factor, then place them all at once.\n\n• SPACEBAR to rotate before placing\n• Multi-Place: collect multiple positions, ESC to finish\n• Wall detection active — free workplane also supported\n• Save and load placement templates for reuse",
                Image           = LoadIcon("icon_fp_16.png"),
                LargeImage      = LoadIcon("icon_fp_32.png"),
            };
            panel.AddItem(fpBtn);
            panel.AddSeparator();

            // ── Lamp Placer ───────────────────────────────────────────────
            var lpBtn = new PushButtonData(
                "LampPlacer", "Lamp\nPlacer", dll,
                "METools.LampPlacer.LampPlacerCommand")
            {
                ToolTip         = "Place lighting fixtures evenly distributed across selected rooms or MEP spaces.",
                LongDescription = $"Lamp Placer — {VENDOR}\n\nSelect a room or MEP space and lamps are placed automatically.\n\n• Configurable wall margin and lamp spacing\n• Height = UKD (underside of ceiling)\n• Multiple rooms simultaneously\n• Manual grid (rows x columns) or area-based auto mode\n• Place on line with fixed spacing",
                Image           = LoadIcon("icon_lamp_16.png"),
                LargeImage      = LoadIcon("icon_lamp_32.png"),
            };
            panel.AddItem(lpBtn);
            panel.AddSeparator();

            // ── Fix Level ─────────────────────────────────────────────────
            var flBtn = new PushButtonData(
                "FixLevel", "Fix\nLevel", dll,
                "METools.FixLevelCommand")
            {
                ToolTip         = "Assign the correct schedule level to all visible electrical elements in the active view.",
                LongDescription = $"Fix Level — {VENDOR}\n\nSets the 'Schedule Level' parameter of all electrical elements visible in the current floor plan view.",
                Image           = LoadIcon("icon_fl_fix_16.png") ?? LoadIcon("icon_fp_16.png"),
                LargeImage      = LoadIcon("icon_fl_fix_32.png") ?? LoadIcon("icon_fp_32.png"),
            };
            panel.AddItem(flBtn);
            panel.AddSeparator();

            // ── Circuit Config ────────────────────────────────────────────
            var cfgBtn = new PushButtonData(
                "CircuitConfig", "Circuit\nConfig", dll,
                "METools.FamilyPlacer.KonfigurationsCommand")
            {
                ToolTip         = "Configure automatic circuit assignment: map room types and special outlets to circuit names.",
                LongDescription = $"Circuit Configuration — {VENDOR}\n\nMap room types (e.g. Bedroom -> 2F1) and special outlets (WM, KS, BO...) to circuit names.\n\n• Synonyms recognized automatically\n• Panel assignment via room schema detection\n• Settings saved as a project parameter",
                Image           = LoadIcon("icon_cfg_new_16.png"),
                LargeImage      = LoadIcon("icon_cfg_new_32.png"),
            };
            panel.AddItem(cfgBtn);

            panel.AddSeparator();

            // ── Clash Detector ────────────────────────────────────────────
            var clashBtn = new PushButtonData(
                "ClashDetector", "Clash\nDetector", dll,
                "METools.ClashDetector.ClashDetectorCommand")
            {
                ToolTip         = "Detect collisions between MEP elements (trays, conduits, ducts, pipes) and architecture / structure. Place opening families automatically.",
                LongDescription = $"Clash Detector — {VENDOR}\n\nChecks cable trays, conduits, ducts and pipes against walls, floors and structural elements — in the current model and all linked files.\n\n• Mark collision zones as red filled areas in the floor plan\n• Place Auxalia CAx opening family with exact tray dimensions\n  (Trassenhöhe, Trassenbreite, X/Z-Überstand, OKB_zu_Achse, Vorzug/Nachzug)\n• MEP element ID stored in family — Sync repositions after tray is moved\n• 3D Inspector view with section-box cropped to each collision\n• Single-click row = floor plan  |  Double-click row = 3D Inspector\n• Flex pipes and flex ducts excluded (cast in concrete)",
                Image           = LoadIcon("icon_clash_16.png"),
                LargeImage      = LoadIcon("icon_clash_32.png"),
            };
            panel.AddItem(clashBtn);

            return Result.Succeeded;
        }

        // ── License / beta splash on every document open ──────────────────
        private static DateTime _lastSplashTime = DateTime.MinValue;

        private static void OnDocumentOpened(object sender, DocumentOpenedEventArgs e)
        {
            if (LicenseManager.IsLicensed()) return;

            // Prevent multiple splashes within 10 seconds (linked docs fire DocumentOpened too)
            if ((DateTime.Now - _lastSplashTime).TotalSeconds < 10) return;
            _lastSplashTime = DateTime.Now;

            try
            {
                System.Windows.Application.Current?.Dispatcher?.BeginInvoke(
                    System.Windows.Threading.DispatcherPriority.ApplicationIdle,
                    new System.Action(() =>
                    {
                        try { new SplashWindow().ShowDialog(); } catch { }
                    }));
            }
            catch { }
        }

        public Result OnShutdown(UIControlledApplication app)
        {
            app.ControlledApplication.DocumentOpened -= OnDocumentOpened;
            return Result.Succeeded;
        }

        private System.Windows.Media.ImageSource LoadIcon(string fileName)
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

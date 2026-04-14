// App.cs — ME-Tools Ribbon Setup
// Mayer E-Concept SRL
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using System.Reflection;
using METools.Licensing;
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
            try { app.CreateRibbonTab(TAB); } catch { }

            var panel  = app.CreateRibbonPanel(TAB, PANEL);
            string dll = Assembly.GetExecutingAssembly().Location;

            // ── Settings (first button — license, theme, language) ───────────
            var settingsBtn = new PushButtonData(
                "Settings", "Settings", dll,
                "METools.SettingsCommand")
            {
                ToolTip         = "Open ME-Tools Settings: theme, language and license management.",
                LongDescription = $"Settings — {VENDOR}\n\n" +
                                  "• Appearance: switch between Dark and Light mode\n" +
                                  "• Language: set display language\n" +
                                  "• License: enter and activate your license key\n\n" +
                                  $"License status: {LicenseManager.StatusText}",
                Image           = LoadIcon("icon_cfg_16.png"),
                LargeImage      = LoadIcon("icon_cfg_32.png"),
            };
            var settingsButton = panel.AddItem(settingsBtn) as PushButton;
            if (settingsButton != null)
                SettingsCommand.RibbonButton = settingsButton;

            panel.AddSeparator();

            // ── Family Placer ───────────────────────────────────────────────
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

            // ── Lamp Placer ─────────────────────────────────────────────────
            var lpBtn = new PushButtonData(
                "LampPlacer", "Lamp\nPlacer", dll,
                "METools.LampPlacer.LampPlacerCommand")
            {
                ToolTip         = "Place lighting fixtures evenly distributed across selected rooms.",
                LongDescription = $"Lamp Placer — {VENDOR}\n\nSelect a room and lamps are placed automatically.\n\n• Configurable wall margin and lamp spacing\n• Height = UKD (underside of ceiling)\n• Multiple rooms simultaneously\n• Manual grid (rows × columns) or area-based auto mode",
                Image           = LoadIcon("icon_lamp_16.png"),
                LargeImage      = LoadIcon("icon_lamp_32.png"),
            };
            panel.AddItem(lpBtn);
            panel.AddSeparator();

            // ── Fix Level ───────────────────────────────────────────────────
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

            // ── Auto Room Separation ─────────────────────────────────────────
            var arsBtn = new PushButtonData(
                "AutoRoomSeparation", "Room\nSeparation", dll,
                "METools.AutoRoomSeparation.AutoRoomSeparationCommand")
            {
                ToolTip         = "Automatically generate Room Separation Lines from DWG / IFC geometry.",
                LongDescription = $"Auto Room Separation — {VENDOR}\n\n" +
                                  "Reads wall geometry from linked DWG files, DirectShape/IFC elements, or native Revit Walls, " +
                                  "finds closed room polygons and places Room Separation Lines in the active floor plan.\n\n" +
                                  "• Configurable area filter (min / max m²)\n" +
                                  "• DWG layer exclusion filter\n" +
                                  "• Duplicate line detection",
                Image           = LoadIcon("icon_ars_16.png") ?? LoadIcon("icon_fl_fix_16.png"),
                LargeImage      = LoadIcon("icon_ars_32.png") ?? LoadIcon("icon_fl_fix_32.png"),
            };
            panel.AddItem(arsBtn);
            panel.AddSeparator();

            // ── Circuit Config ───────────────────────────────────────────────
            var cfgBtn = new PushButtonData(
                "CircuitConfig", "Circuit\nConfig", dll,
                "METools.FamilyPlacer.KonfigurationsCommand")
            {
                ToolTip         = "Configure automatic circuit assignment: map room types and special outlets to circuit names.",
                LongDescription = $"Circuit Configuration — {VENDOR}\n\nMap room types (e.g. Bedroom → 2F1) and special outlets (WM, KS, BO...) to circuit names.\n\n• Synonyms recognized automatically\n• Panel assignment via room schema detection\n• Settings saved as a project parameter",
                Image           = LoadIcon("icon_cfg_new_16.png"),
                LargeImage      = LoadIcon("icon_cfg_new_32.png"),
            };
            panel.AddItem(cfgBtn);

            // ── Startup splash (shown after Revit finishes loading) ──────────
            app.ControlledApplication.ApplicationInitialized += OnApplicationInitialized;

            return Result.Succeeded;
        }

        private void OnApplicationInitialized(
            object sender,
            Autodesk.Revit.DB.Events.ApplicationInitializedEventArgs e)
        {
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

        public Result OnShutdown(UIControlledApplication app) => Result.Succeeded;

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

// App.cs — ME-Tools Ribbon Setup
// Mayer E-Concept SRL
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using System.Reflection;
using System.Windows.Media.Imaging;

namespace METools
{
    public class App : IExternalApplication
    {
        private const string TAB    = "ElecTriX";
        private const string PANEL  = "ElecTriX";
        private const string VENDOR = "Mayer E-Concept SRL";

        public Result OnStartup(UIControlledApplication app)
        {
            // ── Splash / trial reminder gate (first install + ≤5 days left + expired)
            // Intentional single-line hook — all logic lives in SplashGate.cs
            // so the ribbon setup below stays exactly as it was.
            SplashGate.Register(app);

            try { app.CreateRibbonTab(TAB); } catch { }

            var panel  = app.CreateRibbonPanel(TAB, PANEL);
            string dll = Assembly.GetExecutingAssembly().Location;

            // ── Settings (Appearance · Language · License · Worksets) ───────
            // Leftmost — entry point for theme switch, language, license and worksets.
            var stBtn = new PushButtonData(
                "Settings", "Settings", dll,
                "METools.SettingsCommand")
            {
                ToolTip         = "ME-Tools settings: appearance, language, license and worksets.",
                LongDescription = $"Settings — {VENDOR}\n\nAppearance · Language · License · Worksets\n\n" +
                                  $"License status: {LicenseManager.StatusText}",
                Image           = LoadIcon("icon_settings_32.png"),
                LargeImage      = LoadIcon("icon_settings_32.png"),
            };
            var settingsButton = panel.AddItem(stBtn) as PushButton;
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

            // ── Circuit Config ───────────────────────────────────────────────
            var cfgBtn = new PushButtonData(
                "CircuitConfig", "Circuit\nConfig", dll,
                "METools.FamilyPlacer.KonfigurationsCommand")
            {
                ToolTip         = "Configure automatic circuit assignment and create power circuits per room.",
                LongDescription = $"Circuit Configuration — {VENDOR}\n\nPro Raum einen Verteiler + Stromkreis zuweisen und mit 'Create Circuits' automatisch Power-Circuits erstellen.\n\n• Sondersteckdosen (WM, KS, BO, ...) werden automatisch erkannt\n• Config wird im Projekt gespeichert + Backup im AppData",
                Image           = LoadIcon("icon_cfg_new_16.png"),
                LargeImage      = LoadIcon("icon_cfg_new_32.png"),
            };
            panel.AddItem(cfgBtn);

            return Result.Succeeded;
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

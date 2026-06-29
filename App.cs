// App.cs -- ME-Tools Ribbon Setup
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
            // -- Splash / trial reminder gate (first install + ?5 days left + expired)
            // Intentional single-line hook -- all logic lives in SplashGate.cs
            // so the ribbon setup below stays exactly as it was.
            SplashGate.Register(app);

            try { app.CreateRibbonTab(TAB); } catch { }

            var panel  = app.CreateRibbonPanel(TAB, PANEL);
            string dll = Assembly.GetExecutingAssembly().Location;

            // -- Settings (Appearance ? Language ? License ? Worksets) -------
            // Leftmost -- entry point for theme switch, language, license and worksets.
            var stBtn = new PushButtonData(
                "Settings", "Settings", dll,
                "METools.SettingsCommand")
            {
                ToolTip         = "ME-Tools settings: appearance, language, license and worksets.",
                LongDescription = $"Settings -- {VENDOR}\n\nAppearance ? Language ? License ? Worksets\n\n" +
                                  $"License status: {LicenseManager.StatusText}",
                Image           = LoadIcon("icon_settings_32.png"),
                LargeImage      = LoadIcon("icon_settings_32.png"),
            };
            var settingsButton = panel.AddItem(stBtn) as PushButton;
            if (settingsButton != null)
                SettingsCommand.RibbonButton = settingsButton;
            panel.AddSeparator();

            // -- Family Placer -----------------------------------------------
            var fpBtn = new PushButtonData(
                "FamilyPlacer", "Family\nPlacer", dll,
                "METools.FamilyPlacer.FamilyPlacerCommand")
            {
                ToolTip         = "Place stacked combinations of electrical families with configurable height and offset.",
                LongDescription = $"Family Placer -- {VENDOR}\n\nBuild a stack of electrical families, set their mounting height (Niveau) and offset factor, then place them all at once.\n\n? SPACEBAR to rotate before placing\n? Multi-Place: collect multiple positions, ESC to finish\n? Wall detection active -- free workplane also supported\n? Save and load placement templates for reuse",
                Image           = LoadIcon("icon_fp_16.png"),
                LargeImage      = LoadIcon("icon_fp_32.png"),
            };
            panel.AddItem(fpBtn);
            panel.AddSeparator();

            // -- Family Browser ---------------------------------------------
            var fbBtn = new PushButtonData(
                "FamilyBrowser", "Family\nBrowser", dll,
                "METools.FamilyBrowserCommand")
            {
                ToolTip         = "Browse and place loaded electrical CAx families by category.",
                LongDescription = $"Family Browser -- {VENDOR}\n\nLists all loaded _E_CAx families grouped by category.\nHover a family to reveal the Place button.",
                Image           = LoadIcon("icon_fb_16.png") ?? LoadIcon("icon_fp_16.png"),
                LargeImage      = LoadIcon("icon_fb_32.png") ?? LoadIcon("icon_fp_32.png"),
            };
            panel.AddItem(fbBtn);
            panel.AddSeparator();

            // -- Lamp Placer -------------------------------------------------
            var lpBtn = new PushButtonData(
                "LampPlacer", "Lamp\nPlacer", dll,
                "METools.LampPlacer.LampPlacerCommand")
            {
                ToolTip         = "Place lighting fixtures evenly distributed across selected rooms.",
                LongDescription = $"Lamp Placer -- {VENDOR}\n\nSelect a room and lamps are placed automatically.\n\n? Configurable wall margin and lamp spacing\n? Height = UKD (underside of ceiling)\n? Multiple rooms simultaneously\n? Manual grid (rows ? columns) or area-based auto mode",
                Image           = LoadIcon("icon_lamp_16.png"),
                LargeImage      = LoadIcon("icon_lamp_32.png"),
            };
            panel.AddItem(lpBtn);
            panel.AddSeparator();

            // -- Fix Level ---------------------------------------------------
            var flBtn = new PushButtonData(
                "FixLevel", "Fix\nLevel", dll,
                "METools.FixLevelCommand")
            {
                ToolTip         = "Assign the correct schedule level to all visible electrical elements in the active view.",
                LongDescription = $"Fix Level -- {VENDOR}\n\nSets the 'Schedule Level' parameter of all electrical elements visible in the current floor plan view.",
                Image           = LoadIcon("icon_fl_fix_16.png") ?? LoadIcon("icon_fp_16.png"),
                LargeImage      = LoadIcon("icon_fl_fix_32.png") ?? LoadIcon("icon_fp_32.png"),
            };
            panel.AddItem(flBtn);
            panel.AddSeparator();

            // -- Circuit Tagger ---------------------------------------------
            var ctBtn = new PushButtonData(
                "CircuitTagger", "Circuit\nTagger", dll,
                "METools.FamilyPlacer.CircuitTaggerCommand")
            {
                ToolTip         = "Select elements, assign circuit parameters (FI, Stromkreis, Vorsicherung) and place tags.",
                LongDescription = $"Circuit Tagger -- {VENDOR}\n\nSelect any electrical elements, enter circuit parameters and an apartment group tag, then apply.\n\n" +
                                  "* Writes CAx_Vorsicherung, CAx_FI, CAx_Stromkreis, CAx_Beleuchtungskreis, CAx_Apartment\n" +
                                  "* Places a multicategory tag (ME-Tools_CircuitTag) next to each element\n" +
                                  "* Circuit Stats tab: grouped view with socket/lamp/switch counts\n" +
                                  "* All Tagged tab: every tagged element in the project\n" +
                                  "* Export to Excel or CSV",
                Image           = LoadIcon("icon_cfg_new_16.png"),
                LargeImage      = LoadIcon("icon_cfg_new_32.png"),
            };
            panel.AddItem(ctBtn);
            panel.AddSeparator();

            // -- Statistics ------------------------------------------------
            var statsBtn = new PushButtonData(
                "Statistics", "Statistics", dll,
                "METools.StatisticsCommand")
            {
                ToolTip         = "Count all electrical elements by category and floor.",
                LongDescription = $"Statistics -- {VENDOR}\n\nCounts all electrical elements by category with a per-floor breakdown.\n\nExport to CSV.",
                Image           = LoadIcon("icon_stats_16.png") ?? LoadIcon("icon_cfg_new_16.png"),
                LargeImage      = LoadIcon("icon_stats_32.png") ?? LoadIcon("icon_cfg_new_32.png"),
            };
            panel.AddItem(statsBtn);

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

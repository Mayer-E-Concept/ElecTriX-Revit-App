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
                Image           = LoadIcon("icon_settings_light_16.png"),
                LargeImage      = LoadIcon("icon_settings_light_32.png"),
            };
            var settingsButton = panel.AddItem(stBtn) as PushButton;
            if (settingsButton != null)
                SettingsCommand.RibbonButton = settingsButton;
            RibbonThemeWatcher.Register(settingsButton, "icon_settings");
            panel.AddSeparator();

            // -- Family Placer -----------------------------------------------
            var fpBtn = new PushButtonData(
                "FamilyPlacer", "Family\nPlacer", dll,
                "METools.FamilyPlacer.FamilyPlacerCommand")
            {
                ToolTip         = "Place stacked combinations of electrical families with configurable height and offset.",
                LongDescription = $"Family Placer -- {VENDOR}\n\nBuild a stack of electrical families, set their mounting height (Niveau) and offset factor, then place them all at once.\n\n? SPACEBAR to rotate before placing\n? Multi-Place: collect multiple positions, ESC to finish\n? Wall detection active -- free workplane also supported\n? Save and load placement templates for reuse",
                Image           = LoadIcon("icon_fp_light_16.png"),
                LargeImage      = LoadIcon("icon_fp_light_32.png"),
            };
            var fpButton = panel.AddItem(fpBtn) as PushButton;
            RibbonThemeWatcher.Register(fpButton, "icon_fp");
            panel.AddSeparator();

            // -- Family Browser ---------------------------------------------
            var fbBtn = new PushButtonData(
                "FamilyBrowser", "Family\nBrowser", dll,
                "METools.FamilyBrowserCommand")
            {
                ToolTip         = "Browse and place loaded electrical CAx families by category.",
                LongDescription = $"Family Browser -- {VENDOR}\n\nLists all loaded _E_CAx families grouped by category.\nHover a family to reveal the Place button.",
                Image           = LoadIcon("icon_fb_light_16.png") ?? LoadIcon("icon_fp_light_16.png"),
                LargeImage      = LoadIcon("icon_fb_light_32.png") ?? LoadIcon("icon_fp_light_32.png"),
            };
            var fbButton = panel.AddItem(fbBtn) as PushButton;
            RibbonThemeWatcher.Register(fbButton, "icon_fb");
            panel.AddSeparator();

            // -- Lamp Placer -------------------------------------------------
            var lpBtn = new PushButtonData(
                "LampPlacer", "Lamp\nPlacer", dll,
                "METools.LampPlacer.LampPlacerCommand")
            {
                ToolTip         = "Place lighting fixtures evenly distributed across selected rooms.",
                LongDescription = $"Lamp Placer -- {VENDOR}\n\nSelect a room and lamps are placed automatically.\n\n? Configurable wall margin and lamp spacing\n? Height = UKD (underside of ceiling)\n? Multiple rooms simultaneously\n? Manual grid (rows ? columns) or area-based auto mode",
                Image           = LoadIcon("icon_lamp_light_16.png"),
                LargeImage      = LoadIcon("icon_lamp_light_32.png"),
            };
            var lpButton = panel.AddItem(lpBtn) as PushButton;
            RibbonThemeWatcher.Register(lpButton, "icon_lamp");
            panel.AddSeparator();

            // -- Fix Level ---------------------------------------------------
            var flBtn = new PushButtonData(
                "FixLevel", "Fix\nLevel", dll,
                "METools.FixLevelCommand")
            {
                ToolTip         = "Assign the correct schedule level to all visible electrical elements in the active view.",
                LongDescription = $"Fix Level -- {VENDOR}\n\nSets the 'Schedule Level' parameter of all electrical elements visible in the current floor plan view.",
                Image           = LoadIcon("icon_fl_fix_light_16.png") ?? LoadIcon("icon_fp_light_16.png"),
                LargeImage      = LoadIcon("icon_fl_fix_light_32.png") ?? LoadIcon("icon_fp_light_32.png"),
            };
            var flButton = panel.AddItem(flBtn) as PushButton;
            RibbonThemeWatcher.Register(flButton, "icon_fl_fix");
            panel.AddSeparator();

            // -- Level Manager ------------------------------------------------
            var lmBtn = new PushButtonData(
                "LevelManager", "Level\nManager", dll,
                "METools.LevelManager.LevelManagerCommand")
            {
                ToolTip         = "See every level in the project laid out like a section, grouped and sorted, and add new ones.",
                LongDescription = $"Level Manager -- {VENDOR}\n\nShows all project levels stacked top-to-bottom by elevation, like a section.\n\n* Auto-groups levels by shared naming (e.g. UKD / FFB) -- no project-specific setup needed\n* Filter by group and by zone/house tag (e.g. H1, H2)\n* Compact (even spacing) or True Scale (proportional to elevation) display\n* Add a new level by name and elevation directly from the list",
                Image           = LoadIcon("icon_lm_light_16.png") ?? LoadIcon("icon_fp_light_16.png"),
                LargeImage      = LoadIcon("icon_lm_light_32.png") ?? LoadIcon("icon_fp_light_32.png"),
            };
            var lmButton = panel.AddItem(lmBtn) as PushButton;
            RibbonThemeWatcher.Register(lmButton, "icon_lm");
            panel.AddSeparator();

            // -- Project Transfer ---------------------------------------------
            var ptBtn = new PushButtonData(
                "ProjectTransfer", "Project\nTransfer", dll,
                "METools.ProjectTransfer.ProjectTransferCommand")
            {
                ToolTip         = "Copy filters, drafting views/legends, sheets and schedules from this project into another one.",
                LongDescription = $"Project Transfer -- {VENDOR}\n\nCopies Filters, Views, Sheets and Schedules from the active project into another project -- either already open in Revit, or opened from disk.\n\n* Views: Drafting Views and Legends only (plan/section/3D views depend on this project's own levels & grids)\n* Sheets: copied together with whatever is placed on them; sheets flag a warning if they hold a plan/section/3D view\n* Duplicate type names in the target keep the target's own version",
                Image           = LoadIcon("icon_pt_light_16.png") ?? LoadIcon("icon_fp_light_16.png"),
                LargeImage      = LoadIcon("icon_pt_light_32.png") ?? LoadIcon("icon_fp_light_32.png"),
            };
            var ptButton = panel.AddItem(ptBtn) as PushButton;
            RibbonThemeWatcher.Register(ptButton, "icon_pt");
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
                Image           = LoadIcon("icon_ct_light_16.png"),
                LargeImage      = LoadIcon("icon_ct_light_32.png"),
            };
            var ctButton = panel.AddItem(ctBtn) as PushButton;
            RibbonThemeWatcher.Register(ctButton, "icon_ct");
            panel.AddSeparator();

            // -- Statistics ------------------------------------------------
            var statsBtn = new PushButtonData(
                "Statistics", "Statistics", dll,
                "METools.StatisticsCommand")
            {
                ToolTip         = "Count all electrical elements by category and floor.",
                LongDescription = $"Statistics -- {VENDOR}\n\nCounts all electrical elements by category with a per-floor breakdown.\n\nExport to CSV.",
                Image           = LoadIcon("icon_stats_light_16.png"),
                LargeImage      = LoadIcon("icon_stats_light_32.png"),
            };
            var statsButton = panel.AddItem(statsBtn) as PushButton;
            RibbonThemeWatcher.Register(statsButton, "icon_stats");

            // Apply the correct light/dark icon set right now based on Revit's
            // current theme, and subscribe so it stays in sync if the user
            // switches Revit's theme later without restarting.
            RibbonThemeWatcher.Init();

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

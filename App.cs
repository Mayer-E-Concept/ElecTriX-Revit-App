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
        private const string VENDOR = "Mayer E-Concept SRL";

        public Result OnStartup(UIControlledApplication app)
        {
            // -- Splash / trial reminder gate (first install + ?5 days left + expired)
            // Intentional single-line hook -- all logic lives in SplashGate.cs
            // so the ribbon setup below stays exactly as it was.
            SplashGate.Register(app);

            // -- Project Comments background notifier (silent unless a shared
            // folder is configured in the Comments tool's own settings) --------
            METools.Comments.CommentsWatcher.Register(app);

            // Comments' popup (Mark as read / Jump to Level / Go to Item) needs
            // its ExternalEvent created here, in a guaranteed valid API context,
            // NOT lazily on first button click -- see CommentsHandler.Ensure()
            // for why. Must run after CommentsWatcher.Register above only by
            // convention (no actual ordering dependency between the two).
            METools.Comments.CommentsHandler.Ensure();

            // -- Activity Log background tracker (Added/Modified/Deleted per
            // user, shared folder same as Comments) -------------------------
            METools.ActivityLog.ActivityLogWatcher.Register(app);

            try { app.CreateRibbonTab(TAB); } catch { }

            var panelSetup     = app.CreateRibbonPanel(TAB, "Setup");
            var panelPlacement = app.CreateRibbonPanel(TAB, "Placement");
            var panelLevels    = app.CreateRibbonPanel(TAB, "Levels & Structure");
            var panelCircuits  = app.CreateRibbonPanel(TAB, "Circuits & Reporting");
            var panelTeam      = app.CreateRibbonPanel(TAB, "Team");
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
            var settingsButton = panelSetup.AddItem(stBtn) as PushButton;
            if (settingsButton != null)
                SettingsCommand.RibbonButton = settingsButton;
            RibbonThemeWatcher.Register(settingsButton, "icon_settings");

            // -- Project Health Check -----------------------------------------
            var hcBtn = new PushButtonData(
                "ProjectHealthCheck", "Project\nHealth Check", dll,
                "METools.ProjectHealthCheckCommand")
            {
                ToolTip         = "Checks the ME-Tools_CircuitTag family and Circuit Tagger's shared-parameter bindings in this project.",
                LongDescription = $"Project Health Check -- {VENDOR}\n\nOn a new or detached project that didn't inherit the full company template, Circuit Tagger can silently degrade -- writing parameters but placing no tags, or writing nothing at all -- because either:\n\n" +
                                  "* The 'ME-Tools_CircuitTag' Multi-Category Tag family isn't loaded\n" +
                                  "* One or more of the 6 shared parameters (Vorsicherung, FI-Kreis, Stromkreis Tag, Schaltkreis, CAx_Apartment, CAx_Building) aren't bound to all 8 electrical categories\n\n" +
                                  "This checks both, in a few seconds, instead of debugging it by hand.",
                Image           = LoadIcon("icon_healthcheck_light_16.png") ?? LoadIcon("icon_settings_light_16.png"),
                LargeImage      = LoadIcon("icon_healthcheck_light_32.png") ?? LoadIcon("icon_settings_light_32.png"),
            };
            var hcButton = panelSetup.AddItem(hcBtn) as PushButton;
            RibbonThemeWatcher.Register(hcButton, "icon_healthcheck");

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
            var fpButton = panelPlacement.AddItem(fpBtn) as PushButton;
            RibbonThemeWatcher.Register(fpButton, "icon_fp");
            panelPlacement.AddSeparator();

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
            var fbButton = panelPlacement.AddItem(fbBtn) as PushButton;
            RibbonThemeWatcher.Register(fbButton, "icon_fb");
            panelPlacement.AddSeparator();

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
            var lpButton = panelPlacement.AddItem(lpBtn) as PushButton;
            RibbonThemeWatcher.Register(lpButton, "icon_lamp");

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
            var flButton = panelLevels.AddItem(flBtn) as PushButton;
            RibbonThemeWatcher.Register(flButton, "icon_fl_fix");
            panelLevels.AddSeparator();

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
            var lmButton = panelLevels.AddItem(lmBtn) as PushButton;
            RibbonThemeWatcher.Register(lmButton, "icon_lm");
            panelLevels.AddSeparator();

            // -- IFC Level Importer --------------------------------------------
            var ifcBtn = new PushButtonData(
                "IfcLevelImport", "IFC Level\nImporter", dll,
                "METools.IfcImport.IfcLevelImportCommand")
            {
                ToolTip         = "Read levels, units and rough site coordinates from an IFC file before importing anything.",
                LongDescription = $"IFC Level Importer -- {VENDOR}\n\nOpens an IFC file and reads it directly (no full import) to show:\n\n* Every building storey (level) found, with its elevation -- tick which ones to create as real Revit Levels\n* The file's length unit compared against your project's, with a clear warning if they don't match (e.g. the file is in cm but your project is in mm)\n* Rough site placement / geographic / survey coordinates, if the file has them -- shown for reference only, nothing is moved in your project\n\nNothing is changed until you tick levels and click Import.",
                Image           = LoadIcon("icon_lm_light_16.png") ?? LoadIcon("icon_fp_light_16.png"),
                LargeImage      = LoadIcon("icon_lm_light_32.png") ?? LoadIcon("icon_fp_light_32.png"),
            };
            var ifcButton = panelLevels.AddItem(ifcBtn) as PushButton;
            RibbonThemeWatcher.Register(ifcButton, "icon_lm");
            panelLevels.AddSeparator();

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
            var ptButton = panelLevels.AddItem(ptBtn) as PushButton;
            RibbonThemeWatcher.Register(ptButton, "icon_pt");

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
            var ctButton = panelCircuits.AddItem(ctBtn) as PushButton;
            RibbonThemeWatcher.Register(ctButton, "icon_ct");
            panelCircuits.AddSeparator();

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
            var statsButton = panelCircuits.AddItem(statsBtn) as PushButton;
            RibbonThemeWatcher.Register(statsButton, "icon_stats");

            // -- Comments ----------------------------------------------------
            var cmtBtn = new PushButtonData(
                "Comments", "Comments", dll,
                "METools.Comments.CommentsCommand")
            {
                ToolTip         = "Leave a comment tagged to a level; teammates get notified when they open this project.",
                LongDescription = $"Comments -- {VENDOR}\n\nLeave a note on the level you're working on -- a teammate " +
                                  "on another computer gets a popup with a sound cue when they open this project and " +
                                  "navigate there.\n\n" +
                                  "* Requires a shared network folder (configured once in this tool's own settings)\n" +
                                  "* See every comment for this project, by whom, on which level, and its status\n" +
                                  "* Mark Done, Ignore, or Reopen from either the popup or the full list",
                Image           = LoadIcon("icon_comments_light_16.png"),
                LargeImage      = LoadIcon("icon_comments_light_32.png"),
            };
            var cmtButton = panelTeam.AddItem(cmtBtn) as PushButton;
            RibbonThemeWatcher.Register(cmtButton, "icon_comments");

            // -- Activity Log --------------------------------------------------
            var alBtn = new PushButtonData(
                "ActivityLog", "Activity\nLog", dll,
                "METools.ActivityLog.ActivityLogCommand")
            {
                ToolTip         = "See who added, modified, or deleted which electrical/MEP elements, and when.",
                LongDescription = $"Activity Log -- {VENDOR}\n\nTracks Added/Modified/Deleted elements across the electrical/MEP categories ElecTriX works with, per user, per session.\n\n" +
                                  "* Uses the same shared folder as Comments -- nothing extra to configure if that's already set up\n" +
                                  "* Filter by user, action, or a text search (category/family/type/element id)\n" +
                                  "* Export to CSV\n\n" +
                                  "Deleted elements show what they WERE (category, family, type, level) even though the element itself is already gone by the time it's logged.",
                Image           = LoadIcon("icon_activitylog_light_16.png") ?? LoadIcon("icon_comments_light_16.png"),
                LargeImage      = LoadIcon("icon_activitylog_light_32.png") ?? LoadIcon("icon_comments_light_32.png"),
            };
            var alButton = panelTeam.AddItem(alBtn) as PushButton;
            RibbonThemeWatcher.Register(alButton, "icon_activitylog");

            // Apply the correct light/dark icon set right now based on Revit's
            // current theme, and subscribe so it stays in sync if the user
            // switches Revit's theme later without restarting.
            RibbonThemeWatcher.Init();

            // EXPERIMENTAL: attempt to color each panel using undocumented
            // internal Revit UI classes -- see RibbonPanelColorizer.cs for the
            // full explanation and the exact risk involved. Not guaranteed to
            // do anything; check %APPDATA%\METools\ribbon-color-debug.log
            // either way. Safe to delete this block + RibbonPanelColorizer.cs
            // entirely if it doesn't pan out -- nothing else depends on it.
            RibbonPanelColorizer.TryColor(panelSetup,     System.Windows.Media.Color.FromRgb(0x0F, 0x37, 0x37));
            RibbonPanelColorizer.TryColor(panelPlacement, System.Windows.Media.Color.FromRgb(0x18, 0x5F, 0x5F));
            RibbonPanelColorizer.TryColor(panelLevels,    System.Windows.Media.Color.FromRgb(0x23, 0x7D, 0x7D));
            RibbonPanelColorizer.TryColor(panelCircuits,  System.Windows.Media.Color.FromRgb(0x32, 0x9B, 0x9B));
            RibbonPanelColorizer.TryColor(panelTeam,      System.Windows.Media.Color.FromRgb(0x46, 0xB9, 0xB9));
            RibbonPanelColorizer.Init(app);

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

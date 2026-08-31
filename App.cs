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

            // -- Time Tracker background tracker (per-project, per-user time
            // open->close, shared folder same as Comments) -------------------
            METools.TimeTracker.TimeTrackerWatcher.Register(app);

            // -- Collision Checker's live-follow watcher: repositions a
            // placed hole when the conduit/cable tray it belongs to moves.
            // Session-long, independent of whether the tool's own window is
            // open -- see CollisionCheckerWatcher.cs for why this needs its
            // own ExternalEvent created here rather than lazily later.
            METools.CollisionChecker.CollisionCheckerWatcher.Register(app);

            // -- Circuit Tagger: detects a previously-tagged apartment being
            // duplicated (Copy/Paste, Mirror, Array, Group placement) and
            // prompts for a new House/Apartment so it doesn't merge into the
            // original's Stats. ExternalEvent created here for the same
            // reason as CommentsHandler.Ensure() above.
            METools.CircuitDuplicate.CircuitDuplicateWatcher.Register(app);

            try { app.CreateRibbonTab(TAB); } catch { }

            var panelSetup       = app.CreateRibbonPanel(TAB, "Setup");
            var panelDiagnostics = app.CreateRibbonPanel(TAB, "Diagnostics");
            var panelPlacement   = app.CreateRibbonPanel(TAB, "Placement");
            var panelLevels      = app.CreateRibbonPanel(TAB, "Levels & Structure");
            var panelCircuits    = app.CreateRibbonPanel(TAB, "Circuits & Reporting");
            var panelTeam        = app.CreateRibbonPanel(TAB, "Team");
            string dll = Assembly.GetExecutingAssembly().Location;

            // -- Settings (Appearance ? Language ? License ? Worksets) -------
            // Leftmost -- entry point for theme switch, language, license and worksets.
            var stBtn = new PushButtonData(
                "Settings", S._("ribbon.settings"), dll,
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
            RibbonLanguageWatcher.Register(settingsButton, "ribbon.settings");

            // -- Diagnostics (Find Stray Elements ? Project Health Check ? Imported Objects) --
            // Moved here from three separate places: Project Health Check
            // used to have its own standalone button right here in Setup;
            // Imported Objects used to be reachable only as one of six
            // tiles buried inside Settings' own home screen; Find Stray
            // Elements is brand new. All three are "is something wrong
            // with this project, and can I fix it" tools, distinct from
            // Settings (the app's OWN configuration) and from the
            // day-to-day design tools elsewhere on this ribbon, so they
            // get a home of their own instead of being scattered.
            var diagBtn = new PushButtonData(
                "Diagnostics", S._("ribbon.diagnostics"), dll,
                "METools.DiagnosticsCommand")
            {
                ToolTip         = "Find Stray Elements, Project Health Check, and Imported Objects -- model-health and cleanup tools.",
                LongDescription = $"Diagnostics -- {VENDOR}\n\nFind Stray Elements · Project Health Check · Imported Objects\n\n" +
                                  "Tools for finding and fixing things that are wrong with a project, rather than day-to-day design work.",
                Image           = LoadIcon("icon_healthcheck_light_16.png") ?? LoadIcon("icon_settings_light_16.png"),
                LargeImage      = LoadIcon("icon_healthcheck_light_32.png") ?? LoadIcon("icon_settings_light_32.png"),
            };
            var diagButton = panelDiagnostics.AddItem(diagBtn) as PushButton;
            RibbonThemeWatcher.Register(diagButton, "icon_healthcheck");
            RibbonLicenseWatcher.Register(diagButton);
            RibbonLanguageWatcher.Register(diagButton, "ribbon.diagnostics");

            // -- Family Placer -----------------------------------------------
            var fpBtn = new PushButtonData(
                "FamilyPlacer", S._("ribbon.family_placer"), dll,
                "METools.FamilyPlacer.FamilyPlacerCommand")
            {
                ToolTip         = "Place stacked combinations of electrical families with configurable height and offset.",
                LongDescription = $"Family Placer -- {VENDOR}\n\nBuild a stack of electrical families, set their mounting height (Niveau) and offset factor, then place them all at once.\n\n? SPACEBAR to rotate before placing\n? Multi-Place: collect multiple positions, ESC to finish\n? Wall detection active -- free workplane also supported\n? Save and load placement templates for reuse",
                Image           = LoadIcon("icon_fp_light_16.png"),
                LargeImage      = LoadIcon("icon_fp_light_32.png"),
            };
            var fpButton = panelPlacement.AddItem(fpBtn) as PushButton;
            RibbonThemeWatcher.Register(fpButton, "icon_fp");
            RibbonLanguageWatcher.Register(fpButton, "ribbon.family_placer");

            // -- Family Browser ---------------------------------------------
            var fbBtn = new PushButtonData(
                "FamilyBrowser", S._("ribbon.family_browser"), dll,
                "METools.FamilyBrowserCommand")
            {
                ToolTip         = "Browse and place loaded electrical CAx families by category.",
                LongDescription = $"Family Browser -- {VENDOR}\n\nLists all loaded _E_CAx families grouped by category.\nHover a family to reveal the Place button.",
                Image           = LoadIcon("icon_fb_light_16.png") ?? LoadIcon("icon_fp_light_16.png"),
                LargeImage      = LoadIcon("icon_fb_light_32.png") ?? LoadIcon("icon_fp_light_32.png"),
            };
            var fbButton = panelPlacement.AddItem(fbBtn) as PushButton;
            RibbonThemeWatcher.Register(fbButton, "icon_fb");
            RibbonLanguageWatcher.Register(fbButton, "ribbon.family_browser");

            // -- Lamp Placer -------------------------------------------------
            var lpBtn = new PushButtonData(
                "LampPlacer", S._("ribbon.lamp_placer"), dll,
                "METools.LampPlacer.LampPlacerCommand")
            {
                ToolTip         = "Place lighting fixtures evenly distributed across selected rooms.",
                LongDescription = $"Lamp Placer -- {VENDOR}\n\nSelect a room and lamps are placed automatically.\n\n? Configurable wall margin and lamp spacing\n? Height = UKD (underside of ceiling)\n? Multiple rooms simultaneously\n? Manual grid (rows ? columns) or area-based auto mode",
                Image           = LoadIcon("icon_lamp_light_16.png"),
                LargeImage      = LoadIcon("icon_lamp_light_32.png"),
            };
            var lpButton = panelPlacement.AddItem(lpBtn) as PushButton;
            RibbonThemeWatcher.Register(lpButton, "icon_lamp");
            RibbonLanguageWatcher.Register(lpButton, "ribbon.lamp_placer");

            // -- Fix Level ---------------------------------------------------
            var flBtn = new PushButtonData(
                "FixLevel", S._("ribbon.fix_level"), dll,
                "METools.FixLevelCommand")
            {
                ToolTip         = "Assign the correct schedule level to all visible electrical elements in the active view.",
                LongDescription = $"Fix Level -- {VENDOR}\n\nSets the 'Schedule Level' parameter of all electrical elements visible in the current floor plan view.",
                Image           = LoadIcon("icon_fl_fix_light_16.png") ?? LoadIcon("icon_fp_light_16.png"),
                LargeImage      = LoadIcon("icon_fl_fix_light_32.png") ?? LoadIcon("icon_fp_light_32.png"),
            };
            var flButton = panelLevels.AddItem(flBtn) as PushButton;
            RibbonThemeWatcher.Register(flButton, "icon_fl_fix");
            RibbonLanguageWatcher.Register(flButton, "ribbon.fix_level");

            // -- Level Manager (also handles IFC level import -- see its own
            // "Import from IFC" tab; that used to be a separate ribbon button) --
            var lmBtn = new PushButtonData(
                "LevelManager", S._("ribbon.level_manager"), dll,
                "METools.LevelManager.LevelManagerCommand")
            {
                ToolTip         = "See every level in the project laid out like a section, add new ones, or import levels from an IFC file.",
                LongDescription = $"Level & IFC Manager -- {VENDOR}\n\nTwo tabs in one window:\n\n" +
                                  "Project Levels -- shows all project levels stacked top-to-bottom by elevation, like a section.\n" +
                                  "* Auto-groups levels by shared naming (e.g. UKD / FFB) -- no project-specific setup needed\n" +
                                  "* Filter by group and by zone/house tag (e.g. H1, H2)\n" +
                                  "* Compact (even spacing) or True Scale (proportional to elevation) display\n" +
                                  "* Add a new level by name and elevation directly from the list\n\n" +
                                  "Import from IFC -- reads levels, units and rough site coordinates from an IFC file (detects one already linked/imported in the project too) and lets you tick which levels to create.",
                Image           = LoadIcon("icon_lm_light_16.png") ?? LoadIcon("icon_fp_light_16.png"),
                LargeImage      = LoadIcon("icon_lm_light_32.png") ?? LoadIcon("icon_fp_light_32.png"),
            };
            var lmButton = panelLevels.AddItem(lmBtn) as PushButton;
            RibbonThemeWatcher.Register(lmButton, "icon_lm");
            RibbonLicenseWatcher.Register(lmButton);
            RibbonLanguageWatcher.Register(lmButton, "ribbon.level_manager");

            // -- Project Transfer ---------------------------------------------
            var ptBtn = new PushButtonData(
                "ProjectTransfer", S._("ribbon.project_transfer"), dll,
                "METools.ProjectTransfer.ProjectTransferCommand")
            {
                ToolTip         = "Copy filters, drafting views/legends, sheets and schedules from this project into another one.",
                LongDescription = $"Project Transfer -- {VENDOR}\n\nCopies Filters, Views, Sheets and Schedules from the active project into another project -- either already open in Revit, or opened from disk.\n\n* Views: Drafting Views and Legends only (plan/section/3D views depend on this project's own levels & grids)\n* Sheets: copied together with whatever is placed on them; sheets flag a warning if they hold a plan/section/3D view\n* Duplicate type names in the target keep the target's own version",
                Image           = LoadIcon("icon_pt_light_16.png") ?? LoadIcon("icon_fp_light_16.png"),
                LargeImage      = LoadIcon("icon_pt_light_32.png") ?? LoadIcon("icon_fp_light_32.png"),
            };
            var ptButton = panelLevels.AddItem(ptBtn) as PushButton;
            RibbonThemeWatcher.Register(ptButton, "icon_pt");
            RibbonLicenseWatcher.Register(ptButton);
            RibbonLanguageWatcher.Register(ptButton, "ribbon.project_transfer");

            // -- Circuit Tagger ---------------------------------------------
            var ctBtn = new PushButtonData(
                "CircuitTagger", S._("ribbon.circuit_tagger"), dll,
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
            RibbonLicenseWatcher.Register(ctButton);
            RibbonLanguageWatcher.Register(ctButton, "ribbon.circuit_tagger");

            // -- Statistics ------------------------------------------------
            var statsBtn = new PushButtonData(
                "Statistics", S._("ribbon.statistics"), dll,
                "METools.StatisticsCommand")
            {
                ToolTip         = "Count all electrical elements by category and floor.",
                LongDescription = $"Statistics -- {VENDOR}\n\nCounts all electrical elements by category with a per-floor breakdown.\n\nExport to CSV.",
                Image           = LoadIcon("icon_stats_light_16.png"),
                LargeImage      = LoadIcon("icon_stats_light_32.png"),
            };
            var statsButton = panelCircuits.AddItem(statsBtn) as PushButton;
            RibbonThemeWatcher.Register(statsButton, "icon_stats");
            RibbonLanguageWatcher.Register(statsButton, "ribbon.statistics");

            // -- Batch Params (Renumber + Bulk Edit) --------------------------
            // Inspired by DiRoots' ReOrdering (renumber an instance parameter
            // with a prefix/counter/suffix, manually or along a picked detail
            // line) and OneParameter (bulk add-prefix/add-suffix/find-replace/
            // clear across many elements) -- combined into one tool, generic
            // across any category/parameter rather than scoped to electrical
            // ones specifically.
            var bpBtn = new PushButtonData(
                "BatchParams", S._("ribbon.batch_params"), dll,
                "METools.BatchParams.BatchParamsCommand")
            {
                ToolTip         = "Renumber a parameter across many elements (manually or along a line), or bulk add-prefix/suffix/find-replace/clear one parameter across a filtered set.",
                LongDescription = $"Batch Params -- {VENDOR}\n\nFilter elements by scope (selection / active view / whole model) and category, then:\n\n" +
                                  "* Renumber tab: prefix + counter + suffix into any text parameter, ordered manually (click one by one) or along a picked detail line\n" +
                                  "* Bulk Edit tab: add prefix, add suffix, find & replace, set, or clear one parameter across every matched element in one click\n\n" +
                                  "Works on any category and any writable text parameter -- not limited to electrical categories.",
                Image           = LoadIcon("icon_bp_light_16.png"),
                LargeImage      = LoadIcon("icon_bp_light_32.png"),
            };
            var bpButton = panelCircuits.AddItem(bpBtn) as PushButton;
            RibbonThemeWatcher.Register(bpButton, "icon_bp");
            RibbonLicenseWatcher.Register(bpButton);
            RibbonLanguageWatcher.Register(bpButton, "ribbon.batch_params");

            // -- Collision Checker (conduits/cable trays vs walls) -----------
            // Finds where a conduit/cable tray run crosses a wall, lists each
            // crossing with a Go To button and its level/category/wall type,
            // marks unresolved ones red in the current view, and places a
            // user-supplied hole-marker family at any you select. Placed
            // holes are linked to their run via Extensible Storage and kept
            // in sync by CollisionCheckerWatcher if the run is later moved.
            var ccBtn = new PushButtonData(
                "CollisionChecker", S._("ribbon.collision_checker"), dll,
                "METools.CollisionChecker.CollisionCheckerCommand")
            {
                ToolTip         = "Find where conduits/cable trays cross walls, jump to each one, and place a hole marker -- the hole follows if you later move the run.",
                LongDescription = $"Collision Checker -- {VENDOR}\n\nScans conduits and cable trays against every wall in the chosen scope (selection / active view / whole model) and lists every crossing point, with its level, category, and wall type.\n\n" +
                                  "* Go To selects the run and zooms to it\n" +
                                  "* Unresolved crossings are marked red in the current view\n" +
                                  "* Select any number of rows and Place Holes -- your chosen family/type is placed at each point, hosted on the wall face automatically if the family supports it\n" +
                                  "* If you later move a run that already has a hole, the hole moves with it",
                Image           = LoadIcon("icon_cc_light_16.png"),
                LargeImage      = LoadIcon("icon_cc_light_32.png"),
            };
            var ccButton = panelCircuits.AddItem(ccBtn) as PushButton;
            RibbonThemeWatcher.Register(ccButton, "icon_cc");
            RibbonLicenseWatcher.Register(ccButton);
            RibbonLanguageWatcher.Register(ccButton, "ribbon.collision_checker");

            // -- Comments ----------------------------------------------------
            var cmtBtn = new PushButtonData(
                "Comments", S._("ribbon.comments"), dll,
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
            RibbonLicenseWatcher.Register(cmtButton);
            RibbonLanguageWatcher.Register(cmtButton, "ribbon.comments");

            // -- Tasks ---------------------------------------------------------
            // Customer emails processed by the standalone METools.MailBridge
            // service become tasks here -- translated/summarized, filed per
            // project, self-assignable. Same shared folder as Comments.
            var taskBtn = new PushButtonData(
                "Tasks", S._("ribbon.tasks"), dll,
                "METools.Tasks.TasksCommand")
            {
                ToolTip = "Customer emails filed as per-project tasks -- translated, summarized, self-assignable.",
                LongDescription = $"Tasks -- {VENDOR}\n\nIncoming customer emails are translated, summarized, and filed here as tasks for the matching project.\n\n" +
                    "* Requires the same shared network folder as Comments (configured there)\n" +
                    "* Assign to yourself, mark done, or jump to a pinned element\n" +
                    "* Auto-refreshes while the window is open",
                Image = LoadIcon("icon_tasks_light_16.png") ?? LoadIcon("icon_comments_light_16.png"),
                LargeImage = LoadIcon("icon_tasks_light_32.png") ?? LoadIcon("icon_comments_light_32.png"),
            };
            var taskButton = panelTeam.AddItem(taskBtn) as PushButton;
            RibbonThemeWatcher.Register(taskButton, "icon_tasks");
            RibbonLicenseWatcher.Register(taskButton);
            RibbonLanguageWatcher.Register(taskButton, "ribbon.tasks");

            // -- Activity Log & Time Tracker ------------------------------------
            // Time Tracker used to be its own button here; merged into this one
            // as two extra tabs ("Team Totals" / "My Sessions") since it's the
            // same underlying idea as Activity Log -- per-user, per-project
            // history read from the same shared folder. Background tracking is
            // unaffected; only the entry point moved.
            var alBtn = new PushButtonData(
                "ActivityLog", S._("ribbon.activity_log"), dll,
                "METools.ActivityLog.ActivityLogCommand")
            {
                ToolTip         = "See who added, modified, or deleted which elements and when -- plus time spent per user, per project.",
                LongDescription = $"Activity Log & Time Tracker -- {VENDOR}\n\nThree tabs, one shared folder:\n\n" +
                                  "* Activity: Added/Modified/Deleted elements across the electrical/MEP categories ElecTriX works with, per user, per session. Filter by user, action, or a text search; export to CSV.\n" +
                                  "* Team Totals: total time, session count, and last activity for every teammate on this project.\n" +
                                  "* My Sessions: your own daily totals with an expandable per-session breakdown. A session that never closed cleanly (e.g. a crash) is recovered from its last heartbeat and marked accordingly, rather than lost.\n\n" +
                                  "Uses the same shared folder as Comments -- nothing extra to configure if that's already set up.",
                Image           = LoadIcon("icon_activitylog_light_16.png") ?? LoadIcon("icon_comments_light_16.png"),
                LargeImage      = LoadIcon("icon_activitylog_light_32.png") ?? LoadIcon("icon_comments_light_32.png"),
            };
            var alButton = panelTeam.AddItem(alBtn) as PushButton;
            RibbonThemeWatcher.Register(alButton, "icon_activitylog");
            RibbonLanguageWatcher.Register(alButton, "ribbon.activity_log");

            // Apply the correct light/dark icon set right now based on Revit's
            // current theme, and subscribe so it stays in sync if the user
            // switches Revit's theme later without restarting.
            RibbonThemeWatcher.Init();

            // Greys out the full-license tools' ribbon buttons right now if
            // running unlicensed. Also re-run from Settings whenever the
            // license is activated/deactivated, since that's the only other
            // moment license state can actually change mid-session.
            RibbonLicenseWatcher.RefreshAll();

            // EXPERIMENTAL: attempt to color each panel using undocumented
            // internal Revit UI classes -- see RibbonPanelColorizer.cs for the
            // full explanation and the exact risk involved. Not guaranteed to
            // do anything; check %APPDATA%\METools\ribbon-color-debug.log
            // either way. Safe to delete this block + RibbonPanelColorizer.cs
            // entirely if it doesn't pan out -- nothing else depends on it.
            RibbonPanelColorizer.TryColor(panelSetup,       System.Windows.Media.Color.FromRgb(0x0F, 0x37, 0x37));
            RibbonPanelColorizer.TryColor(panelDiagnostics, System.Windows.Media.Color.FromRgb(0x13, 0x4B, 0x4B));
            RibbonPanelColorizer.TryColor(panelPlacement,   System.Windows.Media.Color.FromRgb(0x18, 0x5F, 0x5F));
            RibbonPanelColorizer.TryColor(panelLevels,      System.Windows.Media.Color.FromRgb(0x23, 0x7D, 0x7D));
            RibbonPanelColorizer.TryColor(panelCircuits,    System.Windows.Media.Color.FromRgb(0x32, 0x9B, 0x9B));
            RibbonPanelColorizer.TryColor(panelTeam,        System.Windows.Media.Color.FromRgb(0x46, 0xB9, 0xB9));
            RibbonPanelColorizer.Init(app);

            // Trial-ending reminder -- deliberately NOT called directly here.
            // OnStartup runs while Revit itself is still starting up; a
            // modal TaskDialog at this exact moment risks interfering with
            // that. ApplicationInitialized is the standard, documented way
            // to defer something like this until Revit has actually
            // finished starting.
            app.ControlledApplication.ApplicationInitialized += (s, e) => LicenseManager.ShowTrialNudgeIfDue();

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

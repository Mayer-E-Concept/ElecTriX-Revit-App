// App.cs — ME-Tools Ribbon Setup
// Mayer E-Concept SRL
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using METools.Licensing;
using System.Collections.Generic;
using System.Reflection;
using System.Windows.Media.Imaging;

namespace METools
{
    public class App : IExternalApplication
    {
        private const string TAB    = "ME-Tools";
        private const string PANEL  = "ME-Tools";
        private const string VENDOR = "Mayer E-Concept SRL";

        // All tool buttons (not ThemeToggle) — collected here for trial enforcement
        private readonly List<PushButton> _toolButtons = new List<PushButton>();

        public Result OnStartup(UIControlledApplication app)
        {
            try { app.CreateRibbonTab(TAB); } catch { }

            var panel  = app.CreateRibbonPanel(TAB, PANEL);
            string dll = Assembly.GetExecutingAssembly().Location;

            // ── Theme Toggle (always active — not trial-gated) ────────────
            var themeBtn = new PushButtonData(
                "ThemeToggle", "Dark\nMode", dll,
                "METools.ThemeToggleCommand")
            {
                ToolTip         = "Switch between Dark Mode and Light Mode for all ME-Tools windows.",
                LongDescription = $"Theme Toggle — {VENDOR}\n\nSwitches the UI theme of all open ME-Tools windows simultaneously.",
                Image           = LoadIcon("icon_theme_dark_16.png"),
                LargeImage      = LoadIcon("icon_theme_dark_32.png"),
            };
            var themeButton = panel.AddItem(themeBtn) as PushButton;
            if (themeButton != null)
                ThemeToggleCommand.RibbonButton = themeButton;

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
            var fpButton = panel.AddItem(fpBtn) as PushButton;
            if (fpButton != null) _toolButtons.Add(fpButton);
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
            var lpButton = panel.AddItem(lpBtn) as PushButton;
            if (lpButton != null) _toolButtons.Add(lpButton);
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
            var flButton = panel.AddItem(flBtn) as PushButton;
            if (flButton != null) _toolButtons.Add(flButton);
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
            var cfgButton = panel.AddItem(cfgBtn) as PushButton;
            if (cfgButton != null) _toolButtons.Add(cfgButton);

            // ── Trial enforcement ────────────────────────────────────────────
            ApplyLicenseState();

            return Result.Succeeded;
        }

        public Result OnShutdown(UIControlledApplication app) => Result.Succeeded;

        // ── License / Trial ───────────────────────────────────────────────

        private void ApplyLicenseState()
        {
            if (LicenseManager.IsLicensed())
                return; // Full license — nothing to restrict

            if (LicenseManager.IsTrialExpired)
            {
                foreach (var btn in _toolButtons)
                {
                    btn.Enabled = false;
                    btn.ToolTip = "Trial expired — please contact Mayer E-Concept SRL to activate a full license.";
                }

                TaskDialog dlg = new TaskDialog("ME-Tools — Trial Expired")
                {
                    MainIcon        = TaskDialogIcon.TaskDialogIconWarning,
                    MainInstruction = "Your 30-day trial has expired.",
                    MainContent     =
                        "The ME-Tools add-in is running in trial mode and the evaluation period has ended.\n\n" +
                        "All tools are disabled. Please contact Mayer E-Concept SRL to obtain a full license.\n\n" +
                        "E-Mail: office@mayer-e-concept.com",
                    FooterText      = "ME-Tools Beta — Mayer E-Concept SRL",
                };
                dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, "OK — Close");
                dlg.Show();
            }
            else
            {
                string status = LicenseManager.StatusText;
                foreach (var btn in _toolButtons)
                    btn.ToolTip = $"{btn.ToolTip}\n\n⏱ {status}";
            }
        }

        // ── Icon loader ───────────────────────────────────────────────────

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

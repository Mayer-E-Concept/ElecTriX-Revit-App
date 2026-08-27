// DiagnosticsHandler.cs -- ME-Tools | Diagnostics hub
// Mayer E-Concept SRL
//
// BUG FIXED HERE: DiagnosticsWindow's tiles used to call
// ProjectHealthCheckCommand.Open() / FindStrayElementsCommand.Open() /
// new SettingsWindow(...).ShowDialog() directly from their own click
// handlers. That was safe while DiagnosticsWindow was modal (it inherited
// valid Revit API context from the ribbon click that launched it), but
// broke the moment it became modeless -- a modeless window's event
// handlers have no valid API context of their own, and each of those
// three calls does real, synchronous Revit API work internally
// (Project Health Check's initial scan in particular). Confirmed live:
// this produced exactly the reported symptom -- a pause, then nothing,
// then it just closed, consistent with the internal API call failing
// partway through.
//
// Routing all three through this Handler's Execute(UIApplication) means
// they now always run inside a real Revit-provided callback, exactly the
// same guarantee an IExternalCommand's own Execute() provides -- which is
// what all three were actually relying on in the first place.
using System;
using System.Collections.Generic;
using Autodesk.Revit.UI;

namespace METools
{
    public enum DiagnosticsTileAction { None, OpenStray, OpenHealth, OpenImports, OpenDuplicates, RunAllChecks }

    public class DiagnosticsHandler : IExternalEventHandler
    {
        public DiagnosticsTileAction Action { get; set; }

        // Only used by RunAllChecks -- a consolidated, human-readable
        // summary line per tool, joined by newlines. Deliberately a plain
        // string rather than a structured result: this is a one-shot
        // report shown once in a message box, not something the UI needs
        // to keep querying afterward (the hub tiles themselves read their
        // own history straight from SettingsStore, set as a side effect
        // of the same run below).
        public Action<string> OnRunAllDone { get; set; }

        public string GetName() => "Diagnostics Hub";

        public void Execute(UIApplication app)
        {
            switch (Action)
            {
                case DiagnosticsTileAction.OpenStray:
                    FindStrayElementsCommand.Open(app);
                    break;
                case DiagnosticsTileAction.OpenHealth:
                    ProjectHealthCheckCommand.Open(app);
                    break;
                case DiagnosticsTileAction.OpenImports:
                    // Same reasoning as before for CurrentApp -- still
                    // needed since SettingsWindow reads it internally --
                    // but now set from inside a real valid-context
                    // callback, so ShowDialog() correctly inherits that
                    // context for its whole lifetime, the same way it
                    // would from SettingsCommand.Execute() itself.
                    SettingsCommand.SetCurrentAppForDirectLaunch(app);
                    new SettingsWindow(SettingsWindow.ImportsTabIndex).ShowDialog();
                    SettingsCommand.SetCurrentAppForDirectLaunch(null);
                    break;
                case DiagnosticsTileAction.OpenDuplicates:
                    DuplicateFamilyCommand.Open(app);
                    break;
                case DiagnosticsTileAction.RunAllChecks:
                    RunAllChecks(app);
                    break;
            }
            Action = DiagnosticsTileAction.None;
        }

        // Runs all four tools' own scan logic -- the exact same
        // implementation each tool's own "Scan" button calls, not a
        // separate re-implementation that could quietly drift out of sync.
        // Each check is independently try/caught: one failing (a document
        // in a strange state, a transient API error) shouldn't stop the
        // other three from reporting normally.
        private void RunAllChecks(UIApplication app)
        {
            var doc = app.ActiveUIDocument?.Document;
            var uiDoc = app.ActiveUIDocument;
            if (doc == null || uiDoc == null) { OnRunAllDone?.Invoke(S._("diagnostics.runall_no_document")); return; }

            var lines = new List<string>();

            try
            {
                // Whole Model, not just the active view -- "run everything"
                // should mean everything, matching the spirit of a single
                // consolidated check rather than whatever happens to be on
                // screen right now.
                var stray = FindStrayElementsHandler.ScanForStrayElements(doc, uiDoc, wholeModel: true, out _, out _);
                FindStrayElementsCommand.CachedResults = stray;
                string msg = stray.Count == 0 ? S._("diagnostics.hub_history_clean") : string.Format(S._("straytool.hub_history_found_fmt"), stray.Count);
                SettingsStore.SaveScanHistory("stray", msg);
                lines.Add($"{S._("diagnostics.tile.stray")}: {msg}");
            }
            catch (Exception ex) { lines.Add($"{S._("diagnostics.tile.stray")}: {string.Format(S._("diagnostics.runall_failed_fmt"), ex.Message)}"); }

            try
            {
                var health = ProjectHealthCheckCollector.Run(doc);
                string msg = string.IsNullOrEmpty(health.ErrorMessage)
                    ? (health.AllHealthy ? S._("diagnostics.hub_history_clean") : S._("healthcheck.hub_history_issues"))
                    : health.ErrorMessage;
                if (string.IsNullOrEmpty(health.ErrorMessage)) SettingsStore.SaveScanHistory("health", msg);
                lines.Add($"{S._("diagnostics.tile.health")}: {msg}");
            }
            catch (Exception ex) { lines.Add($"{S._("diagnostics.tile.health")}: {string.Format(S._("diagnostics.runall_failed_fmt"), ex.Message)}"); }

            try
            {
                int orphaned = SettingsWindow.CountOrphanedImportCategories(doc);
                string msg = orphaned == 0 ? S._("diagnostics.hub_history_clean") : string.Format(S._("settings.imports.hub_history_found_fmt"), orphaned);
                SettingsStore.SaveScanHistory("imports", msg);
                lines.Add($"{S._("diagnostics.tile.imports")}: {msg}");
            }
            catch (Exception ex) { lines.Add($"{S._("diagnostics.tile.imports")}: {string.Format(S._("diagnostics.runall_failed_fmt"), ex.Message)}"); }

            try
            {
                var dups = DuplicateFamilyHandler.ScanForDuplicates(doc);
                string msg = dups.Count == 0 ? S._("diagnostics.hub_history_clean") : string.Format(S._("dupfam.hub_history_found_fmt"), dups.Count);
                SettingsStore.SaveScanHistory("dupfam", msg);
                lines.Add($"{S._("diagnostics.tile.duplicates")}: {msg}");
            }
            catch (Exception ex) { lines.Add($"{S._("diagnostics.tile.duplicates")}: {string.Format(S._("diagnostics.runall_failed_fmt"), ex.Message)}"); }

            OnRunAllDone?.Invoke(string.Join("\n", lines));
        }
    }
}

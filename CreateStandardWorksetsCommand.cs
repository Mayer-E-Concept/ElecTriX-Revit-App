// CreateStandardWorksetsCommand.cs — ME-Tools
// Mayer E-Concept SRL
// Legt Standard-Worksets aus einer JSON-Konfigurationsdatei an.
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;

namespace METools
{
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class CreateStandardWorksetsCommand : IExternalCommand
    {
        private const string ConfigRelativePath = "config/standard_worksets.json";

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var doc = commandData.Application.ActiveUIDocument?.Document;
            if (doc == null)
            {
                message = "Kein aktives Dokument gefunden.";
                return Result.Failed;
            }

            // ── Worksharing-Prüfung ─────────────────────────────────────────
            if (!doc.IsWorkshared)
            {
                TaskDialog.Show(
                    "Standard Worksets",
                    "Worksharing ist in diesem Projekt nicht aktiviert.\n\n" +
                    "Bitte aktivieren Sie zuerst das Worksharing über:\n" +
                    "Zusammenarbeiten → Worksharing aktivieren.");
                return Result.Cancelled;
            }

            // ── JSON-Konfiguration laden ────────────────────────────────────
            List<string> configuredWorksets;
            try
            {
                configuredWorksets = LoadWorksetsFromConfig();
            }
            catch (FileNotFoundException ex)
            {
                TaskDialog.Show(
                    "Standard Worksets – Konfigurationsfehler",
                    $"Die Konfigurationsdatei wurde nicht gefunden:\n{ex.Message}\n\n" +
                    $"Erwartet unter: {GetConfigPath()}");
                return Result.Failed;
            }
            catch (Exception ex)
            {
                TaskDialog.Show(
                    "Standard Worksets – Konfigurationsfehler",
                    $"Fehler beim Lesen der Konfigurationsdatei:\n{ex.Message}");
                return Result.Failed;
            }

            if (configuredWorksets.Count == 0)
            {
                TaskDialog.Show("Standard Worksets", "Die Konfigurationsdatei enthält keine Worksets.");
                return Result.Cancelled;
            }

            // ── Vorhandene Worksets einlesen (case-insensitive) ─────────────
            var existingNames = new FilteredWorksetCollector(doc)
                .OfKind(WorksetKind.UserWorkset)
                .ToWorksets()
                .Select(w => w.Name)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            var toCreate  = configuredWorksets.Where(n => !existingNames.Contains(n)).ToList();
            var skipped   = configuredWorksets.Count - toCreate.Count;
            int created   = 0;
            var failed    = new List<string>();

            // ── Worksets anlegen ────────────────────────────────────────────
            if (toCreate.Count > 0)
            {
                using (var t = new Transaction(doc, "Standard Worksets anlegen"))
                {
                    var failOpt = t.GetFailureHandlingOptions();
                    failOpt.SetFailuresPreprocessor(new SilentFailurePreprocessor());
                    t.SetFailureHandlingOptions(failOpt);

                    t.Start();
                    foreach (var name in toCreate)
                    {
                        try
                        {
                            Workset.Create(doc, name);
                            created++;
                        }
                        catch
                        {
                            failed.Add(name);
                        }
                    }
                    t.Commit();
                }
            }

            // ── Zusammenfassung ──────────────────────────────────────────────
            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"✓  {created} Workset(s) neu angelegt");
            sb.AppendLine($"–  {skipped} Workset(s) bereits vorhanden (übersprungen)");

            if (failed.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine($"⚠  {failed.Count} Workset(s) konnten nicht angelegt werden:");
                foreach (var f in failed)
                    sb.AppendLine($"   • {f}");
            }

            TaskDialog.Show("Standard Worksets – Abschluss", sb.ToString());
            return Result.Succeeded;
        }

        // ── Hilfsmethoden ───────────────────────────────────────────────────

        private static string GetConfigPath()
        {
            string addinDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)
                              ?? AppDomain.CurrentDomain.BaseDirectory;
            return Path.Combine(addinDir, ConfigRelativePath.Replace('/', Path.DirectorySeparatorChar));
        }

        private static List<string> LoadWorksetsFromConfig()
        {
            string path = GetConfigPath();
            if (!File.Exists(path))
                throw new FileNotFoundException(path);

            string json = File.ReadAllText(path, System.Text.Encoding.UTF8);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (!root.TryGetProperty("worksets", out var arr))
                return new List<string>();

            return arr.EnumerateArray()
                      .Select(e => e.GetString()?.Trim())
                      .Where(s => !string.IsNullOrEmpty(s))
                      .ToList();
        }

        // ── Stiller Fehler-Präprozessor ──────────────────────────────────────
        private class SilentFailurePreprocessor : IFailuresPreprocessor
        {
            public FailureProcessingResult PreprocessFailures(FailuresAccessor fa)
            {
                foreach (var msg in fa.GetFailureMessages())
                    fa.DeleteWarning(msg);
                return FailureProcessingResult.Continue;
            }
        }
    }
}

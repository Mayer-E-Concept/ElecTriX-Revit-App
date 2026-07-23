// ProjectHealthCheckCommand.cs -- ME-Tools | Project Health Check
// Mayer E-Concept SRL
//
// Checks the things that silently break ElecTriX tools on a project that
// didn't inherit the full company template/setup (e.g. a detached or brand
// new project): the ME-Tools_CircuitTag family, and the shared-parameter
// bindings Circuit Tagger depends on. Born directly from a real debugging
// session where these two gaps took a couple of hours to track down by hand
// -- this turns that into a one-click, few-second check.
//
// Read-only. No transaction, nothing in the model is changed.
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using System;
using System.Collections.Generic;
using System.Linq;

namespace METools
{
    public class ParamCheckRow
    {
        public string ParamName;
        public bool   BoundAtAll;                 // false = parameter not found in ParameterBindings at all
        public List<string> MissingCategories = new List<string>();
        public bool IsHealthy => BoundAtAll && MissingCategories.Count == 0;
    }

    public class HealthCheckResult
    {
        public string ProjectTitle;
        public bool   TagFamilyLoaded;
        public List<ParamCheckRow> ParamRows = new List<ParamCheckRow>();
        public bool AllHealthy => TagFamilyLoaded && ParamRows.All(r => r.IsHealthy);
    }

    public static class ProjectHealthCheckCollector
    {
        private const string TAG_FAMILY_NAME = "ME-Tools_CircuitTag";

        // The exact 6 shared parameters Circuit Tagger writes, and the exact
        // 8 categories it reads/tags across (GetElectricalCategories() in
        // CircuitTaggerHandler.cs) -- kept in sync with that file by hand,
        // since there's no single shared source for both yet.
        private static readonly string[] RequiredParams =
        {
            "Vorsicherung", "FI-Kreis", "Stromkreis Tag", "Schaltkreis", "CAx_Apartment", "CAx_Building",
        };

        private static readonly (BuiltInCategory Cat, string Label)[] RequiredCategories =
        {
            (BuiltInCategory.OST_ElectricalFixtures,   "Electrical Fixtures"),
            (BuiltInCategory.OST_LightingFixtures,      "Lighting Fixtures"),
            (BuiltInCategory.OST_LightingDevices,       "Lighting Devices"),
            (BuiltInCategory.OST_ElectricalEquipment,   "Electrical Equipment"),
            (BuiltInCategory.OST_DataDevices,           "Data Devices"),
            (BuiltInCategory.OST_FireAlarmDevices,      "Fire Alarm Devices"),
            (BuiltInCategory.OST_CommunicationDevices,  "Communication Devices"),
            (BuiltInCategory.OST_SecurityDevices,       "Security Devices"),
        };

        public static HealthCheckResult Run(Document doc)
        {
            var result = new HealthCheckResult { ProjectTitle = doc?.Title ?? "" };
            if (doc == null) return result;

            // 1) Tag family loaded?
            try
            {
                result.TagFamilyLoaded = new FilteredElementCollector(doc)
                    .OfClass(typeof(FamilySymbol))
                    .OfCategory(BuiltInCategory.OST_MultiCategoryTags)
                    .Cast<FamilySymbol>()
                    .Any(fs => string.Equals(fs.Family?.Name, TAG_FAMILY_NAME, StringComparison.OrdinalIgnoreCase));
            }
            catch { result.TagFamilyLoaded = false; }

            // 2) Which categories is each shared parameter actually bound to?
            var boundCategoryIdsByParam = new Dictionary<string, HashSet<int>>(StringComparer.OrdinalIgnoreCase);
            try
            {
                var it = doc.ParameterBindings.ForwardIterator();
                it.Reset();
                while (it.MoveNext())
                {
                    string paramName = it.Key?.Name;
                    if (string.IsNullOrEmpty(paramName)) continue;

                    var binding = it.Current as ElementBinding;
                    if (binding == null) continue;

                    var ids = new HashSet<int>();
                    foreach (Category c in binding.Categories)
                    {
                        try { ids.Add(c.Id.IntegerValue); } catch { }
                    }
                    boundCategoryIdsByParam[paramName] = ids;
                }
            }
            catch { /* leave boundCategoryIdsByParam as whatever was gathered so far */ }

            foreach (var paramName in RequiredParams)
            {
                var row = new ParamCheckRow { ParamName = paramName };
                if (boundCategoryIdsByParam.TryGetValue(paramName, out var boundIds))
                {
                    row.BoundAtAll = true;
                    foreach (var (cat, label) in RequiredCategories)
                        if (!boundIds.Contains((int)cat))
                            row.MissingCategories.Add(label);
                }
                else
                {
                    row.BoundAtAll = false;
                    row.MissingCategories.AddRange(RequiredCategories.Select(c => c.Label));
                }
                result.ParamRows.Add(row);
            }

            return result;
        }
    }

    // Loads/binds the two things Health Check checks for, from files meant to
    // be deployed alongside the installer -- so a new project gets fixed with
    // one click instead of walking through Transfer Project Standards (or
    // manual family-load + parameter binding) by hand every time.
    //
    // Expects two files at %ProgramData%\METools\Resources\:
    //   ME-Tools_CircuitTag.rfa       -- the tag family Circuit Tagger needs
    //   METools_SharedParameters.txt  -- the shared-parameter definition file
    //                                    containing Vorsicherung/FI-Kreis/
    //                                    Stromkreis Tag/Schaltkreis/
    //                                    CAx_Apartment/CAx_Building, with the
    //                                    SAME GUIDs already used elsewhere --
    //                                    reusing a fresh/different file here
    //                                    would create same-name-different-GUID
    //                                    parameters that still show "?" on
    //                                    tags, the exact trap flagged earlier.
    //
    // These files don't ship with this code -- add your actual ones to your
    // Inno Setup script, deployed to the path below (or change ResourcesDir
    // to wherever you'd rather keep them).
    public static class ProjectHealthCheckFixer
    {
        private static readonly string ResourcesDir = System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "METools", "Resources");
        private static readonly string FamilyPath     = System.IO.Path.Combine(ResourcesDir, "ME-Tools_CircuitTag.rfa");
        private static readonly string SharedParamPath = System.IO.Path.Combine(ResourcesDir, "METools_SharedParameters.txt");

        public static List<string> Fix(Document doc)
        {
            var messages = new List<string>();
            if (doc == null) { messages.Add("No active document."); return messages; }

            try
            {
                using (var tx = new Transaction(doc, "ME-Tools: Project Health Check Auto-Fix"))
                {
                    tx.Start();
                    FixTagFamily(doc, messages);
                    FixSharedParameters(doc, messages);
                    tx.Commit();
                }
            }
            catch (Exception ex)
            {
                messages.Add("Auto-fix error: " + ex.Message);
            }
            return messages;
        }

        private static void FixTagFamily(Document doc, List<string> messages)
        {
            bool alreadyLoaded;
            try
            {
                alreadyLoaded = new FilteredElementCollector(doc)
                    .OfClass(typeof(FamilySymbol))
                    .OfCategory(BuiltInCategory.OST_MultiCategoryTags)
                    .Cast<FamilySymbol>()
                    .Any(fs => string.Equals(fs.Family?.Name, "ME-Tools_CircuitTag", StringComparison.OrdinalIgnoreCase));
            }
            catch { alreadyLoaded = false; }

            if (alreadyLoaded) { messages.Add("Tag family: already loaded, nothing to do."); return; }

            if (!System.IO.File.Exists(FamilyPath))
            {
                messages.Add($"Tag family: bundled file not found at '{FamilyPath}' -- add ME-Tools_CircuitTag.rfa to the installer.");
                return;
            }

            try
            {
                bool ok = doc.LoadFamily(FamilyPath, out Family fam);
                messages.Add(ok ? "Tag family: loaded successfully." : "Tag family: LoadFamily reported failure.");
            }
            catch (Exception ex) { messages.Add("Tag family: EXCEPTION " + ex.Message); }
        }

        private static void FixSharedParameters(Document doc, List<string> messages)
        {
            if (!System.IO.File.Exists(SharedParamPath))
            {
                messages.Add($"Shared parameters: bundled file not found at '{SharedParamPath}' -- add METools_SharedParameters.txt to the installer.");
                return;
            }

            var requiredParams = new[] { "Vorsicherung", "FI-Kreis", "Stromkreis Tag", "Schaltkreis", "CAx_Apartment", "CAx_Building" };
            var requiredCats = new[]
            {
                BuiltInCategory.OST_ElectricalFixtures, BuiltInCategory.OST_LightingFixtures,
                BuiltInCategory.OST_LightingDevices,    BuiltInCategory.OST_ElectricalEquipment,
                BuiltInCategory.OST_DataDevices,        BuiltInCategory.OST_FireAlarmDevices,
                BuiltInCategory.OST_CommunicationDevices, BuiltInCategory.OST_SecurityDevices,
            };

            // Application.SharedParametersFilename is session-wide, not scoped
            // to this operation -- save whatever the user already had set and
            // restore it afterward, so this doesn't quietly redirect their
            // shared-parameter file for unrelated work later in the session.
            string previousFile = null;
            try { previousFile = doc.Application.SharedParametersFilename; } catch { }

            try
            {
                doc.Application.SharedParametersFilename = SharedParamPath;
                var defFile = doc.Application.OpenSharedParameterFile();
                if (defFile == null)
                {
                    messages.Add("Shared parameters: could not open the bundled shared parameter file.");
                    return;
                }

                var categorySet = new CategorySet();
                foreach (var cat in requiredCats)
                {
                    var c = doc.Settings.Categories.get_Item(cat);
                    if (c != null) categorySet.Insert(c);
                }

                foreach (var paramName in requiredParams)
                {
                    Definition definition = null;
                    foreach (DefinitionGroup grp in defFile.Groups)
                    {
                        definition = grp.Definitions.get_Item(paramName);
                        if (definition != null) break;
                    }
                    if (definition == null)
                    {
                        messages.Add($"'{paramName}': not found in the bundled shared parameter file -- check it's the same file/GUIDs used elsewhere.");
                        continue;
                    }

                    try
                    {
                        var existing = doc.ParameterBindings.get_Item(definition) as InstanceBinding;
                        if (existing != null)
                        {
                            bool changed = false;
                            foreach (Category c in categorySet)
                            {
                                if (!existing.Categories.Contains(c)) { existing.Categories.Insert(c); changed = true; }
                            }
                            if (changed)
                            {
                                doc.ParameterBindings.ReInsert(definition, existing, BuiltInParameterGroup.PG_DATA);
                                messages.Add($"'{paramName}': added the missing categories to its existing binding.");
                            }
                            else
                            {
                                messages.Add($"'{paramName}': already bound to all categories.");
                            }
                        }
                        else
                        {
                            var binding = new InstanceBinding(categorySet);
                            bool inserted = doc.ParameterBindings.Insert(definition, binding, BuiltInParameterGroup.PG_DATA);
                            messages.Add(inserted ? $"'{paramName}': bound to all 8 categories." : $"'{paramName}': Insert reported failure.");
                        }
                    }
                    catch (Exception ex) { messages.Add($"'{paramName}': EXCEPTION {ex.Message}"); }
                }
            }
            catch (Exception ex) { messages.Add("Shared parameters: EXCEPTION " + ex.Message); }
            finally
            {
                try { if (previousFile != null) doc.Application.SharedParametersFilename = previousFile; } catch { }
            }
        }
    }

    // Re-runs the check on demand (Refresh button) via ExternalEvent, since
    // the window itself has no document access once control returns to the
    // pure UI thread.
    public class ProjectHealthCheckHandler : IExternalEventHandler
    {
        public bool DoFix; // set true before Raise() to run the auto-fix first
        public Action<HealthCheckResult> OnResult;
        public Action<List<string>> OnFixMessages;

        public void Execute(UIApplication app)
        {
            try
            {
                var doc = app.ActiveUIDocument?.Document;
                if (doc == null) return;

                if (DoFix)
                {
                    DoFix = false;
                    var fixMessages = ProjectHealthCheckFixer.Fix(doc);
                    OnFixMessages?.Invoke(fixMessages);
                }

                var result = ProjectHealthCheckCollector.Run(doc);
                OnResult?.Invoke(result);
            }
            catch { }
        }

        public string GetName() => "ME-Tools Project Health Check Refresh";
    }

    [Transaction(TransactionMode.Manual)]
    public class ProjectHealthCheckCommand : IExternalCommand
    {
        private static ProjectHealthCheckWindow _window;

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            Open(commandData.Application);
            return Result.Succeeded;
        }

        public static void Open(UIApplication uiApp)
        {
            if (!METools.LicenseManager.CheckAccessOrExplain()) return;

            var doc = uiApp.ActiveUIDocument?.Document;
            if (doc == null) return;

            if (_window != null && _window.IsVisible)
            { _window.Activate(); _window.Focus(); return; }

            AppSwitcher.Ensure();
            MeToolsWindowBase.RevitHandle = uiApp.MainWindowHandle;

            var result  = ProjectHealthCheckCollector.Run(doc);
            var handler = new ProjectHealthCheckHandler();
            var evt     = ExternalEvent.Create(handler);

            _window = new ProjectHealthCheckWindow(result, evt, handler);
            _window.Closed += (s, e) => _window = null;
            _window.Show();
        }
    }
}

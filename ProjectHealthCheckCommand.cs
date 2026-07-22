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

    // Re-runs the check on demand (Refresh button) via ExternalEvent, since
    // the window itself has no document access once control returns to the
    // pure UI thread.
    public class ProjectHealthCheckHandler : IExternalEventHandler
    {
        public Action<HealthCheckResult> OnResult;

        public void Execute(UIApplication app)
        {
            try
            {
                var doc = app.ActiveUIDocument?.Document;
                if (doc == null) return;
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

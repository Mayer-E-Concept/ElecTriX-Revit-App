// IfcLevelImportCommand.cs -- ME-Tools | IFC Level Importer
// Mayer E-Concept SRL
// Detects any IFC file already linked/imported into the project (best-effort,
// see DetectLinkedIfcFiles below) and hands the list to the window, which
// opens immediately and handles source selection itself -- no file dialog
// or TaskDialog pops up before the window is even visible.
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using System;
using System.Collections.Generic;
using System.Linq;

namespace METools.IfcImport
{
    [Transaction(TransactionMode.Manual)]
    public class IfcLevelImportCommand : IExternalCommand
    {
        private static IfcLevelImportWindow _window;

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            Open(commandData.Application);
            return Result.Succeeded;
        }

        public static void Open(UIApplication uiApp)
        {
            if (!METools.LicenseManager.CheckAccessOrExplain()) return;

            if (_window != null && _window.IsVisible)
            { _window.Activate(); _window.Focus(); return; }

            AppSwitcher.Ensure();
            MeToolsWindowBase.RevitHandle = uiApp.MainWindowHandle;

            var doc = uiApp.ActiveUIDocument?.Document;
            var detected = doc != null ? DetectLinkedIfcFiles(doc) : new List<(string DisplayName, string Path)>();

            var handler  = new IfcLevelImportHandler();
            var extEvent = ExternalEvent.Create(handler);

            _window = new IfcLevelImportWindow(uiApp, detected, extEvent, handler);
            _window.Closed += (s, e) => _window = null;
            _window.Show();
        }

        // Best-effort: looks at every RevitLinkType (covers "Link IFC" on older
        // workflows, which routes through the same link infrastructure as
        // RVT/DWG links), every ImportInstance (older-style "Import IFC" that
        // didn't create a live link), and every CoordinationModel (the
        // mechanism modern Revit actually uses for IFC/NWC coordination
        // links) for an external file reference whose path ends in .ifc.
        // Wrapped defensively throughout -- if anything about this can't be
        // resolved on a given element, that element is just skipped rather
        // than failing the whole detection pass, so this always degrades
        // gracefully to "found nothing, just show the Browse option" rather
        // than ever blocking the tool.
        private static List<(string DisplayName, string Path)> DetectLinkedIfcFiles(Document doc)
        {
            var found = new List<(string, string)>();

            void TryAdd(Element el)
            {
                try
                {
                    var extRef = ExternalFileUtils.GetExternalFileReference(doc, el.Id);
                    if (extRef == null) return;
                    var modelPath = extRef.GetAbsolutePath();
                    if (modelPath == null) return;
                    string path = ModelPathUtils.ConvertModelPathToUserVisiblePath(modelPath);
                    if (string.IsNullOrEmpty(path) || !path.EndsWith(".ifc", StringComparison.OrdinalIgnoreCase)) return;
                    if (found.Any(f => string.Equals(f.Item2, path, StringComparison.OrdinalIgnoreCase))) return;
                    string name = !string.IsNullOrWhiteSpace(el.Name) ? el.Name : System.IO.Path.GetFileName(path);
                    found.Add((name, path));
                }
                catch { /* this element type/version doesn't support it here -- skip quietly */ }
            }

            try { foreach (var e in new FilteredElementCollector(doc).OfClass(typeof(RevitLinkType))) TryAdd(e); } catch { }
            try { foreach (var e in new FilteredElementCollector(doc).OfClass(typeof(ImportInstance))) TryAdd(e); } catch { }
            try { foreach (var e in new FilteredElementCollector(doc).OfClass(typeof(CoordinationModel))) TryAdd(e); } catch { }

            return found;
        }
    }
}

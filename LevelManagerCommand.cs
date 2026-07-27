// LevelManagerCommand.cs — ME-Tools | Level & IFC Manager
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using METools.IfcImport;
using System;
using System.Collections.Generic;
using System.Linq;

namespace METools.LevelManager
{
    [Transaction(TransactionMode.Manual)]
    public class LevelManagerCommand : IExternalCommand
    {
        private static LevelManagerWindow _window;

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

            var handler  = new LevelManagerHandler();
            var extEvent = ExternalEvent.Create(handler);

            // IFC import is now folded into this same window as a second tab
            // rather than being its own separate ribbon app/window.
            var doc = uiApp.ActiveUIDocument?.Document;
            var ifcDetected = doc != null ? DetectLinkedIfcFiles(doc) : new List<(string DisplayName, string Path)>();
            var ifcHandler  = new IfcLevelImportHandler();
            var ifcExtEvent = ExternalEvent.Create(ifcHandler);

            _window = new LevelManagerWindow(extEvent, handler, uiApp, ifcDetected, ifcExtEvent, ifcHandler);
            _window.Closed += (s, e) => _window = null;
            _window.Show();

            // Populate immediately on open.
            handler.Request = new LevelManagerRequest { Action = LevelManagerAction.Refresh };
            extEvent.Raise();
        }

        // Best-effort: looks at every RevitLinkType (this covers "Link IFC" --
        // confirmed against Autodesk's own RevitLinkType.CreateFromIFC() API,
        // which is what actually backs an IFC link under the hood) and every
        // ImportInstance (older-style "Import IFC" that didn't create a live
        // link) for an external file reference whose path ends in .ifc.
        // Wrapped defensively throughout -- if anything about this can't be
        // resolved on a given element, that element is just skipped rather
        // than failing the whole detection pass.
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

            return found;
        }
    }
}

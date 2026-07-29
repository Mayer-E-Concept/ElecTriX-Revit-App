// LevelManagerCommand.cs — ME-Tools | Level & IFC Manager
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using METools.IfcImport;
using System;
using System.Collections.Generic;
using System.IO;
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

        // Best-effort: looks at every RevitLinkType and every ImportInstance
        // for a name containing ".ifc". Detection is name-based because of a
        // documented Revit API quirk: an IFC link's resolved
        // ExternalFileReference path points to an intermediate ".rvt" cache
        // file Revit generates alongside the original on load (e.g.
        // "MyModel.ifc" -> "MyModel.ifc.rvt"), never the ".ifc" file itself --
        // confirmed against Autodesk's own developer forum. Revit sometimes
        // bakes that same ".rvt" suffix into the link's own displayed Name
        // too (e.g. "MyModel.ifc.RVT") rather than leaving it as plain
        // "MyModel.ifc" -- so the name check looks for ".ifc" anywhere in the
        // name, not just as its exact ending.
        //
        // For the actual file path, RevitLinkType.GetExternalResourceReferences()
        // -> ExternalResourceReference.InSessionPath is used first -- this is
        // a distinct, more specific API from ExternalFileUtils, and is what
        // correctly resolves to the real original .ifc file rather than the
        // generated .rvt cache (confirmed against multiple independent Revit
        // API references). ExternalFileUtils is kept only as a fallback for
        // element types that aren't RevitLinkType (e.g. an older "Import IFC"
        // ImportInstance), with the same .rvt-suffix-stripping safety net as
        // before in case that path also resolves to the cache file.
        private static List<(string DisplayName, string Path)> DetectLinkedIfcFiles(Document doc)
        {
            var found = new List<(string, string)>();

            void TryAdd(Element el)
            {
                try
                {
                    string rawName = el.Name ?? "";
                    if (rawName.IndexOf(".ifc", StringComparison.OrdinalIgnoreCase) < 0) return;

                    string path = null;

                    if (el is RevitLinkType linkType)
                    {
                        try
                        {
                            var refs = linkType.GetExternalResourceReferences();
                            foreach (var kvp in refs)
                            {
                                var p = kvp.Value?.InSessionPath;
                                if (!string.IsNullOrEmpty(p)) { path = p; break; }
                            }
                        }
                        catch { /* fall through to the ExternalFileUtils fallback below */ }
                    }

                    if (string.IsNullOrEmpty(path))
                    {
                        try
                        {
                            var extRef = ExternalFileUtils.GetExternalFileReference(doc, el.Id);
                            var modelPath = extRef?.GetAbsolutePath();
                            if (modelPath != null) path = ModelPathUtils.ConvertModelPathToUserVisiblePath(modelPath);
                        }
                        catch { }

                        if (!string.IsNullOrEmpty(path) && path.EndsWith(".rvt", StringComparison.OrdinalIgnoreCase))
                        {
                            string withoutRvt = path.Substring(0, path.Length - 4);
                            if (withoutRvt.EndsWith(".ifc", StringComparison.OrdinalIgnoreCase)) path = withoutRvt;
                        }
                    }

                    if (string.IsNullOrEmpty(path) || !File.Exists(path)) return; // can't locate the real file on disk
                    if (found.Any(f => string.Equals(f.Item2, path, StringComparison.OrdinalIgnoreCase))) return;

                    string displayName = rawName.EndsWith(".rvt", StringComparison.OrdinalIgnoreCase)
                        ? rawName.Substring(0, rawName.Length - 4)
                        : rawName;

                    found.Add((displayName, path));
                }
                catch { /* this element type/version doesn't support it here -- skip quietly */ }
            }

            try { foreach (var e in new FilteredElementCollector(doc).OfClass(typeof(RevitLinkType))) TryAdd(e); } catch { }
            try { foreach (var e in new FilteredElementCollector(doc).OfClass(typeof(ImportInstance))) TryAdd(e); } catch { }

            return found;
        }
    }
}

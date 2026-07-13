// CircuitTaggerCommand.cs -- ME-Tools | Circuit Tagger
// Mayer E-Concept SRL
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using System;

namespace METools.FamilyPlacer
{
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class CircuitTaggerCommand : IExternalCommand
    {
        private static CircuitTaggerWindow _window;
        private static UIApplication       _uiApp;

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try { Open(commandData.Application); return Result.Succeeded; }
            catch (Exception ex) { message = ex.Message; return Result.Failed; }
        }

        public static void Open(UIApplication uiApp)
        {
            if (!METools.LicenseManager.CheckAccessOrExplain()) return;

            if (_window != null && _window.IsVisible)
            {
                _window.Activate(); _window.Focus(); return;
            }

            AppSwitcher.Ensure();
            MeToolsWindowBase.RevitHandle = uiApp.MainWindowHandle;
            _uiApp = uiApp;

            var handler  = new CircuitTaggerHandler();
            var extEvent = ExternalEvent.Create(handler);

            _window = new CircuitTaggerWindow(uiApp, extEvent, handler);
            _window.Closed += (s, e) =>
            {
                // Unsubscribe DocumentChanged when window closes
                try { uiApp.Application.DocumentChanged -= OnDocChanged; } catch { }
                _window = null;
            };

            // Subscribe to DocumentChanged for auto-refresh of stats
            try { uiApp.Application.DocumentChanged += OnDocChanged; } catch { }

            _window.Show();
        }

        private static void OnDocChanged(object sender, Autodesk.Revit.DB.Events.DocumentChangedEventArgs e)
        {
            // Trigger stats refresh whenever elements are added/deleted/modified
            // Only refresh if the stats tab is active to avoid performance cost
            if (_window == null || !_window.IsVisible) return;
            try
            {
                _window.Dispatcher.BeginInvoke(
                    System.Windows.Threading.DispatcherPriority.Background,
                    new Action(() =>
                    {
                        try { _window.RefreshStatsIfVisible(); } catch { }
                    }));
            }
            catch { }
        }
    }
}

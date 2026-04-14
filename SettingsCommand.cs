// SettingsCommand.cs — ME-Tools Settings Button
// Mayer E-Concept SRL
using System.Reflection;
using System.Windows.Media.Imaging;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace METools
{
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class SettingsCommand : IExternalCommand
    {
        private static SettingsWindow _window;

        /// <summary>Reference to the ribbon button — set by App.cs on startup.</summary>
        public static PushButton RibbonButton { get; set; }

        public Result Execute(
            ExternalCommandData commandData,
            ref string message,
            ElementSet elements)
        {
            if (_window != null && _window.IsVisible)
            {
                _window.Activate();
                _window.Focus();
                return Result.Succeeded;
            }

            _window = new SettingsWindow();
            _window.Closed += (s, e) => _window = null;
            _window.Show();

            return Result.Succeeded;
        }
    }
}

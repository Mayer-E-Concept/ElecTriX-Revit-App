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
using Autodesk.Revit.UI;

namespace METools
{
    public enum DiagnosticsTileAction { None, OpenStray, OpenHealth, OpenImports }

    public class DiagnosticsHandler : IExternalEventHandler
    {
        public DiagnosticsTileAction Action { get; set; }

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
            }
            Action = DiagnosticsTileAction.None;
        }
    }
}

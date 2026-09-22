// OpenNexusCommand.cs -- ElecTriX-Revit-App
// Mayer E-Concept SRL
//
// Ribbon button that opens Nexus, the standalone assistant app -- or, if
// it's already running, brings its existing window to the foreground
// instead of launching a second copy. Nexus itself is a separate .exe,
// not something that runs inside Revit's own process, so this command's
// whole job is just finding or starting that process and focusing it.
using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace METools.Nexus
{
    [Transaction(TransactionMode.ReadOnly)]
    public class OpenNexusCommand : IExternalCommand
    {
        // Checked in this order. The first path is where setup.iss
        // actually installs Nexus on a real customer machine (only
        // present at all if the "assistant" Component was selected at
        // install time) -- that's the only one that matters for anyone
        // who isn't actively developing both projects side by side. The
        // X:\ dev-machine paths below it are fallbacks that only ever
        // resolve to anything on this project's own dev machine; Release
        // first since that's what an actual daily-use build would be,
        // Debug as the last resort. The TFM-named folder
        // (net8.0-windows10.0.22621.0) must match Nexus.csproj's own
        // TargetFramework exactly -- .NET builds each target framework
        // into its own uniquely-named subfolder, so if that value ever
        // changes again (say, if the WinRT speech dependency is
        // removed), the dev-path entries need updating to match or
        // they'll silently point at a stale build from the old TFM
        // folder again, exactly like this one did once before.
        private static readonly string[] CandidatePaths =
        {
            @"C:\Program Files\Mayer E-Concept\Nexus\Nexus.exe",
            @"X:\02_sabloane\01_Revit\Nexus-Assistant\bin\Release\net8.0-windows10.0.22621.0\Nexus.exe",
            @"X:\02_sabloane\01_Revit\Nexus-Assistant\bin\Debug\net8.0-windows10.0.22621.0\Nexus.exe",
        };

        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        [DllImport("user32.dll")]
        private static extern bool IsIconic(IntPtr hWnd);

        private const int SW_RESTORE = 9;

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                // Already running -- focus the existing window instead of
                // starting a second instance.
                var running = Process.GetProcessesByName("Nexus");
                foreach (var proc in running)
                {
                    if (proc.MainWindowHandle != IntPtr.Zero)
                    {
                        if (IsIconic(proc.MainWindowHandle))
                            ShowWindow(proc.MainWindowHandle, SW_RESTORE);
                        SetForegroundWindow(proc.MainWindowHandle);
                        return Result.Succeeded;
                    }
                }

                var exePath = Array.Find(CandidatePaths, File.Exists);
                if (exePath == null)
                {
                    message = "Couldn't find Nexus. Either it wasn't installed with your license " +
                              "(the Assistant is an optional component in setup_metools), or it needs " +
                              "to be built first if you're developing it locally. Checked:\n" +
                              string.Join("\n", CandidatePaths);
                    return Result.Failed;
                }

                var startInfo = new ProcessStartInfo
                {
                    FileName = exePath,
                    WorkingDirectory = Path.GetDirectoryName(exePath),
                    UseShellExecute = true,
                };

                // Revit's own Application.Username -- distinct from the
                // Windows login name, and typically the more meaningful
                // one to greet someone by, since it's what Revit itself
                // already calls them (worksharing, sync history, etc).
                // Only available at all because this command runs inside
                // Revit's own process with full API access; Nexus itself,
                // launched as a separate .exe, has no way to ask Revit
                // this directly, so it has to be handed over here at
                // launch time or not at all.
                var revitUsername = commandData.Application.Application.Username;
                if (!string.IsNullOrWhiteSpace(revitUsername))
                    startInfo.ArgumentList.Add($"--revit-user={revitUsername}");

                Process.Start(startInfo);
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                message = $"Couldn't open Nexus: {ex.Message}";
                return Result.Failed;
            }
        }
    }
}

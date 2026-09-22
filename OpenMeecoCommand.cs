// OpenMeecoCommand.cs -- ElecTriX-Revit-App
// Mayer E-Concept SRL
//
// Ribbon button that opens Meeco, the standalone assistant app -- or, if
// it's already running, brings its existing window to the foreground
// instead of launching a second copy. Meeco itself is a separate .exe,
// not something that runs inside Revit's own process, so this command's
// whole job is just finding or starting that process and focusing it.
using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace METools.Meeco
{
    [Transaction(TransactionMode.ReadOnly)]
    public class OpenMeecoCommand : IExternalCommand
    {
        // Checked in this order. The first path is where setup.iss
        // actually installs Meeco on a real customer machine (only
        // present at all if the "assistant" Component was selected at
        // install time) -- that's the only one that matters for anyone
        // who isn't actively developing both projects side by side. The
        // X:\ dev-machine paths below it are fallbacks that only ever
        // resolve to anything on this project's own dev machine; Release
        // first since that's what an actual daily-use build would be,
        // Debug as the last resort. The TFM-named folder
        // (net8.0-windows10.0.22621.0) must match Meeco.csproj's own
        // TargetFramework exactly -- .NET builds each target framework
        // into its own uniquely-named subfolder, so if that value ever
        // changes again (say, if the WinRT speech dependency is
        // removed), the dev-path entries need updating to match or
        // they'll silently point at a stale build from the old TFM
        // folder again, exactly like this one did once before.
        private static readonly string[] CandidatePaths =
        {
            @"C:\Program Files\Mayer E-Concept\Meeco\Meeco.exe",
            @"X:\02_sabloane\01_Revit\Meeco-Assistant\bin\Release\net8.0-windows10.0.22621.0\Meeco.exe",
            @"X:\02_sabloane\01_Revit\Meeco-Assistant\bin\Debug\net8.0-windows10.0.22621.0\Meeco.exe",
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
                var running = Process.GetProcessesByName("Meeco");
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
                    message = "Couldn't find Meeco. Either it wasn't installed with your license " +
                              "(the Assistant is an optional component in setup_metools), or it needs " +
                              "to be built first if you're developing it locally. Checked:\n" +
                              string.Join("\n", CandidatePaths);
                    return Result.Failed;
                }

                Process.Start(new ProcessStartInfo
                {
                    FileName = exePath,
                    WorkingDirectory = Path.GetDirectoryName(exePath),
                    UseShellExecute = true,
                });
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                message = $"Couldn't open Meeco: {ex.Message}";
                return Result.Failed;
            }
        }
    }
}

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
        // Checked in this order -- Release first, since that's what an
        // actual daily-use build would be, falling back to Debug since
        // that's what's most likely to exist during development. The
        // TFM-named folder (net8.0-windows10.0.22621.0) must match
        // Meeco.csproj's own TargetFramework exactly -- .NET builds each
        // target framework into its own uniquely-named subfolder, so if
        // that value ever changes again (say, if the WinRT speech
        // dependency is removed), these paths need updating to match or
        // this will silently launch a stale build from the old TFM
        // folder again, exactly like this one did.
        private static readonly string[] CandidatePaths =
        {
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
                    message = "Couldn't find Meeco.exe. Checked:\n" + string.Join("\n", CandidatePaths) +
                              "\n\nBuild the Meeco project first, or update the path in OpenMeecoCommand.cs " +
                              "if it's moved.";
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

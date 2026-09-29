// UpdateChecker.cs -- ME-Tools | Auto-update from the office share
// Mayer E-Concept SRL
//
// Same idea as Nexus Office's update feed: publish-release.ps1 copies the
// built installer to FeedDir and writes latest.json next to it
// ({ "version": "2.3.7", "installer": "setup_metools_vnxs_2.3.7.exe", "notes": "..." }).
// On every Revit start this reads that file in the background and, if it
// names a newer version than the loaded METools.dll, asks once per session:
//
//   * Install when Revit closes -- the installer starts from OnShutdown,
//     after Revit has let go of METools.dll (recommended).
//   * Install now -- starts the installer wizard right away; its "close
//     applications" page asks to close Revit, so work should be saved first.
//   * Later -- asked again on the next Revit start.
//
// The installer is per-machine (ProgramData\Autodesk\Revit\Addins), so
// Windows shows its admin (UAC) prompt either way. The share being offline
// or the feed not existing yet is silent -- nothing to bother anyone with.
// Every step is logged to %APPDATA%\METools\update.log.
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Events;

namespace METools
{
    public static class UpdateChecker
    {
        public const string FeedDir = @"\\Database\MEC_Database\02_sabloane\01_Revit\ElecTriX-Revit-App\releases";

        private class Feed
        {
            public string version { get; set; } = "";
            public string installer { get; set; } = "";
            public string notes { get; set; } = "";
        }

        private static UIControlledApplication _app;
        private static volatile Feed _pending;       // newer version found, dialog not shown yet
        private static volatile bool _checkDone;
        private static string _installOnExit;        // installer path to start from OnShutdown
        private static Task<string> _localCopy;      // copy of the installer in %TEMP%

        public static Version CurrentVersion =>
            Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0);

        public static void Register(UIControlledApplication app)
        {
            _app = app;
            app.ControlledApplication.ApplicationInitialized += (s, e) =>
            {
                // Subscribed here, on the API thread (Revit events can't be
                // hooked from a worker thread); the dialog is shown from
                // Idling once the check below has finished.
                _app.Idling += OnIdling;

                // Off the UI thread: a share that doesn't answer can take a
                // while to time out, and Revit mustn't wait for that.
                Task.Run(() =>
                {
                    try
                    {
                        var feed = ReadFeed();
                        if (feed != null && ParseVersion(feed.version) > Normalize(CurrentVersion))
                        {
                            Log($"Update available: {feed.version} (installed {CurrentVersion})");
                            _pending = feed;
                        }
                    }
                    catch (Exception ex) { Log("Check failed: " + ex.Message); }
                    finally { _checkDone = true; }
                });
            };
        }

        // From App.OnShutdown.
        public static void OnShutdown()
        {
            if (_installOnExit == null) return;
            try
            {
                var path = _installOnExit;
                if (_localCopy != null && _localCopy.Wait(TimeSpan.FromSeconds(20)) && File.Exists(_localCopy.Result))
                    path = _localCopy.Result;
                // /SILENT: progress window only, same components as last
                // time (Inno remembers them). Still elevated -> UAC prompt.
                Start(path, "/SILENT /NORESTART");
            }
            catch (Exception ex) { Log("Could not start installer on exit: " + ex.Message); }
        }

        private static Feed ReadFeed()
        {
            var file = Path.Combine(FeedDir, "latest.json");
            if (!File.Exists(file)) return null;
            var json = File.ReadAllText(file);
            var feed = JsonSerializer.Deserialize<Feed>(json);
            if (feed == null || string.IsNullOrWhiteSpace(feed.version) || string.IsNullOrWhiteSpace(feed.installer)) return null;
            if (!File.Exists(Path.Combine(FeedDir, feed.installer)))
            {
                Log($"latest.json names {feed.installer}, but that file isn't in {FeedDir}");
                return null;
            }
            return feed;
        }

        private static void OnIdling(object sender, IdlingEventArgs e)
        {
            if (!_checkDone) return; // still reading the share
            _app.Idling -= OnIdling;
            var feed = _pending;
            _pending = null;
            if (feed == null) return;

            try
            {
                var shareInstaller = Path.Combine(FeedDir, feed.installer);
                var td = new TaskDialog("ME-Tools")
                {
                    MainInstruction = string.Format(S._("update.title_fmt"), feed.version),
                    MainContent = string.Format(S._("update.body_fmt"), ShortVersion(CurrentVersion)) +
                                  (string.IsNullOrWhiteSpace(feed.notes) ? "" : "\n\n" + feed.notes.Trim()),
                    FooterText = S._("update.admin_note"),
                    CommonButtons = TaskDialogCommonButtons.Close,
                    DefaultButton = TaskDialogResult.CommandLink1,
                };
                td.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, S._("update.on_exit"), S._("update.on_exit_sub"));
                td.AddCommandLink(TaskDialogCommandLinkId.CommandLink2, S._("update.now"), S._("update.now_sub"));

                switch (td.Show())
                {
                    case TaskDialogResult.CommandLink1:
                        _installOnExit = shareInstaller;
                        _localCopy = Task.Run(() => CopyToTemp(shareInstaller));
                        Log($"Will install {feed.version} when Revit closes");
                        break;
                    case TaskDialogResult.CommandLink2:
                        Log($"Installing {feed.version} now");
                        var local = CopyToTemp(shareInstaller);
                        Start(File.Exists(local) ? local : shareInstaller, "");
                        break;
                    default:
                        Log($"Update {feed.version} postponed");
                        break;
                }
            }
            catch (Exception ex)
            {
                Log("Update dialog failed: " + ex.Message);
            }
        }

        // Running from %TEMP% means the install doesn't depend on the share
        // staying reachable, and no one's Revit keeps the share copy open.
        private static string CopyToTemp(string source)
        {
            try
            {
                var dest = Path.Combine(Path.GetTempPath(), Path.GetFileName(source));
                File.Copy(source, dest, true);
                return dest;
            }
            catch (Exception ex)
            {
                Log("Copy to temp failed, will run from the share: " + ex.Message);
                return source;
            }
        }

        private static void Start(string path, string args)
        {
            // UseShellExecute so Windows honors the installer's
            // "requires administrator" manifest and shows the UAC prompt.
            Process.Start(new ProcessStartInfo(path, args) { UseShellExecute = true });
            Log($"Started {path} {args}");
        }

        // "nxs_2.3.7", "A_2.3.7", "2.3.7" -> 2.3.7.0
        private static Version ParseVersion(string s)
        {
            var m = System.Text.RegularExpressions.Regex.Match(s ?? "", @"\d+(\.\d+)+");
            return m.Success && Version.TryParse(m.Value, out var v) ? Normalize(v) : new Version(0, 0, 0, 0);
        }

        private static Version Normalize(Version v) =>
            new Version(v.Major, v.Minor, Math.Max(v.Build, 0), Math.Max(v.Revision, 0));

        private static string ShortVersion(Version v) => $"{v.Major}.{v.Minor}.{Math.Max(v.Build, 0)}";

        private static void Log(string line)
        {
            try
            {
                var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "METools");
                Directory.CreateDirectory(dir);
                File.AppendAllText(Path.Combine(dir, "update.log"), $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {line}{Environment.NewLine}");
            }
            catch { /* logging must never break Revit startup */ }
        }
    }
}

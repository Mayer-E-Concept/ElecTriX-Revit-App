// SharedJsonStorage.cs -- ME-Tools | Shared JSON file I/O
// Mayer E-Concept SRL
//
// The read-modify-write-with-retry logic Comments and Tasks each needed
// for their own shared-folder JSON files -- previously two separate,
// hand-copied implementations of the exact same mechanics. Extracted here
// so a bug found in one path gets fixed in both, instead of relying on
// remembering to patch two places.
//
// Deliberately generic over the WRAPPER type only (not the item type),
// and doesn't know or care what that wrapper's JSON property is called --
// CommentsFile's "Comments" property and TasksFileWrapper's "Tasks"
// property both keep their exact existing names untouched, so every
// already-written file on the shared drive keeps parsing exactly as it
// did before this refactor. This helper only owns the file I/O mechanics;
// each caller still owns its own concrete wrapper/item shape.
using System;
using System.IO;
using System.Text.Json;
using System.Threading;

namespace METools
{
    public static class SharedJsonStorage
    {
        // Distinguishes "nothing there yet" (missing/empty file -- a fresh
        // TWrapper is the correct, safe answer) from "the file exists but
        // won't parse" (e.g. a network interruption mid-write) -- the
        // second case must never be silently treated as empty, since the
        // only caller of this that writes (Mutate) would otherwise
        // overwrite whatever's actually still in that file.
        public static bool TryReadRaw<TWrapper>(string path, out TWrapper wrapper, out string parseError)
            where TWrapper : new()
        {
            wrapper = new TWrapper();
            parseError = null;
            if (path == null || !File.Exists(path)) return true;

            for (int attempt = 0; attempt < 3; attempt++)
            {
                try
                {
                    var json = File.ReadAllText(path);
                    if (string.IsNullOrWhiteSpace(json)) return true;
                    wrapper = JsonSerializer.Deserialize<TWrapper>(json) ?? new TWrapper();
                    return true;
                }
                catch (IOException)
                {
                    Thread.Sleep(150); // likely someone else writing right now
                }
                catch (Exception ex)
                {
                    parseError = ex.Message;
                    return false;
                }
            }

            parseError = "File was locked/busy after several attempts.";
            return false;
        }

        // Read-modify-write with retry: reloads the file fresh immediately
        // before writing (so a near-simultaneous save from someone else
        // isn't clobbered), retries briefly if momentarily locked, and
        // refuses to write at all if the existing file can't be read
        // cleanly -- see TryReadRaw's comment for why overwriting in that
        // state would be actively destructive, not just inconvenient.
        public static bool Mutate<TWrapper>(string sharedFolder, string path, Action<TWrapper> mutation,
            string missingFolderMessage, out string error)
            where TWrapper : new()
        {
            error = "";
            if (string.IsNullOrWhiteSpace(sharedFolder))
            {
                error = missingFolderMessage;
                return false;
            }
            if (string.IsNullOrWhiteSpace(path))
            {
                error = "Could not identify this project.";
                return false;
            }

            try { Directory.CreateDirectory(sharedFolder); }
            catch (Exception ex) { error = "Shared folder not reachable: " + ex.Message; return false; }

            for (int attempt = 0; attempt < 5; attempt++)
            {
                try
                {
                    if (!TryReadRaw<TWrapper>(path, out var wrapper, out string parseError))
                    {
                        error = $"Shared file appears corrupted ({parseError}). Nothing was changed -- " +
                                "check the file on the shared drive directly before trying again.";
                        return false;
                    }

                    mutation(wrapper);

                    var json = JsonSerializer.Serialize(wrapper, new JsonSerializerOptions { WriteIndented = true });
                    File.WriteAllText(path, json);
                    return true;
                }
                catch (IOException)
                {
                    Thread.Sleep(200);
                }
                catch (Exception ex)
                {
                    error = ex.Message;
                    return false;
                }
            }

            error = "Shared file was busy after several attempts -- try again.";
            return false;
        }
    }
}

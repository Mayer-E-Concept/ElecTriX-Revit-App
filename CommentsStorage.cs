// CommentsStorage.cs -- ME-Tools | Project Comments shared storage
// Mayer E-Concept SRL
//
// Two separate problems, each solved differently:
//   1. Identifying "this same project" across different computers -- solved
//     by stamping a persistent GUID into the model itself via Extensible
//     Storage (the exact technique KonfigStorage.cs already uses for circuit
//     config), so comments stay matched to the project even if the .rvt file
//     is renamed or moved. A file path or file name would NOT survive that.
//   2. Making a comment left on one computer visible on another -- solved by
//     one JSON file per project on a shared network folder every team member
//     can reach (confirmed available before building this). Reads/writes
//     retry briefly on failure, since two people saving within the same
//     second is a real, if rare, possibility with a plain shared file.
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.ExtensibleStorage;

namespace METools.Comments
{
    public static class CommentsStorage
    {
        private static readonly Guid SCHEMA_GUID = new Guid("6F3A9C1D-2B4E-4A7F-9C3D-1E5B8A6D4F20");
        private const string SCHEMA_NAME = "METoolsCommentsProjectId";
        private const string FIELD_ID    = "ProjectCommentsId";

        // ── Per-project identity, stamped into the model itself ───────────
        public static string GetOrCreateProjectId(Document doc)
        {
            if (doc == null) return null;

            try
            {
                var schema = Schema.Lookup(SCHEMA_GUID);
                if (schema != null && doc.ProjectInformation != null)
                {
                    var entity = doc.ProjectInformation.GetEntity(schema);
                    if (entity != null && entity.IsValid())
                    {
                        var id = entity.Get<string>(FIELD_ID);
                        if (!string.IsNullOrWhiteSpace(id)) return id;
                    }
                }
            }
            catch { }

            // Not stamped yet on this model -- mint one and save it.
            var newId = Guid.NewGuid().ToString("N");
            try
            {
                if (doc.IsReadOnly || doc.ProjectInformation == null) return newId; // usable this session either way
                var schema = Schema.Lookup(SCHEMA_GUID) ?? BuildSchema();
                using (var tx = new Transaction(doc, "ME-Tools: Stamp project comments ID"))
                {
                    tx.Start();
                    var entity = new Entity(schema);
                    entity.Set<string>(FIELD_ID, newId);
                    doc.ProjectInformation.SetEntity(entity);
                    tx.Commit();
                }
            }
            catch { }
            return newId;
        }

        private static Schema BuildSchema()
        {
            var builder = new SchemaBuilder(SCHEMA_GUID);
            builder.SetSchemaName(SCHEMA_NAME);
            builder.SetReadAccessLevel(AccessLevel.Public);
            builder.SetWriteAccessLevel(AccessLevel.Public);
            builder.SetVendorId("MayerEConceptSRL");
            builder.AddSimpleField(FIELD_ID, typeof(string));
            return builder.Finish();
        }

        // ── Local settings: shared folder path + sound toggle ─────────────
        // These two are genuinely per-machine (each user points at the same
        // shared drive, but from their own local settings), unlike the
        // comments themselves -- so %APPDATA% is correct here, not the
        // shared folder.
        private static string SettingsPath =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "METools", "comments-settings.json");

        private class LocalSettings
        {
            public string SharedFolder { get; set; } = "";
            public bool SoundEnabled { get; set; } = true;
        }

        private static LocalSettings LoadLocalSettings()
        {
            try
            {
                if (File.Exists(SettingsPath))
                {
                    var json = File.ReadAllText(SettingsPath);
                    var s = JsonSerializer.Deserialize<LocalSettings>(json);
                    if (s != null) return s;
                }
            }
            catch { }
            return new LocalSettings();
        }

        private static void SaveLocalSettings(LocalSettings s)
        {
            try
            {
                var dir = Path.GetDirectoryName(SettingsPath);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                File.WriteAllText(SettingsPath, JsonSerializer.Serialize(s, new JsonSerializerOptions { WriteIndented = true }));
            }
            catch { }
        }

        public static string GetSharedFolder() => LoadLocalSettings().SharedFolder ?? "";

        public static void SetSharedFolder(string path)
        {
            var s = LoadLocalSettings();
            s.SharedFolder = path ?? "";
            SaveLocalSettings(s);
        }

        public static bool GetSoundEnabled() => LoadLocalSettings().SoundEnabled;

        public static void SetSoundEnabled(bool enabled)
        {
            var s = LoadLocalSettings();
            s.SoundEnabled = enabled;
            SaveLocalSettings(s);
        }

        // ── Comment file I/O on the shared folder ──────────────────────────
        private static string GetFilePath(string projectId)
        {
            var folder = GetSharedFolder();
            if (string.IsNullOrWhiteSpace(folder) || string.IsNullOrWhiteSpace(projectId)) return null;
            return Path.Combine(folder, $"METools_Comments_{projectId}.json");
        }

        public static List<ProjectComment> LoadAll(string projectId)
        {
            var path = GetFilePath(projectId);
            if (path == null || !File.Exists(path)) return new List<ProjectComment>();

            for (int attempt = 0; attempt < 3; attempt++)
            {
                try
                {
                    var json = File.ReadAllText(path);
                    if (string.IsNullOrWhiteSpace(json)) return new List<ProjectComment>();
                    var file = JsonSerializer.Deserialize<CommentsFile>(json);
                    return file?.Comments ?? new List<ProjectComment>();
                }
                catch (IOException) { Thread.Sleep(150); } // likely someone else writing right now
                catch { break; }
            }
            return new List<ProjectComment>();
        }

        // Read-modify-write with retry: reloads the file fresh immediately before
        // writing (so a near-simultaneous save from someone else isn't clobbered),
        // and retries briefly if the file is momentarily locked by that other save.
        public static bool Mutate(string projectId, Action<List<ProjectComment>> mutation, out string error)
        {
            error = "";
            var folder = GetSharedFolder();
            if (string.IsNullOrWhiteSpace(folder))
            {
                error = "No shared comments folder configured yet.";
                return false;
            }
            if (string.IsNullOrWhiteSpace(projectId))
            {
                error = "Could not identify this project.";
                return false;
            }
            try { Directory.CreateDirectory(folder); }
            catch (Exception ex) { error = "Shared folder not reachable: " + ex.Message; return false; }

            var path = GetFilePath(projectId);
            for (int attempt = 0; attempt < 5; attempt++)
            {
                try
                {
                    var list = LoadAll(projectId);
                    mutation(list);
                    var json = JsonSerializer.Serialize(new CommentsFile { Comments = list },
                        new JsonSerializerOptions { WriteIndented = true });
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
            error = "Shared comments file was busy after several attempts -- try again.";
            return false;
        }
    }
}

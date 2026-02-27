using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using Newtonsoft.Json;
using UnityEngine;

namespace SaveGame
{
    public class SaveManager
    {
        public bool DebugMode           = true;
        public int  MaxSnapshotsPerSave = 10;

        // ── Configurable file/folder names ───────────────────────────────
        public string SavesFolderName     = "Saves";
        public string WorldDataFilename   = "WorldData.json";
        public string ThumbnailFilename   = "savefile_thumb.png";
        public string SnapshotsFolderName = "Snapshots";
        public string SaveFileExtension   = ".gamesave";
        public string SidecarExtension    = ".gamesave.meta";

        private readonly Dictionary<string, Texture2D> _thumbnailCache = new();

        // ── Path helpers ─────────────────────────────────────────────────

        /// <summary>Absolute path to the root saves folder on disk.</summary>
        public string RootSavesFolder =>
            Path.Combine(Application.persistentDataPath, SavesFolderName);

        private string GetSaveFilePath(string identifier) =>
            Path.Combine(RootSavesFolder, identifier + SaveFileExtension);

        private string GetSidecarPath(string identifier) =>
            Path.Combine(RootSavesFolder, identifier + SidecarExtension);

        private string GetSnapshotDataEntry(string snapshotId) =>
            $"{SnapshotsFolderName}/{snapshotId}.data";

        private string GetSnapshotPngEntry(string snapshotId) =>
            $"{SnapshotsFolderName}/{snapshotId}.png";

        // ── Factory ──────────────────────────────────────────────────────

        /// <summary>Creates a new save slot, writes zip and sidecar, returns the SaveFile handle.</summary>
        public SaveFile CreateSave(
            string name,
            string worldData = null,
            byte[] thumbnail = null,
            Dictionary<string, string> metadata = null)
        {
            if (string.IsNullOrEmpty(name))
            {
                Debug.LogError("Save name cannot be null or empty.");
                return null;
            }

            Directory.CreateDirectory(RootSavesFolder);

            var saveFile = new SaveFile
            {
                Identifier         = GenerateIdentifier("save"),
                Name               = name,
                CreatedAt          = DateTime.UtcNow,
                LastModified       = DateTime.UtcNow,
                SnapshotCount      = 0,
                MetadataProperties = metadata ?? new Dictionary<string, string>(),
                Snapshots          = new List<SnapshotMetadata>(),
                WorldData          = worldData,
                ThumbnailPNG       = thumbnail
            };

            using (var archive = ZipFile.Open(GetSaveFilePath(saveFile.Identifier), ZipArchiveMode.Create))
            {
                if (!string.IsNullOrEmpty(worldData))
                    WriteEntry(archive, WorldDataFilename, worldData);

                if (thumbnail != null && thumbnail.Length > 0)
                    WriteEntryBytes(archive, ThumbnailFilename, thumbnail);
            }

            saveFile.SetManager(this);
            WriteSidecar(saveFile);
            return saveFile;
        }

        // ── Discovery ────────────────────────────────────────────────────

        /// <summary>Returns all save slots by reading sidecars only. No zip is opened.</summary>
        public List<SaveFile> GetAllSaves()
        {
            if (!Directory.Exists(RootSavesFolder))
                return new List<SaveFile>();

            return Directory
                .GetFiles(RootSavesFolder, "*" + SidecarExtension)
                .Select(path =>
                {
                    try
                    {
                        var save = ReadSidecar(path);
                        save.SetManager(this);
                        return save;
                    }
                    catch (Exception e)
                    {
                        Debug.LogWarning($"Failed to read sidecar {path}: {e.Message}");
                        return null;
                    }
                })
                .Where(s => s != null)
                .ToList();
        }

        /// <summary>Returns one save slot by reading its sidecar. No zip is opened.</summary>
        public SaveFile GetSave(string identifier)
        {
            string path = GetSidecarPath(identifier);
            if (!File.Exists(path))
            {
                Debug.LogWarning($"Sidecar not found: {path}");
                return null;
            }

            var save = ReadSidecar(path);
            save.SetManager(this);
            return save;
        }

        // ── Internal ops (called by SaveFile) ────────────────────────────

        internal SnapshotMetadata AddSnapshotInternal(
            SaveFile saveFile,
            string snapshotData,
            SaveType saveType,
            string name = null,
            byte[] screenshotPNG = null,
            Dictionary<string, string> metadata = null)
        {
            if (string.IsNullOrEmpty(snapshotData))
            {
                Debug.LogError("SnapshotData cannot be null or empty.");
                return null;
            }

            var snapshotMeta = new SnapshotMetadata
            {
                Identifier         = GenerateIdentifier("snap"),
                Name               = name ?? string.Empty,
                Timestamp          = DateTime.UtcNow,
                SaveType           = saveType,
                MetadataProperties = metadata ?? new Dictionary<string, string>()
            };

            using (var archive = ZipFile.Open(GetSaveFilePath(saveFile.Identifier), ZipArchiveMode.Update))
            {
                WriteEntry(archive, GetSnapshotDataEntry(snapshotMeta.Identifier), snapshotData);

                if (screenshotPNG != null && screenshotPNG.Length > 0)
                    WriteEntryBytes(archive, GetSnapshotPngEntry(snapshotMeta.Identifier), screenshotPNG);

                saveFile.Snapshots.Add(snapshotMeta);
                saveFile.Snapshots = saveFile.Snapshots.OrderByDescending(s => s.Timestamp).ToList();

                while (saveFile.Snapshots.Count > MaxSnapshotsPerSave)
                {
                    SnapshotMetadata oldest = saveFile.Snapshots.Last();
                    saveFile.Snapshots.RemoveAt(saveFile.Snapshots.Count - 1);
                    archive.GetEntry(GetSnapshotDataEntry(oldest.Identifier))?.Delete();
                    archive.GetEntry(GetSnapshotPngEntry(oldest.Identifier))?.Delete();
                    _thumbnailCache.Remove(ThumbnailKey(saveFile.Identifier, oldest.Identifier));
                }
            }

            saveFile.SnapshotCount = saveFile.Snapshots.Count;
            saveFile.LastModified  = DateTime.UtcNow;
            WriteSidecar(saveFile);
            return snapshotMeta;
        }

        internal void LoadGlobalDataInternal(SaveFile saveFile)
        {
            string zipPath = GetSaveFilePath(saveFile.Identifier);
            if (!File.Exists(zipPath))
            {
                Debug.LogError($"Save file not found: {zipPath}");
                return;
            }

            using var archive = ZipFile.OpenRead(zipPath);

            var worldEntry = archive.GetEntry(WorldDataFilename);
            if (worldEntry != null) saveFile.WorldData = ReadEntry(worldEntry);

            var thumbEntry = archive.GetEntry(ThumbnailFilename);
            if (thumbEntry != null) saveFile.ThumbnailPNG = ReadEntryBytes(thumbEntry);
        }

        internal Snapshot LoadSnapshotInternal(SaveFile saveFile, string snapshotIdentifier)
        {
            string zipPath = GetSaveFilePath(saveFile.Identifier);
            if (!File.Exists(zipPath))
            {
                Debug.LogError($"Save file not found: {zipPath}");
                return null;
            }

            using var archive = ZipFile.OpenRead(zipPath);

            var dataEntry = archive.GetEntry(GetSnapshotDataEntry(snapshotIdentifier));
            if (dataEntry == null)
            {
                Debug.LogError($"Snapshot not found in archive: {snapshotIdentifier}");
                return null;
            }

            var snapshot = new Snapshot
            {
                Identifier   = snapshotIdentifier,
                SnapshotData = ReadEntry(dataEntry)
            };

            var pngEntry = archive.GetEntry(GetSnapshotPngEntry(snapshotIdentifier));
            if (pngEntry != null) snapshot.ScreenshotPNG = ReadEntryBytes(pngEntry);

            return snapshot;
        }

        internal void DeleteSnapshotInternal(SaveFile saveFile, string snapshotIdentifier)
        {
            string zipPath = GetSaveFilePath(saveFile.Identifier);
            if (File.Exists(zipPath))
            {
                using var archive = ZipFile.Open(zipPath, ZipArchiveMode.Update);
                archive.GetEntry(GetSnapshotDataEntry(snapshotIdentifier))?.Delete();
                archive.GetEntry(GetSnapshotPngEntry(snapshotIdentifier))?.Delete();
            }

            saveFile.Snapshots.RemoveAll(s => s.Identifier == snapshotIdentifier);
            saveFile.SnapshotCount = saveFile.Snapshots.Count;
            saveFile.LastModified  = DateTime.UtcNow;
            WriteSidecar(saveFile);
            _thumbnailCache.Remove(ThumbnailKey(saveFile.Identifier, snapshotIdentifier));
        }

        internal void DeleteInternal(SaveFile saveFile)
        {
            string zipPath     = GetSaveFilePath(saveFile.Identifier);
            string sidecarPath = GetSidecarPath(saveFile.Identifier);

            if (File.Exists(zipPath))     File.Delete(zipPath);
            if (File.Exists(sidecarPath)) File.Delete(sidecarPath);

            string prefix = saveFile.Identifier + "_";
            foreach (var key in _thumbnailCache.Keys.Where(k => k.StartsWith(prefix)).ToList())
                _thumbnailCache.Remove(key);
        }

        // ── Thumbnails ───────────────────────────────────────────────────

        /// <summary>Returns the save-level thumbnail, reading from zip and caching.</summary>
        public Texture2D GetSaveThumbnail(string saveIdentifier)
        {
            string key = $"{saveIdentifier}_savethumb";
            if (_thumbnailCache.TryGetValue(key, out Texture2D cached)) return cached;
            return ReadThumbnailEntry(saveIdentifier, ThumbnailFilename, key);
        }

        /// <summary>Returns a snapshot thumbnail, reading from zip and caching.</summary>
        public Texture2D GetSnapshotThumbnail(string saveIdentifier, string snapshotIdentifier)
        {
            string key = ThumbnailKey(saveIdentifier, snapshotIdentifier);
            if (_thumbnailCache.TryGetValue(key, out Texture2D cached)) return cached;
            return ReadThumbnailEntry(saveIdentifier, GetSnapshotPngEntry(snapshotIdentifier), key);
        }

        /// <summary>Clears the in-memory thumbnail cache.</summary>
        public void ClearThumbnailCache() => _thumbnailCache.Clear();

        // ── Private helpers ──────────────────────────────────────────────

        private Texture2D ReadThumbnailEntry(string saveIdentifier, string entryName, string cacheKey)
        {
            string zipPath = GetSaveFilePath(saveIdentifier);
            if (!File.Exists(zipPath)) return null;

            try
            {
                using var archive = ZipFile.OpenRead(zipPath);
                var entry = archive.GetEntry(entryName);
                if (entry == null) return null;

                var tex = new Texture2D(2, 2);
                tex.LoadImage(ReadEntryBytes(entry));
                _thumbnailCache[cacheKey] = tex;
                return tex;
            }
            catch (Exception e)
            {
                Debug.LogError($"Failed to load thumbnail '{entryName}': {e.Message}");
                return null;
            }
        }

        private void WriteSidecar(SaveFile saveFile)
        {
            Directory.CreateDirectory(RootSavesFolder);
            string json = JsonConvert.SerializeObject(saveFile, Formatting.Indented);
            File.WriteAllText(GetSidecarPath(saveFile.Identifier), json, Encoding.UTF8);
        }

        private static SaveFile ReadSidecar(string path) =>
            JsonConvert.DeserializeObject<SaveFile>(File.ReadAllText(path, Encoding.UTF8));

        private static void WriteEntry(ZipArchive archive, string entryName, string content)
        {
            archive.GetEntry(entryName)?.Delete();
            using var stream = archive.CreateEntry(entryName).Open();
            using var writer = new StreamWriter(stream, Encoding.UTF8);
            writer.Write(content);
        }

        private static void WriteEntryBytes(ZipArchive archive, string entryName, byte[] data)
        {
            archive.GetEntry(entryName)?.Delete();
            using var stream = archive.CreateEntry(entryName).Open();
            stream.Write(data, 0, data.Length);
        }

        private static string ReadEntry(ZipArchiveEntry entry)
        {
            using var stream = entry.Open();
            using var reader = new StreamReader(stream, Encoding.UTF8);
            return reader.ReadToEnd();
        }

        private static byte[] ReadEntryBytes(ZipArchiveEntry entry)
        {
            using var stream = entry.Open();
            using var ms     = new MemoryStream();
            stream.CopyTo(ms);
            return ms.ToArray();
        }

        private static string GenerateIdentifier(string prefix) =>
            $"{prefix}_{DateTime.UtcNow:yyyyMMdd_HHmmss_fff}";

        private static string ThumbnailKey(string saveId, string snapshotId) =>
            $"{saveId}_{snapshotId}_thumb";
    }
}

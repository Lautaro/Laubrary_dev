using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using UnityEngine;

namespace SaveGame
{
    /// <summary>
    /// Represents a save slot. Serves as the sidecar on disk and the in-memory handle
    /// for all save operations. Heavy data (WorldData, ThumbnailPNG) is null until
    /// LoadGlobalData() is called.
    /// </summary>
    [System.Serializable]
    public class SaveFile
    {
        // ── Identity ─────────────────────────────────────────────────────

        /// <summary>Generated unique key. Used for all disk addressing. Never changes.</summary>
        public string Identifier;

        /// <summary>User-facing display name. Free text, not unique, can change.</summary>
        public string Name;

        // ── Timestamps ───────────────────────────────────────────────────

        public DateTime CreatedAt;
        public DateTime LastModified;

        // ── Metadata ─────────────────────────────────────────────────────

        public int SnapshotCount;
        public Dictionary<string, string> MetadataProperties = new();
        public List<SnapshotMetadata> Snapshots = new();

        // ── Heavy data (not in sidecar — null until LoadGlobalData) ──────

        [JsonIgnore] public string WorldData;
        [JsonIgnore] public byte[] ThumbnailPNG;

        // ── Internals ────────────────────────────────────────────────────

        [JsonIgnore] private SaveManager _manager;

        [JsonIgnore]
        public SnapshotMetadata LatestSnapshot =>
            Snapshots?.OrderByDescending(s => s.Timestamp).FirstOrDefault();

        internal void SetManager(SaveManager manager) => _manager = manager;

        // ── Operations ───────────────────────────────────────────────────

        /// <summary>Adds a snapshot to this save slot and writes it to disk immediately.</summary>
        public SnapshotMetadata AddSnapshot(
            string snapshotData,
            SaveType saveType,
            string name = null,
            byte[] screenshotPNG = null,
            Dictionary<string, string> metadata = null)
            => _manager.AddSnapshotInternal(this, snapshotData, saveType, name, screenshotPNG, metadata);

        /// <summary>Loads WorldData and ThumbnailPNG from the zip into this object.</summary>
        public void LoadGlobalData() => _manager.LoadGlobalDataInternal(this);

        /// <summary>Loads a single snapshot's heavy data from the zip by identifier.</summary>
        public Snapshot LoadSnapshot(string snapshotIdentifier)
            => _manager.LoadSnapshotInternal(this, snapshotIdentifier);

        /// <summary>Deletes one snapshot entry from disk and updates the sidecar.</summary>
        public void DeleteSnapshot(string snapshotIdentifier)
            => _manager.DeleteSnapshotInternal(this, snapshotIdentifier);

        /// <summary>Deletes this entire save slot from disk.</summary>
        public void Delete() => _manager.DeleteInternal(this);

        /// <summary>Returns the save-level thumbnail, reading from zip and caching.</summary>
        public Texture2D GetThumbnail() => _manager.GetSaveThumbnail(Identifier);

        /// <summary>Returns a snapshot thumbnail, reading from zip and caching.</summary>
        public Texture2D GetSnapshotThumbnail(string snapshotIdentifier)
            => _manager.GetSnapshotThumbnail(Identifier, snapshotIdentifier);
    }
}


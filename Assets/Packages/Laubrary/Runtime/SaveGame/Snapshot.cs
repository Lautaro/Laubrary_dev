using System;
using System.Collections.Generic;

namespace SaveGame
{
    /// <summary>
    /// Snapshot metadata — lives inside SaveFile.Snapshots (sidecar).
    /// Fast-readable without touching the zip.
    /// </summary>
    [System.Serializable]
    public class SnapshotMetadata
    {
        /// <summary>Generated unique key. Used as zip entry name. Never changes.</summary>
        public string Identifier;

        /// <summary>User-facing display name. Free text, not unique, can change.</summary>
        public string Name;

        public DateTime Timestamp;
        public SaveType SaveType;
        public Dictionary<string, string> MetadataProperties = new();
    }

    /// <summary>
    /// Heavy snapshot data — loaded individually from the zip by identifier.
    /// Name and timestamps are on SnapshotMetadata; this carries only the blobs.
    /// </summary>
    [System.Serializable]
    public class Snapshot
    {
        /// <summary>Matches SnapshotMetadata.Identifier for look-up.</summary>
        public string Identifier;
        public string SnapshotData;
        public byte[] ScreenshotPNG;
    }

    public enum SaveType
    {
        Autosave,
        Quicksave,
        ManualSave
    }
}


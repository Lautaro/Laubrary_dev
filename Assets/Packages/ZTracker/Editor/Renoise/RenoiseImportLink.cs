using System;
using System.Collections.Generic;
using Laubrary.Audio;
using Laubrary.ZTracker.Renoise;
using UnityEngine;

namespace Laubrary.ZTracker.Editor
{
    public enum RenoiseMappingStatus { Unmapped, Approximate, Mapped }

    [Serializable] public sealed class RenoiseMapping
    {
        public RenoiseSourceEntry source;
        public RenoiseMappingStatus status;
        public bool dealtWith, removed, explicitTarget, bypass;
        public string detail = "", matchedBy = "", targetTrackId = "";
        public ZTrackerInstrument instrument;
        public UnityEngine.Object chainOwner;
        public string chainId = "";
        public AudioEffectChainData chainSnapshot;
        public int notes, rows, lowest = -1, highest = -1;
        public float songShare;
        public List<string> tracks = new List<string>();
        public List<RenoiseUse> uses = new List<RenoiseUse>();
    }

    [Serializable] public sealed class RenoiseUse
    {
        public int sequence, pattern, track, firstRow, lastRow;
        public string name;
    }

    [Serializable] public sealed class RenoisePatternStamp
    {
        public int index;
        public string fingerprint;
    }

    /// <summary>Editor-owned manifest. Runtime songs contain only normal tracker data and instrument references.</summary>
    public sealed class RenoiseImportLink : ScriptableObject
    {
        public int version = 1;
        public string sourcePath, sourceIdentity, sourceHash, archivePath, ownedFolder;
        public string sourceName, sourceKind, sourceUrl = "", licenseNote = "";
        public ZTrackerSong song;
        public List<ZTrackerInstrument> instruments = new List<ZTrackerInstrument>();
        public List<RenoiseMapping> mappings = new List<RenoiseMapping>();
        public List<RenoiseIssue> issues = new List<RenoiseIssue>();
        public List<RenoisePatternStamp> patterns = new List<RenoisePatternStamp>();
        public List<string> changes = new List<string>();
        public List<string> ownedAssets = new List<string>();
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Laubrary.Zoetrope.Editor
{
    /// <summary>
    /// The persisted half of the honesty surface (ZOE_PALETTE_BUILD_PLAN.md task 7). Subscribes to
    /// <see cref="ZoeReactionTelemetry.Requested"/> once, on load, and remembers what it hears across domain
    /// reloads and editor restarts by writing a small JSON file to the project's Library folder (not Assets —
    /// this is observed history, not authored content, and has no business in source control).
    ///
    /// Two things this makes possible, both project-wide:
    /// <list type="bullet">
    /// <item>Per-row usage (<see cref="GetUsage"/>) — "has anything ever actually asked for THIS row, on THIS
    /// character" — the row-level chip in <c>ZoeWindow</c>.</item>
    /// <item>The aggregated miss list (<see cref="AllMisses"/>) — every name that was requested but did not
    /// match a declared row at the time, across every character and every session this has been running —
    /// surfaced in <c>ZoePaletteHealthWindow</c>. <see cref="ReactionFxPlayer.WarnNoSuchState"/> already logs
    /// each of these once to the console; this is the same fact, collected in one place instead of scattered
    /// across console history.</item>
    /// </list>
    ///
    /// Deliberately NOT a static-analysis / project-wide text scan for who calls <c>Raise("Name")</c> in game
    /// code — Unity ships no such API cheaply, and a grep-based approximation would drift from what "requested"
    /// actually means the moment someone builds the id string at runtime. This only ever knows what has
    /// genuinely been asked for, live — through Play mode, or through a Preview click in the editor (both go
    /// through the exact same <see cref="ReactionFxPlayer"/> path, so a Preview click IS a real request).
    /// </summary>
    [InitializeOnLoad]
    public static class ZoePaletteUsageLog
    {
        [Serializable]
        class Record
        {
            public string zoeGuid = "";
            public string zoeName = "";
            public string id = "";
            public int hitCount;
            public int missCount;
            public string lastHitUtc = "";
            public string lastMissUtc = "";
        }

        [Serializable]
        class Store { public List<Record> records = new List<Record>(); }

        static readonly string FilePath =
            System.IO.Path.Combine(UnityEngine.Application.dataPath, "..", "Library", "ZoePaletteUsage.json");

        static Store _store;
        // Keyed by "guidid" (case-insensitive on id — same comparison rule Zoe.EventNamed uses).
        static readonly Dictionary<string, Record> _byKey = new Dictionary<string, Record>();

        static ZoePaletteUsageLog()
        {
            ZoeReactionTelemetry.Requested += OnRequested;
        }

        static void EnsureLoaded()
        {
            if (_store != null) return;
            _store = new Store();
            try
            {
                if (File.Exists(FilePath))
                {
                    string json = File.ReadAllText(FilePath);
                    var loaded = JsonUtility.FromJson<Store>(json);
                    if (loaded?.records != null) _store.records = loaded.records;
                }
            }
            catch (Exception e) { Debug.LogWarning($"[ZoePaletteUsageLog] Could not read usage cache: {e.Message}"); }

            _byKey.Clear();
            foreach (var r in _store.records)
                if (r != null) _byKey[Key(r.zoeGuid, r.id)] = r;
        }

        static void Save()
        {
            try
            {
                string json = JsonUtility.ToJson(_store);
                File.WriteAllText(FilePath, json);
            }
            catch (Exception e) { Debug.LogWarning($"[ZoePaletteUsageLog] Could not write usage cache: {e.Message}"); }
        }

        static string Key(string guid, string id) => (guid ?? "") + "" + (id ?? "").ToLowerInvariant();

        static void OnRequested(Zoe def, string id, bool matched)
        {
            if (string.IsNullOrEmpty(id)) return;
            EnsureLoaded();

            string guid = "";
            string zoeName = "(unknown character)";
            if (def != null)
            {
                zoeName = def.name;
                string path = AssetDatabase.GetAssetPath(def);
                if (!string.IsNullOrEmpty(path)) guid = AssetDatabase.AssetPathToGUID(path);
            }

            string key = Key(guid, id);
            if (!_byKey.TryGetValue(key, out var rec))
            {
                rec = new Record { zoeGuid = guid, zoeName = zoeName, id = id };
                _byKey[key] = rec;
                _store.records.Add(rec);
            }
            rec.zoeName = zoeName;   // keep the display name current across renames

            string now = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm");
            if (matched) { rec.hitCount++; rec.lastHitUtc = now; }
            else { rec.missCount++; rec.lastMissUtc = now; }

            Save();
        }

        /// The usage record for one row on one character, or null if nothing has ever asked for it. Used by the
        /// row chip: <c>hitCount &gt; 0</c> → "requested"; a record that exists with only misses (declared now,
        /// but every past ask predates the row, or a typo elsewhere never matched it) still reads as unused for
        /// THIS row, since a miss by definition never resolved to it.
        public static (int hitCount, string lastHit, int missCount, string lastMiss) GetUsage(Zoe zoe, string id)
        {
            EnsureLoaded();
            if (zoe == null || string.IsNullOrEmpty(id)) return (0, "", 0, "");
            string path = AssetDatabase.GetAssetPath(zoe);
            string guid = string.IsNullOrEmpty(path) ? "" : AssetDatabase.AssetPathToGUID(path);
            return _byKey.TryGetValue(Key(guid, id), out var rec)
                ? (rec.hitCount, rec.lastHitUtc, rec.missCount, rec.lastMissUtc)
                : (0, "", 0, "");
        }

        /// Every (character, name) pair that has been requested and did NOT match a declared row, project-wide,
        /// across every session this log has observed — the aggregated view of what
        /// <see cref="ReactionFxPlayer.WarnNoSuchState"/> already warns about one line at a time.
        public static IReadOnlyList<(string zoeName, string id, int count, string lastSeen)> AllMisses()
        {
            EnsureLoaded();
            var list = new List<(string, string, int, string)>();
            foreach (var r in _store.records)
                if (r != null && r.missCount > 0)
                    list.Add((r.zoeName, r.id, r.missCount, r.lastMissUtc));
            return list;
        }

        /// Clears the whole cache — for a stale entry left behind by a deleted/renamed character, or just to
        /// start the honesty surface fresh. Not undoable; it's observed history, not authored data.
        public static void ClearAll()
        {
            _store = new Store();
            _byKey.Clear();
            Save();
        }
    }
}

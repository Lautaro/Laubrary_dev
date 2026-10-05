using System;
using System.Collections.Generic;
using Laubrary.ZTracker.Model;
using UnityEditor;
using UnityEngine;

namespace Laubrary.ZTracker.Editor
{
    // One transaction covers explicit migration, projected legacy edits and the
    // authoritative result. Complete snapshots also restore dense cropped data.
    public sealed class ZTrackerLegacyEdit : IDisposable
    {
        readonly UnityEngine.Object owner;
        readonly string original;
        readonly SongData songBefore;
        readonly InstrumentData instrumentBefore;
        readonly List<ZTrackerPattern> patternObjects;
        readonly List<ZTrackerChannelConfig> trackObjects;
        readonly string operation;
        bool committed;
        public ZTrackerLegacyEdit(UnityEngine.Object asset, string label)
        {
            owner = asset;
            operation = label;
            string error;
            bool allowed = asset is ZTrackerSong song ? ZTrackerLegacyCompatibility.CanEdit(song,out error) : asset is ZTrackerInstrument instrument ? ZTrackerLegacyCompatibility.CanEdit(instrument,out error) : throw new ArgumentException("Not a tracker asset");
            if (!allowed) throw new InvalidOperationException(error);
            original = JsonUtility.ToJson(asset);
            Undo.RegisterCompleteObjectUndo(asset,"Tracker: " + label);
            if (asset is ZTrackerSong s)
            {
                if (!ZTrackerMigration.Upgrade(s,out error)) throw new InvalidOperationException(error);
                songBefore = ZTrackerMigration.Copy(s.model);
                patternObjects = new List<ZTrackerPattern>(s.patterns); trackObjects = new List<ZTrackerChannelConfig>(s.channels);
            }
            else if (asset is ZTrackerInstrument i)
            {
                if (!ZTrackerMigration.Upgrade(i,out error)) throw new InvalidOperationException(error);
                instrumentBefore = ZTrackerMigration.Copy(i.model);
            }
        }
        public void Commit()
        {
            if (owner is ZTrackerSong song)
            {
                // Existing objects carry their identity through deletion/reordering. Clones
                // and additions get a fresh identity, not the identity of their old ordinal.
                var ids = ZTrackerMigration.Copy(songBefore);
                ids.tracks.Clear(); ids.patterns.Clear();
                for (int t = 0; t < song.channelCount; t++) { int old = trackObjects.IndexOf(song.channels[t]); ids.tracks.Add(new TrackData { id = old >= 0 ? songBefore.tracks[old].id : ZTrackerMigration.Identity() }); }
                ids.tracks.Add(songBefore.tracks.Find(t => t.kind == TrackKind.Master));
                foreach (var p in song.patterns) { int old = patternObjects.IndexOf(p); ids.patterns.Add(new PatternData { id = old >= 0 ? songBefore.patterns[old].id : ZTrackerMigration.Identity() }); }
                ids.sequence.Clear(); var used = new HashSet<string>();
                for (int s = 0; s < song.orderList.Count; s++)
                {
                    string pid = ids.patterns[song.orderList[s]].id;
                    var previous = operation == "assign pattern" && s < songBefore.sequence.Count ? songBefore.sequence[s] : songBefore.sequence.Find(slot => slot.patternId == pid && !used.Contains(slot.id));
                    string id = previous?.id ?? ZTrackerMigration.Identity(); used.Add(id); ids.sequence.Add(new SequenceSlot { id = id });
                }
                ZTrackerLegacyCompatibility.Reconcile(song,ids);
            }
            else if (owner is ZTrackerInstrument i) ZTrackerLegacyCompatibility.Reconcile(i,instrumentBefore);
            committed = true; EditorUtility.SetDirty(owner);
        }
        public void Dispose()
        {
            if (!committed && owner != null) JsonUtility.FromJsonOverwrite(original,owner);
        }
    }
}

using System;
using System.Collections.Generic;
using Laubrary.ZTracker.Model;
using UnityEngine;

namespace Laubrary.ZTracker.Engine
{
    public sealed unsafe partial class TrackerPreparedSong
    {
        sealed class DrumLink
        {
            public int kit, source;
            public DrumMapping mapping;
        }
        readonly List<DrumLink> drumLinks = new List<DrumLink>();

        // Expand to ordinary source instruments only while preparing. Authoring slots and assets stay intact.
        SongData ExpandDrumKits(SongData authored, List<ZTrackerInstrument> temporary)
        {
            if (!authored.instruments.Exists(i => i != null && i.model.family == InstrumentFamily.DrumKit)) return authored;
            var song = ZTrackerMigration.Copy(authored);
            for (int i = 0; i < authored.instruments.Count; i++)
            {
                var asset = authored.instruments[i];
                if (asset == null || asset.model.family != InstrumentFamily.DrumKit) continue;
                var kit = ZTrackerMigration.Copy(asset.model);
                kit.family = InstrumentFamily.Sampler;
                kit.sampler = new SamplerData { nna = NewNoteAction.Continue };
                kit.drumKit = new DrumKitData();
                kit.parameters.presets.Clear();
                song.instruments[i] = DrumWrapper(kit, asset, temporary);
                for (int m = 0; m < asset.model.drumKit.mappings.Count; m++)
                {
                    var mapping = asset.model.drumKit.mappings[m];
                    if (mapping.pcm == null && mapping.synth == null) continue;
                    InstrumentData data;
                    if (mapping.synth != null)
                    {
                        data = ZTrackerMigration.Copy(mapping.synth.model);
                        string error = ZTrackerModelValidation.Validate(data);
                        if (error != null) throw new ArgumentException("Drum synth: " + error);
                        data.parameters.volume *= mapping.volume;
                        data.parameters.glideEnabled = false;
                    }
                    else
                    {
                        data = ZTrackerMigration.Convert(new InstrumentParameters { type = InstrumentType.Sample }, mapping.name);
                        data.provenance = "drum-source";
                        data.modulation.Clear();
                        data.sampler = new SamplerData { nna = NewNoteAction.Continue };
                        data.sampler.samples.Add(new SampleData { id = "hit", name = mapping.name, pcm = mapping.pcm, volume = mapping.volume, oneShot = true, nna = NewNoteAction.Continue });
                        data.sampler.zones.Add(new Keyzone { sample = 0, baseNote = mapping.baseNote, keyTracking = mapping.keyTracking });
                    }
                    string oldId = data.id;
                    data.id = kit.id + "/drum/" + m;
                    foreach (var macro in data.macros) foreach (var route in macro.mappings)
                        if (route.target.instrumentId == oldId) route.target.instrumentId = data.id;
                    data.parameters.presets.Clear();
                    int source = song.instruments.Count;
                    song.instruments.Add(DrumWrapper(data, mapping.synth != null ? mapping.synth : asset, temporary));
                    drumLinks.Add(new DrumLink { kit = i, source = source, mapping = mapping });
                }
            }
            if (song.instruments.Count > 4096) throw new ArgumentException("Drum sources exceed instrument preparation capacity");
            return song;
        }
        static ZTrackerInstrument DrumWrapper(InstrumentData data, ZTrackerInstrument source, List<ZTrackerInstrument> temporary)
        {
            var asset = ScriptableObject.CreateInstance<ZTrackerInstrument>();
            temporary.Add(asset);
            asset.hideFlags = HideFlags.HideAndDontSave;
            asset.schemaVersion = 1;
            asset.model = data;
            asset.playbackSourceIdentity = source.playbackSourceIdentity != 0 ? source.playbackSourceIdentity : source.GetInstanceID();
            return asset;
        }
        void LinkDrumZones(List<TrackerSample> samples, List<TrackerZone> zones, List<TrackerInstrument> instruments)
        {
            int currentKit = -1;
            foreach (var link in drumLinks)
            {
                var kit = instruments[link.kit];
                if (currentKit != link.kit) { kit.zoneStart = zones.Count; kit.zoneCount = 0; currentKit = link.kit; }
                var source = instruments[link.source];
                for (int z = 0; z < source.zoneCount; z++)
                {
                    var zone = zones[source.zoneStart + z];
                    zone.minNote = link.mapping.noteMin; zone.maxNote = link.mapping.noteMax;
                    zone.baseNote = link.mapping.baseNote;
                    zone.tracking = link.mapping.synth == null && link.mapping.keyTracking;
                    var sample = samples[zone.sample];
                    sample.nna = NewNoteAction.Continue;
                    sample.oneShot = true;
                    sample.drumGateSeconds = link.mapping.synth != null ? link.mapping.gateSeconds : 0;
                    samples[zone.sample] = sample;
                    zones.Add(zone); kit.zoneCount++;
                }
                kit.nna = NewNoteAction.Continue;
                instruments[link.kit] = kit;
            }
        }
    }
}

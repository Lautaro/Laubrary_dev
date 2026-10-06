using System;
using System.Collections.Generic;
using Laubrary.ZTracker.Model;
using UnityEngine;

namespace Laubrary.ZTracker.Engine
{
    public sealed unsafe partial class TrackerPreparedSong
    {
        readonly Dictionary<string, int> parameterSetIds = new Dictionary<string, int>();
        readonly List<int> presetSliceMarkers = new List<int>();
        void BuildParameterSets(SongData song, int rate, int maxFrames, List<TrackerSample> samples, List<TrackerZone> zones, List<TrackerInstrument> instruments, List<float> pcm, List<TrackerPcm> clips, List<TrackerMod> mods, List<TrackerModPoint> points, ref int stride)
        {
            var sets = new List<TrackerParameterSet>();
            var tones = new List<TrackerTone>();
            var envs = new List<TrackerMod>();
            var parameters = new List<float>();
            var ambiguousSetIds = new HashSet<string>();
            for (int i = 0; i < samples.Count; i++)
            {
                var tone = state.tones[i];
                tone.parameterSet = -1;
                tones.Add(tone);
            }

            for (int i = 0; i < state.toneEnvelopes.Length; i++)
                envs.Add(state.toneEnvelopes[i]);
            for (int ii = 0; ii < song.instruments.Count; ii++)
                if (song.instruments[ii] != null)
                {
                    var authored = song.instruments[ii].model;
                    var resolved = ZTrackerMigration.ResolveParameterSets(authored);
                    for (int selector = 0; selector < resolved.Count; selector++)
                    {
                        var source = resolved[selector];
                        if (parameterSetIds.ContainsKey(source.id) || ambiguousSetIds.Contains(source.id))
                        {
                            diagnostics.Add("PARAMETER_SET_ID_AMBIGUOUS " + source.id);
                            parameterSetIds.Remove(source.id);
                            ambiguousSetIds.Add(source.id);
                            continue;
                        }

                        if (sets.Count >= 65536)
                            throw new ArgumentException("Parameter set capacity exceeded");
                        int setIndex = sets.Count;
                        parameterSetIds.Add(source.id, setIndex);
                        if (selector == 0)
                        {
                            sets.Add(new TrackerParameterSet { instrument = ii, zones = instruments[ii].zoneStart, zoneCount = instruments[ii].zoneCount });
                            for (int p = 0; p < TrackerParameters.Stride; p++)
                                parameters.Add(state.parameterBase[ii * TrackerParameters.Stride + p]);
                            continue;
                        }

                        // A preparation-only instrument object compiles a parameter set, never a canonical slot.
                        var asset = ScriptableObject.CreateInstance<ZTrackerInstrument>();
                        asset.schemaVersion = 1;
                        asset.model = source.data;
                        asset.model.parameters.presets.Clear();
                        try
                        {
                            var temporary = new SongData
                            {
                                id = "preset-compile",
                                voiceCapacity = 1,
                                beatTicks = false
                            };
                            temporary.instruments.Add(asset);
                            temporary.tracks.Add(new TrackData { id = "notes" });
                            temporary.tracks.Add(new TrackData { id = "master", kind = TrackKind.Master, visibleNoteColumns = 0 });
                            // Explicit sends retain destinations needed by a preset's authored effect sends.
                            foreach (var tr in song.tracks)
                                if (tr.kind == TrackKind.Send)
                                {
                                    var copy = ZTrackerMigration.Copy(tr);
                                    copy.outputTrackId = "master";
                                    copy.parentGroupId = "";
                                    copy.sends.Clear();
                                    temporary.tracks.Add(copy);
                                }

                            var pattern = new PatternData
                            {
                                id = "pattern",
                                lineCount = 1
                            };
                            foreach (var tr in temporary.tracks)
                                pattern.tracks.Add(new PatternTrack { trackId = tr.id });
                            temporary.patterns.Add(pattern);
                            temporary.sequence.Add(new SequenceSlot { id = "slot", patternId = "pattern" });
                            using (var prepared = Prepare(temporary, rate, maxFrames))
                            {
                                var child = prepared.state;
                                int sampleStart = samples.Count, pointStart = points.Count, modStart = mods.Count, envStart = envs.Count;
                                for (int p = 0; p < TrackerParameters.Stride; p++)
                                    parameters.Add(child.parameterBase[p]);
                                var clipMap = new int[child.pcmCount];
                                for (int c = 0; c < child.pcmCount; c++)
                                {
                                    var cp = child.clips[c];
                                    int match = -1;
                                    for (int x = 0; x < clips.Count; x++)
                                    {
                                        var existing = clips[x];
                                        if (existing.frames != cp.frames || existing.channels != cp.channels || existing.frequency != cp.frequency)
                                            continue;
                                        bool equal = true;
                                        for (int p = 0; p < cp.frames * cp.channels; p++)
                                            if (pcm[existing.offset + p] != child.pcm[cp.offset + p])
                                            {
                                                equal = false;
                                                break;
                                            }

                                        if (equal)
                                        {
                                            match = x;
                                            break;
                                        }
                                    }

                                    if (match < 0)
                                    {
                                        match = clips.Count;
                                        var imported = cp;
                                        if ((long)pcm.Count + (long)cp.frames * cp.channels > 67108864)
                                            throw new ArgumentException("PCM pool exceeds 256 MiB including presets");
                                        imported.offset = pcm.Count;
                                        for (int p = 0; p < cp.frames * cp.channels; p++)
                                            pcm.Add(child.pcm[cp.offset + p]);
                                        clips.Add(imported);
                                    }

                                    clipMap[c] = match;
                                }

                                for (int p = 0; p < child.points.Length; p++)
                                    points.Add(child.points[p]);
                                for (int m = 0; m < child.mods.Length; m++)
                                {
                                    var mod = child.mods[m];
                                    mod.points += pointStart;
                                    mods.Add(mod);
                                }

                                for (int e = 0; e < child.toneEnvelopes.Length; e++)
                                {
                                    var env = child.toneEnvelopes[e];
                                    env.points += pointStart;
                                    envs.Add(env);
                                }

                                for (int s = 0; s < child.sampleCount; s++)
                                {
                                    var sm = child.samples[s];
                                    int markerStart = presetSliceMarkers.Count;
                                    for (int marker = 0; marker < sm.sliceCount; marker++)
                                        presetSliceMarkers.Add(child.sliceMarkers[sm.slices + marker]);
                                    sm.slices = markerStart;
                                    sm.instrument = ii;
                                    sm.pcm = sm.pcm >= 0 ? clipMap[sm.pcm] : -1;
                                    sm.modStart += modStart;
                                    sm.delayDestination = sm.delayDestination < 0 ? -1 : song.tracks.FindIndex(t => t.kind == TrackKind.Send && t.devices.nodes.Exists(n => n.type == Laubrary.Zounds.ZoundEffectType.Delay));
                                    sm.reverbDestination = sm.reverbDestination < 0 ? -1 : song.tracks.FindIndex(t => t.kind == TrackKind.Send && t.devices.nodes.Exists(n => n.type == Laubrary.Zounds.ZoundEffectType.Reverb));
                                    samples.Add(sm);
                                    var tone = child.tones[s];
                                    tone.parameterSet = setIndex;
                                    tone.pcmB = tone.pcmB < 0 ? -1 : clipMap[tone.pcmB];
                                    tone.envelopes += envStart;
                                    tone.arpPoints += pointStart;
                                    tone.arpNotes += pointStart;
                                    tones.Add(tone);
                                }

                                var ins = child.instruments[0];
                                int start = zones.Count;
                                for (int z = 0; z < ins.zoneCount; z++)
                                {
                                    var zone = child.zones[ins.zoneStart + z];
                                    zone.sample += sampleStart;
                                    zones.Add(zone);
                                }

                                sets.Add(new TrackerParameterSet { instrument = ii, zones = start, zoneCount = ins.zoneCount });
                                stride = Math.Max(stride, child.modStride);
                            }
                        }
                        finally
                        {
                            UnityEngine.Object.DestroyImmediate(asset);
                        }
                    }
                }

            Free(ref state.tones);
            Free(ref state.toneEnvelopes);
            state.tones = Native(tones);
            state.toneEnvelopes = Native(envs);
            state.parameterSets = Native(sets);
            state.presetParameters = Native(parameters);
        }
    }
}

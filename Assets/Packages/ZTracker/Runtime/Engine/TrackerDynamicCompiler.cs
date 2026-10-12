using System.Collections.Generic;
using Laubrary.ZTracker.Model;
using Unity.Mathematics;

namespace Laubrary.ZTracker.Engine
{
    /// <summary>
    /// Compiles the song's command definitions (dynamic parameters) into fixed-size records the renderer drives. One
    /// record per valid definition; a command or an automation lane then names the record by index. A definition that
    /// cannot be resolved is reported and skipped; neighbouring definitions still compile. See
    /// Documentation~/COMMAND_MODEL_DESIGN.md section 7.
    /// </summary>
    public sealed unsafe partial class TrackerPreparedSong
    {
        // Upper-case two-character id → record index, and stable uid → record index.
        readonly Dictionary<string, int> dynamicIds = new Dictionary<string, int>();
        readonly Dictionary<string, int> dynamicUids = new Dictionary<string, int>();
        // The song has used the command model, so an unknown id in a track FX column is worth naming.
        bool commandModelPresent;

        void BuildDynamicParameters(SongData song, List<TrackerChain> chains, List<TrackerDeviceParameter> parameters, Dictionary<string, int> instrumentIds)
        {
            var records = new List<int>();
            commandModelPresent = song.commandModelVersion > 0 || song.dynamicParameters != null && song.dynamicParameters.Count > 0;
            if (song.dynamicParameters != null)
                foreach (var definition in song.dynamicParameters)
                {
                    if (definition?.target == null) { diagnostics.Add("DYNAMIC_PARAMETER_MALFORMED"); continue; }
                    string id = ZTrackerCommandRegistry.Normalize(definition.id);
                    string where = " id=" + id + " uid=" + definition.uid;
                    if (id.Length != 2 || ZTrackerCommandRegistry.IsReserved(id) || Base36(id[0]) < 0 || Base36(id[1]) < 0) { diagnostics.Add("DYNAMIC_ID_INVALID" + where); continue; }
                    if (dynamicIds.ContainsKey(id)) { diagnostics.Add("DYNAMIC_ID_DUPLICATE" + where); continue; }
                    if (string.IsNullOrEmpty(definition.uid) || dynamicUids.ContainsKey(definition.uid)) { diagnostics.Add("DYNAMIC_UID_INVALID" + where); continue; }
                    if (!math.isfinite(definition.min) || !math.isfinite(definition.max)) { diagnostics.Add("DYNAMIC_RANGE_UNSUPPORTED" + where); continue; }
                    var p = new TrackerDeviceParameter { device = -1, track = -1, instrument = -1, chain = -1, node = -1, route = -1, min = definition.min, max = definition.max, dynamic = true, smooth = true, written = true };
                    var t = definition.target;
                    bool valid = false;
                    switch (definition.scope)
                    {
                        case DynamicScope.Song:
                            if (t.parameter == "bpm")
                            {
                                // Tempo applies at row entry and is not ramped; its whole range must be a playable tempo.
                                valid = math.min(p.min, p.max) >= 32 && math.max(p.min, p.max) <= 999;
                                p.kind = 5; p.parameter = 0; p.smooth = false; p.destinationAuthored = (float)song.bpm;
                            }
                            break;
                        case DynamicScope.Track:
                            {
                                int ti = song.tracks.FindIndex(x => x != null && x.id == t.trackId);
                                int which = System.Array.IndexOf(ZTrackerCommandRegistry.TrackParameters, t.parameter);
                                if (ti < 0 || which < 0) break;
                                // Order of TrackParameters: preVolume, prePan, width, postVolume, postPan → engine 0, 1, 4, 2, 3.
                                p.kind = 4; p.track = ti; p.parameter = which == 0 ? 0 : which == 1 ? 1 : which == 2 ? 4 : which == 3 ? 2 : 3;
                                var tr = state.tracks[ti];
                                p.destinationAuthored = p.parameter == 0 ? tr.authoredPreGain : p.parameter == 1 ? tr.authoredPrePan : p.parameter == 4 ? tr.authoredWidth : p.parameter == 2 ? tr.authoredPostGain : tr.authoredPostPan;
                                valid = true;
                                break;
                            }
                        case DynamicScope.Effect:
                            {
                                int ti = song.tracks.FindIndex(x => x != null && x.id == t.trackId);
                                if (ti < 0) break;
                                int node = song.tracks[ti].devices.nodes.FindIndex(n => n != null && n.uid == t.deviceId);
                                var tr = state.tracks[ti];
                                for (int ci = 0; ci < tr.chainCount && !valid; ci++)
                                {
                                    int chainIndex = tr.chainStart + ci, begin = ci == 0 ? 0 : chains[chainIndex - 1].position, end = chains[chainIndex].position;
                                    if (node < begin || node >= end || t.index < 0 || t.index >= chains[chainIndex].layout.paramCountOf[node - begin]) continue;
                                    int local = node - begin, flat = chains[chainIndex].layout.paramOffset[local] + t.index;
                                    var type = chains[chainIndex].layout.nodeType[local];
                                    // A delay's longest time sizes its memory; it cannot change while playing.
                                    if (type == Laubrary.Zounds.ZoundEffectType.Delay && t.index == 3) { diagnostics.Add("MEMORY_SIZING_TARGET" + where); break; }
                                    p.kind = 3; p.chain = chainIndex; p.node = local; p.parameter = t.index;
                                    p.destinationAuthored = chains[chainIndex].layout.pBase[flat];
                                    var layout = chains[chainIndex].layout;
                                    if (layout.bindCount > 0) for (int r = 0; r < layout.rampedCount; r++) if (layout.ramped[r] == flat) p.ramped = true;
                                    valid = true;
                                }
                                break;
                            }
                        case DynamicScope.Instrument:
                            {
                                if (!instrumentIds.TryGetValue(t.instrumentId ?? "", out int ii)) break;
                                if (t.kind == ParameterKind.InstrumentMacro)
                                {
                                    if (t.index < 0 || t.index >= 8) break;
                                    p.kind = 1; p.instrument = ii; p.parameter = t.index;
                                    p.destinationAuthored = state.macros[ii * 8 + t.index].authored;
                                    valid = math.min(p.min, p.max) >= 0 && math.max(p.min, p.max) <= 1;
                                    break;
                                }
                                var model = song.instruments[ii]?.model;
                                if (!TrackerParameters.Resolve(t.parameter, out var parameter) || !Applicable(model, parameter, model != null && model.family == InstrumentFamily.Sampler ? ParameterKind.Sample : ParameterKind.Synth, false)) break;
                                p.kind = 2; p.instrument = ii; p.parameter = (int)parameter;
                                p.destinationAuthored = state.parameterBase[ii * TrackerParameters.Stride + (int)parameter];
                                valid = true;
                                break;
                            }
                    }
                    if (!valid) { diagnostics.Add("DYNAMIC_TARGET_UNRESOLVED" + where + " scope=" + definition.scope); continue; }
                    // Normalized authored position; a lane or command at exactly this value restores the authored value.
                    p.authored = p.max != p.min ? math.saturate((float)(((double)p.destinationAuthored - p.min) / ((double)p.max - p.min))) : 0;
                    p.latent = p.emitted = p.authored;
                    p.destination = p.target = p.destinationAuthored;
                    int index = parameters.Count;
                    parameters.Add(p);
                    records.Add(index);
                    dynamicIds.Add(id, index);
                    dynamicUids.Add(definition.uid, index);
                }
            state.dynamicParameters = Native(records);
            state.dynamicCount = records.Count;
        }
    }
}

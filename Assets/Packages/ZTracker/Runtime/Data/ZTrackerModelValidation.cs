using System;
using System.Collections.Generic;
using Laubrary.Audio;

namespace Laubrary.ZTracker.Model
{
    public static class ZTrackerModelValidation
    {
        static bool Finite(double n) => !double.IsNaN(n) && !double.IsInfinity(n);
        static string Unique<T>(List<T> list, Func<T,string> id, string label) where T : class
        {
            if (list == null) return label + " collection missing";
            var seen = new HashSet<string>();
            foreach (var value in list) if (value == null || string.IsNullOrEmpty(id(value)) || !seen.Add(id(value))) return label + " has null, empty or duplicate identity";
            return null;
        }
        public static string Validate(SongData song)
        {
            if (song == null) return "Song model missing";
            if (!Finite(song.bpm) || song.bpm <= 0 || song.linesPerBeat < 1 || song.ticksPerLine < 1 || song.voiceCapacity < 1 || song.beatIntervalLines < 1) return "Invalid clock or voice capacity";
            string error = Unique(song.tracks, t => t.id, "Tracks") ?? Unique(song.patterns, p => p.id, "Patterns") ?? Unique(song.sequence, s => s.id, "Sequence");
            if (error != null) return error;
            if (song.instruments == null || song.tracks.Count == 0 || song.patterns.Count == 0 || song.sequence.Count == 0) return "Song needs tracks, patterns, sequence and instrument slots";
            var tracks = new Dictionary<string,TrackData>(); int masters = 0, sequencers = 0;
            foreach (var t in song.tracks)
            {
                tracks.Add(t.id,t); if (t.kind == TrackKind.Master) masters++; if (t.kind == TrackKind.Sequencer) sequencers++;
                if (!Enum.IsDefined(typeof(TrackKind), t.kind)) return "Unknown track kind";
                int min = t.kind == TrackKind.Sequencer ? 1 : 0;
                if (t.visibleNoteColumns < min || t.visibleNoteColumns > 12 || (t.kind != TrackKind.Sequencer && t.visibleNoteColumns != 0) || t.visibleEffectColumns < 0 || t.visibleEffectColumns > 8) return "Track column capacity " + t.id;
                if (t.columns == null || !Finite(t.preVolume) || !Finite(t.prePan) || !Finite(t.preWidth) || !Finite(t.postVolume) || !Finite(t.postPan) || t.beatIntervalLines < 1) return "Track mixer/visibility data " + t.id;
                error = ValidateChain(t.devices); if (error != null) return t.id + ": " + error;
            }
            if (masters != 1 || sequencers > 64) return "Require one master and at most 64 sequencer tracks";
            foreach (var t in song.tracks)
            {
                if (t.parentGroupId != "" && (!tracks.TryGetValue(t.parentGroupId,out var parent) || parent.kind != TrackKind.Group)) return "Invalid group parent " + t.id;
                if (t.outputTrackId != "" && (!tracks.TryGetValue(t.outputTrackId,out var dest) || (dest.kind != TrackKind.Group && dest.kind != TrackKind.Send && dest.kind != TrackKind.Master))) return "Invalid output destination " + t.id;
                if (t.kind == TrackKind.Master && (t.outputTrackId != "" || t.parentGroupId != "")) return "Master cannot route outward";
                if (t.sends == null) return "Send collection missing";
                foreach (var send in t.sends) if (send == null || !tracks.TryGetValue(send.trackId,out var target) || target.kind != TrackKind.Send || !Finite(send.gain) || send.devicePosition < 0 || send.devicePosition > t.devices.nodes.Count) return "Invalid send destination/tap " + t.id;
            }
            foreach (var t in song.tracks)
            {
                error = Cycle(t.id, tracks, new HashSet<string>(), new HashSet<string>(), false); if (error != null) return error;
                error = Cycle(t.id, tracks, new HashSet<string>(), new HashSet<string>(), true); if (error != null) return error;
            }
            var patterns = new HashSet<string>();
            foreach (var p in song.patterns)
            {
                patterns.Add(p.id);
                if (p.lineCount < 1 || p.lineCount > 512) return "Pattern line capacity " + p.id;
                var owners = new HashSet<string>();
                if (p.tracks == null || p.tracks.Count != song.tracks.Count) return "Pattern needs one record per track " + p.id;
                foreach (var pt in p.tracks)
                {
                    if (pt == null || !tracks.ContainsKey(pt.trackId) || !owners.Add(pt.trackId) || pt.lines == null || pt.automation == null) return "Invalid pattern track " + p.id;
                    int last = -1;
                    foreach (var line in pt.lines)
                    {
                        if (line == null || line.line <= last || line.line >= p.lineCount || line.notes == null || line.effects == null || line.events == null || !line.HasPayload) return "Sparse line order/range/empty " + p.id;
                        last = line.line; var columns = new HashSet<int>();
                        foreach (var n in line.notes)
                        {
                            if (n == null || n.column < 0 || (!n.inactive && n.column >= 12) || !columns.Add(n.column) || n.volume == null || n.pan == null || n.sampleFx == null) return "Note column capacity/duplicate";
                            if (tracks[pt.trackId].kind != TrackKind.Sequencer) return "Bus/Event track contains notes";
                            if (n.note == NoteKind.Note && (n.pitch < 0 || n.pitch > 119)) return "Note outside profile";
                            if (n.delayPresent && (n.delay < 0 || n.delay > 255)) return "Delay outside profile";
                            error = Value(n.volume) ?? Value(n.pan) ?? Command(n.sampleFx); if (error != null) return error;
                        }
                        columns.Clear();
                        foreach (var fx in line.effects) { if (fx == null || fx.column < 0 || (!fx.inactive && fx.column >= 8) || !columns.Add(fx.column)) return "Effect column capacity/duplicate"; error = Command(fx.command); if (error != null) return error; }
                        columns.Clear(); foreach (var ev in line.events) if (ev == null || ev.column < 0 || !columns.Add(ev.column)) return "Event column duplicate";
                    }
                    foreach (var lane in pt.automation)
                    {
                        if (lane == null || lane.points == null || lane.target == null || !Enum.IsDefined(typeof(AutomationInterpolation),lane.interpolation)) return "Automation data missing";
                        error = Target(lane.target, song, pt.trackId); if (error != null) return error;
                        double time = -1;
                        foreach (var point in lane.points) { if (point == null || !Finite(point.line) || !Finite(point.value) || point.line <= time || point.line < 0 || point.line > p.lineCount) return "Automation points must be finite, ordered and unique"; time = point.line; }
                    }
                }
            }
            foreach (var slot in song.sequence) { if (!patterns.Contains(slot.patternId) || slot.mutedTrackIds == null) return "Invalid sequence pattern"; var mutes = new HashSet<string>(); foreach (var id in slot.mutedTrackIds) if (!tracks.ContainsKey(id) || !mutes.Add(id)) return "Invalid sequence mute"; }
            return null;
        }
        static string Cycle(string id, Dictionary<string,TrackData> tracks, HashSet<string> active, HashSet<string> done, bool parentOnly)
        {
            if (done.Contains(id)) return null;
            if (!active.Add(id)) return parentOnly ? "Group ancestry cycle" : "Routing/send cycle";
            var t = tracks[id]; string next = parentOnly ? t.parentGroupId : t.outputTrackId != "" ? t.outputTrackId : t.parentGroupId;
            if (!parentOnly && next == "" && t.kind != TrackKind.Master) foreach (var master in tracks.Values) if (master.kind == TrackKind.Master) next = master.id;
            string e = next != "" ? Cycle(next,tracks,active,done,parentOnly) : null;
            if (!parentOnly) foreach (var send in t.sends) { e = e ?? Cycle(send.trackId,tracks,active,done,false); }
            active.Remove(id); done.Add(id); return e;
        }
        static string Value(ColumnValue v) => v == null ? "Column value missing" : v.kind == ValueKind.Value && (v.value < 0 || v.value > 128) ? "Volume/pan outside profile" : Command(v.command);
        static string Command(CommandData c)
        {
            if (c == null) return "Command missing";
            if (c.present && c.profile != "native-v0" && !c.unsupported && (c.identifier == null || c.identifier.Length != 2 || c.value < 0 || c.value > 255)) return "Command identifier/value outside profile";
            if (c.scope == CommandScope.Column && c.targetColumn < -1) return "Command column target invalid";
            return null;
        }
        static string Target(ParameterTarget target, SongData song, string owner)
        {
            if (target.unresolved) return null;
            if (string.IsNullOrEmpty(target.parameter)) return "Parameter identity missing";
            string trackId = target.trackId == "" ? owner : target.trackId;
            var track = song.tracks.Find(t => t.id == trackId); if (track == null) return "Automation target track missing";
            if (target.kind == ParameterKind.Device && !track.devices.nodes.Exists(n => n.uid == target.deviceId)) return "Automation target device missing";
            if (target.kind == ParameterKind.InstrumentMacro && (target.index < 0 || target.index >= 8 || !song.instruments.Exists(i => i != null && i.model != null && i.model.id == target.instrumentId))) return "Automation instrument/macro missing";
            return null;
        }
        public static string ValidateChain(AudioEffectChainData chain)
        {
            if (chain == null || chain.nodes == null || chain.modifiers == null || chain.bindings == null) return "Chain collections missing";
            foreach (var n in chain.nodes) { if (n == null || n.p == null) return "Chain node missing"; foreach (var p in n.p) if (!Finite(p)) return "Nonfinite chain parameter"; }
            foreach (var m in chain.modifiers) if (m == null || m.p == null || m.steps == null || m.curve == null) return "Chain modifier missing";
            foreach (var b in chain.bindings) if (b == null || b.modifierIndex < 0 || b.modifierIndex >= chain.modifiers.Count || b.nodeIndex < -1 || b.nodeIndex >= chain.nodes.Count || b.paramIndex < 0 || !Finite(b.depth)) return "Chain binding invalid";
            return null;
        }
        public static string Validate(InstrumentData data)
        {
            if (data == null || data.parameters == null || data.sampler == null || data.macros == null || data.macros.Length != 8) return "Instrument requires parameters, sampler and exactly eight macros";
            if (!Enum.IsDefined(typeof(InstrumentFamily),data.family) || !Enum.IsDefined(typeof(SynthMode),data.synthMode) || !Enum.IsDefined(typeof(NewNoteAction),data.sampler.nna)) return "Unknown instrument family/engine/NNA";
            if (data.modulation == null || data.fxChains == null || data.externalParameters == null || data.archivedMacros == null || data.sampler.samples == null || data.sampler.zones == null) return "Instrument collections missing";
            if (!Finite(data.sampler.volume) || !Finite(data.sampler.pan) || !Finite(data.sampler.fineTuneCents)) return "Invalid sampler globals";
            foreach (var sample in data.sampler.samples)
            {
                if (sample == null || !Finite(sample.volume) || !Finite(sample.pan) || !Finite(sample.fineTuneCents) || sample.modulationSet < -1 || sample.modulationSet >= data.modulation.Count || sample.fxChain < -1 || sample.fxChain >= data.fxChains.Count || sample.muteGroup < -1) return "Invalid sample settings/references";
                if (!Enum.IsDefined(typeof(SampleLoop),sample.loop) || !Enum.IsDefined(typeof(SampleInterpolation),sample.interpolation) || !Enum.IsDefined(typeof(NewNoteAction),sample.nna)) return "Unknown sample mode";
                if (sample.loop != SampleLoop.Off && (sample.pcm == null || sample.loopStartFrame < 0 || sample.loopEndFrame <= sample.loopStartFrame || sample.loopEndFrame > sample.pcm.samples)) return "Invalid sample loop frame bounds";
            }
            foreach (var z in data.sampler.zones) if (z == null || z.sample < 0 || z.sample >= data.sampler.samples.Count || (!z.inactive && (z.noteMin < 0 || z.noteMax > 119 || z.noteMin > z.noteMax || z.velocityMin < 0 || z.velocityMax > 127 || z.velocityMin > z.velocityMax))) return "Invalid keyzone";
            foreach (var set in data.modulation)
            {
                if (set == null || set.devices == null) return "Modulation set missing";
                foreach (var d in set.devices)
                {
                    if (d == null || d.points == null || !Finite(d.attack) || !Finite(d.hold) || !Finite(d.decay) || !Finite(d.sustain) || !Finite(d.release) || !Finite(d.rate) || !Finite(d.depth) || d.attack < 0 || d.hold < 0 || d.decay < 0 || d.release < 0) return "Invalid modulation device";
                    double time = -1; foreach (var p in d.points) { if (p == null || !Finite(p.time) || !Finite(p.value) || !Finite(p.exponent) || p.time <= time) return "Modulation points must be ordered and unique"; time = p.time; }
                    if (d.loopEnabled && (d.loop == SampleLoop.Off || d.loopStart < 0 || d.loopEnd <= d.loopStart || d.loopEnd > time)) return "Modulation loop bounds";
                    if (d.sustainEnabled && (d.sustainPosition < 0 || d.sustainPosition > time)) return "Modulation sustain bounds";
                }
            }
            foreach (var chain in data.fxChains) { var e = ValidateChain(chain); if (e != null) return e; }
            foreach (var m in data.macros) { if (m == null || m.mappings == null || !Finite(m.value) || m.value < 0 || m.value > 1) return "Invalid macro"; foreach (var map in m.mappings) { var e = Mapping(map); if (e != null) return e; } }
            var external = new HashSet<string>(); foreach (var map in data.externalParameters) { if (map == null || string.IsNullOrEmpty(map.externalId) || !external.Add(map.externalId)) return "External parameter identity duplicate/missing"; var e = Mapping(map.mapping); if (e != null) return e; }
            return null;
        }
        static string Mapping(Mapping m) => m == null || m.target == null || !Finite(m.min) || !Finite(m.max) || !Finite(m.curve) || m.curve <= 0 || (!m.target.unresolved && string.IsNullOrEmpty(m.target.parameter)) ? "Invalid parameter mapping" : null;
    }
}

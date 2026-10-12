using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Laubrary.ZTracker.Model
{
    /// <summary>
    /// The one place that answers questions about a song's command ids: which ids are free, which definition an id
    /// names, what a definition points at, and which definitions belong on a given row. The engine, the pattern editor
    /// and every later column/suggestion/colour feature read this; none of them keeps its own list. Mutations here are
    /// plain data edits; callers wrap them in their own Undo step. See Documentation~/COMMAND_MODEL_DESIGN.md.
    /// </summary>
    public static class ZTrackerCommandRegistry
    {
        public const int CurrentVersion = 1;
        public const int MaxNameLength = 24;
        public static readonly string[] TrackParameters = { "preVolume", "prePan", "width", "postVolume", "postPan" };
        public static readonly string[] SongParameters = { "bpm" };
        const string Digits = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ";

        // ───────────── ids ─────────────

        /// <summary>Trimmed and upper-cased: ids are case-insensitive and stored upper case.</summary>
        public static string Normalize(string typed) => (typed ?? "").Trim().ToUpperInvariant();

        /// <summary>The whole 0… and Z… families belong to built-in commands, including combinations no built-in uses yet.</summary>
        public static bool IsReserved(string id) => id != null && id.Length == 2 && (id[0] == '0' || id[0] == 'Z');

        /// <summary>
        /// Null when <paramref name="typed"/> can name a new or renamed definition (<paramref name="exceptUid"/> is the
        /// definition being renamed); otherwise one plain reason. <paramref name="normalized"/> is the id to store.
        /// </summary>
        public static string ValidateId(SongData song, string typed, string exceptUid, out string normalized)
        {
            normalized = Normalize(typed);
            if (normalized.Length != 2) return "Type two characters";
            if (Digits.IndexOf(normalized[0]) < 0 || Digits.IndexOf(normalized[1]) < 0) return "Only letters A–Z and digits 0–9";
            if (normalized[0] == '0') return "Reserved for built-in commands (0…)";
            if (normalized[0] == 'Z') return "Reserved for song commands (Z…)";
            string device = DeviceUsing(song, normalized);
            if (device != null) return "Taken by " + device;
            string id = normalized;
            var other = song?.dynamicParameters?.FirstOrDefault(p => p != null && p.uid != exceptUid && Normalize(p.id) == id);
            if (other != null) return "Taken by ‘" + other.name + "’";
            return null;
        }

        /// <summary>Null when the friendly name can be stored; otherwise one plain reason.</summary>
        public static string ValidateName(string name)
        {
            string trimmed = (name ?? "").Trim();
            if (trimmed.Length == 0) return "Give it a name";
            if (trimmed.Length > MaxNameLength) return "At most " + MaxNameLength + " characters";
            return null;
        }

        /// <summary>An imported source device addresses its parameters as ab (a = device position 1…Y, b = parameter 0…Z).</summary>
        static string DeviceUsing(SongData song, string id)
        {
            if (song?.tracks == null) return null;
            int ordinal = Digits.IndexOf(id[0]);
            if (ordinal < 1 || ordinal > 34) return null;
            foreach (var t in song.tracks)
            {
                if (t == null) continue;
                bool used = t.sourceDevices != null && t.sourceDevices.Any(d => d != null && d.ordinal == ordinal)
                         || t.externalSources != null && t.externalSources.Any(d => d != null && d.sourceOrdinal == ordinal);
                if (used) return "imported device " + id[0] + " on track " + (string.IsNullOrEmpty(t.name) ? t.id : t.name);
            }
            return null;
        }

        // ───────────── lookup ─────────────

        public static DynamicParameterData Find(SongData song, string id)
        {
            if (song?.dynamicParameters == null || string.IsNullOrEmpty(id)) return null;
            string n = Normalize(id);
            return song.dynamicParameters.FirstOrDefault(p => p != null && Normalize(p.id) == n);
        }

        public static DynamicParameterData FindByUid(SongData song, string uid)
            => string.IsNullOrEmpty(uid) ? null : song?.dynamicParameters?.FirstOrDefault(p => p != null && p.uid == uid);

        /// <summary>The definition already giving this exact target an id, if any.</summary>
        public static DynamicParameterData FindByTarget(SongData song, DynamicScope scope, ParameterTarget target)
            => song?.dynamicParameters?.FirstOrDefault(p => p != null && p.scope == scope && SameTarget(p.target, target));

        public static bool SameTarget(ParameterTarget a, ParameterTarget b)
            => a != null && b != null && a.kind == b.kind && (a.trackId ?? "") == (b.trackId ?? "") && (a.deviceId ?? "") == (b.deviceId ?? "")
               && (a.instrumentId ?? "") == (b.instrumentId ?? "") && (a.parameter ?? "") == (b.parameter ?? "") && a.index == b.index;

        /// <summary>Whether the definition's owner still exists in the song (the track, the effect node, the instrument).</summary>
        public static bool OwnerExists(SongData song, DynamicParameterData p)
        {
            if (song == null || p?.target == null) return false;
            var t = p.target;
            switch (p.scope)
            {
                case DynamicScope.Song: return SongParameters.Contains(t.parameter);
                case DynamicScope.Track: return song.tracks.Any(x => x != null && x.id == t.trackId) && TrackParameters.Contains(t.parameter);
                case DynamicScope.Effect:
                    var track = song.tracks.FirstOrDefault(x => x != null && x.id == t.trackId);
                    var node = track?.devices?.nodes?.FirstOrDefault(n => n != null && n.uid == t.deviceId);
                    return node != null && t.index >= 0;
                case DynamicScope.Instrument:
                    return song.instruments.Any(i => i != null && i.model != null && i.model.id == t.instrumentId);
            }
            return false;
        }

        /// <summary>Default colour of a scope; a definition's own colour (alpha above zero) wins.</summary>
        public static Color DefaultColor(DynamicScope scope)
        {
            switch (scope)
            {
                case DynamicScope.Instrument: return new Color(.42f, .78f, .46f);
                case DynamicScope.Track: return new Color(.40f, .66f, .95f);
                case DynamicScope.Effect: return new Color(.80f, .55f, .95f);
                default: return new Color(.95f, .78f, .35f);
            }
        }
        public static Color ColorOf(DynamicParameterData p) => p == null ? Color.gray : p.color.a > 0 ? p.color : DefaultColor(p.scope);

        // ───────────── context ─────────────

        /// <summary>
        /// Definitions that belong on <paramref name="line"/> of <paramref name="trackId"/> in <paramref name="patternId"/>:
        /// song ids everywhere; track ids on their own track; effect ids on the track whose chain holds the effect;
        /// instrument ids where that instrument is playing (selected in one of the track's note columns at or above the
        /// row in this pattern; before any selection in the pattern, every instrument the track uses in the song).
        /// </summary>
        public static List<DynamicParameterData> InContext(SongData song, string patternId, string trackId, int line)
        {
            var result = new List<DynamicParameterData>();
            if (song?.dynamicParameters == null) return result;
            HashSet<string> playing = null;
            foreach (var p in song.dynamicParameters)
            {
                if (p?.target == null) continue;
                switch (p.scope)
                {
                    case DynamicScope.Song: result.Add(p); break;
                    case DynamicScope.Track:
                    case DynamicScope.Effect: if (p.target.trackId == trackId) result.Add(p); break;
                    case DynamicScope.Instrument:
                        if (playing == null) playing = InstrumentsPlaying(song, patternId, trackId, line);
                        if (playing.Contains(p.target.instrumentId)) result.Add(p);
                        break;
                }
            }
            return result;
        }

        /// <summary>Instrument identities playing on a row of a track (see <see cref="InContext"/>).</summary>
        public static HashSet<string> InstrumentsPlaying(SongData song, string patternId, string trackId, int line)
        {
            var ids = new HashSet<string>();
            var pattern = song?.patterns?.FirstOrDefault(p => p != null && p.id == patternId);
            var pt = pattern?.tracks?.FirstOrDefault(t => t != null && t.trackId == trackId);
            // The selection a note column holds at this row: the last instrument it named at or above the row.
            var lastPerColumn = new Dictionary<int, int>();
            if (pt?.lines != null)
                foreach (var l in pt.lines.Where(l => l != null && l.line <= line && l.notes != null).OrderBy(l => l.line))
                    foreach (var n in l.notes)
                        if (n != null && !n.inactive && n.instrumentPresent) lastPerColumn[n.column] = n.instrument;
            var indices = new HashSet<int>(lastPerColumn.Values);
            if (indices.Count == 0)
                foreach (var p in song?.patterns ?? new List<PatternData>())
                    foreach (var t in p?.tracks ?? new List<PatternTrack>())
                        if (t != null && t.trackId == trackId && t.lines != null)
                            foreach (var l in t.lines)
                                foreach (var n in l?.notes ?? new List<NoteCell>())
                                    if (n != null && !n.inactive && n.instrumentPresent) indices.Add(n.instrument);
            foreach (int index in indices)
            {
                var asset = song != null && index >= 0 && index < song.instruments.Count ? song.instruments[index] : null;
                if (asset?.model != null) ids.Add(asset.model.id);
            }
            return ids;
        }

        // ───────────── edits (callers own the Undo step) ─────────────

        /// <summary>Sets the command model version on first use. Refuses a version newer than this ZTracker.</summary>
        public static string Upgrade(SongData song)
        {
            if (song == null) return "No song";
            if (song.commandModelVersion < 0 || song.commandModelVersion > CurrentVersion) return "This song's commands were made by a newer ZTracker";
            if (song.dynamicParameters == null) song.dynamicParameters = new List<DynamicParameterData>();
            song.commandModelVersion = CurrentVersion;
            return null;
        }

        /// <summary>Gives a target an id and a name together. Returns the new definition, or null with <paramref name="error"/>.</summary>
        public static DynamicParameterData Define(SongData song, DynamicScope scope, ParameterTarget target, string id, string name, float min, float max, out string error)
        {
            error = song == null ? "No song" : song.commandModelVersion < 0 || song.commandModelVersion > CurrentVersion ? "This song's commands were made by a newer ZTracker" : null;
            if (error == null) error = ValidateId(song, id, null, out string checkedId) ?? ValidateName(name);
            if (error == null && target == null) error = "No target";
            if (error == null && FindByTarget(song, scope, target) != null) error = "This setting already has a command";
            if (error == null && (!float.IsFinite(min) || !float.IsFinite(max))) error = "The range must be finite";
            if (error == null) error = Upgrade(song);
            if (error != null) return null;
            ValidateId(song, id, null, out string normalized);
            var p = new DynamicParameterData { uid = Guid.NewGuid().ToString("N"), id = normalized, name = name.Trim(), scope = scope, target = Copy(target), min = min, max = max };
            song.dynamicParameters.Add(p);
            return p;
        }

        /// <summary>
        /// Changes id and name together. Every track FX cell that used the old id is rewritten to the new one, so the
        /// pattern keeps doing what it did. Returns null or the reason it was refused (nothing is changed then).
        /// </summary>
        public static string Rename(SongData song, DynamicParameterData p, string id, string name)
        {
            if (p == null || song?.dynamicParameters == null || !song.dynamicParameters.Contains(p)) return "No such command";
            string error = ValidateId(song, id, p.uid, out string normalized) ?? ValidateName(name) ?? Upgrade(song);
            if (error != null) return error;
            string old = Normalize(p.id);
            if (old != normalized)
                foreach (var pattern in song.patterns)
                    foreach (var t in pattern?.tracks ?? new List<PatternTrack>())
                        foreach (var l in t?.lines ?? new List<PatternLine>())
                            foreach (var e in l?.effects ?? new List<EffectCell>())
                                if (e?.command != null && e.command.present && Normalize(e.command.identifier) == old) e.command.identifier = normalized;
            p.id = normalized;
            p.name = name.Trim();
            return null;
        }

        /// <summary>Removes the definition only; cells that used its id stay and are reported until it is defined again.</summary>
        public static bool Remove(SongData song, DynamicParameterData p) => p != null && song?.dynamicParameters != null && song.dynamicParameters.Remove(p);

        /// <summary>How many track FX cells in the song use this id.</summary>
        public static int Uses(SongData song, string id)
        {
            string n = Normalize(id);
            int count = 0;
            if (song?.patterns == null || n.Length != 2) return 0;
            foreach (var pattern in song.patterns)
                foreach (var t in pattern?.tracks ?? new List<PatternTrack>())
                    foreach (var l in t?.lines ?? new List<PatternLine>())
                        foreach (var e in l?.effects ?? new List<EffectCell>())
                            if (e?.command != null && e.command.present && Normalize(e.command.identifier) == n) count++;
            return count;
        }

        static ParameterTarget Copy(ParameterTarget t) => new ParameterTarget { kind = t.kind, trackId = t.trackId ?? "", deviceId = t.deviceId ?? "", parameter = t.parameter ?? "", instrumentId = t.instrumentId ?? "", index = t.index, units = t.units ?? "", unresolved = false };
    }
}

using System;
using System.Collections.Generic;
using Laubrary.ZTracker.Engine;
using Laubrary.ZTracker.Model;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace Laubrary.ZTracker
{
    /// <summary>Main-thread facade over the clipless Burst SAP renderer. Idle owns no audio graph.</summary>
    [AddComponentMenu("")]
    [ExecuteAlways]
    [RequireComponent(typeof(AudioSource))]
    public sealed class ZTrackerPlayback : MonoBehaviour
    {
        static ZTrackerPlayback current;
        public static ZTrackerPlayback Current => current;
        public bool IsPlaying => generator != null && !stopping;
        public bool IsSong { get; private set; }
        public int AuditionNote { get; private set; }
        public int AuditionPreset { get; private set; }
        public TrackerSapGenerator Generator => generator;
        public string LastError { get; private set; }
        public bool LastStopConfirmed { get; private set; }
        public long RenderedFrames => generator != null ? generator.RenderedFrames : stoppedFrames;
        public long RenderedBlocks => generator != null ? generator.RenderedBlocks : stoppedBlocks;
        public bool RenderCompiled => generator != null && generator.RenderCompiled;
        public long EventOverflow => generator != null ? generator.EventOverflow : 0;
        public event Action<TrackerEvent> EngineEventReceived;
        TrackerSapGenerator generator;
        PlaybackSnapshot snapshot;
        readonly Queue<EventCatalog> catalogs = new Queue<EventCatalog>();
        readonly Queue<ZTrackerEventData> delivery = new Queue<ZTrackerEventData>();
        EventCatalog catalog;
        bool stopping, subscribed;
        long stoppedFrames, stoppedBlocks, lastSample;
        int lastOrder, lastRow, lastPattern = -1;
        bool haveRow;

        sealed class EventCatalog
        {
            public string[] payloads, tracks;
            public int[] bases, counts;
            public EventCatalog(SongData data, TrackerPreparedSong prepared)
            {
                payloads = prepared.eventPayloads.ToArray();
                tracks = new string[data.tracks.Count]; bases = new int[tracks.Length]; counts = new int[tracks.Length];
                int column = 0;
                for (int i = 0; i < tracks.Length; i++) { tracks[i] = data.tracks[i].id; bases[i] = column; counts[i] = data.tracks[i].visibleNoteColumns; column += counts[i]; }
            }
        }

        public static bool TryPlay(ZTrackerSong song, out ZTrackerPlayback playback, out string error, int order = 0, int row = 0)
            => TryStart(song, null, 69, out playback, out error, order, row, -1);
        public static bool TryAudition(ZTrackerInstrument instrument, int note, out ZTrackerPlayback playback, out string error, int preset = -1)
            => TryStart(null, instrument, note, out playback, out error, 0, 0, preset);

        static bool TryStart(ZTrackerSong song, ZTrackerInstrument instrument, int note, out ZTrackerPlayback playback, out string error, int order, int row, int preset)
        {
            playback = null; error = null;
            if (song == null && instrument == null) { error = "Choose a song or instrument first."; return false; }
            if (current != null) { error = "Stop the current tracker playback first."; return false; }
            if (!ZTrackerCapability.CheckPlayback(out error)) return false;
            GameObject root = null;
            PlaybackSnapshot data = null;
            TrackerPreparedSong prepared = null;
            try
            {
                data = PlaybackSnapshot.Create(song, instrument, preset);
                prepared = TrackerPreparedSong.Prepare(data.Data, AudioSettings.outputSampleRate);
                if (song != null && (order < 0 || order >= data.Data.sequence.Count || row < 0 || row >= data.Data.patterns.Find(p => p.id == data.Data.sequence[order].patternId).lineCount))
                    throw new ArgumentOutOfRangeException("Playback start position");
                root = new GameObject("ZTracker Burst playback");
                if (Application.isPlaying) DontDestroyOnLoad(root); else root.hideFlags = HideFlags.HideAndDontSave;
                var host = root.AddComponent<ZTrackerPlayback>();
                host.snapshot = data; data = null;
                host.catalog = new EventCatalog(host.snapshot.Data, prepared);
                host.IsSong = song != null; host.AuditionNote = note; host.AuditionPreset = preset;
                host.generator = root.AddComponent<TrackerSapGenerator>();
                host.generator.Configure(prepared, false); prepared = null;
                if (song != null)
                {
                    host.generator.SendCommand(TrackerCommand.Play());
                    if (order != 0 || row != 0) host.generator.SendCommand(new TrackerCommand { kind = TrackerCommandKind.Seek, a = order, b = row });
                }
                else if (!host.generator.SendCommand(TrackerCommand.Audition(0, Mathf.Clamp(note, 0, 119))))
                    throw new InvalidOperationException("Audition command refused");
                var source = root.GetComponent<AudioSource>();
                source.playOnAwake = false; source.spatialBlend = 0; source.volume = 1; source.generator = host.generator;
                current = host; host.Subscribe(); source.Play(); playback = host;
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                if (prepared != null) prepared.Dispose();
                data?.Dispose();
                if (root != null) { var host = root.GetComponent<ZTrackerPlayback>(); if (host != null) host.Stop(); else DestroyImmediate(root); }
                return false;
            }
        }

        public bool TryGetPosition(out int order, out int row) { order = lastOrder; row = lastRow; return IsPlaying && haveRow; }
        /// <summary>The detached song data that is playing (never the asset itself). Read only.</summary>
        public SongData PlayingSong => snapshot?.Data;
        ZTrackerSongClock clock;
        /// <summary>Live song position: current row, time into it and until the next, tempo, and the row's cells.
        /// Reading it consumes nothing, so it can be used alongside the event stream.</summary>
        public ZTrackerSongClock Clock => clock ??= new ZTrackerSongClock(this);
        // Retained method name for legacy view callers. No native channel or pointer is involved.
        public bool TryMapNativeChannel(int channel, out string trackId, out int noteColumn)
        {
            trackId = null; noteColumn = -1;
            if (catalog == null || channel < 0) return false;
            for (int i = 0; i < catalog.tracks.Length; i++)
                if (channel >= catalog.bases[i] && channel < catalog.bases[i] + catalog.counts[i]) { trackId = catalog.tracks[i]; noteColumn = channel - catalog.bases[i]; return true; }
            return false;
        }
        public bool SendCommand(TrackerCommand command) => IsPlaying && generator.SendCommand(command);

        /// <summary>Gates a track: its own pattern notes stop sounding, while notes played with <see cref="PlayNote"/>
        /// still sound. Ungating restores the pattern notes. The track's output mute is kept as authored.</summary>
        public bool SetTrackGated(int track, bool gated)
        {
            var song = PlayingSong;
            if (song == null || track < 0 || track >= song.tracks.Count) return false;
            return SendCommand(new TrackerCommand { kind = TrackerCommandKind.TrackMute, a = track, b = song.tracks[track].outputMute ? 1 : 0, c = gated || song.tracks[track].triggerMute ? 1 : 0 });
        }

        /// <summary>Plays one note on a track's first note column, as if the pattern had it: instrument is an index
        /// into the song's instruments, note 0..119, velocity 0..1. It replaces the column's previous note.</summary>
        public bool PlayNote(int track, int instrument, int note, float velocity = 1f)
            => SendCommand(TrackerCommand.Audition(instrument, Mathf.Clamp(note, 0, 119), Mathf.Clamp01(velocity), track));

        /// <summary>Releases the note playing on a track's first note column (its envelope's release runs).</summary>
        public bool ReleaseNote(int track) => SendCommand(new TrackerCommand { kind = TrackerCommandKind.AuditionOff, a = track, b = 0 });
        public bool Refresh(out string reason)
            => Refresh(null, out reason);
        bool Refresh(ZTrackerInstrument instrument, out string reason)
        {
            reason = null;
            if (!IsPlaying) { reason = "Playback unavailable"; return false; }
            TrackerPreparedSong next = null;
            PlaybackSnapshot candidate = null;
            try
            {
                candidate = snapshot.CreateRefresh(instrument);
                next = TrackerPreparedSong.Prepare(candidate.Data, AudioSettings.outputSampleRate);
                var nextCatalog = new EventCatalog(candidate.Data, next);
                if (!generator.RefreshPrepared(next, out reason)) return false;
                var previous = snapshot; snapshot = candidate; candidate = null; previous.Dispose();
                catalogs.Enqueue(nextCatalog); next = null; LastError = null;
                return true;
            }
            catch (Exception ex) { reason = ex.Message; return false; }
            finally { candidate?.Dispose(); next?.Dispose(); if (reason != null) LastError = reason; }
        }
        public void RefreshInstrument(ZTrackerInstrument instrument, int preset)
        {
            if (!snapshot.Contains(instrument)) return;
            if (preset != AuditionPreset && !IsSong) throw new InvalidOperationException("Variation change requires explicit Stop and Play");
            if (!Refresh(instrument, out var reason)) throw new InvalidOperationException(reason);
        }

        public bool TryReadEvent(out ZTrackerEventData value)
        {
            if (delivery.Count > 0) { value = delivery.Dequeue(); return true; }
            while (generator != null && generator.ReadEvent(out var ev))
            {
                lastSample = ev.samplePosition;
                EngineEventReceived?.Invoke(ev);
                if (ev.kind == TrackerEventKind.PreparedSwap)
                {
                    if (catalogs.Count == 0) { LastError = "Event catalog boundary missing"; continue; }
                    catalog = catalogs.Dequeue(); continue;
                }
                var mapped = new ZTrackerEventData { samplePosition = (ulong)Math.Max(0, ev.samplePosition), sequenceIndex = ev.sequence, patternIndex = ev.pattern, rowIndex = ev.row, channelIndex = ev.track, noteColumn = ev.column, noteValue = ev.note, instrumentID = ev.instrument, intParam = ev.payload,
                    trackId = ev.track >= 0 && ev.track < catalog.tracks.Length ? catalog.tracks[ev.track] : null };
                switch (ev.kind)
                {
                    case TrackerEventKind.Row:
                        if (!haveRow || ev.sequence != lastOrder || ev.pattern != lastPattern) { var change = mapped; change.type = (byte)ZTrackerEventType.PATTERN_CHANGED; delivery.Enqueue(change); }
                        lastOrder = ev.sequence; lastRow = ev.row; lastPattern = ev.pattern; haveRow = true;
                        mapped.type = (byte)ZTrackerEventType.ROW_CHANGED; break;
                    case TrackerEventKind.Started: mapped.type = (byte)ZTrackerEventType.SONG_STARTED; break;
                    case TrackerEventKind.Stopped: mapped.type = (byte)ZTrackerEventType.SONG_STOPPED; break;
                    case TrackerEventKind.NoteOn: mapped.type = (byte)ZTrackerEventType.CHANNEL_NOTE_ON; break;
                    case TrackerEventKind.NoteOff: mapped.type = (byte)ZTrackerEventType.CHANNEL_NOTE_OFF; break;
                    case TrackerEventKind.Beat: mapped.type = (byte)ZTrackerEventType.BEAT_TICK; break;
                    case TrackerEventKind.SongLooped: mapped.type = (byte)ZTrackerEventType.SONG_LOOPED; break;
                    case TrackerEventKind.TempoChanged: mapped.type = (byte)ZTrackerEventType.TEMPO_CHANGED; mapped.floatParam = (float)ev.bpm; mapped.intParam = (int)ev.bpm; break;
                    case TrackerEventKind.Authored:
                        mapped.type = (byte)ZTrackerEventType.EVENT_TRACK_FIRED;
                        if (ev.payload < 0 || ev.payload >= catalog.payloads.Length) { LastError = "Authored event catalog index invalid"; continue; }
                        mapped.stringPayload = catalog.payloads[ev.payload]; break;
                    default: continue;
                }
                delivery.Enqueue(mapped);
                value = delivery.Dequeue(); return true;
            }
            value = default; return false;
        }

        public void Stop()
        {
            if (stopping) return;
            stopping = true; Unsubscribe();
            if (generator != null)
            {
                LastStopConfirmed = generator.StopAndConfirm(.5);
                stoppedFrames = generator.RenderedFrames; stoppedBlocks = generator.RenderedBlocks;
                // A timeout retains native ownership in the SAP registry, never frees a potentially live reader.
                if (!LastStopConfirmed) LastError = "Audio graph retirement pending";
            }
            if (current == this) current = null;
            snapshot?.Dispose(); snapshot = null;
            if (Application.isPlaying) Destroy(gameObject); else DestroyImmediate(gameObject);
        }
        public void ReconfigureOutput(bool deviceChanged)
        {
            LastError = deviceChanged ? "Audio device configuration changed; call Play explicitly to restart." : "Audio output configuration changed; call Play explicitly to restart.";
            Stop();
        }
        void Subscribe()
        {
            subscribed = true; AudioSettings.OnAudioConfigurationChanged += ReconfigureOutput;
#if UNITY_EDITOR
            AssemblyReloadEvents.beforeAssemblyReload += Stop;
            EditorApplication.playModeStateChanged += ModeChanged;
            EditorApplication.quitting += Stop;
            EditorApplication.update += TickEditor;
#endif
        }
        void Unsubscribe()
        {
            if (!subscribed) return; subscribed = false; AudioSettings.OnAudioConfigurationChanged -= ReconfigureOutput;
#if UNITY_EDITOR
            AssemblyReloadEvents.beforeAssemblyReload -= Stop;
            EditorApplication.playModeStateChanged -= ModeChanged;
            EditorApplication.quitting -= Stop;
            EditorApplication.update -= TickEditor;
#endif
        }
#if UNITY_EDITOR
        void TickEditor() { if (!Application.isPlaying && IsPlaying) { generator.PollRetirement(); EditorApplication.QueuePlayerLoopUpdate(); } }
        void ModeChanged(PlayModeStateChange state) { if (state == PlayModeStateChange.ExitingEditMode || state == PlayModeStateChange.ExitingPlayMode) Stop(); }
#endif
        void OnDestroy() { Unsubscribe(); if (current == this) current = null; snapshot?.Dispose(); snapshot = null; }
    }

    /// <summary>Detached main-thread migration snapshots. Shared source assets and PCM are never rewritten.</summary>
    public sealed class PlaybackSnapshot : IDisposable
    {
        readonly ZTrackerSong source;
        readonly ZTrackerInstrument audition;
        readonly int preset;
        readonly Dictionary<ZTrackerInstrument, ZTrackerInstrument> copies = new Dictionary<ZTrackerInstrument, ZTrackerInstrument>();
        ZTrackerInstrument[] slots;
        public SongData Data { get; private set; }
        PlaybackSnapshot(ZTrackerSong source, ZTrackerInstrument audition, int preset) { this.source = source; this.audition = audition; this.preset = preset; }
        public bool Contains(ZTrackerInstrument instrument) => instrument != null && copies.ContainsKey(instrument);
        public static PlaybackSnapshot Create(ZTrackerSong source, ZTrackerInstrument audition, int preset)
        {
            var result = new PlaybackSnapshot(source, audition, preset);
            try { result.Build(null, null); return result; } catch { result.Dispose(); throw; }
        }
        public PlaybackSnapshot CreateRefresh(ZTrackerInstrument instrument = null)
        {
            var result = new PlaybackSnapshot(source, audition, preset);
            try { result.Build(this, instrument); return result; } catch { result.Dispose(); throw; }
        }
        ZTrackerInstrument Copy(ZTrackerInstrument original, PlaybackSnapshot previous, bool update)
        {
            if (original == null) return null;
            if (copies.TryGetValue(original, out var copy)) return copy;
            ZTrackerInstrument accepted = null;
            previous?.copies.TryGetValue(original, out accepted);
            InstrumentData model;
            if (!update && accepted != null) model = ZTrackerMigration.Copy(accepted.model);
            else
            {
                string error = ZTrackerMigration.VersionError(original.schemaVersion);
                if (error != null) throw new InvalidOperationException(error);
                model = original.schemaVersion == 0 ? ZTrackerMigration.Convert(ZTrackerMigration.Capture(original), original.name, accepted?.model.id) : ZTrackerMigration.Copy(original.model);
                ZTrackerMigration.MigrateInstrumentAuthoring(model);
                error = ZTrackerModelValidation.Validate(model);
                if (error != null) throw new InvalidOperationException(error);
                if (source == null && preset >= 0)
                {
                    var sets = ZTrackerMigration.ResolveParameterSets(model);
                    if (preset + 1 >= sets.Count) throw new ArgumentOutOfRangeException(nameof(preset));
                    model = ZTrackerMigration.Copy(sets[preset + 1].data);
                }
            }
            copy = ScriptableObject.CreateInstance<ZTrackerInstrument>();
            copy.hideFlags = HideFlags.HideAndDontSave; copy.name = original.name;
            copy.schemaVersion = ZTrackerMigration.CurrentVersion; copy.model = model;
            copy.playbackSourceIdentity = original.GetInstanceID();
            copies.Add(original, copy);
            return copy;
        }
        void Build(PlaybackSnapshot previous, ZTrackerInstrument instrument)
        {
            if (previous != null && instrument != null)
            {
                // Instrument edits follow captured source identities, never the
                // author's current slot order. Repeated references share a copy.
                Data = ZTrackerMigration.Copy(previous.Data);
                slots = (ZTrackerInstrument[])previous.slots.Clone();
            }
            else if (source != null)
            {
                string error = ZTrackerMigration.VersionError(source.schemaVersion);
                if (error != null) throw new InvalidOperationException(error);
                Data = source.schemaVersion == 0 ? ZTrackerMigration.Convert(ZTrackerMigration.Capture(source), previous?.Data) : ZTrackerMigration.Copy(source.model);
                if (Data == null) throw new InvalidOperationException("Song model missing");
                slots = Data.instruments.ToArray();
            }
            else
            {
                slots = new[] { audition };
                Data = new SongData { id = "audition", instruments = new List<ZTrackerInstrument> { audition } };
                Data.tracks.Add(new TrackData { id = "audition-track", visibleNoteColumns = 1, outputTrackId = "master" });
                Data.tracks.Add(new TrackData { id = "master", kind = TrackKind.Master, visibleNoteColumns = 0, visibleEffectColumns = 0 });
                var pattern = new PatternData { id = "audition-pattern", lineCount = 64 };
                pattern.tracks.Add(new PatternTrack { trackId = "audition-track" }); pattern.tracks.Add(new PatternTrack { trackId = "master" });
                Data.patterns.Add(pattern); Data.sequence.Add(new SequenceSlot { id = "audition-slot", patternId = pattern.id });
            }
            for (int i = 0; i < slots.Length; i++) Data.instruments[i] = Copy(slots[i], previous, instrument == null || slots[i] == instrument);
            // The engine validates before publication, retaining its existing
            // policy of diagnosing optional malformed lanes/commands separately.
        }
        public void Dispose() { foreach (var copy in copies.Values) if (copy != null) UnityEngine.Object.DestroyImmediate(copy); copies.Clear(); }
    }
}

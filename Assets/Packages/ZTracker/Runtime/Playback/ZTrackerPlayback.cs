using System;
using System.Threading;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace Laubrary.ZTracker
{
    [AddComponentMenu("")]
    [ExecuteAlways]
    [RequireComponent(typeof(AudioSource))]
    public sealed class ZTrackerPlayback : MonoBehaviour
    {
        static ZTrackerPlayback current;
        public static ZTrackerPlayback Current => current;
        public bool IsPlaying => context != IntPtr.Zero;
        public IntPtr NativeContext => context;
        public bool IsSong { get; private set; }
        public int AuditionNote { get; private set; }
        public int AuditionPreset { get; private set; }
        IntPtr context;
        // 0 = idle, 1 = rendering, 2 = main-thread ownership. The audio thread never waits.
        int gate;
        readonly float[] left = new float[4096];
        readonly float[] right = new float[4096];
        AudioSource source;
        AudioClip silence;
        ZTrackerSong playingSong;
        readonly float[] macroValues = new float[4];
        readonly bool[,] macroTouched = new bool[32, 4];
        readonly System.Collections.Generic.List<ZTrackerInstrument> refreshInstruments = new System.Collections.Generic.List<ZTrackerInstrument>();
        int refreshVariation;
        ZTrackerInstrument auditionInstrument;
        ZTrackerSong sourceSong;
        ZTrackerInstrument sourceAudition;
        Model.PreparedLegacySong preparedSong;

        public static bool TryPlay(ZTrackerSong song, out ZTrackerPlayback playback, out string error, int order = 0, int row = 0)
            => TryStart(song, null, 69, out playback, out error, order, row, -1);

        public static bool TryAudition(ZTrackerInstrument instrument, int note,
            out ZTrackerPlayback playback, out string error, int preset = -1)
            => TryStart(null, instrument, note, out playback, out error, 0, 0, preset);

        static bool TryStart(ZTrackerSong song, ZTrackerInstrument instrument, int note,
            out ZTrackerPlayback playback, out string error, int order, int row, int preset)
        {
            playback = null;
            error = null;
            if (song == null && instrument == null) { error = "Choose a song or instrument first."; return false; }
            if (current != null) { error = "Stop the current tracker playback first."; return false; }
            if (!ZTrackerCapability.CheckNative(out error)) return false;

            var go = new GameObject("ZTracker playback");
            if (Application.isPlaying) DontDestroyOnLoad(go);
            else go.hideFlags = HideFlags.HideAndDontSave;
            var host = go.AddComponent<ZTrackerPlayback>();
            try
            {
                host.sourceSong = song;
                host.sourceAudition = instrument;
                if (song != null)
                {
                    host.preparedSong = Model.ZTrackerLegacyCompatibility.Prepare(song);
                    song = host.preparedSong.song;
                    if (!Validate(song,out error)) throw new InvalidOperationException(error);
                }
                else { instrument = Model.ZTrackerLegacyCompatibility.Prepare(instrument); host.auditionInstrument = instrument; }
                host.context = ZTrackerNative.ZT_Create(AudioSettings.outputSampleRate);
                host.IsSong = song != null;
                host.playingSong = song;
                host.AuditionNote = note;
                host.AuditionPreset = preset;
                host.auditionInstrument = instrument;
                if (host.context == IntPtr.Zero) throw new InvalidOperationException("Tracker engine creation failed.");
                ZTrackerInstrument.InvalidateClipCache();
                // Upload finishes before the source can enter the renderer.
                if (song != null)
                {
                    host.preparedSong.PushToNative(host.context);
                    order = Mathf.Clamp(order, 0, song.orderList.Count - 1);
                    row = Mathf.Clamp(row, 0, song.patterns[song.orderList[order]].rowCount - 1);
                    ZTrackerNative.ZT_Play(host.context, order, row);
                }
                else
                {
                    instrument.PushToNative(host.context, 0, preset);
                    ZTrackerNative.ZT_NoteOn(host.context, 0, Mathf.Clamp(note, 0, 126), 1f);
                }
                host.source = go.GetComponent<AudioSource>();
                host.silence = AudioClip.Create("Tracker stream", AudioSettings.outputSampleRate, 2,
                    AudioSettings.outputSampleRate, true, host.ReadAudio);
                host.silence.hideFlags = HideFlags.HideAndDontSave;
                host.source.clip = host.silence;
                host.source.loop = true;
                host.source.playOnAwake = false;
                host.source.volume = 1f;
                current = host;
                host.Subscribe();
                host.source.Play();
                playback = host;
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                host.Stop();
                return false;
            }
        }

        // Read queries share the owner gate: no UI caller dereferences the context while rendering
        // or destruction owns it. If rendering is busy, the UI keeps its last displayed position.
        public bool TryGetPosition(out int order, out int row)
        {
            order = row = 0;
            if (Interlocked.CompareExchange(ref gate, 2, 0) != 0) return false;
            try
            {
                if (context == IntPtr.Zero) return false;
                order = ZTrackerNative.ZT_GetCurrentOrder(context);
                row = ZTrackerNative.ZT_GetCurrentRow(context);
                return true;
            }
            finally { Volatile.Write(ref gate, 0); }
        }
        public bool TryMapNativeChannel(int nativeChannel, out string trackId, out int noteColumn)
        {
            trackId = null; noteColumn = -1;
            if (preparedSong == null || nativeChannel < 0 || nativeChannel >= preparedSong.nativeTrackIds.Count) return false;
            trackId = preparedSong.nativeTrackIds[nativeChannel]; noteColumn = preparedSong.nativeNoteColumns[nativeChannel]; return true;
        }

        // Scalar sound edits retain the source, voice phase and envelope age. Structural edits use
        // the explicit restart path instead. The main thread retries on contention; audio never waits.
        public void RefreshInstrument(ZTrackerInstrument instrument, int preset)
        {
            // Resolve the edited authoritative asset before polling or native scalar writes.
            // Never consult an obsolete dense/source copy in the active host.
            var snapshot = Model.ZTrackerLegacyCompatibility.Prepare(instrument);
            try
            {
                var targets = new System.Collections.Generic.List<ZTrackerInstrument>();
                if (sourceSong == null) { if (instrument == sourceAudition && auditionInstrument != null) targets.Add(auditionInstrument); }
                else
                {
                    // These are the references at upload time, not the current source
                    // list, which may have since been reordered or replaced.
                    var sources = preparedSong.sourceModel.instruments;
                    for (int i = 0; i < sources.Count && i < playingSong.instruments.Count; i++) if (sources[i] == instrument && playingSong.instruments[i] != null) targets.Add(playingSong.instruments[i]);
                }
                // Validate every affected slot before modifying any snapshot/native
                // definition. Repeated authored references still own distinct slots.
                foreach (var target in targets) Model.ZTrackerLegacyCompatibility.RequireScalarRefresh(target,snapshot);
                foreach (var target in targets) { Model.ZTrackerMigration.Overwrite(snapshot,target); if (!refreshInstruments.Contains(target)) refreshInstruments.Add(target); }
                refreshVariation = preset; PumpInstrumentRefresh();
            }
            finally { DestroyImmediate(snapshot); }
        }

        void PumpInstrumentRefresh()
        {
            if (refreshInstruments.Count == 0 || context == IntPtr.Zero || Interlocked.CompareExchange(ref gate,2,0) != 0) return;
            try
            {
                if (playingSong == null)
                {
                    var inst = auditionInstrument;
                    if (!refreshInstruments.Contains(inst)) return;
                    inst.RefreshScalarDefinition(context,0,refreshVariation);
                    for(int voice=0;voice<128;voice++)
                        if(ZTrackerNative.ZT_GetVoiceCurrentNote(context,voice)>=0)
                        { ZTrackerNative.ZT_SetVoiceBlend(context,voice,inst.ResolveBlend(refreshVariation)); ZTrackerNative.ZT_SetVoicePulseWidth(context,voice,inst.ResolvePulseWidth(refreshVariation)); ZTrackerNative.ZT_SetVoiceWaveBRatio(context,voice,inst.ResolveWaveBRatio(refreshVariation)); }
                }
                else
                {
                    int slot=0;
                    for(int user=0;user<playingSong.instruments.Count;user++)
                    {
                        var i=playingSong.instruments[user];if(i==null)continue;
                        if(refreshInstruments.Contains(i)) { i.RefreshScalarDefinition(context,slot,-1); for(int p=0;p<(i.presets?.Count??0);p++)i.RefreshScalarDefinition(context,slot+p+1,p); }
                        slot+=1+(i.presets?.Count??0);
                    }
                    for(int ch=0;ch<playingSong.GetTotalNativeChannels();ch++)
                    {
                        int user=ZTrackerNative.ZT_GetChannelInstrument(context,ch),voice=ZTrackerNative.ZT_GetChannelVoiceID(context,ch);
                        if(voice<0||user<0||user>=playingSong.instruments.Count||!refreshInstruments.Contains(playingSong.instruments[user]))continue;
                        var inst = playingSong.instruments[user];
                        int p=ZTrackerNative.ZT_GetChannelPreset(context,ch)-1;
                        ZTrackerNative.ZT_SetVoiceBlend(context,voice,inst.ResolveBlend(p));ZTrackerNative.ZT_SetVoicePulseWidth(context,voice,inst.ResolvePulseWidth(p));ZTrackerNative.ZT_SetVoiceWaveBRatio(context,voice,inst.ResolveWaveBRatio(p));
                    }
                }
                refreshInstruments.Clear();
            }
            finally { Volatile.Write(ref gate,0); }
        }

        static bool Validate(ZTrackerSong song, out string error)
        {
            error = null;
            if (song.patterns == null || song.orderList == null || song.channels == null || song.instruments == null)
            { error = "The song has missing collections."; return false; }
            if (song.channelCount < 1 || song.GetTotalNativeChannels() > 32 ||
                song.patterns.Count < 1 || song.patterns.Count > 256 || song.orderList.Count < 1 ||
                song.orderList.Count > 256 || song.bpm < 1 || song.ticksPerRow < 1 || song.linesPerBeat < 1)
                error = "The song needs valid tempo, channels, patterns and an order list.";
            foreach (var p in song.patterns)
                if (p == null || p.rowCount < 1 || p.rowCount > 256 || p.cells == null ||
                    p.cells.Count < p.rowCount * song.channelCount)
                    error = "A pattern has missing rows or cells.";
            foreach (int p in song.orderList)
                if (p < 0 || p >= song.patterns.Count) error = "The order list references a missing pattern.";
            int slots = 0;
            foreach (var i in song.instruments) if (i != null) slots += 1 + (i.presets?.Count ?? 0);
            if (slots > 512) error = "The song uses more than 512 instrument and preset slots.";
            return error == null;
        }

        void Subscribe()
        {
            AudioSettings.OnAudioConfigurationChanged += ConfigurationChanged;
#if UNITY_EDITOR
            AssemblyReloadEvents.beforeAssemblyReload += Stop;
            EditorApplication.playModeStateChanged += ModeChanged;
            EditorApplication.quitting += Stop;
            EditorApplication.update += TickEditor;
#endif
        }

        void Unsubscribe()
        {
            AudioSettings.OnAudioConfigurationChanged -= ConfigurationChanged;
#if UNITY_EDITOR
            AssemblyReloadEvents.beforeAssemblyReload -= Stop;
            EditorApplication.playModeStateChanged -= ModeChanged;
            EditorApplication.quitting -= Stop;
            EditorApplication.update -= TickEditor;
#endif
        }

        void ConfigurationChanged(bool _) => Stop();
#if UNITY_EDITOR
        void TickEditor() { if (!Application.isPlaying) { PumpInstrumentRefresh(); PumpMacros(); EditorApplication.QueuePlayerLoopUpdate(); } }
        void ModeChanged(PlayModeStateChange mode)
        {
            if (mode == PlayModeStateChange.ExitingEditMode || mode == PlayModeStateChange.ExitingPlayMode) Stop();
        }
#endif

        void Update() { if (Application.isPlaying) { PumpInstrumentRefresh(); PumpMacros(); } }

        void PumpMacros()
        {
            if (playingSong == null || context == IntPtr.Zero || Interlocked.CompareExchange(ref gate, 2, 0) != 0) return;
            try
            {
                int order = ZTrackerNative.ZT_GetCurrentOrder(context), row = ZTrackerNative.ZT_GetCurrentRow(context);
                for (int logical = 0; logical < playingSong.channelCount; logical++)
                {
                    int start = playingSong.GetTrackNativeBase(logical), size = Mathf.Clamp(playingSong.channels[logical].noteColumnCount, 1, 12);
                    // Commands on the displayed row mark macros as authored, including an explicit zero.
                    if (order >= 0 && order < playingSong.orderList.Count)
                    {
                        var pattern = playingSong.patterns[playingSong.orderList[order]];
                        var cell = pattern.GetCell(Mathf.Clamp(row, 0, pattern.rowCount - 1), logical, playingSong.channelCount);
                        for (int fx = 0; fx < playingSong.channels[logical].fxColumnCount; fx++)
                            if (cell.GetEffectCmd(fx) == 16 || cell.GetEffectCmd(fx) == 17)
                            { int m = cell.GetEffectParam(fx) >> 4; if (m < 4) macroTouched[start + fx % size, m] = true; }
                    }
                    for (int k = 0; k < size; k++)
                    {
                        int ch = start + k, user = ZTrackerNative.ZT_GetChannelInstrument(context, ch), voice = ZTrackerNative.ZT_GetChannelVoiceID(context, ch);
                        if (voice < 0 || user < 0 || user >= playingSong.instruments.Count) continue;
                        var inst = playingSong.instruments[user]; if (inst == null || inst.macros == null || inst.macros.Length == 0) continue;
                        int variant = ZTrackerNative.ZT_GetChannelPreset(context, ch), slot = 0;
                        for (int i = 0; i < user; i++) if (playingSong.instruments[i] != null) slot += 1 + (playingSong.instruments[i].presets?.Count ?? 0);
                        int preset = variant > 0 && variant <= (inst.presets?.Count ?? 0) ? variant - 1 : -1;
                        if (preset >= 0) slot += variant;
                        for (int m = 0; m < 4; m++) macroValues[m] = macroTouched[ch,m] ? ZTrackerNative.ZT_GetChannelMacro(context,ch,m) : m < inst.macros.Length ? inst.macros[m].defaultValue : 0;
                        for (int m = 0; m < inst.macros.Length && m < 4; m++)
                        {
                            if (inst.macros[m].links == null) continue;
                            foreach (var link in inst.macros[m].links)
                            {
                                float value = Mathf.Lerp(link.minValue,link.maxValue,macroValues[m]);
                                switch (link.parameterName) {
                                    case "blend": ZTrackerNative.ZT_SetVoiceBlend(context,voice,value); break;
                                    case "pulseWidth": ZTrackerNative.ZT_SetVoicePulseWidth(context,voice,value); break;
                                    case "waveBRatio": ZTrackerNative.ZT_SetVoiceWaveBRatio(context,voice,value); break;
                                    case "volume": ZTrackerNative.ZT_SetVoiceVolumeMod(context,voice,m,Mathf.Max(.001f,value)); break;
                                }
                            }
                        }
                        // Structural parameters retain the legacy next-note behavior, but are updated only
                        // while the owner excludes rendering, and only on the correct instrument/preset slot.
                        if (inst.type == InstrumentType.Synth)
                        {
                            inst.ResolveSynthParams(preset,out int wa,out int wb,out int mode,out int voices,out float spread);
                            inst.ResolveVolPan(preset,out float volume,out float pan);
                            inst.ResolveAdsr(preset,out float a,out float d,out float s,out float r);
                            ZTrackerNative.ZT_SetSynthInstrument(context,slot,wa,wb,mode,
                                inst.EvaluateParamWithMacros("blend",inst.ResolveBlend(preset),macroValues),
                                inst.EvaluateParamWithMacros("pmDepth",inst.ResolvePMDepth(preset),macroValues),
                                inst.EvaluateParamWithMacros("waveBRatio",inst.ResolveWaveBRatio(preset),macroValues),
                                inst.blendEnvelope?1:0,inst.blendAttack,inst.blendDecay,inst.blendSustain,inst.blendRelease,
                                voices,inst.EvaluateParamWithMacros("unisonDetune",inst.ResolveDetune(preset),macroValues),inst.EvaluateParamWithMacros("unisonSpread",spread,macroValues),
                                volume,inst.EvaluateParamWithMacros("pan",pan,macroValues),
                                inst.EvaluateParamWithMacros("attack",a,macroValues),inst.EvaluateParamWithMacros("decay",d,macroValues),inst.EvaluateParamWithMacros("sustain",s,macroValues),inst.EvaluateParamWithMacros("release",r,macroValues),
                                inst.EvaluateParamWithMacros("pulseWidth",inst.ResolvePulseWidth(preset),macroValues));
                            inst.ResolveVibrato(preset,out float depth,out float rate,out float fade,out float random);
                            ZTrackerNative.ZT_SetInstrumentVibrato(context,slot,inst.EvaluateParamWithMacros("vibratoDepth",depth,macroValues),inst.EvaluateParamWithMacros("vibratoRate",rate,macroValues),fade,random);
                        }
                    }
                }
            }
            finally { Volatile.Write(ref gate,0); }
        }

        public void Stop()
        {
            Release();
            if (this != null)
            {
                if (Application.isPlaying) Destroy(gameObject);
                else DestroyImmediate(gameObject);
            }
        }

        void Release()
        {
            Unsubscribe();
            if (source != null) source.Stop();
            // Once gate 2 is held, no callback can snapshot or dereference this context.
            var spin = new SpinWait();
            while (Interlocked.CompareExchange(ref gate, 2, 0) != 0) spin.SpinOnce();
            try
            {
                var doomed = context;
                context = IntPtr.Zero;
                if (doomed != IntPtr.Zero) ZTrackerNative.ZT_Destroy(doomed);
                ZTrackerInstrument.InvalidateClipCache();
                if (current == this) current = null;
            }
            finally { Volatile.Write(ref gate, 0); }
            if (silence != null)
            {
                if (Application.isPlaying) Destroy(silence);
                else DestroyImmediate(silence);
                silence = null;
            }
            if (preparedSong != null) { preparedSong.Dispose(); preparedSong = null; }
            if (auditionInstrument != null) { DestroyImmediate(auditionInstrument); auditionInstrument = null; }
        }

        void OnDisable() => Release();
        void OnApplicationQuit() => Release();

        void ReadAudio(float[] data)
        {
            const int channels = 2;
            Array.Clear(data, 0, data.Length);
            if (channels < 1 || Interlocked.CompareExchange(ref gate, 1, 0) != 0) return;
            try
            {
                if (context == IntPtr.Zero) return;
                int frames = data.Length / channels;
                for (int offset = 0; offset < frames; offset += left.Length)
                {
                    int count = Math.Min(left.Length, frames - offset);
                    ZTrackerNative.ZT_Process(context, left, right, count);
                    for (int f = 0; f < count; f++)
                    {
                        int dst = (offset + f) * channels;
                        if (channels == 1) data[dst] += (left[f] + right[f]) * 0.5f;
                        else { data[dst] += left[f]; data[dst + 1] += right[f]; }
                    }
                }
            }
            finally { Volatile.Write(ref gate, 0); }
        }
    }
}

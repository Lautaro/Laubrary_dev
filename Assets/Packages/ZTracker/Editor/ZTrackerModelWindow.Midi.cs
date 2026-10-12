using System;
using System.Collections.Generic;
using System.Linq;
using Laubrary.ZTracker.Engine;
using Laubrary.ZTracker.Midi;
using Laubrary.ZTracker.Model;
using Laubrary.Zui;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Laubrary.ZTracker.Editor
{
    // MIDI keyboard input and the live-key routing it shares with the computer keyboard. MIDI is off until the user
    // turns it on. In the Instrument workspace keys from either keyboard play the edited instrument on a clean preview
    // channel of its own (no track settings, only the instrument's own effect chains), which may sound over a playing
    // song. Everywhere else MIDI plays the selected instrument through the cursor's track and note column, as a pattern
    // note there would, spreading chords over that track's note columns.
    public sealed partial class ZTrackerModelWindow
    {
        const string MidiPrefs = "Laubrary.ZTracker.Midi.";
        readonly ZTrackerMidiRouter midiRouter = new ZTrackerMidiRouter();
        ZTrackerMidiWindows midiWindows;
        bool midiOn, midiStarted;
        [SerializeField] bool midiPreferenceRestoreEnabled;
        internal bool midiPersistPreferences;
        bool midiSettingsInitialized;
        string midiDevice, midiProblem, midiTargetText = "";
        double midiNextScan, midiRouteRetry;
        ZuiToggleButton midiToggleButton, midiRecordButton;
        bool midiRecord;
        ZTrackerSongClock midiClock;
        readonly List<int> recordHeld = new List<int>();
        Label midiStatus;
        ZuiPopover midiMenu;
        // The clean preview channel: its own renderer, separate from the song's.
        GameObject midiPreviewObject;
        TrackerSapGenerator midiPreviewHost;
        PlaybackSnapshot midiPreviewSnapshot;
        ZTrackerInstrument midiPreviewInstrument;
        double midiPreviewIdleSince = -1;
        // Checks list only the simulated keyboard, so they never open a real device on this machine.
        bool midiSimulatedOnly;
        static readonly string[] midiBufferLabels = { "Project", "1024", "512", "256" };
        static readonly int[] midiBufferSizes = { 0, 1024, 512, 256 };
        int midiBuffer;

        static ZTrackerMidiSimulated MidiSimulated => ZTrackerMidiSimulated.Shared;
        internal bool MidiEnabled => midiOn;
        internal TrackerSapGenerator MidiPreviewHost => midiPreviewHost;
        internal TrackerSapGenerator SongHost => host;
        internal string MidiStatusText => midiStatus?.text;

        VisualElement BuildMidiControls()
        {
            LoadMidiPrefs();
            var cluster = Z.Row(); cluster.name = "midi-controls"; cluster.style.flexShrink = 0; cluster.style.alignItems = Align.Center; cluster.style.marginLeft = 8;
            var toggle = Z.ToggleButton("MIDI", "Play instruments from a MIDI keyboard. In the Instrument workspace it previews the edited instrument on a clean channel; elsewhere it plays the selected instrument through the cursor's track and column. Right-click for the device and keyboard settings.", midiOn, SetMidiEnabled);
            toggle.name = "midi-toggle"; toggle.style.flexShrink = 0; midiToggleButton = toggle;
            toggle.RegisterCallback<PointerDownEvent>(e => { if (e.button == 1) { ShowMidiMenu(toggle); e.StopPropagation(); } }, TrickleDown.TrickleDown);
            cluster.Add(toggle);
            midiRecordButton = Z.ToggleButton("Rec", "Record keys into the pattern. While the song plays, each key is written at the row you heard when you pressed it (rounded to the nearest row); while stopped, keys are written at the cursor, which moves on by the edit step when every key is up. Chords fill the track's next note columns. Each recorded note is one Undo step.", midiRecord, v => { midiRecord = v; SaveMidiBool("Record", v); recordHeld.Clear(); UpdateMidiCapture(); RefreshMidiStatus(); });
            midiRecordButton.name = "midi-record"; midiRecordButton.style.flexShrink = 0; midiRecordButton.style.marginLeft = 2; midiRecordButton.style.visibility = midiOn ? UnityEngine.UIElements.Visibility.Visible : UnityEngine.UIElements.Visibility.Hidden;
            cluster.Add(midiRecordButton);
            midiStatus = Z.Text("", tooltip: ""); midiStatus.name = "midi-status";
            midiStatus.style.width = 300; midiStatus.style.height = 24; midiStatus.style.whiteSpace = WhiteSpace.NoWrap; midiStatus.style.overflow = Overflow.Hidden; midiStatus.style.textOverflow = TextOverflow.Ellipsis; midiStatus.style.unityTextAlign = TextAnchor.MiddleLeft; midiStatus.style.marginLeft = 4;
            midiStatus.RegisterCallback<PointerDownEvent>(e => { if (e.button == 1) { ShowMidiMenu(midiStatus); e.StopPropagation(); } });
            cluster.Add(midiStatus);
            var panic = Named(Z.Button("Panic", "Silence every note played from the MIDI keyboard or the computer keyboard at once (the song keeps playing).", MidiPanic), "midi-panic");
            panic.style.flexShrink = 0; panic.style.flexGrow = 0; cluster.Add(panic);
            RefreshMidiStatus();
            if (midiOn && midiPreferenceRestoreEnabled && !midiSimulatedOnly) SetMidiEnabled(true);
            return cluster;
        }

        void LoadMidiPrefs()
        {
            midiPersistPreferences = midiPreferenceRestoreEnabled && !midiSimulatedOnly;
            if (midiSettingsInitialized) return;

            // Ordinary tracker windows do not opt into persisted MIDI. Defer initialization so an explicit
            // opt-in after GetWindow/CreateGUI can still restore preferences into the already-built window.
            if (!midiPreferenceRestoreEnabled && !midiSimulatedOnly) return;

            // The simulated fixture never reads real preferences. Its settings start from the same documented
            // defaults and then remain live across UI rebuilds, just like the real window's settings.
            if (midiSimulatedOnly)
            {
                midiOn = false; midiRecord = false; midiDevice = null;
                midiRouter.Channel = -1; midiRouter.OctaveShift = 0;
                midiRouter.VelocityCurve = ZTrackerVelocityCurve.Linear;
                midiRouter.BendRange = 2; midiRouter.ModWheelMacro = 0; midiBuffer = 0;
                midiSettingsInitialized = true;
                return;
            }

            var saved = MidiPreferenceSnapshot.Read(MidiPrefs,
                (key, fallback) => EditorPrefs.GetString(key, fallback),
                (key, fallback) => EditorPrefs.GetInt(key, fallback),
                (key, fallback) => EditorPrefs.GetFloat(key, fallback),
                (key, fallback) => EditorPrefs.GetBool(key, fallback));
            midiDevice = saved.device;
            midiOn = midiPreferenceRestoreEnabled && !midiSimulatedOnly && saved.enabled;
            midiRecord = midiPreferenceRestoreEnabled && !midiSimulatedOnly && saved.record;
            midiRouter.Channel = saved.channel;
            midiRouter.OctaveShift = saved.octave;
            midiRouter.VelocityCurve = (ZTrackerVelocityCurve)saved.curve;
            midiRouter.BendRange = saved.bend;
            midiRouter.ModWheelMacro = saved.modWheel;
            midiBuffer = Mathf.Clamp(saved.buffer, 0, midiBufferSizes.Length - 1);
            midiSettingsInitialized = true;
        }

        List<string> MidiDevices()
        {
            var list = new List<string>();
            if (midiWindows != null && !midiSimulatedOnly) for (int i = 0; i < midiWindows.DeviceCount; i++) list.Add(midiWindows.DeviceName(i));
            if (MidiSimulated.DeviceCount > 0) list.Add(ZTrackerMidiSimulated.Device);
            return list;
        }

        void ShowMidiMenu(VisualElement anchor)
        {
            if (!midiSimulatedOnly)
            {
                if (midiWindows == null) midiWindows = new ZTrackerMidiWindows(); else midiWindows.RefreshDevices();
            }
            var devices = MidiDevices();
            var menu = Z.Menu(anchor).Width(300).Section("Device", "MIDI inputs Windows reports. A device held by another program (a DAW) cannot be opened until that program releases it.");
            if (devices.Count == 0) menu.Item("No MIDI input found", "Plug in a MIDI keyboard; the list refreshes on its own.", null, enabled: false);
            else menu.Radio("", devices.ToArray(), Math.Max(-1, devices.IndexOf(CurrentMidiDevice(devices))), "The MIDI input to listen to. Simulated keyboard is driven by checks only.", i => { midiDevice = devices[i]; SaveMidiString("Device", midiDevice); ReopenMidi(); });
            menu.Item("Refresh devices", "Read the device list again now (it also refreshes on its own every two seconds).", () => { midiWindows?.RefreshDevices(); ReopenMidi(); });
            menu.Section("Keyboard");
            var channels = new string[17]; channels[0] = "Omni"; for (int i = 1; i <= 16; i++) channels[i] = i.ToString();
            menu.Radio("Channel", channels, midiRouter.Channel + 1, "Listen to every MIDI channel (Omni) or only one.", i => { midiRouter.Channel = i - 1; SaveMidiInt("Channel", i - 1); });
            menu.Custom((body, close) => body.Add(Z.MicroSlider("Octave", midiRouter.OctaveShift, -4, 4, "Shift every played key by whole octaves.", v => { midiRouter.OctaveShift = Mathf.RoundToInt(v); SaveMidiInt("Octave", midiRouter.OctaveShift); }, 200, defaultValue: 0, decimals: 0)));
            menu.Radio("Velocity", new[] { "Linear", "Soft", "Hard" }, (int)midiRouter.VelocityCurve, "How key velocity becomes loudness: Linear follows the key; Soft makes gentle playing louder; Hard needs a firmer touch.", i => { midiRouter.VelocityCurve = (ZTrackerVelocityCurve)i; SaveMidiInt("Curve", i); });
            menu.Radio("Buffer", midiBufferLabels, midiBuffer, "Audio buffer size for MIDI. Smaller answers keys sooner (256 is about 5 ms per buffer) but costs more processor time; if you hear crackles, pick a larger size. Changing it restarts the editor's audio, so a playing song stops. Your choice is reapplied when MIDI is enabled, including after a script reload; turning MIDI off or quitting restores the project's setting.", i => { midiBuffer = i; SaveMidiInt("Buffer", i); ApplyMidiBuffer(); });
            menu.Section("Controllers", "Sustain pedal holds notes; All Notes Off (controller 123) releases them. Knobs sending controllers 21 to 28 drive macros 1 to 8 of the instrument being played. Program changes are ignored.");
            menu.Custom((body, close) => body.Add(Z.MicroSlider("Bend range", midiRouter.BendRange, 0, 12, "How far the pitch wheel bends live notes, in semitones each way.", v => { midiRouter.BendRange = Mathf.Round(v); SaveMidiFloat("Bend", midiRouter.BendRange); }, 200, defaultValue: 2, decimals: 0)));
            menu.Radio("Mod wheel", new[] { "1", "2", "3", "4", "5", "6", "7", "8" }, midiRouter.ModWheelMacro, "The macro of the played instrument that the mod wheel moves.", i => { midiRouter.ModWheelMacro = i; SaveMidiInt("ModWheel", i); });
            midiMenu = menu.Show();
        }

        string CurrentMidiDevice(List<string> devices)
        {
            return MidiPreferenceSnapshot.ResolveDevice(midiDevice, devices, ZTrackerMidiSimulated.Device);
        }

        void SetMidiEnabled(bool on)
        {
            if (on && !midiSimulatedOnly && midiPersistPreferences) midiPreferenceRestoreEnabled = true;
            if (midiPersistPreferences) EditorPrefs.SetBool(MidiPrefs + "Enabled", on);
            if (!on) MidiShutdown();
            else
            {
                if (midiStarted) { RefreshMidiStatus(); return; }
                midiOn = true;
                midiStarted = true;
                if (midiRecordButton != null) midiRecordButton.style.visibility = UnityEngine.UIElements.Visibility.Visible;
                if (!midiSimulatedOnly && midiWindows == null) midiWindows = new ZTrackerMidiWindows();
                if (!midiSimulatedOnly && midiBuffer != 0) ApplyMidiBuffer();
                midiNextScan = 0; ReopenMidi(); MidiPump();
            }
            RefreshMidiStatus();
        }

        void ReopenMidi()
        {
            midiWindows?.Close(); MidiSimulated.Close(); midiProblem = null;
            if (!midiOn) return;
            var devices = MidiDevices(); var device = CurrentMidiDevice(devices);
            if (device == null) { midiProblem = "No MIDI input found"; return; }
            string error;
            bool ok = device == ZTrackerMidiSimulated.Device ? MidiSimulated.Open(device, midiRouter, out error) : midiWindows.Open(device, midiRouter, out error);
            if (!ok) midiProblem = device + ": " + error;
            else if (string.IsNullOrEmpty(midiDevice)) { midiDevice = device; SaveMidiString("Device", device); }
        }

        string OpenMidiDevice => MidiSimulated.IsOpen ? ZTrackerMidiSimulated.Device : midiWindows != null && midiWindows.IsOpen ? midiWindows.OpenDevice : null;

        // Main thread, every pump: keep the device open, follow hot-plug, and point new notes at the current target.
        void MidiPump()
        {
            if (midiOn)
            {
                double now = EditorApplication.timeSinceStartup;
                if (now >= midiNextScan)
                {
                    midiNextScan = now + 2;
                    if (midiWindows != null && midiWindows.RefreshDevices() && midiWindows.IsOpen && !MidiDevices().Contains(midiWindows.OpenDevice)) { midiWindows.Close(); }
                    MidiSimulated.RefreshDevices();
                    if (OpenMidiDevice == null) ReopenMidi();
                }
                RouteLiveKeys();
                var route = midiRouter.Current;
                UpdateMidiCapture();
                while (midiRouter.TryReadCaptured(out var key)) RecordKey(key, route);
            }
            ReleaseIdleMidiPreview();
            RefreshMidiStatus();
        }

        // Points the router at where keys from either keyboard go now. The computer keyboard calls it on each key-down,
        // so it works with MIDI off.
        void RouteLiveKeys()
        {
            // A target that failed to start (an invalid song or instrument) is retried every two seconds, not every pump.
            if (EditorApplication.timeSinceStartup < midiRouteRetry) return;
            try
            {
                if (pane == 3 && instrument != null)
                {
                    EnsureMidiPreview();
                    midiRouter.SetRoute(midiPreviewHost.LiveInput, 0, 0, 0);
                    midiTargetText = "→ clean preview · " + MidiInstrumentName(instrument);
                    return;
                }
                if (Data == null) { midiRouter.SetRoute(null, 0, 0, 0); midiTargetText = "→ no song"; return; }
                // An audio configuration change (a new buffer size, another output device) stops renderers; start a fresh one.
                if (host != null && (host.LiveInput == null || !host.LiveInput.IsOpen)) StopPreview();
                if (Data.instruments.Count == 0) { midiRouter.SetRoute(null, 0, 0, 0); midiTargetText = "→ no instrument in the song"; return; }
                int t = FirstSequencer();
                if (t < 0) { midiRouter.SetRoute(null, 0, 0, 0); midiTargetText = "→ no note track"; return; }
                int column = 0;
                if (pane == 0 && sub >= 0 && sub < fields.Count && fields[sub].track == t && fields[sub].kind <= FieldKind.LocalValue) column = fields[sub].column;
                column = Mathf.Clamp(column, 0, Math.Max(0, Data.tracks[t].visibleNoteColumns - 1));
                int ins = Mathf.Clamp(entryInstrument, 0, Data.instruments.Count - 1);
                if (host == null) EnsurePreview();
                midiRouter.SetRoute(host.LiveInput, t, column, ins);
                midiTargetText = $"→ {Data.tracks[t].name} col {column + 1} · {ins:X2} {MidiInstrumentName(Data.instruments[ins])}";
            }
            catch (Exception ex) { midiRouter.SetRoute(null, 0, 0, 0); midiTargetText = "→ unavailable: " + ex.Message; midiRouteRetry = EditorApplication.timeSinceStartup + 2; }
        }

        void EnsureMidiPreview()
        {
            if (midiPreviewHost != null && midiPreviewInstrument == instrument && midiPreviewHost.LiveInput != null && midiPreviewHost.LiveInput.IsOpen) { midiPreviewIdleSince = -1; return; }
            DisposeMidiPreview();
            PlaybackSnapshot candidate = PlaybackSnapshot.CreateClean(instrument, 12);
            TrackerPreparedSong prepared = null;
            try
            {
                prepared = TrackerPreparedSong.Prepare(candidate.Data, AudioSettings.outputSampleRate);
                midiPreviewObject = new GameObject("ZTracker MIDI preview") { hideFlags = HideFlags.HideAndDontSave };
                midiPreviewHost = midiPreviewObject.AddComponent<TrackerSapGenerator>();
                midiPreviewHost.Configure(prepared, false); prepared = null;
                var source = midiPreviewObject.GetComponent<AudioSource>(); source.playOnAwake = false; source.spatialBlend = 0; source.generator = midiPreviewHost; source.Play();
                midiPreviewSnapshot = candidate; candidate = null; midiPreviewInstrument = instrument; midiPreviewIdleSince = -1;
            }
            catch { DisposeMidiPreview(); throw; }
            finally { prepared?.Dispose(); candidate?.Dispose(); }
        }

        // An instrument edit reaches the clean preview the same way it reaches the song: a compatible change keeps the
        // channel and its held notes; a structural one rebuilds the channel (it has no song position to lose).
        void RefreshMidiPreview(ZTrackerInstrument changed)
        {
            if (midiPreviewHost == null || midiPreviewSnapshot == null) return;
            if (changed != null && changed != midiPreviewInstrument) return;
            PlaybackSnapshot candidate = null; TrackerPreparedSong next = null;
            try
            {
                candidate = midiPreviewSnapshot.CreateRefresh(changed);
                next = TrackerPreparedSong.Prepare(candidate.Data, AudioSettings.outputSampleRate);
                if (midiPreviewHost.RefreshPrepared(next, out _)) { next = null; var old = midiPreviewSnapshot; midiPreviewSnapshot = candidate; candidate = null; old.Dispose(); }
                else DisposeMidiPreview();
            }
            catch { DisposeMidiPreview(); }
            finally { candidate?.Dispose(); next?.Dispose(); }
        }

        // The preview channel exists only while it is used: it closes two seconds after the Instrument workspace (or the
        // instrument it was made for) was left and its notes have rung out, or at once when MIDI is turned off.
        void ReleaseIdleMidiPreview()
        {
            if (midiPreviewHost == null) return;
            if (pane == 3 && instrument == midiPreviewInstrument) { midiPreviewIdleSince = -1; return; }
            if (midiPreviewHost.LiveInput != null && midiPreviewHost.LiveInput.HeldNotes > 0) { midiPreviewIdleSince = -1; return; }
            double t = EditorApplication.timeSinceStartup;
            if (midiPreviewIdleSince < 0) midiPreviewIdleSince = t;
            else if (t - midiPreviewIdleSince > 2) DisposeMidiPreview();
        }

        void DisposeMidiPreview()
        {
            if (midiPreviewObject != null) DestroyImmediate(midiPreviewObject);
            midiPreviewObject = null; midiPreviewHost = null; midiPreviewInstrument = null; midiPreviewIdleSince = -1;
            midiPreviewSnapshot?.Dispose(); midiPreviewSnapshot = null;
        }

        // The project's own buffer size as saved on disk (the running value changes with AudioSettings.Reset).
        static int ProjectDspBuffer()
        {
            try
            {
                foreach (var line in System.IO.File.ReadLines("ProjectSettings/AudioManager.asset"))
                {
                    var t = line.Trim();
                    if (t.StartsWith("m_DSPBufferSize:") && int.TryParse(t.Substring(16).Trim(), out int size)) return size > 0 ? size : 1024;
                }
            }
            catch (System.IO.IOException) { }
            return 1024;
        }

        static bool midiBufferChanged, midiBufferHooked;

        // The selected MIDI buffer is persisted separately; Unity's project buffer is restored on shutdown/reload and
        // the selected value is applied again when the window rebuilds.
        void ApplyMidiBuffer()
        {
            int want = midiBuffer == 0 ? ProjectDspBuffer() : midiBufferSizes[midiBuffer];
            bool changed = AudioSettings.GetConfiguration().dspBufferSize != want;
            if (changed && !SetDspBuffer(want)) midiProblem = "Audio buffer " + want + " was refused";
            midiBufferChanged = midiBuffer != 0 && want != ProjectDspBuffer();
            if (midiBuffer != 0 && !midiBufferHooked) { midiBufferHooked = true; AssemblyReloadEvents.beforeAssemblyReload += RestoreProjectBuffer; EditorApplication.quitting += RestoreProjectBuffer; }
            if (!changed) return;
            // A script-made audio restart silences existing audio sources without telling them; start fresh renderers
            // (the pump makes new ones at once).
            StopPreview(); DisposeMidiPreview();
            playing = false; RefreshTransport();
        }

        static bool SetDspBuffer(int size)
        {
            var config = AudioSettings.GetConfiguration();
            if (config.dspBufferSize == size) return true;
            config.dspBufferSize = size;
            return AudioSettings.Reset(config);
        }

        static void RestoreProjectBuffer()
        {
            if (!midiBufferChanged) return;
            midiBufferChanged = false;
            if (AudioSettings.GetConfiguration().dspBufferSize == ProjectDspBuffer()) return;
            SetDspBuffer(ProjectDspBuffer());
            foreach (var window in Resources.FindObjectsOfTypeAll<ZTrackerModelWindow>()) { window.StopPreview(); window.DisposeMidiPreview(); }
        }

        void MidiPanic()
        {
            midiRouter.Panic();
            host?.LiveInput?.Write(TrackerLiveEvent.AllSoundOff());
            midiPreviewHost?.LiveInput?.Write(TrackerLiveEvent.AllSoundOff());
        }

        void MidiShutdown()
        {
            if (!midiOn && !midiStarted) { DisposeMidiPreview(); return; }
            RestoreProjectBuffer();
            midiRouter.Panic();
            midiRouter.SetRoute(null, 0, 0, 0);
            midiWindows?.Close(); MidiSimulated.Close();
            DisposeMidiPreview();
            midiOn = false; midiStarted = false; midiProblem = null; midiRouter.Capturing = false; recordHeld.Clear();
            if (midiRecordButton != null) midiRecordButton.style.visibility = UnityEngine.UIElements.Visibility.Hidden;
            midiToggleButton?.SetValueWithoutNotify(false);
        }

        void RefreshMidiStatus()
        {
            if (midiStatus == null) return;
            string text, tip;
            if (!midiOn) { text = "Off"; tip = "MIDI input is off. Turn MIDI on to play from a keyboard; right-click for the device and keyboard settings."; }
            else if (midiProblem != null) { text = "⚠ " + midiProblem; tip = midiProblem.Contains("In use") ? midiProblem + ". Another program (a DAW or another editor) holds this device; close it there and MIDI reconnects on its own." : midiProblem + ". Plug in or choose a device (right-click); the list refreshes on its own."; }
            else
            {
                var device = OpenMidiDevice;
                text = (midiRouter.Capturing ? "● " : "") + midiTargetText + "  ·  " + (device ?? "opening…");
                AudioSettings.GetDSPBufferSize(out int buffer, out int buffers); int rate = Math.Max(1, AudioSettings.outputSampleRate);
                tip = $"Listening to {device ?? "no device yet"} on {(midiRouter.Channel < 0 ? "every channel" : "channel " + (midiRouter.Channel + 1))}. Keys play {midiTargetText.TrimStart('→', ' ')}." +
                      (midiRouter.LastNote >= 0 ? $" Last key: {NoteName(midiRouter.LastNote)} velocity {midiRouter.LastVelocity}." : "") + $" Audio buffer {buffer} samples ({buffer * 1000.0 / rate:F1} ms, {buffers} queued). Right-click for settings.";
            }
            if (midiStatus.text != text) midiStatus.text = text;
            midiStatus.tooltip = tip;
        }

        // Keys are captured for recording only while Rec is on and they go to the song's renderer (not the clean preview).
        void UpdateMidiCapture() { var r = midiRouter.Current; midiRouter.Capturing = midiOn && midiRecord && r != null && host != null && r.target == host.LiveInput; }

        // Writes one recorded key into the pattern. Playing: at the row heard when the key went down, rounded to the
        // nearest row, in the pattern of the sequence slot heard then. Stopped: at the cursor, stepping on once every key is up.
        void RecordKey(ZTrackerMidiRouter.Captured key, ZTrackerMidiRouter.Route route)
        {
            if (!key.on) { recordHeld.Remove(key.note); if (!playing && recordHeld.Count == 0 && pane == 0 && Pattern != null) Advance(); return; }
            if (Data == null || route == null || route.track < 0 || route.track >= Data.tracks.Count) return;
            var t = Data.tracks[route.track]; int count = Math.Max(1, t.visibleNoteColumns);
            int column = (route.column + recordHeld.Count) % count;
            recordHeld.Add(key.note);
            int slot = order, line = row;
            if (playing && host != null)
            {
                midiClock ??= new ZTrackerSongClock(() => playing ? host : null, () => playbackSnapshot?.Data);
                double pressed = Time.realtimeSinceStartupAsDouble - (System.Diagnostics.Stopwatch.GetTimestamp() - key.stamp) / (double)System.Diagnostics.Stopwatch.Frequency;
                if (!midiClock.TryGetHeardAt(pressed, out var heard)) return;
                slot = heard.Order; line = heard.Row;
                var p = slot >= 0 && slot < Data.sequence.Count ? Data.patterns.Find(x => x.id == Data.sequence[slot].patternId) : null;
                if (p == null) return;
                if (heard.RowFraction >= .5 && line + 1 < p.lineCount) line++;
            }
            var pattern = slot >= 0 && slot < Data.sequence.Count ? Data.patterns.Find(x => x.id == Data.sequence[slot].patternId) : null;
            if (pattern == null || line < 0 || line >= pattern.lineCount) return;
            int instrumentIndex = route.instrument, pitch = key.note, volume = Mathf.Clamp(Mathf.RoundToInt(key.gain * 128), 0, 128); string trackId = t.id;
            SongEdit("record note", () =>
            {
                var pt = pattern.tracks.Find(x => x.trackId == trackId); if (pt == null) { pt = new PatternTrack { trackId = trackId }; pattern.tracks.Add(pt); }
                var pl = Clone(pt.ReadLine(line)); pl.notes.RemoveAll(x => x.column == column);
                pl.notes.Add(new NoteCell { column = column, note = NoteKind.Note, pitch = pitch, instrumentPresent = true, instrument = instrumentIndex, volume = new ColumnValue { kind = ValueKind.Value, value = volume } });
                pt.WriteLine(pl);
            });
        }

        static string MidiInstrumentName(ZTrackerInstrument value) { var t = InstrumentTitle(value); return string.IsNullOrEmpty(t) && value != null ? value.name : t; }
        internal void EnableMidiPreferenceRestore()
        {
            midiPreferenceRestoreEnabled = true;
            midiPersistPreferences = !midiSimulatedOnly;
            if (midiToggleButton == null) return;
            LoadMidiPrefs();
            midiToggleButton.SetValueWithoutNotify(midiOn);
            if (midiRecordButton != null)
            {
                midiRecordButton.SetValueWithoutNotify(midiRecord);
                midiRecordButton.style.visibility = midiOn ? UnityEngine.UIElements.Visibility.Visible : UnityEngine.UIElements.Visibility.Hidden;
            }
            if (midiOn) SetMidiEnabled(true);
            else RefreshMidiStatus();
        }
        void SaveMidiString(string key, string value) { if (midiPersistPreferences) EditorPrefs.SetString(MidiPrefs + key, value ?? ""); }
        void SaveMidiInt(string key, int value) { if (midiPersistPreferences) EditorPrefs.SetInt(MidiPrefs + key, value); }
        void SaveMidiFloat(string key, float value) { if (midiPersistPreferences) EditorPrefs.SetFloat(MidiPrefs + key, value); }
        void SaveMidiBool(string key, bool value) { if (midiPersistPreferences) EditorPrefs.SetBool(MidiPrefs + key, value); }
        static string NoteName(int note) => note < 0 ? "---" : noteNames[note % 12] + (note / 12);
    }

    internal struct MidiPreferenceSnapshot
    {
        internal bool enabled, record;
        internal string device;
        internal int channel, octave, curve, modWheel, buffer;
        internal float bend;

        internal static MidiPreferenceSnapshot Read(string prefix, Func<string, string, string> getString, Func<string, int, int> getInt, Func<string, float, float> getFloat, Func<string, bool, bool> getBool)
            => new MidiPreferenceSnapshot
            {
                enabled = getBool(prefix + "Enabled", false), record = getBool(prefix + "Record", false),
                device = getString(prefix + "Device", ""), channel = getInt(prefix + "Channel", -1),
                octave = getInt(prefix + "Octave", 0), curve = getInt(prefix + "Curve", 0),
                bend = getFloat(prefix + "Bend", 2), modWheel = getInt(prefix + "ModWheel", 0),
                buffer = getInt(prefix + "Buffer", 0)
            };

        internal static void Write(string prefix, MidiPreferenceSnapshot value, Action<string, string> setString, Action<string, int> setInt, Action<string, float> setFloat, Action<string, bool> setBool)
        {
            setBool(prefix + "Enabled", value.enabled); setBool(prefix + "Record", value.record);
            setString(prefix + "Device", value.device ?? ""); setInt(prefix + "Channel", value.channel);
            setInt(prefix + "Octave", value.octave); setInt(prefix + "Curve", value.curve);
            setFloat(prefix + "Bend", value.bend); setInt(prefix + "ModWheel", value.modWheel);
            setInt(prefix + "Buffer", value.buffer);
        }

        internal static string ResolveDevice(string preferred, IList<string> available, string simulatedName)
        {
            if (!string.IsNullOrEmpty(preferred)) return available.Contains(preferred) ? preferred : null;
            foreach (var name in available) if (name != simulatedName) return name;
            return null;
        }
    }
}

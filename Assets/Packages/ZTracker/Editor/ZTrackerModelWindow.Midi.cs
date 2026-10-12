using System;
using System.Collections.Generic;
using Laubrary.ZTracker.Engine;
using Laubrary.ZTracker.Midi;
using Laubrary.ZTracker.Model;
using Laubrary.Zui;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Laubrary.ZTracker.Editor
{
    // MIDI keyboard input. Off until the user turns it on. In the Instrument workspace the keyboard plays the edited
    // instrument on a clean preview channel of its own (no track settings, only the instrument's own effect chains),
    // which may sound over a playing song. Everywhere else it plays the selected instrument through the cursor's track
    // and note column, as a pattern note there would, spreading chords over that track's note columns.
    public sealed partial class ZTrackerModelWindow
    {
        const string MidiPrefs = "Laubrary.ZTracker.Midi.";
        static readonly ZTrackerMidiRouter midiRouter = new ZTrackerMidiRouter();
        ZTrackerMidiWindows midiWindows;
        bool midiOn;
        string midiDevice, midiProblem, midiTargetText = "";
        double midiNextScan;
        ZuiToggleButton midiToggleButton;
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
            midiStatus = Z.Text("", tooltip: ""); midiStatus.name = "midi-status";
            midiStatus.style.width = 250; midiStatus.style.height = 24; midiStatus.style.whiteSpace = WhiteSpace.NoWrap; midiStatus.style.overflow = Overflow.Hidden; midiStatus.style.textOverflow = TextOverflow.Ellipsis; midiStatus.style.unityTextAlign = TextAnchor.MiddleLeft; midiStatus.style.marginLeft = 4;
            midiStatus.RegisterCallback<PointerDownEvent>(e => { if (e.button == 1) { ShowMidiMenu(midiStatus); e.StopPropagation(); } });
            cluster.Add(midiStatus);
            var panic = Named(Z.Button("Panic", "Silence every note played from the MIDI keyboard at once (the song keeps playing).", MidiPanic), "midi-panic");
            panic.style.flexShrink = 0; panic.style.flexGrow = 0; cluster.Add(panic);
            RefreshMidiStatus();
            return cluster;
        }

        void LoadMidiPrefs()
        {
            midiDevice = EditorPrefs.GetString(MidiPrefs + "Device", "");
            midiRouter.Channel = EditorPrefs.GetInt(MidiPrefs + "Channel", -1);
            midiRouter.OctaveShift = EditorPrefs.GetInt(MidiPrefs + "Octave", 0);
            midiRouter.VelocityCurve = (ZTrackerVelocityCurve)EditorPrefs.GetInt(MidiPrefs + "Curve", 0);
            midiRouter.BendRange = EditorPrefs.GetFloat(MidiPrefs + "Bend", 2);
            midiRouter.ModWheelMacro = EditorPrefs.GetInt(MidiPrefs + "ModWheel", 0);
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
            if (midiWindows == null) midiWindows = new ZTrackerMidiWindows(); else midiWindows.RefreshDevices();
            var devices = MidiDevices();
            var menu = Z.Menu(anchor).Width(300).Section("Device", "MIDI inputs Windows reports. A device held by another program (a DAW) cannot be opened until that program releases it.");
            if (devices.Count == 0) menu.Item("No MIDI input found", "Plug in a MIDI keyboard; the list refreshes on its own.", null, enabled: false);
            else menu.Radio("", devices.ToArray(), Math.Max(-1, devices.IndexOf(CurrentMidiDevice(devices))), "The MIDI input to listen to. Simulated keyboard is driven by checks only.", i => { midiDevice = devices[i]; EditorPrefs.SetString(MidiPrefs + "Device", midiDevice); ReopenMidi(); });
            menu.Item("Refresh devices", "Read the device list again now (it also refreshes on its own every two seconds).", () => { midiWindows.RefreshDevices(); ReopenMidi(); });
            menu.Section("Keyboard");
            var channels = new string[17]; channels[0] = "Omni"; for (int i = 1; i <= 16; i++) channels[i] = i.ToString();
            menu.Radio("Channel", channels, midiRouter.Channel + 1, "Listen to every MIDI channel (Omni) or only one.", i => { midiRouter.Channel = i - 1; EditorPrefs.SetInt(MidiPrefs + "Channel", i - 1); });
            menu.Custom((body, close) => body.Add(Z.MicroSlider("Octave", midiRouter.OctaveShift, -4, 4, "Shift every played key by whole octaves.", v => { midiRouter.OctaveShift = Mathf.RoundToInt(v); EditorPrefs.SetInt(MidiPrefs + "Octave", midiRouter.OctaveShift); }, 200, defaultValue: 0, decimals: 0)));
            menu.Radio("Velocity", new[] { "Linear", "Soft", "Hard" }, (int)midiRouter.VelocityCurve, "How key velocity becomes loudness: Linear follows the key; Soft makes gentle playing louder; Hard needs a firmer touch.", i => { midiRouter.VelocityCurve = (ZTrackerVelocityCurve)i; EditorPrefs.SetInt(MidiPrefs + "Curve", i); });
            menu.Section("Controllers", "Sustain pedal holds notes; All Notes Off (controller 123) releases them. Knobs sending controllers 21 to 28 drive macros 1 to 8 of the instrument being played. Program changes are ignored.");
            menu.Custom((body, close) => body.Add(Z.MicroSlider("Bend range", midiRouter.BendRange, 0, 12, "How far the pitch wheel bends live notes, in semitones each way.", v => { midiRouter.BendRange = Mathf.Round(v); EditorPrefs.SetFloat(MidiPrefs + "Bend", midiRouter.BendRange); }, 200, defaultValue: 2, decimals: 0)));
            menu.Radio("Mod wheel", new[] { "1", "2", "3", "4", "5", "6", "7", "8" }, midiRouter.ModWheelMacro, "The macro of the played instrument that the mod wheel moves.", i => { midiRouter.ModWheelMacro = i; EditorPrefs.SetInt(MidiPrefs + "ModWheel", i); });
            midiMenu = menu.Show();
        }

        string CurrentMidiDevice(List<string> devices)
        {
            if (!string.IsNullOrEmpty(midiDevice) && devices.Contains(midiDevice)) return midiDevice;
            foreach (var d in devices) if (d != ZTrackerMidiSimulated.Device) return d;
            return null;
        }

        void SetMidiEnabled(bool on)
        {
            midiOn = on;
            if (on) { if (midiWindows == null) midiWindows = new ZTrackerMidiWindows(); midiNextScan = 0; ReopenMidi(); MidiPump(); }
            else MidiShutdown();
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
        }

        string OpenMidiDevice => MidiSimulated.IsOpen ? ZTrackerMidiSimulated.Device : midiWindows != null && midiWindows.IsOpen ? midiWindows.OpenDevice : null;

        // Main thread, every pump: keep the device open, follow hot-plug, and point new notes at the current target.
        void MidiPump()
        {
            if (!midiOn) { ReleaseIdleMidiPreview(true); return; }
            double now = EditorApplication.timeSinceStartup;
            if (now >= midiNextScan)
            {
                midiNextScan = now + 2;
                if (midiWindows != null && midiWindows.RefreshDevices() && midiWindows.IsOpen && !MidiDevices().Contains(midiWindows.OpenDevice)) { midiWindows.Close(); }
                MidiSimulated.RefreshDevices();
                if (OpenMidiDevice == null) ReopenMidi();
            }
            RouteMidi();
            ReleaseIdleMidiPreview(false);
            RefreshMidiStatus();
        }

        void RouteMidi()
        {
            try
            {
                if (pane == 3 && instrument != null)
                {
                    EnsureMidiPreview();
                    midiRouter.SetRoute(midiPreviewHost.LiveInput, 0, 0, 0);
                    midiTargetText = "→ clean preview · " + InstrumentTitle(instrument);
                    return;
                }
                if (Data == null) { midiRouter.SetRoute(null, 0, 0, 0); midiTargetText = "→ no song"; return; }
                if (Data.instruments.Count == 0) { midiRouter.SetRoute(null, 0, 0, 0); midiTargetText = "→ no instrument in the song"; return; }
                int t = FirstSequencer();
                if (t < 0) { midiRouter.SetRoute(null, 0, 0, 0); midiTargetText = "→ no note track"; return; }
                int column = 0;
                if (pane == 0 && sub >= 0 && sub < fields.Count && fields[sub].track == t && fields[sub].kind <= FieldKind.LocalValue) column = fields[sub].column;
                column = Mathf.Clamp(column, 0, Math.Max(0, Data.tracks[t].visibleNoteColumns - 1));
                int ins = Mathf.Clamp(entryInstrument, 0, Data.instruments.Count - 1);
                if (host == null) EnsurePreview();
                midiRouter.SetRoute(host.LiveInput, t, column, ins);
                midiTargetText = $"→ {Data.tracks[t].name} col {column + 1} · {ins:X2} {InstrumentTitle(Data.instruments[ins])}";
            }
            catch (Exception ex) { midiRouter.SetRoute(null, 0, 0, 0); midiTargetText = "→ unavailable: " + ex.Message; }
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

        // The preview channel exists only while it is used: it closes two seconds after the keyboard moved elsewhere
        // and its notes have rung out, or at once when MIDI is turned off.
        void ReleaseIdleMidiPreview(bool now)
        {
            if (midiPreviewHost == null) return;
            if (now) { DisposeMidiPreview(); return; }
            var r = midiRouter.Current;
            if (r != null && r.target == midiPreviewHost.LiveInput) { midiPreviewIdleSince = -1; return; }
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

        void MidiPanic()
        {
            midiRouter.Panic();
            host?.LiveInput?.Write(TrackerLiveEvent.AllSoundOff());
            midiPreviewHost?.LiveInput?.Write(TrackerLiveEvent.AllSoundOff());
        }

        void MidiShutdown()
        {
            midiRouter.Panic();
            midiRouter.SetRoute(null, 0, 0, 0);
            midiWindows?.Close(); MidiSimulated.Close();
            DisposeMidiPreview();
            midiOn = false; midiProblem = null;
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
                text = (device ?? "Opening…") + " " + midiTargetText;
                tip = $"Listening to {device ?? "no device yet"} on {(midiRouter.Channel < 0 ? "every channel" : "channel " + (midiRouter.Channel + 1))}. Keys play {midiTargetText.TrimStart('→', ' ')}." +
                      (midiRouter.LastNote >= 0 ? $" Last key: {NoteName(midiRouter.LastNote)} velocity {midiRouter.LastVelocity}." : "") + " Right-click for settings.";
            }
            if (midiStatus.text != text) midiStatus.text = text;
            midiStatus.tooltip = tip;
        }

        static string NoteName(int note) => note < 0 ? "---" : noteNames[note % 12] + (note / 12);
    }
}

using System;
using Laubrary.Zui;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Laubrary.ZTracker.Editor
{
    public sealed class ZTrackerWindow : ZuiWindow
    {
        [SerializeField] ZTrackerSong song;
        [SerializeField] ZTrackerInstrument instrument;
        [SerializeField] int note = 69;
        ZTrackerPlayback playback;
        Label status;
        Button play, audition, stop;
        string lastError;

        [MenuItem("Laubrary/ZTracker")]
        public static void Open() => GetWindow<ZTrackerWindow>("ZTracker");

        protected override void BuildUI(VisualElement root)
        {
            minSize = new Vector2(450, 180);
            var songPicker = Z.Object(song, "Choose an existing song. Selecting it does not start audio.", v =>
                { StopPreview(); song = v; Rebuild(); }, 220);
            var newSong = Sized(Z.Button("New song", "Create a saved synth song with four notes in Assets/ZTracker.", CreateSong), 90);
            var undo = Sized(Z.Button("↶", "Undo the last asset edit.", Undo.PerformUndo), 28);
            var redo = Sized(Z.Button("↷", "Redo an asset edit.", Undo.PerformRedo), 28);
            root.Add(Z.Row(songPicker, newSong, undo, redo));

            var tempo = Z.MicroSlider("Tempo", song != null ? song.bpm : 120, 32, 300,
                "Song beats per minute. Changes are heard on the next Play.", v => EditSong(() => song.bpm = Mathf.RoundToInt(v)), 150, decimals: 0);
            tempo.SetEnabled(song != null);
            var gain = Z.MicroSlider("Gain", song != null && song.channels.Count > 0 ? song.channels[0].volume : 1, 0, 1,
                "First channel gain. Zero silences its notes on the next Play.", v => EditSong(() => song.channels[0].volume = v), 150);
            var pan = Z.MicroSlider("Pan", song != null && song.channels.Count > 0 ? song.channels[0].pan : 0, -1, 1,
                "First channel balance. Left is -1; right is 1. Heard on the next Play.", v => EditSong(() => song.channels[0].pan = v), 150);
            gain.SetEnabled(song != null && song.channels.Count > 0);
            pan.SetEnabled(song != null && song.channels.Count > 0);
            root.Add(Z.Row(tempo, gain, pan));

            var instPicker = Z.Object(instrument, "Choose an instrument to audition without playing a song.", v =>
                { StopPreview(); instrument = v; RefreshTransport(); }, 220);
            var pitch = Z.MicroSlider("Note", note, 0, 126, "MIDI pitch used by Audition. 69 is A4.", v => note = Mathf.RoundToInt(v), 150, decimals: 0);
            root.Add(Z.Row(instPicker, pitch));

            play = Sized(Z.Button("Play", "Play the selected song. Uses the optional managed tracker audio host.", () =>
            {
                StopPreview();
                ZTrackerPlayback.TryPlay(song, out playback, out lastError);
                RefreshTransport();
            }), 65);
            audition = Sized(Z.Button("Audition", "Play the selected instrument at the chosen pitch until Stop.", () =>
            {
                StopPreview();
                ZTrackerPlayback.TryAudition(instrument, note, out playback, out lastError);
                RefreshTransport();
            }), 80);
            stop = Sized(Z.Button("Stop", "Stop this window's preview and release its audio engine.", StopPreview), 65);
            status = Z.Text("Idle", tooltip: ZTrackerCapability.Description);
            status.style.width = 160;
            status.style.height = 24;
            status.style.whiteSpace = WhiteSpace.NoWrap;
            status.style.overflow = Overflow.Hidden;
            root.Add(Z.Row(play, audition, stop, status));
            root.schedule.Execute(RefreshTransport).Every(100);
            RefreshTransport();
        }

        static Button Sized(Button button, float width) { button.style.width = width; return button; }

        void EditSong(Action edit)
        {
            if (song == null) return;
            Undo.RecordObject(song, "Edit tracker song");
            edit();
            EditorUtility.SetDirty(song);
        }

        void CreateSong()
        {
            StopPreview();
            if (!AssetDatabase.IsValidFolder("Assets/ZTracker")) AssetDatabase.CreateFolder("Assets", "ZTracker");
            Undo.IncrementCurrentGroup();
            int group = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Create tracker song");
            instrument = CreateInstance<ZTrackerInstrument>();
            instrument.type = InstrumentType.Synth;
            instrument.waveA = 0;
            instrument.volume = 0.25f;
            AssetDatabase.CreateAsset(instrument, AssetDatabase.GenerateUniqueAssetPath("Assets/ZTracker/Synth.asset"));
            Undo.RegisterCreatedObjectUndo(instrument, "Create tracker instrument");
            song = CreateInstance<ZTrackerSong>();
            song.channelCount = 1;
            song.instruments.Add(instrument);
            song.patterns.Add(new ZTrackerPattern("Pattern 0", 4, 1));
            int[] notes = { 60, 64, 67, 72 };
            for (int r = 0; r < notes.Length; r++)
                song.patterns[0].SetCell(r, 0, 1, new ZTrackerCellSerialized { note = notes[r], instrument = 0 });
            song.EnsureDefaults();
            AssetDatabase.CreateAsset(song, AssetDatabase.GenerateUniqueAssetPath("Assets/ZTracker/Song.asset"));
            Undo.RegisterCreatedObjectUndo(song, "Create tracker song");
            Undo.CollapseUndoOperations(group);
            Undo.IncrementCurrentGroup();
            AssetDatabase.SaveAssets();
            Selection.activeObject = song;
            Rebuild();
        }

        void RefreshTransport()
        {
            bool active = playback != null && playback.IsPlaying;
            bool available = ZTrackerCapability.IsSupportedPlatform;
            play?.SetEnabled(song != null && available);
            audition?.SetEnabled(instrument != null && available);
            stop?.SetEnabled(active);
            if (status == null) return;
            status.text = lastError != null ? "Unavailable" : active ? "Playing" : available ? "Idle" : "Windows x64 only";
            status.tooltip = lastError ?? ZTrackerCapability.Description;
        }

        void StopPreview()
        {
            if (playback != null) playback.Stop();
            playback = null;
            lastError = null;
            RefreshTransport();
        }

        protected override void OnDisable() { StopPreview(); base.OnDisable(); }
    }
}

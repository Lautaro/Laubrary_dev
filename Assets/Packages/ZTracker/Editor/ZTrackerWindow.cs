using System;
using System.Collections.Generic;
using System.Linq;
using Laubrary.Zui;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Laubrary.ZTracker.Editor
{
    public sealed partial class ZTrackerWindow : ZuiWindow
    {
        [SerializeField] ZTrackerSong song;
        [SerializeField] ZTrackerInstrument instrument;
        [SerializeField] int order, row, track, sub, octave = 5, step = 1, pane, preset = -1, entryInstrument;
        int anchorRow = -1, anchorTrack;
        string pending = "", lastError;
        ZTrackerPlayback playback;
        Label status;
        VisualElement controls, stage, cellEditor;
        ScrollView grid;
        readonly Dictionary<string, Label> cellLabels = new Dictionary<string, Label>();
        List<ZTrackerCellSerialized> clipboard;
        int clipboardWidth;
        bool follow = true;
        int refreshVersion;
        bool refreshPending, refreshSong;
        int refreshPitch, refreshPreset, refreshOrder, refreshRow;
        int followedRow = -1;
        ZTrackerPattern Pattern => song != null && song.orderList.Count > 0 && song.patterns.Count > 0 ? song.patterns[Mathf.Clamp(song.orderList[Mathf.Clamp(order, 0, song.orderList.Count - 1)], 0, song.patterns.Count - 1)] : null;
        [MenuItem("Laubrary/ZTracker")]
        public static void Open() => GetWindow<ZTrackerWindow>("ZTracker");
        protected override void BuildUI(VisualElement root)
        {
            minSize = new Vector2(760, 440); cellLabels.Clear();
            var sheetPath = AssetDatabase.FindAssets("ZTracker t:StyleSheet").Select(AssetDatabase.GUIDToAssetPath).FirstOrDefault(p => p.EndsWith("/ZTracker.uss"));
            var sheet = sheetPath == null ? null : AssetDatabase.LoadAssetAtPath<StyleSheet>(sheetPath);
            if (sheet != null && !root.styleSheets.Contains(sheet)) root.styleSheets.Add(sheet);
            root.Add(Flow(Named(Z.Object(song, "Choose a saved song without starting audio.", v => { StopPreview(); song = v; order = row = track = sub = 0; Rebuild(); }, 210), "song"),
                Button("New song", "Create a saved empty song with four tracks.", CreateSong, "new-song"), Button("Save", "Save this song and instrument without flushing other assets.", Save, "save"),
                Z.IconButton("arrow-counter-clockwise", "Undo the last asset edit.", Undo.PerformUndo), Z.IconButton("arrow-clockwise", "Redo an asset edit.", Undo.PerformRedo)));
            status = Z.Text("Idle", tooltip: "Preview position or reason playback is unavailable."); status.name = "transport-status"; status.style.width = 180; status.style.height = 24; status.style.whiteSpace = WhiteSpace.NoWrap; status.style.overflow = Overflow.Hidden;
            var play = Button("Play", "Play from the beginning; Space toggles playback in the pattern.", () => Play(false), "play"); play.SetEnabled(song != null);
            var cursor = Button("From cursor", "Play from the current order and row; Ctrl+Space in the pattern.", () => Play(true), "play-cursor"); cursor.SetEnabled(Pattern != null);
            root.Add(Flow(play, cursor, Button("Stop", "Stop this window's preview. Escape also stops all its notes.", StopPreview, "stop"), Z.Toggle("Follow", "Keep the playing row in view.", follow, v => follow = v), status));
            var left = Z.Column(); left.style.minWidth = 300;
            left.Add(Z.Segmented(pane, new[] { "Song", "Instrument" }, "Choose the settings beside the pattern workspace.", v => { pane = v; RebuildControls(); }));
            var scroll = new ScrollView(); scroll.style.flexGrow = 1; scroll.style.minHeight = 0; controls = Z.Column(); scroll.Add(controls); left.Add(scroll);
            stage = Z.Column(); stage.style.flexGrow = 1; stage.style.minWidth = 0; stage.style.minHeight = 0;
            root.Add(Z.Split("ztracker.authoring", 350, left, stage)); RebuildControls(); BuildPattern(); root.schedule.Execute(RefreshTransport).Every(80);
        }
        static T Named<T>(T c, string name) where T : VisualElement { c.name = name; return c; }
        static Button Button(string label, string tip, Action action, string name = null) { var b = Z.Button(label, tip, action); b.name = name ?? label; b.style.width = Mathf.Max(40,label.Length*7+24); b.style.alignSelf = Align.FlexStart; return b; }
        static VisualElement Flow(params VisualElement[] c) { var r = Z.Row(c); r.style.flexWrap = Wrap.Wrap; return r; }
        void Change(UnityEngine.Object owner, string label, Action edit, bool rebuild = false, bool liveScalar = false)
        {
            if (owner == null) return;
            if(owner==instrument && playback!=null && playback.IsPlaying && liveScalar)
            {
                Undo.RecordObject(owner,"Tracker: "+label);edit();EditorUtility.SetDirty(owner);
                playback.RefreshInstrument(instrument,preset);
                if(rebuild){RebuildControls();BuildPattern();}else RefreshCells();
                return;
            }
            bool active = playback != null && playback.IsPlaying, wasSong = active ? playback.IsSong : refreshSong;
            bool resume = active || refreshPending;
            int pitch = active ? playback.AuditionNote : refreshPitch, variation = active ? playback.AuditionPreset : refreshPreset, startOrder = refreshPending ? refreshOrder : order, startRow = refreshPending ? refreshRow : row;
            if (active && wasSong) playback.TryGetPosition(out startOrder, out startRow);
            StopPreview(); Undo.RecordObject(owner, "Tracker: " + label); edit(); EditorUtility.SetDirty(owner);
            if (rebuild) { RebuildControls(); BuildPattern(); } else RefreshCells();
            if (resume)
            {
                refreshPending = true; refreshSong = wasSong; refreshPitch = pitch; refreshPreset = variation; refreshOrder = startOrder; refreshRow = startRow;
                int version = ++refreshVersion;
                rootVisualElement.schedule.Execute(() => {
                    if (version != refreshVersion || this == null) return;
                    refreshPending = false;
                    if (wasSong) ZTrackerPlayback.TryPlay(song, out playback, out lastError, startOrder, startRow);
                    else ZTrackerPlayback.TryAudition(instrument, pitch, out playback, out lastError, variation);
                    RefreshTransport();
                }).StartingIn(80);
            }
        }
        void BeginInstrumentGesture(string label)
        {
            bool active = playback != null && playback.IsPlaying;
            bool resume = active || refreshPending, isSong = active ? playback.IsSong : refreshSong;
            int pitch = active ? playback.AuditionNote : refreshPitch, variation = active ? playback.AuditionPreset : refreshPreset, o = refreshOrder, r = refreshRow;
            if (active && isSong) playback.TryGetPosition(out o,out r);
            StopPreview(); refreshPending = resume; refreshSong = isSong; refreshPitch = pitch; refreshPreset = variation; refreshOrder = o; refreshRow = r;
            Undo.RecordObject(instrument,"Tracker: " + label);
        }
        void EndInstrumentGesture()
        {
            EditorUtility.SetDirty(instrument);
            if (!refreshPending) return;
            int version = ++refreshVersion;
            rootVisualElement.schedule.Execute(() => { if (version != refreshVersion || this == null) return; refreshPending = false; if (refreshSong) ZTrackerPlayback.TryPlay(song,out playback,out lastError,refreshOrder,refreshRow); else ZTrackerPlayback.TryAudition(instrument,refreshPitch,out playback,out lastError,refreshPreset); RefreshTransport(); }).StartingIn(80);
        }
        void SongEdit(string label, Action edit, bool rebuild = false) => Change(song, label, edit, rebuild);
        void InstrumentEdit(string label, Action edit, bool rebuild = false, bool liveScalar = false) => Change(instrument, label, edit, rebuild, liveScalar);
        void Save() { if (song != null) AssetDatabase.SaveAssetIfDirty(song); if (instrument != null) AssetDatabase.SaveAssetIfDirty(instrument); }
        static void Folder() { if (!AssetDatabase.IsValidFolder("Assets/ZTracker")) AssetDatabase.CreateFolder("Assets", "ZTracker"); }
        void CreateSong()
        {
            StopPreview(); Folder(); song = CreateInstance<ZTrackerSong>(); song.EnsureDefaults(); AssetDatabase.CreateAsset(song, AssetDatabase.GenerateUniqueAssetPath("Assets/ZTracker/Song.asset"));
            Undo.RegisterCreatedObjectUndo(song, "Tracker: create song"); AssetDatabase.SaveAssetIfDirty(song); order = row = track = sub = 0; instrument = null; entryInstrument = 0; Rebuild();
        }
        void CreateInstrument()
        {
            StopPreview(); Folder(); instrument = CreateInstance<ZTrackerInstrument>(); instrument.type = InstrumentType.Synth; instrument.waveA = 0; instrument.volume = .25f;
            AssetDatabase.CreateAsset(instrument, AssetDatabase.GenerateUniqueAssetPath("Assets/ZTracker/Instrument.asset")); Undo.RegisterCreatedObjectUndo(instrument, "Tracker: create instrument");
            if (song != null) { SongEdit("add instrument", () => song.instruments.Add(instrument)); entryInstrument = song.instruments.Count - 1; }
            AssetDatabase.SaveAssetIfDirty(instrument); preset = -1; pane = 1; Rebuild();
        }
        void Play(bool cursor) { StopPreview(); ZTrackerPlayback.TryPlay(song, out playback, out lastError, cursor ? order : 0, cursor ? row : 0); RefreshTransport(); }
        void Audition(int pitch) { StopPreview(); ZTrackerPlayback.TryAudition(instrument, pitch, out playback, out lastError, preset); RefreshTransport(); }
        void StopPreview() { refreshVersion++; refreshPending = false; if (playback != null) playback.Stop(); playback = null; lastError = null; RefreshTransport(); }
        void RefreshTransport()
        {
            if (status == null) return; bool active = playback != null && playback.IsPlaying;
            if (active && playback.TryGetPosition(out int o, out int r))
            {
                status.text = playback.IsSong ? $"Playing {o:00}:{r:000}" : "Audition";
                if (follow && Pattern != null && playback.IsSong) { if (o != order) { order = o; BuildPattern(); followedRow = -1; } foreach (var l in cellLabels.Values) l.EnableInClassList("tracker-playing", l.userData is Vector3Int p && p.x == r); if (r != followedRow && grid != null) { var target = cellLabels.Values.FirstOrDefault(l => l.userData is Vector3Int p && p.x == r); if (target != null) grid.ScrollTo(target); followedRow = r; } }
            }
            else status.text = lastError != null ? "Unavailable" : active ? "Playing" : "Idle";
            status.tooltip = lastError ?? "Blend and oscillator edits update the held voice. Structural edits safely restart preview after a short pause. " + ZTrackerCapability.Description;
        }
        protected override void OnBeforeRebuild() { StopPreview(); }
        protected override void OnDisable() { StopPreview(); base.OnDisable(); }
        void RebuildControls()
        {
            if (controls == null) return; var scroll = controls.GetFirstAncestorOfType<ScrollView>(); Vector2 offset = scroll?.scrollOffset ?? Vector2.zero; controls.Clear();
            if (pane == 1) BuildInstrument(controls); else if (song != null) BuildSongControls();
            if (scroll != null) scroll.schedule.Execute(() => scroll.scrollOffset = offset);
        }
        void BuildSongControls()
        {
            controls.Add(Flow(Z.Field("Name", "Song title.", Z.TextInput(song.songName, "Rename the song title.", v => SongEdit("song title", () => song.songName = v), 190)),
                DialSong("Tempo", song.bpm, 32, 300, "Beats per minute.", v => song.bpm = (int)v), DialSong("Lines/beat", song.linesPerBeat, 1, 32, "Rows per musical beat.", v => song.linesPerBeat = (int)v), DialSong("Ticks/row", song.ticksPerRow, 1, 32, "Ticks spent on each row.", v => song.ticksPerRow = (int)v)));
            var orders = Z.BoxKeyed("Order", "Pattern sequence; drag an order label onto another to reorder.", "tracker.order"); var flow = Flow();
            for (int i = 0; i < song.orderList.Count; i++) { int n = i; var b = Button($"{i:00} {song.patterns[song.orderList[i]].name}", "Select this order; drag onto another order to move it.", () => { order = n; row = 0; RebuildControls(); BuildPattern(); }); b.EnableInClassList("tracker-picked", i == order); Reorder(b, "order", n, (a, z) => SongEdit("reorder sequence", () => Move(song.orderList, a, z), true)); flow.Add(b); }
            orders.Add(flow); orders.Add(Flow(Button("Append", "Append the selected pattern to the sequence.", () => SongEdit("append order", () => song.orderList.Add(song.orderList[order]), true)), Button("Remove", "Remove this order, keeping its pattern.", () => { if (song.orderList.Count > 1) SongEdit("remove order", () => { song.orderList.RemoveAt(order); order = Mathf.Min(order, song.orderList.Count - 1); }, true); }))); controls.Add(orders);
            var patterns = Z.BoxKeyed("Pattern", "Create, clone, choose and resize patterns.", "tracker.pattern");
            patterns.Add(Z.MiniRadio(song.orderList[order], song.patterns.Select((p, i) => $"{i:00} {p.name}").ToArray(), "Pick the pattern used at this order.", v => SongEdit("assign pattern", () => { song.orderList[order] = v; row = 0; }, true), wrap: true));
            patterns.Add(Flow(Button("New pattern", "Append a new empty pattern.", () => SongEdit("new pattern", () => { song.patterns.Add(new ZTrackerPattern("Pattern " + song.patterns.Count, 64, song.channelCount)); song.orderList.Add(song.patterns.Count - 1); order = song.orderList.Count - 1; row = 0; }, true)), Button("Clone", "Append a deep copy of this pattern.", () => SongEdit("clone pattern", () => { var p = Clone(Pattern); p.name += " copy"; song.patterns.Add(p); song.orderList.Add(song.patterns.Count - 1); order = song.orderList.Count - 1; }, true))));
            if (Pattern != null) { patterns.Add(Z.Field("Name", "Current pattern name.", Z.TextInput(Pattern.name, "Rename this pattern.", v => SongEdit("pattern name", () => Pattern.name = v), 190))); patterns.Add(DialSong("Rows", Pattern.rowCount, 1, 256, "Resize the pattern. Undo restores cropped rows.", v => Pattern.Resize((int)v, song.channelCount, song.channelCount), true)); } controls.Add(patterns);
            var inst = Z.BoxKeyed("Instruments", "Pick an instrument for new notes or edit its sound.", "tracker.instruments");
            inst.Add(Flow(Button("New instrument", "Create and add a synth instrument.", CreateInstrument, "new-instrument"), Named(Z.Object<ZTrackerInstrument>(null, "Add an existing instrument asset.", v => { if (v != null) SongEdit("add instrument", () => song.instruments.Add(v), true); }, 185), "add-instrument")));
            inst.Add(Z.MiniRadio(entryInstrument, song.instruments.Select((v, i) => $"{i:X2} {(v != null ? v.name : "Missing")}").ToArray(), "Pick the instrument used by note entry.", v => { entryInstrument = v; instrument = song.instruments[v]; preset = -1; }, wrap: true));
            inst.Add(Button("Edit instrument", "Show the selected instrument's sound, envelopes, presets and macros.", () => { if (song.instruments.Count > 0) instrument = song.instruments[Mathf.Clamp(entryInstrument, 0, song.instruments.Count - 1)]; pane = 1; Rebuild(); })); controls.Add(inst);
            var tracks = Z.BoxKeyed("Tracks", "Configure note and effect columns and mix each track.", "tracker.tracks");
            tracks.Add(Flow(Button("Add track", "Add an empty track to every pattern.", () => { if (song.GetTotalNativeChannels() < 32) SongEdit("add track", () => { foreach (var p in song.patterns) p.Resize(p.rowCount, song.channelCount + 1, song.channelCount); song.channelCount++; song.channels.Add(new ZTrackerChannelConfig()); }, true); }), Button("Duplicate track", "Copy selected track and mixer settings to a new track.", DuplicateTrack), Button("Remove track", "Remove selected track and its cells. Undo restores them.", RemoveTrack)));
            for (int t = 0; t < song.channelCount && t < song.channels.Count; t++) { var c = song.channels[t]; var card = Z.BoxKeyed("Track " + (t + 1), "Mixer and column counts for this track.", "tracker.track." + t);
                card.Add(Flow(Z.Toggle("Mute", "Silence all note columns on this track.", c.muted, v => SongEdit("mute track", () => c.muted = v)), DialSong("Gain", c.volume, 0, 1, "Gain applied to each voice on this track.", v => c.volume = v, decimals: 2), DialSong("Pan", c.pan, -1, 1, "Stereo balance; -1 left, 0 center, 1 right.", v => c.pan = v, decimals: 2)));
                card.Add(Flow(DialSong("Notes", c.noteColumnCount, 1, 12, "Visible note columns; hidden notes are kept. Maximum 32 native channels across the song.", v => c.noteColumnCount = Mathf.Min((int)v, 32 - song.GetTotalNativeChannels() + c.noteColumnCount), true), DialSong("FX", c.fxColumnCount, 1, 8, "Visible effects. Extra effects wrap over note columns; later effects sharing a channel win.", v => c.fxColumnCount = (int)v, true))); tracks.Add(card); } controls.Add(tracks);
        }
        VisualElement DialSong(string label, float value, float min, float max, string tip, Action<float> apply, bool rebuild = false, int decimals = 0) => Named(Z.MicroSlider(label, value, min, max, tip, v => SongEdit(label, () => apply(v), rebuild), 145, decimals: decimals), "song-" + label);
        static T Clone<T>(T value) => JsonUtility.FromJson<T>(JsonUtility.ToJson(value));
        static void Move<T>(IList<T> list, int from, int to) { var v = list[from]; list.RemoveAt(from); list.Insert(to, v); }
        static void Reorder(VisualElement item, string kind, int index, Action<int, int> moved)
        {
            item.RegisterCallback<PointerDownEvent>(e => { if (e.button == 0) { DragAndDrop.PrepareStartDrag(); DragAndDrop.SetGenericData("tracker." + kind, index); } });
            item.RegisterCallback<PointerMoveEvent>(e => { if ((e.pressedButtons & 1) != 0 && DragAndDrop.GetGenericData("tracker." + kind) is int) DragAndDrop.StartDrag("Reorder " + kind); });
            item.RegisterCallback<DragUpdatedEvent>(e => { if (DragAndDrop.GetGenericData("tracker." + kind) is int) { DragAndDrop.visualMode = DragAndDropVisualMode.Move; e.StopPropagation(); } });
            item.RegisterCallback<DragPerformEvent>(e => { if (DragAndDrop.GetGenericData("tracker." + kind) is int from) { DragAndDrop.AcceptDrag(); DragAndDrop.SetGenericData("tracker." + kind, null); if (from != index) moved(from, index); e.StopPropagation(); } });
        }
        void DuplicateTrack()
        {
            if (song == null || song.GetTotalNativeChannels() + song.channels[track].noteColumnCount > 32) return;
            SongEdit("duplicate track", () => { int old = song.channelCount; foreach (var p in song.patterns) { p.Resize(p.rowCount, old + 1, old); for (int r = 0; r < p.rowCount; r++) p.SetCell(r, old, old + 1, Clone(p.GetCell(r, track, old + 1))); } song.channels.Add(Clone(song.channels[track])); song.channelCount++; }, true);
        }
        void RemoveTrack()
        {
            if (song == null || song.channelCount <= 1) return;
            SongEdit("remove track", () => { foreach (var p in song.patterns) { var cells = new List<ZTrackerCellSerialized>(); for (int r = 0; r < p.rowCount; r++) for (int t = 0; t < song.channelCount; t++) if (t != track) cells.Add(p.GetCell(r, t, song.channelCount)); p.cells = cells; } song.channels.RemoveAt(track); song.channelCount--; track = Mathf.Min(track, song.channelCount - 1); sub = 0; }, true);
        }
    }
}

using System;
using System.IO;
using System.Linq;
using System.Text;
using Laubrary.AssetKit.Editor;
using Laubrary.ZTracker.Engine;
using Laubrary.ZTracker.Model;
using Laubrary.Zui;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace Laubrary.ZTracker.Editor
{
    public sealed partial class ZTrackerModelWindow
    {
        /// <summary>
        /// T-0607: ZTracker songs and instruments as LauAssets, in a disposable window on assets made in a scratch folder
        /// that is deleted afterwards. The owner's remembered last song (an editor preference) is put back as it was.
        /// </summary>
        public static string CheckLauAssets()
        {
            var report = new StringBuilder(); int passed = 0, failed = 0;
            void Need(bool ok, string why) { if (!ok) throw new InvalidOperationException(why); }
            void Check(string title, Action test) { try { test(); passed++; report.AppendLine("PASS " + title); } catch (Exception e) { failed++; report.AppendLine("FAIL " + title + ": " + e.Message); } }
            const string Folder = "Assets/ZTrackerLauAssetCheck";
            string lastKey = $"Laubrary.AssetKit.LastAsset.{typeof(ZTrackerSong).FullName}.{Application.dataPath}";
            string lastBefore = EditorPrefs.HasKey(lastKey) ? EditorPrefs.GetString(lastKey) : null;
            var demo = AssetDatabase.LoadAssetAtPath<ZTrackerSong>("Assets/Demos/ZTrackerDemo/Song.asset");
            bool demoDirty = demo != null && EditorUtility.IsDirty(demo);
            ZTrackerModelWindow window = null;
            ZTrackerSong song = null; ZTrackerInstrument synth = null, sampler = null;
            AudioClip clip = null;
            void Layout() => LayoutChain(window);
            Button ToolbarButton(string text) => window.rootVisualElement.Query<Button>().ToList().FirstOrDefault(b => b.text == text && string.IsNullOrEmpty(b.name) && b.worldBound.width > 0);
            try
            {
                Check("Songs and instruments are registered LauAsset types (open, create, picked as asset references)", () =>
                {
                    Need(LauAssetEditors.CanCreate(typeof(ZTrackerSong)) && LauAssetEditors.CanCreate(typeof(ZTrackerInstrument)), "no creator registered");
                    Need(LauAssetEditors.HasRegistration(typeof(ZTrackerSong)) && LauAssetEditors.HasRegistration(typeof(ZTrackerInstrument)), "no opener registered");
                    Need(LauAssetHook.IsAssetReference(typeof(ZTrackerSong)) && LauAssetHook.IsAssetReference(typeof(ZTrackerInstrument)), "not treated as a LauAsset reference");
                    Need(LauAssetEditors.RegisteredTypesFor(typeof(ZTrackerInstrument)).Contains(typeof(ZTrackerInstrument)), "instrument not offered by type");
                });

                Check("Create through the registry: a seeded song and a ready instrument in the chosen folder", () =>
                {
                    song = (ZTrackerSong)LauAssetEditors.Create(typeof(ZTrackerSong), "Check song", Folder);
                    synth = (ZTrackerInstrument)LauAssetEditors.Create(typeof(ZTrackerInstrument), "Check synth", Folder);
                    Need(song != null && AssetDatabase.GetAssetPath(song) == Folder + "/Check song.asset", "song path " + AssetDatabase.GetAssetPath(song));
                    Need(song.model.name == "Check song", "the new song is not titled after its file");
                    Need(song.schemaVersion == 1 && song.model.tracks.Count == 2 && song.model.patterns.Count == 1 && song.model.sequence.Count == 1, "song not seeded like New");
                    Need(synth != null && synth.schemaVersion == 1 && synth.model.family == InstrumentFamily.Synth && synth.model.name == "Check synth" && !string.IsNullOrEmpty(synth.model.id), "instrument not seeded");
                    using (var prepared = TrackerPreparedSong.Prepare(song.model)) Need(prepared != null, "new song does not prepare");
                });

                Check("Pictures: a song draws its first pattern, a sampler its waveform, a synth its envelope; drawing dirties nothing", () =>
                {
                    clip = AudioClip.Create("lauasset-check", 4800, 1, 48000, false); var raw = new float[4800]; for (int i = 0; i < raw.Length; i++) raw[i] = .8f * Mathf.Sin(i * .05f); clip.SetData(raw, 0);
                    sampler = TrackerEngineCheck.FixtureInstrument(clip);
                    song.model.instruments.Add(synth);
                    song.model.patterns[0].tracks[0].WriteLine(new PatternLine { line = 0, notes = new System.Collections.Generic.List<NoteCell> { TrackerEngineCheck.Note(60) } });
                    song.model.patterns[0].tracks[0].WriteLine(new PatternLine { line = 32, notes = new System.Collections.Generic.List<NoteCell> { TrackerEngineCheck.Note(72) } });
                    EditorUtility.SetDirty(song); AssetDatabase.SaveAssetIfDirty(song);
                    int Lit(Texture2D t, Color background) { int n = 0; foreach (var c in t.GetPixels()) if (Math.Abs(c.r - background.r) + Math.Abs(c.g - background.g) + Math.Abs(c.b - background.b) > .3f) n++; return n; }
                    var back = new Color32(0x1A, 0x27, 0x32, 0xFF);
                    foreach (var (name, preview, min) in new (string, Laubrary.PreviewKit.IVisualPreview, int)[] { ("song", song, 12), ("sampler", sampler, 40), ("synth", synth, 40) })
                    {
                        var tex = preview.RenderPreviewTexture();
                        try { Need(tex != null && tex.width == ZTrackerThumbnails.Size, name + " has no picture"); int lit = Lit(tex, back); Need(lit >= min, name + " picture nearly empty (" + lit + " drawn pixels)"); report.AppendLine("MEASURE " + name + " picture: " + lit + " drawn pixels of 4096"); }
                        finally { if (tex != null) Object.DestroyImmediate(tex); }
                    }
                    if (demo != null)
                    {
                        var tex = ((Laubrary.PreviewKit.IVisualPreview)demo).RenderPreviewTexture(); Object.DestroyImmediate(tex);
                        Need(EditorUtility.IsDirty(demo) == demoDirty, "drawing the demo song's picture dirtied it");
                    }
                });

                Check("A window with no song shows the shared browser (with the song's picture); picking opens it", () =>
                {
                    window = CreateInstance<ZTrackerModelWindow>();
                    window.titleContent = new GUIContent("ZTracker LauAsset check");
                    window.Show(); window.position = new Rect(60, 60, 1250, 820);
                    window.asset = null; window.CreateGUI(); Layout();
                    var library = window.rootVisualElement.Q(className: "lau-asset-browser__library");
                    Need(library != null, "no browser in the empty window");
                    var cell = library.Query(className: "lau-asset-browser__cell").ToList().FirstOrDefault(c => c.Q<Label>(className: "lau-asset-browser__cell-name")?.text == "Check song");
                    Need(cell != null, "the new song is not in the browser");
                    Need(cell.Q<Image>() != null, "the song's cell has no picture");
                    using (var e = PointerDownEvent.GetPooled(new Event { type = EventType.MouseDown, button = 0, clickCount = 2, mousePosition = cell.worldBound.center })) { e.target = cell; cell.SendEvent(e); }
                    Layout();
                    Need(window.song == song, "double-click did not open the song");
                    Need(window.rootVisualElement.Q("tracker-stage") != null, "the tracker workspace did not appear");
                });

                Check("The song's toolbar is the shared one (picker, New, Browse, Duplicate, Rename, Delete) on one row with Save, Undo, Redo and MIDI; Tags sit in Song", () =>
                {
                    var picker = window.rootVisualElement.Q("lau-asset-field");
                    Need(picker != null, "no asset picker");
                    foreach (var text in new[] { "New", "Browse", "Duplicate", "Rename", "Delete" }) Need(ToolbarButton(text) != null, "no " + text);
                    var save = window.rootVisualElement.Q<Button>("save");
                    Need(save != null && save.parent.parent == picker.parent, "Save is not on the asset row");
                    Need(window.rootVisualElement.Q("undo")?.parent == save.parent && window.rootVisualElement.Q("redo")?.parent == save.parent, "Undo/Redo not beside Save");
                    Need(window.rootVisualElement.Q("song-tags") == null, "Tags shown outside the Song view");
                    window.pane = 4; window.BuildPane(); Layout();
                    var tags = window.rootVisualElement.Q("song-tags");
                    Need(tags is IMGUIContainer, "no Tags in the Song view");
                    window.pane = 0; window.BuildPane();
                });

                Check("Duplicate and Rename of the song through the shared toolbar; the copy gets its own song identity", () =>
                {
                    string id = song.model.id;
                    window.ClickToolbar("Duplicate"); Layout();
                    var copy = window.song;
                    Need(copy != song && AssetDatabase.GetAssetPath(copy) == Folder + "/Check song 1.asset", "duplicate not opened: " + AssetDatabase.GetAssetPath(copy));
                    Need(copy.model.id != id && copy.model.tracks.Count == song.model.tracks.Count && copy.model.instruments.SequenceEqual(song.model.instruments), "copy not a separate song with the same content");
                    window.ClickToolbar("Rename"); Layout();
                    var field = window.rootVisualElement.Query<TextField>().ToList().FirstOrDefault(f => f.value == "Check song 1");
                    Need(field != null, "no rename field");
                    field.value = "Check song copy";
                    window.ClickToolbar("OK"); Layout();
                    Need(AssetDatabase.GetAssetPath(copy) == Folder + "/Check song copy.asset", "rename failed: " + AssetDatabase.GetAssetPath(copy));
                    Need(copy.model.name == "Check song copy", "the song's title did not follow the rename: " + copy.model.name);
                    window.SetAsset(song); Layout();
                });

                Check("Instruments: Add… is the shared browser; the instrument header has the file name, Rename, Duplicate, Select and Tags", () =>
                {
                    window.pane = 0; window.BuildPane(); Layout();
                    var add = window.rootVisualElement.Q<Button>("add-instrument");
                    Need(add != null && add.text == "Add…", "no Add… button");
                    Need(window.rootVisualElement.Q<ObjectField>("add-instrument") == null, "the old object field is still there");
                    window.AddInstrument(synth); window.AddInstrument(synth);
                    Need(song.model.instruments.Count(i => i == synth) == 1, "adding the same instrument twice put it in twice");
                    window.instrument = synth; window.pane = 3; window.BuildPane(); Layout();
                    Need(window.rootVisualElement.Q<Label>("instrument-file-name") == null, "file name repeated although it equals the instrument's name");
                    window.InstrumentEdit("check name", () => synth.model.name = "Lead"); window.BuildPane(); Layout();
                    Need(window.rootVisualElement.Q<Label>("instrument-file-name")?.text == "Check synth", "a file named differently from the instrument is not shown");
                    foreach (var name in new[] { "instrument-rename", "instrument-duplicate", "instrument-ping", "instrument-tags" }) Need(window.rootVisualElement.Q(name) != null, "missing " + name);
                    Need(window.rootVisualElement.Q("instrument-tags").parent == window.rootVisualElement.Q("instrument-name").parent, "Tags not on the instrument's header row");
                });

                Check("Instrument Rename renames the file; Duplicate makes a separate copy beside it, adds it to the song and edits it", () =>
                {
                    PointerClick(window.rootVisualElement.Q("instrument-rename")); Layout();
                    var field = window.rootVisualElement.Q<TextField>("instrument-rename-field");
                    Need(field != null && field.value == "Check synth", "no rename field");
                    field.value = "Check lead";
                    ClickButton(window.rootVisualElement.Q<Button>("instrument-rename-ok")); Layout();
                    Need(AssetDatabase.GetAssetPath(synth) == Folder + "/Check lead.asset", "rename failed: " + AssetDatabase.GetAssetPath(synth));
                    Need(synth.model.name == "Check lead" && window.rootVisualElement.Q<Label>("instrument-file-name") == null, "the instrument's name did not follow the rename: " + synth.model.name);
                    string top = Undo.GetCurrentGroupName(); Undo.PerformUndo(); Need(synth.model.name == "Lead", "Undo did not restore the display name (undid '" + top + "', name " + synth.model.name + ")"); Undo.PerformRedo(); Need(synth.model.name == "Check lead", "Redo failed"); window.BuildPane(); Layout();
                    string id = synth.model.id; int count = song.model.instruments.Count;
                    ClickButton(window.rootVisualElement.Q<Button>("instrument-duplicate")); Layout();
                    var copy = window.instrument;
                    Need(copy != synth && AssetDatabase.GetAssetPath(copy) == Folder + "/Check lead 1.asset", "duplicate not edited: " + AssetDatabase.GetAssetPath(copy));
                    Need(copy.model.id != id && song.model.instruments.Count == count + 1 && song.model.instruments.Last() == copy, "copy not separate or not in the song");
                    Need(copy.model.name == "Check lead 1", "the copy is not named after its file: " + copy.model.name);
                    using (var prepared = TrackerPreparedSong.Prepare(song.model)) Need(!prepared.diagnostics.Any(d => d.Contains("AMBIGUOUS")), "the copy is confused with the original");
                });

                Check("A drum kit's Synth source is the shared chip (only synths offered)", () =>
                {
                    var kit = CreateInstrumentAsset("Check kit", Folder);
                    kit.model.family = InstrumentFamily.DrumKit; kit.model.drumKit.mappings.Add(new DrumMapping { id = "m1", name = "Kick", noteMin = 60, noteMax = 60 });
                    window.AddInstrument(kit); window.instrument = kit; window.drumId = "m1"; window.pane = 3; window.BuildPane(); Layout();
                    var chip = window.rootVisualElement.Q("drum-synth");
                    Need(chip is ZuiChip, "drum synth source is " + (chip == null ? "missing" : chip.GetType().Name));
                    Need(((ZuiChip)chip).Accepts(synth) && !((ZuiChip)chip).Accepts(kit), "chip accepts the wrong instruments");
                });

                Check("An open window saved before the change keeps its song: the old field (read by Unity's own reload under its former name) is handed to the asset base once", () =>
                {
                    var legacy = CreateInstance<ZTrackerModelWindow>();
                    try
                    {
                        var field = typeof(ZTrackerModelWindow).GetField("legacySong", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                        var former = field.GetCustomAttributes(typeof(UnityEngine.Serialization.FormerlySerializedAsAttribute), false).Cast<UnityEngine.Serialization.FormerlySerializedAsAttribute>().FirstOrDefault();
                        Need(former != null && former.oldName == "song", "the old field name is not declared");
                        legacy.asset = null; legacy.legacySong = song;
                        legacy.OnEnable();
                        Need(legacy.song == song && legacy.legacySong == null, "the saved song was not handed over");
                    }
                    finally { DestroyImmediate(legacy); }
                });
            }
            finally
            {
                if (window != null) { window.StopPreview(); window.Close(); DestroyImmediate(window); }
                if (sampler != null) DestroyImmediate(sampler);
                if (clip != null) DestroyImmediate(clip);
                foreach (var o in new Object[] { song, synth }) if (o != null) Undo.ClearUndo(o);
                AssetDatabase.DeleteAsset(Folder);
                if (lastBefore == null) EditorPrefs.DeleteKey(lastKey); else EditorPrefs.SetString(lastKey, lastBefore);
            }
            report.Insert(0, "TOTAL passed=" + passed + " failed=" + failed + "\n");
            return report.ToString();
        }

        /// The shared asset toolbar's own buttons have no names; a check presses them by their text.
        void ClickToolbar(string text)
        {
            var button = rootVisualElement.Query<Button>().ToList().FirstOrDefault(b => b.text == text && string.IsNullOrEmpty(b.name));
            if (button == null) throw new InvalidOperationException("no toolbar button " + text);
            ClickButton(button);
        }
    }
}

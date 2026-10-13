using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using Laubrary.Audio;
using Laubrary.Zounds;
using Laubrary.ZTracker.Engine;
using Laubrary.ZTracker.Model;
using Laubrary.Zui;
using Unity.Collections;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Laubrary.ZTracker.Editor
{
    public sealed partial class ZTrackerModelWindow
    {
        /// <summary>
        /// Command model checks (T-0595): the id validation matrix, define/rename/remove rules, row context, a song-file
        /// round trip through a scratch asset, migration of an existing song (unchanged data and bit-identical audio),
        /// and the real window: right-click a slider, type id and name, see validation, create, rename, remove, Undo/Redo.
        /// Disposable fixtures only; the scratch folder is deleted.
        /// </summary>
        public static string CheckCommandModel()
        {
            var report = new StringBuilder(); int passed = 0, failed = 0;
            void Need(bool condition, string message) { if (!condition) throw new Exception(message); }
            void Check(string label, Action run) { try { run(); passed++; report.AppendLine("PASS " + label); } catch (Exception e) { failed++; report.AppendLine("FAIL " + label + ": " + e.Message); } }
            var inst = CreateInstance<ZTrackerInstrument>(); ZTrackerMigration.Upgrade(inst, out _);
            var inst2 = CreateInstance<ZTrackerInstrument>(); ZTrackerMigration.Upgrade(inst2, out _);
            SongData Fixture()
            {
                var s = TrackerEngineCheck.FixtureSong(inst, 2, 16);
                s.instruments.Add(inst2);
                s.tracks[0].name = "Lead";
                s.tracks[0].devices.nodes.Add(new AudioEffectNodeData { type = ZoundEffectType.Gain, uid = "fx-gain", p = new[] { 1f } });
                return s;
            }
            ParameterTarget Mixer(SongData s, string parameter) => new ParameterTarget { kind = ParameterKind.Mixer, trackId = s.tracks[0].id, parameter = parameter };
            try
            {
                Check("id validation matrix: reserved, taken, malformed, case, device addresses, names", () =>
                {
                    var s = Fixture();
                    Need(ZTrackerCommandRegistry.Define(s, DynamicScope.Track, Mixer(s, "postVolume"), "fc", "Filter cutoff", 0, 2, out var e0) != null && e0 == null, "lower-case id refused: " + e0);
                    Need(s.dynamicParameters[0].id == "FC", "id not stored upper case");
                    var cases = new (string id, string reason)[]
                    {
                        ("", "Type two characters"), ("F", "Type two characters"), ("FCX", "Type two characters"), ("  ", "Type two characters"),
                        ("F-", "Only letters A–Z and digits 0–9"), ("é1", "Only letters A–Z and digits 0–9"), ("F ", "Type two characters"),
                        ("0A", "Reserved for built-in commands (0…)"), ("0q", "Reserved for built-in commands (0…)"), ("09", "Reserved for built-in commands (0…)"),
                        ("ZT", "Reserved for song commands (Z…)"), ("zz", "Reserved for song commands (Z…)"),
                        ("FC", "Taken by ‘Filter cutoff’"), ("fc", "Taken by ‘Filter cutoff’"), (" fc ", "Taken by ‘Filter cutoff’"),
                        ("A1", null), ("10", null), ("9Z", null), ("YY", null), ("m3", null)
                    };
                    foreach (var c in cases)
                    {
                        string got = ZTrackerCommandRegistry.ValidateId(s, c.id, null, out _);
                        Need(got == c.reason, "'" + c.id + "' gave '" + got + "' expected '" + c.reason + "'");
                    }
                    Need(ZTrackerCommandRegistry.ValidateId(s, "fc", s.dynamicParameters[0].uid, out _) == null, "renaming to its own id refused");
                    s.tracks[1].sourceDevices.Add(new SourceDeviceData { id = "imported", ordinal = 3, kind = SourceDeviceKind.InstrumentMacros });
                    Need(ZTrackerCommandRegistry.ValidateId(s, "3A", null, out _)?.StartsWith("Taken by imported device 3") == true, "imported device address offered");
                    Need(ZTrackerCommandRegistry.ValidateId(s, "4A", null, out _) == null, "free device position refused");
                    Need(ZTrackerCommandRegistry.ValidateName("") == "Give it a name" && ZTrackerCommandRegistry.ValidateName("   ") == "Give it a name", "empty name accepted");
                    Need(ZTrackerCommandRegistry.ValidateName(new string('a', 24)) == null && ZTrackerCommandRegistry.ValidateName(new string('a', 25)) == "At most 24 characters", "name length rule");
                    Need(ZTrackerCommandRegistry.Define(s, DynamicScope.Track, Mixer(s, "postVolume"), "PV", "Again", 0, 2, out var e1) == null && e1 == "This setting already has a command", "a target got two ids");
                    Need(ZTrackerCommandRegistry.Define(s, DynamicScope.Track, Mixer(s, "preVolume"), "0Q", "Bad", 0, 2, out _) == null && s.dynamicParameters.Count == 1, "reserved id stored");
                });

                Check("rename rewrites the pattern cells that use the id; remove keeps them; version set on first use only", () =>
                {
                    var s = Fixture();
                    Need(s.commandModelVersion == 0, "fixture already versioned");
                    ZTrackerCommandRegistry.Define(s, DynamicScope.Track, Mixer(s, "preVolume"), "0Z", "bad", 0, 1, out _);
                    Need(s.commandModelVersion == 0, "a refused definition still upgraded the song");
                    var p = ZTrackerCommandRegistry.Define(s, DynamicScope.Track, Mixer(s, "postVolume"), "TV", "Track volume", 0, 2, out _);
                    Need(s.commandModelVersion == 1, "version not set");
                    var pt = s.patterns[0].tracks[0];
                    pt.WriteLine(new PatternLine { line = 2, effects = new List<EffectCell> { new EffectCell { column = 0, command = new CommandData { present = true, valuePresent = true, identifier = "TV", value = 0x40 } } } });
                    pt.WriteLine(new PatternLine { line = 5, effects = new List<EffectCell> { new EffectCell { column = 0, command = new CommandData { present = true, valuePresent = true, identifier = "tv", value = 0x10 } } } });
                    Need(ZTrackerCommandRegistry.Uses(s, "TV") == 2, "uses " + ZTrackerCommandRegistry.Uses(s, "TV"));
                    Need(ZTrackerCommandRegistry.Rename(s, p, "0V", "x") == "Reserved for built-in commands (0…)" && p.id == "TV", "refused rename changed data");
                    Need(ZTrackerCommandRegistry.Rename(s, p, "vl", "Lead level") == null && p.id == "VL" && p.name == "Lead level", "rename failed");
                    Need(pt.ReadLine(2).effects[0].command.identifier == "VL" && pt.ReadLine(5).effects[0].command.identifier == "VL" && pt.ReadLine(2).effects[0].command.value == 0x40, "cells did not follow the rename");
                    Need(ZTrackerCommandRegistry.Remove(s, p) && s.dynamicParameters.Count == 0 && pt.ReadLine(2).effects[0].command.identifier == "VL", "remove erased cells or kept the definition");
                    using (var prepared = TrackerPreparedSong.Prepare(s)) Need(prepared.diagnostics.Any(d => d.StartsWith("DYNAMIC_PARAMETER_UNDEFINED")), "orphaned cells not reported");
                });

                Check("context: song everywhere, track and effect on their track, instruments where they play", () =>
                {
                    var s = Fixture();
                    s.tracks[1].visibleNoteColumns = 0;
                    ZTrackerCommandRegistry.Define(s, DynamicScope.Song, new ParameterTarget { kind = ParameterKind.Timing, parameter = "bpm" }, "BT", "Tempo", 60, 240, out _);
                    ZTrackerCommandRegistry.Define(s, DynamicScope.Track, Mixer(s, "postVolume"), "TV", "Lead level", 0, 2, out _);
                    ZTrackerCommandRegistry.Define(s, DynamicScope.Effect, new ParameterTarget { kind = ParameterKind.Device, trackId = s.tracks[0].id, deviceId = "fx-gain", index = 0, parameter = "1" }, "GA", "Lead gain", 0, 4, out _);
                    ZTrackerCommandRegistry.Define(s, DynamicScope.Instrument, new ParameterTarget { kind = ParameterKind.Sample, instrumentId = inst.model.id, parameter = "volume" }, "I1", "First", 0, 2, out _);
                    ZTrackerCommandRegistry.Define(s, DynamicScope.Instrument, new ParameterTarget { kind = ParameterKind.Sample, instrumentId = inst2.model.id, parameter = "volume" }, "I2", "Second", 0, 2, out _);
                    var pt = s.patterns[0].tracks[0];
                    var a = TrackerEngineCheck.Note(60, 0); a.instrument = 0; var b = TrackerEngineCheck.Note(60, 1); b.instrument = 1;
                    pt.WriteLine(new PatternLine { line = 4, notes = new List<NoteCell> { a } });
                    pt.WriteLine(new PatternLine { line = 8, notes = new List<NoteCell> { b } });
                    string Ids(string track, int line) => string.Join(",", ZTrackerCommandRegistry.InContext(s, s.patterns[0].id, track, line).Select(p => p.id).OrderBy(x => x));
                    Need(Ids(s.tracks[0].id, 2) == "BT,GA,I1,I2,TV", "before any selection in the pattern, every instrument the track uses: " + Ids(s.tracks[0].id, 2));
                    Need(Ids(s.tracks[0].id, 5) == "BT,GA,I1,TV", "row 5: " + Ids(s.tracks[0].id, 5));
                    Need(Ids(s.tracks[0].id, 9) == "BT,GA,I1,I2,TV", "row 9 (both columns selected): " + Ids(s.tracks[0].id, 9));
                    Need(Ids(s.tracks[1].id, 9) == "BT", "other track: " + Ids(s.tracks[1].id, 9));
                });

                Check("song-file round trip through a saved scratch asset keeps every field", () =>
                {
                    string folder = "Assets/ZTrackerCommandScratch-" + Guid.NewGuid().ToString("N").Substring(0, 8);
                    AssetDatabase.CreateFolder("Assets", folder.Substring("Assets/".Length));
                    try
                    {
                        var asset = CreateInstance<ZTrackerSong>(); asset.schemaVersion = 1; asset.model = Fixture(); asset.model.instruments.Clear();
                        var p = ZTrackerCommandRegistry.Define(asset.model, DynamicScope.Track, Mixer(asset.model, "postPan"), "PN", "Lead pan", -1, 1, out _);
                        p.color = new Color(.2f, .4f, .6f, 1);
                        string before = JsonUtility.ToJson(asset.model);
                        string path = folder + "/Song.asset";
                        AssetDatabase.CreateAsset(asset, path); AssetDatabase.SaveAssetIfDirty(asset);
                        Need(File.ReadAllText(path).Contains("commandModelVersion: 1") && File.ReadAllText(path).Contains("id: PN"), "saved YAML lacks the command fields");
                        Resources.UnloadAsset(asset);
                        var loaded = AssetDatabase.LoadAssetAtPath<ZTrackerSong>(path);
                        Need(JsonUtility.ToJson(loaded.model) == before, "reloaded song differs");
                        var q = loaded.model.dynamicParameters.Single();
                        Need(q.uid == p.uid && q.id == "PN" && q.name == "Lead pan" && q.scope == DynamicScope.Track && q.target.parameter == "postPan" && q.min == -1 && q.max == 1 && q.color == p.color, "definition fields changed");
                    }
                    finally { Need(folder.StartsWith("Assets/ZTrackerCommandScratch-"), "unsafe scratch path"); AssetDatabase.DeleteAsset(folder); }
                });

                Check("migration: an existing saved song reads as version 0, is unchanged by opening and plays bit-identically once upgraded", () =>
                {
                    string path = AssetDatabase.FindAssets("t:ZTrackerSong", new[] { "Assets/Demos/ZTrackerDemo" }).Select(AssetDatabase.GUIDToAssetPath).FirstOrDefault();
                    Need(path != null, "no demo song");
                    string text = File.ReadAllText(path);
                    var demo = AssetDatabase.LoadAssetAtPath<ZTrackerSong>(path);
                    Need(!text.Contains("commandModelVersion") || demo.schemaVersion != 1 || demo.model.commandModelVersion == 0, "fixture is already versioned");
                    if (demo.schemaVersion != 1) { report.AppendLine("     (demo song is schema " + demo.schemaVersion + "; migrated copy used)"); }
                    // Its instrument files may be older-format too: they must stay byte-identical as well.
                    var instrumentFiles = demo.instruments.Where(i => i != null).Select(AssetDatabase.GetAssetPath).Distinct().ToDictionary(p => p, File.ReadAllText);
                    // Play the song the way the product does: a playback snapshot converts older-format songs and instruments
                    // into detached schema-1 copies. Handing older-format instrument assets straight to the engine is refused.
                    using var snapshot = PlaybackSnapshot.Create(demo, null, -1);
                    var model = snapshot.Data;
                    Need(model.commandModelVersion == 0 && model.dynamicParameters.Count == 0, "old song does not read as version 0 with no commands");
                    Need(File.ReadAllText(path) == text && instrumentFiles.All(f => File.ReadAllText(f.Key) == f.Value), "loading changed a file");
                    // A JSON song with the new fields absent reads the same way.
                    var stripped = JsonUtility.FromJson<SongData>(JsonUtility.ToJson(model).Replace(",\"commandModelVersion\":0", "").Replace(",\"dynamicParameters\":[]", ""));
                    Need(stripped.commandModelVersion == 0 && stripped.dynamicParameters != null && stripped.dynamicParameters.Count == 0, "absent fields do not default");
                    string before = JsonUtility.ToJson(model);
                    var upgraded = ZTrackerMigration.Copy(model);
                    Need(ZTrackerCommandRegistry.Upgrade(upgraded) == null && upgraded.commandModelVersion == 1, "upgrade failed");
                    Need(JsonUtility.ToJson(upgraded).Replace("\"commandModelVersion\":1", "\"commandModelVersion\":0") == before, "upgrade changed more than the version");
                    var a = RenderOffline(model); var b = RenderOffline(upgraded);
                    Need(a.Any(v => v != 0), "demo song rendered silence");
                    Need(a.Length == b.Length && a.Zip(b, (x, y) => BitConverter.SingleToInt32Bits(x) == BitConverter.SingleToInt32Bits(y)).All(x => x), "upgraded song does not render bit-identically");
                    var future = ZTrackerMigration.Copy(model); future.commandModelVersion = 2;
                    Need(ZTrackerCommandRegistry.Upgrade(future) != null && ZTrackerCommandRegistry.Define(future, DynamicScope.Song, new ParameterTarget { kind = ParameterKind.Timing, parameter = "bpm" }, "BT", "Tempo", 60, 240, out _) == null, "future version edited");
                    // An older-format instrument handed to the engine directly is refused with a plain message, never a null reference.
                    if (demo.schemaVersion == 0 && demo.instruments.Any(i => i != null && i.schemaVersion == 0))
                    {
                        string refusal = null;
                        try { using (TrackerPreparedSong.Prepare(ZTrackerMigration.Convert(ZTrackerMigration.Capture(demo)))) { } } catch (ArgumentException e) { refusal = e.Message; }
                        Need(refusal != null && refusal.StartsWith("Instrument requires schema 1 at slot "), "older-format instrument not refused plainly: " + refusal);
                    }
                });

                Check("older-format fixtures: the committed demo song and instrument load unchanged, migrate and render identically", () => OlderFormatFixtures(Need, report));

                Check("window: right-click a mixer slider, type id and name with inline validation, create, rename, remove, Undo/Redo", () => CommandWindowWalk(Fixture(), inst, Need, report));
            }
            finally { DestroyImmediate(inst); DestroyImmediate(inst2); }
            report.Insert(0, "TOTAL passed=" + passed + " failed=" + failed + "\n");
            return report.ToString();
        }

        /// <summary>Renders <paramref name="blocks"/> blocks of 1024 interleaved stereo frames from the song start, offline.</summary>
        static float[] RenderOffline(SongData s, int blocks = 96)
        {
            var l = new NativeArray<float>(1024, Allocator.Persistent); var r = new NativeArray<float>(1024, Allocator.Persistent); var all = new List<float>();
            try { using (var e = new TrackerOffline(TrackerPreparedSong.Prepare(s))) { e.SendCommand(TrackerCommand.Play()); for (int i = 0; i < blocks; i++) { e.Render(l, r, 1024, 1024); for (int f = 0; f < 1024; f++) { all.Add(l[f]); all.Add(r[f]); } } } }
            finally { l.Dispose(); r.Dispose(); }
            return all.ToArray();
        }
        static bool BitIdentical(float[] a, float[] b) => a.Length == b.Length && a.Zip(b, (x, y) => BitConverter.SingleToInt32Bits(x) == BitConverter.SingleToInt32Bits(y)).All(x => x);

        /// <summary>The committed demo song and instrument are deliberately kept in the older (schema 0) format as the
        /// fixture for old saved work (T-0612). Each must read without touching its file, convert to a valid model,
        /// upgrade on an in-memory clone, and render audibly and bit-identically before and after the upgrade. The
        /// package's shipped sample copies are the same data (line endings aside), so they are covered by comparison.</summary>
        internal static void OlderFormatFixtures(Action<bool, string> Need, StringBuilder report)
        {
            const string folder = "Assets/Demos/ZTrackerDemo", samples = "Assets/Packages/ZTracker/Samples~/ZTrackerDemo";
            string Lines(string s) => s.Replace("\r\n", "\n");
            var songs = AssetDatabase.FindAssets("t:ZTrackerSong", new[] { folder }).Select(AssetDatabase.GUIDToAssetPath).ToList();
            var instruments = AssetDatabase.FindAssets("t:ZTrackerInstrument", new[] { folder }).Select(AssetDatabase.GUIDToAssetPath).ToList();
            Need(songs.Count > 0 && instruments.Count > 0, "demo song or instrument missing under " + folder);
            var texts = songs.Concat(instruments).ToDictionary(p => p, File.ReadAllText);
            foreach (var p in texts.Keys)
            {
                string sample = samples + "/" + Path.GetFileName(p);
                Need(File.Exists(sample) && Lines(File.ReadAllText(sample)) == Lines(texts[p]), "package sample differs from the demo: " + sample);
            }
            string Restore(string path, int schema) => path + " is no longer older-format (schema " + schema + "); it is the schema-0 fixture, restore it with git checkout";
            var temporary = new List<UnityEngine.Object>();
            T Temp<T>(T o) where T : UnityEngine.Object { o.hideFlags = HideFlags.HideAndDontSave; temporary.Add(o); return o; }
            try
            {
                foreach (var path in instruments)
                {
                    var asset = AssetDatabase.LoadAssetAtPath<ZTrackerInstrument>(path);
                    Need(asset.schemaVersion == 0, Restore(path, asset.schemaVersion));
                    var view = ZTrackerMigration.Convert(ZTrackerMigration.Capture(asset), asset.name);
                    string invalid = ZTrackerModelValidation.Validate(view);
                    Need(invalid == null, path + " converts to an invalid model: " + invalid);
                    var clone = Temp(Instantiate(asset));
                    Need(ZTrackerMigration.Upgrade(clone, out var error) && clone.schemaVersion == 1 && clone.model != null && clone.model.family == view.family && clone.legacyArchive != null, path + " upgrade failed: " + error);
                    float[] Play(ZTrackerInstrument inst)
                    {
                        var song = Temp(CreateInstance<ZTrackerSong>()); song.schemaVersion = 1; song.model = TrackerEngineCheck.FixtureSong(inst, 1, 16);
                        song.model.patterns[0].tracks[0].WriteLine(new PatternLine { line = 0, notes = new List<NoteCell> { TrackerEngineCheck.Note(60) } });
                        using var snapshot = PlaybackSnapshot.Create(song, null, -1);
                        return RenderOffline(snapshot.Data, 24);
                    }
                    var before = Play(asset); var after = Play(clone);
                    Need(before.Any(v => v != 0), path + " rendered silence");
                    Need(BitIdentical(before, after), path + " does not render bit-identically once upgraded");
                    report.AppendLine("     " + path + ": schema 0, " + view.family + ", peak " + before.Max(Math.Abs).ToString("0.000"));
                }
                foreach (var path in songs)
                {
                    var asset = AssetDatabase.LoadAssetAtPath<ZTrackerSong>(path);
                    Need(asset.schemaVersion == 0, Restore(path, asset.schemaVersion));
                    var clone = Temp(Instantiate(asset));
                    Need(ZTrackerMigration.Upgrade(clone, out var error) && clone.schemaVersion == 1 && clone.model != null, path + " upgrade failed: " + error);
                    Need(clone.model.tracks.Count == asset.channelCount + 1 && clone.model.patterns.Count == asset.patterns.Count && clone.model.sequence.Count == asset.orderList.Count && clone.model.instruments.SequenceEqual(asset.instruments), path + " upgraded with a different shape");
                    float[] before, after;
                    using (var snapshot = PlaybackSnapshot.Create(asset, null, -1)) before = RenderOffline(snapshot.Data);
                    using (var snapshot = PlaybackSnapshot.Create(clone, null, -1)) after = RenderOffline(snapshot.Data);
                    Need(before.Any(v => v != 0), path + " rendered silence");
                    Need(BitIdentical(before, after), path + " does not render bit-identically once upgraded");
                    report.AppendLine("     " + path + ": schema 0, " + asset.patterns.Count + " pattern(s), " + asset.instruments.Count + " instrument(s), peak " + before.Max(Math.Abs).ToString("0.000"));
                }
            }
            finally { foreach (var o in temporary) DestroyImmediate(o); }
            Need(texts.All(t => File.ReadAllText(t.Key) == t.Value), "a fixture file changed on disk");
        }

        static void RightClick(VisualElement target)
        {
            var root = target.panel.visualTree; var at = target.worldBound.center;
            using (var right = PointerDownEvent.GetPooled(new Event { type = EventType.MouseDown, button = 1, mousePosition = at })) { right.target = target; target.SendEvent(right); }
            PointerRelease(root, at, 1);
        }

        static void PickMenuItem(ZTrackerModelWindow window, string label)
        {
            var root = window.rootVisualElement.panel.visualTree;
            window.rootVisualElement.panel.GetType().GetMethod("ValidateLayout", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.Invoke(window.rootVisualElement.panel, null);
            var item = root.Query<VisualElement>(className: "zui-menu__item").ToList().FirstOrDefault(e => e.Query<Label>().ToList().Any(l => l.text == label));
            if (item == null) throw new Exception("Missing menu item " + label + "; have: " + string.Join(" | ", root.Query<VisualElement>(className: "zui-menu__item").ToList().SelectMany(e => e.Query<Label>().ToList()).Select(l => l.text)));
            using (var down = PointerDownEvent.GetPooled(new Event { type = EventType.MouseDown, button = 0, mousePosition = item.worldBound.center })) { down.target = item; item.SendEvent(down); }
            using (var up = PointerUpEvent.GetPooled(new Event { type = EventType.MouseUp, button = 0, mousePosition = item.worldBound.center })) { up.target = item; item.SendEvent(up); }
        }

        static void ClickButton(Button button)
        {
            var invoke = typeof(Clickable).GetMethod("SimulateSingleClick", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            invoke.Invoke(button.clickable, new object[] { null, 0 });
        }

        /// <summary>The window walk, shared by the check and by the eye-verification capture. Leaves the window open when <paramref name="keep"/> is set.</summary>
        internal static ZTrackerModelWindow CommandWindowWalk(SongData model, ZTrackerInstrument inst, Action<bool, string> Need, StringBuilder report, bool keep = false)
        {
            var window = CreateInstance<ZTrackerModelWindow>(); var asset = CreateInstance<ZTrackerSong>(); asset.schemaVersion = 1; asset.model = model;
            window.song = asset; window.pane = 1; window.mixerTab = 0; window.track = 0; window.titleContent = new GUIContent("ZTracker command walk"); window.Show(); window.CreateGUI();
            bool done = false;
            try
            {
                window.BuildPane(); LayoutChain(window);
                var slider = window.rootVisualElement.Q<ZuiMicroSlider>("mixer-post-volume");
                Need(slider != null, "post gain slider missing");
                RightClick(slider);
                PickMenuItem(window, "Command id…");
                var editor = window.commandEditor;
                Need(editor != null, "command editor did not open");
                var panelRoot = window.rootVisualElement.panel.visualTree;
                var id = panelRoot.Q<TextField>("command-editor-id"); var name = panelRoot.Q<TextField>("command-editor-name"); var status = panelRoot.Q<Label>("command-editor-status"); var confirm = panelRoot.Q<Button>("command-editor-confirm");
                Need(id != null && name != null && status != null && confirm != null, "editor fields missing");
                Need(name.value == "Post gain", "name not prefilled: " + name.value);
                Need(status.text == "⚠ Type two characters" && !confirm.enabledSelf, "empty id not flagged: " + status.text);
                id.value = "0a"; Need(status.text == "⚠ Reserved for built-in commands (0…)" && !confirm.enabledSelf, "reserved not flagged: " + status.text);
                id.value = "z1"; Need(status.text == "⚠ Reserved for song commands (Z…)", "song family not flagged: " + status.text);
                id.value = "v-"; Need(status.text == "⚠ Only letters A–Z and digits 0–9", "malformed not flagged: " + status.text);
                id.value = "vl"; name.value = ""; Need(status.text == "⚠ Give it a name" && !confirm.enabledSelf, "empty name not flagged");
                name.value = "Lead level"; Need(status.text == "" && confirm.enabledSelf, "valid id/name still flagged: " + status.text);
                ClickButton(confirm);
                var def = asset.model.dynamicParameters.SingleOrDefault();
                Need(def != null && def.id == "VL" && def.name == "Lead level" && def.scope == DynamicScope.Track && def.target.parameter == "postVolume" && asset.model.commandModelVersion == 1, "definition not created as typed");
                Undo.PerformUndo(); Need(asset.model.dynamicParameters.Count == 0 && asset.model.commandModelVersion == 0, "Undo did not remove the definition and version");
                Undo.PerformRedo(); Need(asset.model.dynamicParameters.Count == 1 && asset.model.dynamicParameters[0].id == "VL", "Redo did not restore the definition");
                // Rename from the same slider, then use it from a pattern cell.
                window.pane = 1; window.BuildPane(); LayoutChain(window);
                slider = window.rootVisualElement.Q<ZuiMicroSlider>("mixer-post-volume");
                RightClick(slider); PickMenuItem(window, "Command VL…");
                panelRoot = window.rootVisualElement.panel.visualTree;
                id = panelRoot.Q<TextField>("command-editor-id"); name = panelRoot.Q<TextField>("command-editor-name"); status = panelRoot.Q<Label>("command-editor-status"); confirm = panelRoot.Q<Button>("command-editor-confirm");
                Need(id.value == "VL" && confirm.text == "Rename", "rename editor not prefilled");
                asset.model.patterns[0].tracks[0].WriteLine(new PatternLine { line = 3, effects = new List<EffectCell> { new EffectCell { column = 0, command = new CommandData { present = true, valuePresent = true, identifier = "VL", value = 0, scope = CommandScope.Track } } } });
                id.value = "lv"; name.value = "Lead volume"; ClickButton(confirm);
                Need(asset.model.dynamicParameters[0].id == "LV" && asset.model.patterns[0].tracks[0].ReadLine(3).effects[0].command.identifier == "LV", "rename did not carry the pattern cell");
                Undo.PerformUndo(); Need(asset.model.dynamicParameters[0].id == "VL" && asset.model.patterns[0].tracks[0].ReadLine(3).effects[0].command.identifier == "VL", "rename Undo did not restore id and cell");
                Undo.PerformRedo(); Need(asset.model.dynamicParameters[0].id == "LV", "rename Redo failed");
                // The Song view lists it; its range slider resets on double-click; Remove from the list.
                window.pane = 4; window.BuildPane(); LayoutChain(window);
                Need(window.rootVisualElement.Q("command-row-LV") != null, "Song › Commands does not list LV");
                var range = window.rootVisualElement.Q<ZuiMicroMinMax>("command-range-LV");
                Need(range != null, "range control missing");
                window.SongEdit("fixture range", () => asset.model.dynamicParameters[0].max = 1.2f, true); LayoutChain(window);
                range = window.rootVisualElement.Q<ZuiMicroMinMax>("command-range-LV");
                var rangeRoot = range.panel.visualTree; var rangeAt = range.worldBound.center;
                using (var down = PointerDownEvent.GetPooled(new Event { type = EventType.MouseDown, button = 0, clickCount = 2, mousePosition = rangeAt })) { down.target = range; range.SendEvent(down); }
                PointerRelease(rangeRoot, rangeAt);
                Need(Mathf.Approximately(asset.model.dynamicParameters[0].max, 2) && Mathf.Approximately(asset.model.dynamicParameters[0].min, 0), "range double-click did not reset to 0–2: " + asset.model.dynamicParameters[0].min + "–" + asset.model.dynamicParameters[0].max);
                Undo.PerformUndo(); Need(Mathf.Approximately(asset.model.dynamicParameters[0].max, 1.2f), "range reset Undo failed");
                Undo.PerformRedo();
                window.BuildPane(); LayoutChain(window);
                ClickButton(window.rootVisualElement.Q<Button>("command-remove-LV"));
                Need(asset.model.dynamicParameters.Count == 0 && asset.model.patterns[0].tracks[0].ReadLine(3).effects[0].command.identifier == "LV", "remove did not keep the cell or left the definition");
                Need(window.rootVisualElement.Q("commands-empty") != null, "empty state not shown");
                Undo.PerformUndo(); Need(asset.model.dynamicParameters.Count == 1, "remove Undo failed");
                // The pattern help line names the command.
                window.pane = 0; window.order = 0; window.row = 3; window.BuildPane();
                window.sub = window.fields.FindIndex(f => f.kind == FieldKind.EffectId && f.track == 0); window.RefreshHelp();
                Need(window.help.text.Contains("Lead volume"), "help line does not name the command: " + window.help.text);
                // Effect setting right-click inside the shared chain editor.
                window.pane = 1; window.mixerTab = 1; window.BuildPane(); LayoutChain(window);
                var setting = window.rootVisualElement.Q("chain-param-0-0");
                Need(setting != null, "chain setting missing");
                RightClick(setting); PickMenuItem(window, "Command id…");
                panelRoot = window.rootVisualElement.panel.visualTree;
                panelRoot.Q<TextField>("command-editor-id").value = "GA"; panelRoot.Q<TextField>("command-editor-name").value = "Lead gain"; ClickButton(panelRoot.Q<Button>("command-editor-confirm"));
                Need(asset.model.dynamicParameters.Any(p => p.id == "GA" && p.scope == DynamicScope.Effect && p.target.deviceId == "fx-gain" && p.target.index == 0), "effect setting command not created");
                // The song with both commands compiles them.
                using (var prepared = TrackerPreparedSong.Prepare(asset.model)) Need(prepared.state.dynamicCount == 2 && !prepared.diagnostics.Any(d => d.StartsWith("DYNAMIC_ID") || d.StartsWith("DYNAMIC_TARGET")), "commands did not compile: " + string.Join("; ", prepared.diagnostics.Where(d => d.StartsWith("DYNAMIC"))));
                done = true;
                return keep ? window : null;
            }
            finally
            {
                window.commandEditor?.Close();
                if (!keep || !done) { window.StopPreview(); window.Close(); DestroyImmediate(window); Undo.ClearUndo(asset); DestroyImmediate(asset); }
            }
        }
    }
}

using System;
using System.IO;
using System.Linq;
using System.Text;
using Laubrary.ZTracker.Engine;
using Laubrary.ZTracker.Model;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace Laubrary.ZTracker.Editor
{
    public sealed partial class ZTrackerModelWindow
    {
        /// <summary>
        /// T-0608: a window closed and opened again shows the same song, instrument, view, cursor, tabs, selections and
        /// saved page state; a missing asset, a shrunk song or a damaged file fall back quietly; viewing never dirties the
        /// song; only the user's window writes the file. Uses its own state file and scratch assets, both deleted after.
        /// </summary>
        public static string CheckViewRestore()
        {
            var report = new StringBuilder(); int passed = 0, failed = 0;
            void Need(bool ok, string why) { if (!ok) throw new InvalidOperationException(why); }
            void Check(string title, Action test) { try { test(); passed++; report.AppendLine("PASS " + title); } catch (Exception e) { failed++; report.AppendLine("FAIL " + title + ": " + e.Message); } }
            const string Folder = "Assets/ZTrackerViewRestoreCheck";
            string stateFile = Path.Combine(Application.temporaryCachePath, "ztracker-view-restore-check.json");
            string previousOverride = viewStatePathOverride;
            viewStatePathOverride = stateFile;
            if (File.Exists(stateFile)) File.Delete(stateFile);
            ZTrackerSong song = null; ZTrackerInstrument lead = null, bass = null;
            ZTrackerModelWindow first = null, second = null;
            ZTrackerModelWindow Fresh(string title, bool user)
            {
                var w = CreateInstance<ZTrackerModelWindow>();
                w.titleContent = new GUIContent(title);
                w.asset = null;
                w.rememberView = user;
                return w;
            }
            void ShowWindow(ZTrackerModelWindow w) { w.Show(); w.position = new Rect(70, 70, 1200, 800); w.CreateGUI(); LayoutChain(w); }
            try
            {
                song = CreateSongAsset("View song", Folder);
                lead = CreateInstrumentAsset("View lead", Folder);
                bass = CreateInstrumentAsset("View bass", Folder);
                var d = song.model;
                d.instruments.Add(lead); d.instruments.Add(bass);
                d.tracks.Insert(1, new TrackData { id = "t2", name = "Track 2", columns = new System.Collections.Generic.List<ColumnVisibility> { new ColumnVisibility() } });
                d.tracks.Insert(2, new TrackData { id = "t3", name = "Track 3", columns = new System.Collections.Generic.List<ColumnVisibility> { new ColumnVisibility() } });
                foreach (var p in d.patterns) { p.tracks.Add(new PatternTrack { trackId = "t2" }); p.tracks.Add(new PatternTrack { trackId = "t3" }); }
                var second_pattern = NewPattern(d, "Pattern 2"); d.patterns.Add(second_pattern); d.sequence.Add(new SequenceSlot { id = Id(), patternId = second_pattern.id });
                EditorUtility.SetDirty(song); AssetDatabase.SaveAssetIfDirty(song);

                Check("Closing the user's window writes its view; opening again shows the same song, instrument, view, tabs, cursor and selections", () =>
                {
                    first = Fresh("ZTracker view check A", true);
                    first.asset = song; ShowWindow(first);
                    first.order = 1; first.track = 2; first.row = 20; first.octave = 3; first.step = 2; first.editMode = false; first.follow = false;
                    first.pane = 0; first.BuildPane(); LayoutChain(first);
                    first.Select(20, first.fields.FindIndex(f => f.track == 2 && f.kind == FieldKind.Note), false);
                    int subBefore = first.sub;
                    first.entryInstrument = 1; first.instrument = bass; first.pane = 3; first.instrumentTab = 2; first.mixerTab = 1; first.presetIndex = -1;
                    first.BuildPane(); LayoutChain(first);
                    string pageKey = first.WorkspaceKey();
                    first.Close(); DestroyImmediate(first); first = null;
                    Need(File.Exists(stateFile), "closing did not write the view");
                    Need(!EditorUtility.IsDirty(song) && !EditorUtility.IsDirty(bass), "viewing dirtied an asset");

                    second = Fresh("ZTracker view check B", true);
                    Need(second.RestoreViewState(), "the saved view was not read");
                    second.viewRestored = true;
                    ShowWindow(second);
                    Need(second.song == song, "a different song opened");
                    Need(second.pane == 3 && second.instrument == bass && second.instrumentTab == 2 && second.mixerTab == 1 && second.entryInstrument == 1, "the instrument view was not restored: pane " + second.pane + " tab " + second.instrumentTab + " instrument " + (second.instrument == null ? "none" : second.instrument.name));
                    Need(second.order == 1 && second.track == 2 && second.row == 20 && second.sub == subBefore, "the pattern cursor was not restored: slot " + second.order + " track " + second.track + " row " + second.row + " field " + second.sub);
                    Need(second.octave == 3 && second.step == 2 && !second.editMode && !second.follow, "octave, step, Edit or Follow not restored");
                    Need(second.workspacePages.Any(p => p.key == pageKey), "the page's folds and scrolls were not restored");
                    Need(second.rootVisualElement.Q("instrument-tabs") != null, "the Instrument view is not what was built");
                    Need(!EditorUtility.IsDirty(song) && !EditorUtility.IsDirty(bass), "restoring dirtied an asset");
                    second.Close(); DestroyImmediate(second); second = null;
                });

                Check("A window that is not the user's (a check, a walk) never writes the file", () =>
                {
                    File.WriteAllText(stateFile + ".probe", "x");
                    long before = new FileInfo(stateFile).LastWriteTimeUtc.Ticks; string content = File.ReadAllText(stateFile);
                    var other = Fresh("ZTracker view check C", false);
                    other.asset = song; ShowWindow(other); other.pane = 1; other.BuildPane();
                    other.OnLostFocus(); other.Close(); DestroyImmediate(other);
                    Need(File.ReadAllText(stateFile) == content, "a non-user window wrote the view");
                    File.Delete(stateFile + ".probe");
                });

                Check("Quiet fallbacks: a deleted instrument, a shrunk song, a deleted song, a damaged file", () =>
                {
                    AssetDatabase.DeleteAsset(AssetDatabase.GetAssetPath(bass)); bass = null;
                    d.sequence.RemoveAt(1); EditorUtility.SetDirty(song); AssetDatabase.SaveAssetIfDirty(song);
                    var w = Fresh("ZTracker view check D", true);
                    try
                    {
                        Need(w.RestoreViewState(), "the view with a missing instrument was not read");
                        w.viewRestored = true; ShowWindow(w);
                        Need(w.song == song && w.instrument == null, "a missing instrument was not skipped");
                        Need(w.order == 0 && w.entryInstrument <= 1, "indices past the end of the shrunk song were kept: slot " + w.order);
                        w.pane = 0; w.BuildPane(); LayoutChain(w);
                        Need(w.sub < w.fields.Count && w.row < w.Pattern.lineCount, "cursor outside the grid");
                    }
                    finally { w.rememberView = false; w.Close(); DestroyImmediate(w); }
                    string path = AssetDatabase.GetAssetPath(song);
                    AssetDatabase.DeleteAsset(path); song = null;
                    var x = Fresh("ZTracker view check E", true);
                    try { Need(!x.RestoreViewState(), "a deleted song was reported as restored"); ShowWindow(x); Need(x.song == null || x.song != song, "the deleted song is still shown"); }
                    finally { x.rememberView = false; x.Close(); DestroyImmediate(x); }
                    File.WriteAllText(stateFile, "{ not json");
                    var y = Fresh("ZTracker view check F", true);
                    try { Need(!y.RestoreViewState(), "a damaged file was reported as restored"); }
                    finally { y.rememberView = false; DestroyImmediate(y); }
                });
            }
            finally
            {
                foreach (var w in new[] { first, second }) if (w != null) { w.rememberView = false; w.Close(); DestroyImmediate(w); }
                viewStatePathOverride = previousOverride;
                foreach (var f in new[] { stateFile, stateFile + ".probe" }) if (File.Exists(f)) File.Delete(f);
                foreach (var o in new Object[] { song, lead, bass }) if (o != null) Undo.ClearUndo(o);
                AssetDatabase.DeleteAsset(Folder);
            }
            report.Insert(0, "TOTAL passed=" + passed + " failed=" + failed + "\n");
            return report.ToString();
        }
    }
}

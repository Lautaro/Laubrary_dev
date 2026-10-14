using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Laubrary.ZTracker.Engine;
using Laubrary.ZTracker.Model;
using Unity.Collections;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Laubrary.ZTracker.Editor
{
    public sealed partial class ZTrackerModelWindow
    {
        /// <summary>
        /// Envelope columns in the Pattern view (T-0601), driven through the real attached window with panel pointer and
        /// key events on a disposable in-memory song: adding, binding and removing columns, mouse and keyboard point
        /// editing, segment shapes, Undo per gesture, copy/paste/insert, the drawing, and a drawn line playing.
        /// </summary>
        public static string CheckEnvelopeColumns()
        {
            var report = new StringBuilder(); int passed = 0, failed = 0;
            var window = CreateInstance<ZTrackerModelWindow>(); var song = CreateInstance<ZTrackerSong>();
            var clip = AudioClip.Create("envelope-columns-check", 4800, 1, 48000, false); var raw = new float[4800]; for (int i = 0; i < raw.Length; i++) raw[i] = .5f; clip.SetData(raw, 0);
            var instrument = TrackerEngineCheck.FixtureInstrument(clip);
            void Need(bool okay, string why) { if (!okay) throw new InvalidOperationException(why); }
            void Check(string title, Action test) { try { test(); passed++; report.AppendLine("PASS " + title); } catch (Exception e) { failed++; report.AppendLine("FAIL " + title + ": " + e.Message); } }
            void Key(KeyCode code, char ch = '\0', EventModifiers mods = EventModifiers.None) { window.grid.Focus(); using (var e = KeyDownEvent.GetPooled(new Event { type = EventType.KeyDown, keyCode = code, character = ch, modifiers = mods })) { e.target = window.grid; window.grid.SendEvent(e); } }
            void Type(string text) { foreach (char c in text) { var code = char.IsDigit(c) ? KeyCode.Alpha0 + (c - '0') : KeyCode.A + (char.ToUpperInvariant(c) - 'A'); Key(code, char.ToLowerInvariant(c)); } }
            void Rebuild() { window.BuildPane(); LayoutChain(window); }
            Label Cell(string name) { LayoutChain(window); return window.gridCanvas.Q<Label>(name); }
            int Field(int t, int column, FieldKind kind) => window.fields.FindIndex(f => f.track == t && f.column == column && f.kind == kind);
            int Env(int column = 0) => Field(0, column, FieldKind.Envelope);
            Label EnvCell(int r, int column = 0) => Cell("cell-" + r + "-" + Env(column));
            TextField Search() => window.rootVisualElement.panel.visualTree.Query<TextField>(className: "zui-menu__search").ToList().LastOrDefault();
            void Enter(TextField field, string text) { field.value = text; using (var e = KeyDownEvent.GetPooled(new Event { type = EventType.KeyDown, keyCode = KeyCode.Return, character = '\n' })) { e.target = field; field.SendEvent(e); } }
            void Press(VisualElement target, Vector2 at, int button = 0, EventModifiers mods = EventModifiers.None) { using (var e = PointerDownEvent.GetPooled(new Event { type = EventType.MouseDown, button = button, modifiers = mods, mousePosition = at })) { e.target = target; target.SendEvent(e); } }
            void Move(VisualElement root, Vector2 at, EventModifiers mods = EventModifiers.None) { using (var e = PointerMoveEvent.GetPooled(new Event { type = EventType.MouseDrag, button = 0, modifiers = mods, mousePosition = at })) root.SendEvent(e); }
            void Release(VisualElement root, Vector2 at, int button = 0) { using (var e = PointerUpEvent.GetPooled(new Event { type = EventType.MouseUp, button = button, mousePosition = at })) root.SendEvent(e); }
            // A point inside an envelope cell at value u (0 left edge … 1 right edge).
            Vector2 At(Label cell, float u) => new Vector2(cell.worldBound.xMin + EnvelopePad + u * (EnvelopeWidth - 2 * EnvelopePad), cell.worldBound.center.y);
            void ClickAt(Label cell, float u, int button = 0, EventModifiers mods = EventModifiers.None) { var root = cell.panel.visualTree; Press(cell, At(cell, u), button, mods); Release(root, At(cell, u), button); }
            AutomationLane Lane(int column = 0) => ZTrackerEnvelopes.Lane(window.Pattern.tracks.Find(x => x.trackId == song.model.tracks[0].id), ZTrackerEnvelopes.Column(song.model.tracks[0], column));
            string Points(int column = 0) => Lane(column) == null ? "" : string.Join(" ", Lane(column).points.Select(p => p.line + ":" + Mathf.RoundToInt(p.value * 255).ToString("X2") + (p.segment == AutomationShape.Step ? "h" : "")));
            float? PointValue(int r, int column = 0) => ZTrackerEnvelopes.PointAt(Lane(column), r)?.value;
            string Json() => JsonUtility.ToJson(song.model);
            try
            {
                song.schemaVersion = 1; song.model = TrackerEngineCheck.FixtureSongNew(instrument, lines: 24); song.model.tracks[0].name = "Lead"; song.model.tracks[1].name = "Master";
                song.model.tracks[0].columns = new List<ColumnVisibility> { new ColumnVisibility { volume = true } };
                window.song = song; window.instrument = instrument; window.pane = 0; window.editMode = true; window.Show(); window.position = new Rect(60, 60, 1200, 820); window.CreateGUI(); Rebuild();

                Check("header + adds an envelope column and opens its menu; Enter on 0l binds it; − removes it with its points; Undo/Redo", () =>
                {
                    Need(Cell("pattern-env-add-0") != null, "no envelope + on the Lead header");
                    Need(Cell("pattern-env-remove-0").resolvedStyle.visibility == UnityEngine.UIElements.Visibility.Hidden, "− offered with no envelope column");
                    PointerClick(Cell("pattern-env-add-0"));
                    Need(song.model.tracks[0].envelopeColumns.Count == 1 && Env() >= 0, "+ did not add an envelope column");
                    Need(window.rootVisualElement.Q("pattern-env-columns") != null, "the track settings do not show the envelope count");
                    // The + also opens this menu by itself on the next editor tick (seen in the walk); a synchronous check opens it directly.
                    window.ShowEnvelopeMenu(0, 0, Cell("pattern-env-title-0-0"));
                    var search = Search(); Need(window.bindMenu?.IsOpen == true && search != null, "the bind menu did not open");
                    Enter(search, "0l"); Need(song.model.tracks[0].envelopeColumns[0].command == "0L", "Enter did not bind the column to 0L");
                    Need(Cell("pattern-env-title-0-0").text == "0L", "title does not show 0L: " + Cell("pattern-env-title-0-0")?.text);
                    Undo.PerformUndo(); Rebuild(); Need(song.model.tracks[0].envelopeColumns[0].command == "", "Undo did not unbind"); Undo.PerformRedo(); Rebuild(); Need(song.model.tracks[0].envelopeColumns[0].command == "0L", "Redo did not rebind");
                    window.Select(3, Env(), false); Type("80"); Need(Points() == "3:80", "points: " + Points());
                    PointerClick(Cell("pattern-env-remove-0")); Need(song.model.tracks[0].envelopeColumns.Count == 0 && Lane() == null && window.Pattern.tracks[0].automation.Count == 0, "− left the column or its lane behind");
                    Need(window.fields[window.sub].track == 0, "cursor left the track whose column was removed");
                    Undo.PerformUndo(); Rebuild(); Need(song.model.tracks[0].envelopeColumns.Count == 1 && Points() == "3:80", "Undo did not restore the column and its points");
                });

                Check("an unbound column refuses drawing with a note; Alt+B opens its menu; a song command is bound by its permanent key and a rename keeps it", () =>
                {
                    window.SetEnvelopeColumns(0, 2); Rebuild(); Need(song.model.tracks[0].envelopeColumns.Count == 2, "second column");
                    window.bindMenu?.Close();
                    ClickAt(EnvCell(5, 1), .5f); Need(Lane(1) == null && window.help.text.Contains("not bound"), "an unbound column took a point or said nothing: " + window.help.text);
                    var p = ZTrackerCommandRegistry.Define(song.model, DynamicScope.Track, new ParameterTarget { kind = ParameterKind.Mixer, trackId = song.model.tracks[0].id, parameter = "postVolume" }, "vl", "Lead level", 0, 2, out var error); Need(p != null, error);
                    window.Select(0, Env(1), false); Key(KeyCode.B, '\0', EventModifiers.Alt);
                    var search = Search(); Need(window.bindMenu?.IsOpen == true && search != null, "Alt+B did not open the envelope menu");
                    Need(window.rootVisualElement.panel.visualTree.Query<VisualElement>(className:"zui-menu__item").ToList().Any(x=>x.Q<Label>(className:"zui-menu__chip")?.text=="VL"&&x.Q<Label>(className:"zui-menu__label")?.text=="Lead level"), "song command not listed");
                    Enter(search, "vl"); Need(song.model.tracks[0].envelopeColumns[1].command == p.uid, "binding did not store the permanent key");
                    Need(Cell("pattern-env-title-0-1").text == "VL", "title does not show VL");
                    Need(ZTrackerCommandRegistry.Rename(song.model, p, "lv", "Lead level") == null, "rename refused"); Rebuild(); Need(Cell("pattern-env-title-0-1").text == "LV", "binding did not follow the rename");
                });

                Check("click adds a point at the pointer's value; dragging a point moves it; a wobble does not; one Undo step each", () =>
                {
                    window.SetEnvelopeColumns(0, 1); window.Pattern.tracks[0].automation.Clear(); Rebuild(); string before = Json();
                    ClickAt(EnvCell(2), .25f); Need(PointValue(2).HasValue && Math.Abs(PointValue(2).Value - .25f) < .03f, "click did not add a point at a quarter: " + Points());
                    Undo.PerformUndo(); Need(Json() == before, "one Undo did not remove the clicked point"); Undo.PerformRedo(); Rebuild();
                    ClickAt(EnvCell(8), 1f); Need(PointValue(2).HasValue && PointValue(8) == 1f, "a click at the right edge should set FF: " + Points());
                    string twoPoints = Json();
                    var root = EnvCell(2).panel.visualTree; Press(EnvCell(2), At(EnvCell(2), .25f)); Move(root, At(EnvCell(2), .25f) + new Vector2(1, 1)); Release(root, At(EnvCell(2), .25f));
                    Need(Json() == twoPoints, "a wobble moved the point: " + Points());
                    Press(EnvCell(2), At(EnvCell(2), .25f)); Move(root, At(EnvCell(4), .6f)); Move(root, At(EnvCell(5), .75f)); Release(root, At(EnvCell(5), .75f));
                    Need(PointValue(2) == null && PointValue(5).HasValue && Math.Abs(PointValue(5).Value - .75f) < .03f && PointValue(8).HasValue, "drag did not move the point to row 5 at three quarters: " + Points());
                    Undo.PerformUndo(); Need(Json() == twoPoints, "one Undo did not put the moved point back");
                });

                Check("a stroke across rows draws a point per row (rows a fast stroke skips are filled), Alt snaps to sixteenths, one Undo", () =>
                {
                    window.Pattern.tracks[0].automation.Clear(); Rebuild(); string before = Json();
                    PointerDrag(EnvCell(10), At(EnvCell(16), .9f), 3);
                    var drawn = Enumerable.Range(10, 7).Select(r => PointValue(r)).ToList();
                    Need(drawn.All(v => v.HasValue), "stroke did not put a point on every row: " + Points());
                    for (int i = 1; i < drawn.Count; i++) Need(drawn[i] >= drawn[i - 1] - .001f, "stroke values are not rising: " + Points());
                    Need(Math.Abs(drawn.Last().Value - .9f) < .03f, "stroke end value " + drawn.Last());
                    Undo.PerformUndo(); Need(Json() == before, "one Undo did not remove the stroke");
                    ClickAt(EnvCell(3), .52f, mods: EventModifiers.Alt); Need(PointValue(3) == .5f, "Alt did not snap to a sixteenth: " + PointValue(3));
                });

                Check("right-click deletes a point; right-click on the line chooses straight or held; the Cell box shows the same choice", () =>
                {
                    window.Pattern.tracks[0].automation.Clear(); Rebuild();
                    window.Select(1, Env(), false); Type("20"); window.Select(6, Env(), false); Type("e0"); window.Select(9, Env(), false); Type("40");
                    Need(Points() == "1:20 6:E0 9:40", "typed points: " + Points());
                    var root = EnvCell(3).panel.visualTree; Press(EnvCell(3), EnvCell(3).worldBound.center, 1); Release(root, EnvCell(3).worldBound.center, 1);
                    var hold = root.Query(className: "zui-menu__item").ToList().FirstOrDefault(x => x.Q<Label>(className: "zui-menu__label")?.text == "Hold, then jump"); Need(hold != null, "the line's menu has no Hold choice");
                    PointerClick(hold); Need(Points() == "1:20h 6:E0 9:40", "Hold did not set the segment: " + Points());
                    window.Select(7, Env(), false); var segmented = window.rootVisualElement.Q("envelope-segment"); Need(segmented != null, "Cell box has no Line/Hold choice");
                    Undo.PerformUndo(); Need(Points() == "1:20 6:E0 9:40", "Undo did not restore the straight segment");
                    Press(EnvCell(6), EnvCell(6).worldBound.center, 1); Release(root, EnvCell(6).worldBound.center, 1); Need(Points() == "1:20 9:40", "right-click did not delete the point: " + Points());
                    Undo.PerformUndo(); Need(Points() == "1:20 6:E0 9:40", "Undo did not restore the deleted point");
                });

                Check("keyboard: Left/Right nudge, Alt for fine, hex sets, Delete removes, Ctrl+Right leaves the column; outside Edit mode arrows move", () =>
                {
                    window.Select(6, Env(), false); Key(KeyCode.LeftArrow); Need(PointValue(6) == (0xE0 - 8) / 255f, "Left did not nudge by 8: " + Points());
                    Key(KeyCode.RightArrow, '\0', EventModifiers.Alt); Need(PointValue(6) == (0xE0 - 7) / 255f, "Alt+Right did not nudge by 1");
                    window.Select(4, Env(), false); Key(KeyCode.RightArrow); var made = PointValue(4);
                    Need(made.HasValue, "Right on the line did not make a point");
                    Key(KeyCode.Delete); Need(PointValue(4) == null, "Delete did not remove the point");
                    window.Select(6, Env(), false); Key(KeyCode.LeftArrow, '\0', EventModifiers.Control); Need(window.fields[window.sub].kind != FieldKind.Envelope, "Ctrl+Left did not leave the column");
                    window.editMode = false; window.Select(6, Env(), false); Key(KeyCode.LeftArrow); Need(window.fields[window.sub].kind != FieldKind.Envelope && Points().Contains("6:D9"), "outside Edit mode Left nudged instead of moving"); window.editMode = true;
                    window.Select(12, Env(), false); Type("g"); Need(PointValue(12) == null && window.help.text.Contains("hex"), "a non-hex key was not refused with a note");
                });

                Check("copy, cut, paste and Paste Insert move points; one Undo restores the pattern byte for byte", () =>
                {
                    window.Pattern.tracks[0].automation.Clear(); Rebuild();
                    window.Select(0, Env(), false); Type("10"); window.Select(1, Env(), false); Type("80"); window.Select(2, Env(), false); Type("f0");
                    window.Select(0, Env(), false); window.Select(2, Env(), true); Key(KeyCode.C, '\0', EventModifiers.Control);
                    window.Select(10, Env(), false); Key(KeyCode.V, '\0', EventModifiers.Control); Need(Points() == "0:10 1:80 2:F0 10:10 11:80 12:F0", "paste: " + Points());
                    string before = Json(); window.Select(1, Env(), false); Key(KeyCode.V, '\0', EventModifiers.Control | EventModifiers.Shift);
                    Need(Points() == "0:10 1:10 2:80 3:F0 4:80 5:F0 13:10 14:80 15:F0", "Paste Insert: " + Points());
                    Undo.PerformUndo(); Need(Json() == before, "one Undo did not restore the Paste Insert");
                    window.Select(10, Env(), false); window.Select(12, Env(), true); Key(KeyCode.X, '\0', EventModifiers.Control); Need(Points() == "0:10 1:80 2:F0", "cut: " + Points());
                    window.Select(20, Env(), false); Key(KeyCode.V, '\0', EventModifiers.Control); Need(Points().EndsWith("20:10 21:80 22:F0"), "cut points did not paste: " + Points());
                    // Paste into the end drops what does not fit.
                    window.Select(22, Env(), false); Key(KeyCode.V, '\0', EventModifiers.Control); Need(Points().EndsWith("22:10 23:80"), "points past the end were not dropped: " + Points());
                });

                Check("the drawing: a point sits at its value, the line takes the command's colour, rows stay 20 px; building the view writes nothing", () =>
                {
                    window.Pattern.tracks[0].automation.Clear(); Rebuild(); window.Select(2, Env(), false); Type("40"); window.Select(6, Env(), false); Type("c0");
                    string before = Json(); Rebuild(); Need(Json() == before, "building the view changed the song");
                    var cell = EnvCell(4); Need(cell != null && Math.Abs(cell.worldBound.height - 20) < .5f, "envelope row is not 20 px: " + cell?.worldBound.height);
                    Need(cell.Q("env-line") != null, "no line painter in the cell");
                    Need(cell.tooltip.Contains("on the line") && cell.tooltip.Contains("80"), "row 4 should read halfway (80) on the line: " + cell.tooltip);
                    Need(EnvCell(2).tooltip.Contains("point 0L 40"), "row 2 tooltip: " + EnvCell(2).tooltip);
                    Need(EnvCell(9).tooltip.Contains("no line"), "after the last point the tooltip should say there is no line");
                    var title = Cell("pattern-env-title-0-0"); Need(title != null && title.resolvedStyle.color != Color.white, "title is not drawn in the command's colour");
                });

                Check("the line drawn in the grid plays: rendered level follows it (song command on post gain)", () =>
                {
                    var t = song.model.tracks[0]; var p = ZTrackerCommandRegistry.Find(song.model, "LV");
                    window.Pattern.tracks[0].automation.Clear(); window.BindEnvelope(0, 0, p.uid); Rebuild();
                    var s0 = instrument.model.sampler.samples[0]; s0.loop = SampleLoop.Forward; s0.loopStartFrame = 0; s0.loopEndFrame = clip.samples;
                    window.Pattern.tracks[0].WriteLine(new PatternLine { line = 0, notes = { TrackerEngineCheck.Note(60) } });
                    window.Select(0, Env(), false); Type("ff"); window.Select(8, Env(), false); Type("00");
                    // LV spans 0…2, so FF is gain 2 and 00 silence; halfway down the line (row 4) is gain 1.
                    var data = ZTrackerMigration.Copy(song.model); data.ticksPerLine = 4;
                    var l = new NativeArray<float>(60000, Allocator.Persistent); var r = new NativeArray<float>(60000, Allocator.Persistent);
                    try
                    {
                        using (var e = new TrackerOffline(TrackerPreparedSong.Prepare(data, 48000))) { e.SendCommand(TrackerCommand.Play()); e.Render(l, r, 60000, 512); }
                        int row = 6000; double gain2 = l[100], gain1 = l[row * 4], end = l[row * 8 + 10];
                        Need(gain2 > 0 && Math.Abs(gain1 / gain2 - .5) < .002 && Math.Abs(end) < 1e-4, "levels at rows 0/4/8: " + gain2 + " / " + gain1 + " / " + end);
                    }
                    finally { l.Dispose(); r.Dispose(); }
                });
            }
            finally { if (window.envelopeGesture) window.EndEnvelopeGesture(); window.bindMenu?.Close(); window.StopPreview(); window.Close(); DestroyImmediate(window); Undo.ClearUndo(song); DestroyImmediate(song); DestroyImmediate(instrument); UnityEngine.Object.DestroyImmediate(clip); }
            report.AppendLine($"TOTAL passed={passed} failed={failed}"); return report.ToString();
        }
    }
}

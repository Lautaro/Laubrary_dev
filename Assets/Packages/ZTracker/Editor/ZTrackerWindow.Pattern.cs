using System;
using System.Collections.Generic;
using System.Linq;
using Laubrary.Zui;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Laubrary.ZTracker.Editor
{
    public sealed partial class ZTrackerWindow
    {
        int Notes(int t) => Mathf.Clamp(song.channels[t].noteColumnCount, 1, 12);
        int Effects(int t) => Mathf.Clamp(song.channels[t].fxColumnCount, 1, 8);
        int Subs(int t) => Notes(t) + 2 + Effects(t);
        int entryNote = 60;
        static readonly string[] CommandNames = { "None", "1 Pitch up", "2 Pitch down", "3 Glide", "4 Vibrato", "5 Glide + volume", "6 Vibrato + volume", "7 Tremolo", "8 Pan", "9 Sample offset", "A Volume slide", "B Order jump", "C Volume", "D Pattern break", "E Extended", "F Tempo", "G Macro", "H Macro slide", "I Preset" };
        static readonly string[] CommandTips = { "No effect.", "Pitch-slide speed per tick.", "Pitch-slide speed per tick.", "Glide to the note on this row; FF is instant, 00 continues.", "High nibble speed; low nibble depth.", "Continue glide and slide volume.", "Continue vibrato and slide volume.", "High nibble speed; low nibble depth.", "00 left, 80 center, FF right.", "Sample start offset.", "High nibble up speed, low nibble down speed.", "Jump to this order index.", "Volume from 00 to 40.", "Continue at this row of the next pattern.", "Extended tracker command.", "Below 20 sets ticks per row; otherwise sets BPM.", "High nibble macro index, low nibble value 0-F.", "High nibble macro index, low nibble target, reached over one row.", "00 Base; 01 first preset, 02 second preset." };
        void BuildPattern()
        {
            if (stage == null) return;
            Vector2 offset = grid?.scrollOffset ?? Vector2.zero; stage.Clear(); cellLabels.Clear();
            if (Pattern == null) return;
            row = Mathf.Clamp(row, 0, Pattern.rowCount - 1); track = Mathf.Clamp(track, 0, song.channelCount - 1); sub = Mathf.Clamp(sub, 0, Subs(track) - 1);
            stage.Add(Flow(Named(Z.MicroSlider("Octave", octave, 0, 8, "Pitch register for piano-key note entry; Z is C, Q is C one octave higher.", v => octave = (int)v, 120, decimals: 0), "octave"),
                Named(Z.MicroSlider("Step", step, 0, 16, "Rows advanced after entry; zero edits in place.", v => step = (int)v, 120, decimals: 0), "step"),
                Button("Insert row", "Shift rows down at the cursor, dropping the last row. Undo restores it.", () => ShiftRow(true)), Button("Delete row", "Shift rows up at the cursor and clear the last row.", () => ShiftRow(false)), Button("Commands", "Show effect choices and their parameter meanings.", ShowCommands)));
            var cellScroll = new ScrollView(); cellScroll.style.height = 74; cellScroll.style.flexShrink = 0;
            cellEditor = Z.Column(); cellScroll.Add(cellEditor); stage.Add(cellScroll); BuildCellEditor();
            grid = new ScrollView(ScrollViewMode.VerticalAndHorizontal); grid.name = "pattern-scroll"; grid.style.flexGrow = 1; grid.style.minHeight = 0;
            grid.contentContainer.style.alignItems = Align.FlexStart;
            var content = Z.Column(); content.name = "pattern-grid"; content.focusable = true;
            content.tooltip = "Click a cell. Z/S/X/D/C/V/G/B/H/N/J/M and Q/2/W/3/E/R/5/T/6/Y/7/U enter notes. Arrows or Tab navigate; Shift selects; Ctrl+C/X/V clipboard; Delete clears; backquote inserts OFF. Right click for editing actions.";
            content.RegisterCallback<KeyDownEvent>(PatternKey); content.RegisterCallback<KeyUpEvent>(e => { if (playback != null && !playback.IsSong) StopPreview(); });
            var header = Z.Row(); header.Add(Z.Text("Row", tooltip: "Zero-based pattern row.")); header[0].AddToClassList("tracker-row-number");
            for (int t = 0; t < song.channelCount; t++) for (int s = 0; s < Subs(t); s++) { string label = s < Notes(t) ? $"N{s + 1}" : s == Notes(t) ? "Inst" : s == Notes(t) + 1 ? "Vol" : $"FX{s - Notes(t) - 1}"; var l = Z.Text(label, tooltip: $"Track {t + 1}: {label}. Effects fan out across note columns."); l.AddToClassList("tracker-cell"); if (s == Subs(t) - 1) l.AddToClassList("tracker-track-gap"); header.Add(l); }
            var trackHeader = Z.Row(); var gutter = Z.Text("",tooltip:"Pattern tracks."); gutter.AddToClassList("tracker-row-number"); trackHeader.Add(gutter);
            for (int t=0;t<song.channelCount;t++) { var title = Z.Text("Track " + (t+1),tooltip:"Logical track " + (t+1) + "; all its note columns share instrument and volume."); title.style.width = Subs(t)*45+12; trackHeader.Add(title); }
            var headers = new ScrollView(ScrollViewMode.Horizontal); headers.name = "pattern-headers"; headers.style.height = 44; headers.style.flexShrink = 0; headers.horizontalScrollerVisibility = ScrollerVisibility.Hidden; headers.verticalScrollerVisibility = ScrollerVisibility.Hidden;
            var heading = Z.Column(); heading.style.alignItems = Align.FlexStart; heading.Add(trackHeader); heading.Add(header); headers.Add(heading); stage.Add(headers);
            grid.horizontalScroller.valueChanged += v => headers.scrollOffset = new Vector2(v,0);
            for (int r = 0; r < Pattern.rowCount; r++)
            {
                var line = Z.Row(); line.AddToClassList("tracker-grid-row"); line.EnableInClassList("tracker-beat", r % song.linesPerBeat == 0);
                var index = Z.Text(r.ToString("000"), tooltip: "Row " + r); index.AddToClassList("tracker-row-number"); line.Add(index);
                for (int t = 0; t < song.channelCount; t++) for (int s = 0; s < Subs(t); s++)
                {
                    var p = new Vector3Int(r, t, s); var l = Z.Text("...", tooltip: content.tooltip); l.name = $"cell-{r}-{t}-{s}"; l.userData = p; l.AddToClassList("tracker-cell"); if (s == Subs(t) - 1) l.AddToClassList("tracker-track-gap");
                    l.RegisterCallback<PointerDownEvent>(e => { if (e.shiftKey && anchorRow < 0) { anchorRow = row; anchorTrack = track; } else if (!e.shiftKey) anchorRow = -1; row = p.x; track = p.y; sub = p.z; pending = ""; content.Focus(); RefreshCells(); BuildCellEditor(); if (e.button == 1) ShowCellMenu(l); e.StopPropagation(); });
                    l.RegisterCallback<PointerMoveEvent>(e => { if ((e.pressedButtons & 1) != 0) { if (anchorRow < 0) { anchorRow = row; anchorTrack = track; } row = p.x; track = p.y; sub = p.z; RefreshCells(); } });
                    cellLabels[l.name] = l; line.Add(l);
                }
                content.Add(line);
            }
            grid.Add(content); stage.Add(grid); grid.schedule.Execute(() => grid.scrollOffset = offset); RefreshCells();
        }
        void RefreshCells()
        {
            if (Pattern == null) return;
            foreach (var kv in cellLabels)
            {
                var l = kv.Value; var p = (Vector3Int)l.userData; if (p.x >= Pattern.rowCount || p.y >= song.channelCount) continue;
                var c = Pattern.GetCell(p.x, p.y, song.channelCount); int n = Notes(p.y);
                string text;
                if (p.z < n) { int note = c.GetNote(p.z); text = NoteName(note); if (note >= 0 && note < 127 && c.instrument >= 0 && c.instrument < song.instruments.Count && song.instruments[c.instrument] != null && song.instruments[c.instrument].type == InstrumentType.Kit) text = song.instruments[c.instrument].GetNoteDisplayName(note); }
                else if (p.z == n) text = c.instrument >= 0 ? c.instrument.ToString("X2") : "..";
                else if (p.z == n + 1) text = c.volume == 255 ? ".." : c.volume.ToString("X2");
                else { int fx = p.z - n - 2; int cmd = c.GetEffectCmd(fx); text = cmd == 0 ? "..." : "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ"[Mathf.Clamp(cmd, 0, 35)] + c.GetEffectParam(fx).ToString("X2"); }
                bool selected = p.x == row && p.y == track && p.z == sub;
                l.text = selected && pending.Length > 0 ? pending.PadRight(p.z >= n + 2 ? 3 : 2, '_') : text;
                l.EnableInClassList("tracker-picked", selected);
                l.EnableInClassList("tracker-selected", anchorRow >= 0 && p.x >= Mathf.Min(row, anchorRow) && p.x <= Mathf.Max(row, anchorRow) && p.y >= Mathf.Min(track, anchorTrack) && p.y <= Mathf.Max(track, anchorTrack));
            }
        }
        public static string NoteName(int n) => n < 0 ? "..." : n == 127 ? "OFF" : new[] { "C-", "C#", "D-", "D#", "E-", "F-", "F#", "G-", "G#", "A-", "A#", "B-" }[Mathf.Clamp(n, 0, 126) % 12] + (n / 12);
        void BuildCellEditor()
        {
            if (cellEditor == null || Pattern == null) return; cellEditor.Clear(); var c = Pattern.GetCell(row, track, song.channelCount); int n = Notes(track);
            var group = Flow(); group.Add(Z.Text($"{row:000} / T{track + 1}", tooltip: "Current row and track."));
            if (sub < n)
            {
                int slot = sub;
                group.Add(Named(Z.MicroSlider("Note", c.GetNote(slot) < 0 || c.GetNote(slot) == 127 ? entryNote : c.GetNote(slot), 0, 126, "MIDI note for this column; sets the selected instrument when the row has none.", v => SongEdit("note", () => { entryNote=(int)v; c.SetNote(slot,entryNote); if(c.instrument<0 && song.instruments.Count>0) c.instrument=Mathf.Clamp(entryInstrument,0,song.instruments.Count-1); }), 135, decimals: 0), "cell-note"));
                group.Add(Button("Enter note", "Write the chosen pitch with the selected instrument and advance by Step.", () => EnterNote(c.GetNote(slot) < 0 || c.GetNote(slot)==127 ? entryNote : c.GetNote(slot)), "enter-note"));
                group.Add(Button("OFF", "Release the voice on this note column.", () => { SongEdit("note off", () => c.SetNote(slot, 127)); Advance(); }, "note-off"));
            }
            else if (sub == n)
                group.Add(Z.MiniRadio(c.instrument + 1, new[] { "None" }.Concat(song.instruments.Select((i, k) => $"{k:X2} {(i != null ? i.name : "Missing")}")).ToArray(), "Instrument shared by all note columns on this row.", v => SongEdit("cell instrument", () => { c.instrument = v - 1; if (v > 0) entryInstrument = v - 1; }), wrap: true));
            else if (sub == n + 1)
            {
                group.Add(Z.MicroSlider("Volume", c.volume == 255 ? 64 : c.volume, 0, 64, "Row volume; 40 hex is full level.", v => SongEdit("cell volume", () => c.volume = (int)v), 140, decimals: 0));
                group.Add(Button("Default", "Use the instrument's default volume.", () => SongEdit("default volume", () => c.volume = 255)));
            }
            else
            {
                int slot = sub - n - 2; int cmd = c.GetEffectCmd(slot);
                group.Add(Button(cmd < CommandNames.Length ? CommandNames[cmd] : "Command " + cmd, "Pick an effect command; current parameter meaning: " + CommandTips[Mathf.Clamp(cmd, 0, CommandTips.Length - 1)], () => ShowCommands()));
                group.Add(Z.MicroSlider("Param", c.GetEffectParam(slot), 0, 255, CommandTips[Mathf.Clamp(cmd, 0, CommandTips.Length - 1)], v => SongEdit("effect parameter", () => c.SetEffect(slot, cmd, (int)v)), 140, decimals: 0));
            }
            group.Add(Button("Clear", "Clear the current sub-column or selected rectangle.", Clear, "clear-cell")); cellEditor.Add(group);
        }
        void EnterNote(int pitch)
        {
            if (Pattern == null || sub >= Notes(track)) return;
            var c = Pattern.GetCell(row, track, song.channelCount); int slot = sub; entryNote=pitch;
            SongEdit("enter note", () => { c.SetNote(slot, Mathf.Clamp(pitch, 0, 126)); c.instrument = song.instruments.Count > 0 ? Mathf.Clamp(entryInstrument, 0, song.instruments.Count - 1) : -1; }); Advance();
        }
        void Advance() { row = (row + step) % Pattern.rowCount; pending = ""; RefreshCells(); BuildCellEditor(); Reveal(); }
        void Reveal() { if (cellLabels.TryGetValue($"cell-{row}-{track}-{sub}", out var l)) grid?.ScrollTo(l); }
        void Navigate(int delta) { sub += delta; if (sub < 0) { track = (track - 1 + song.channelCount) % song.channelCount; sub = Subs(track) - 1; } else if (sub >= Subs(track)) { track = (track + 1) % song.channelCount; sub = 0; } }
        void PatternKey(KeyDownEvent e)
        {
            if (Pattern == null) return;
            bool handled = true, navigated = false;
            if (e.keyCode == KeyCode.Escape) StopPreview();
            else if (e.keyCode == KeyCode.Space) { if (playback != null) StopPreview(); else Play(e.ctrlKey || e.commandKey); }
            else if (e.ctrlKey || e.commandKey)
            {
                switch (e.keyCode) { case KeyCode.C: Copy(); break; case KeyCode.X: Copy(); Clear(); break; case KeyCode.V: Paste(); break; case KeyCode.Z: if (e.shiftKey) Undo.PerformRedo(); else Undo.PerformUndo(); break; case KeyCode.I: Interpolate(); break; case KeyCode.A: anchorRow = 0; anchorTrack = 0; row = Pattern.rowCount - 1; track = song.channelCount - 1; break; default: handled = false; break; }
            }
            else
            {
                if (e.shiftKey && anchorRow < 0 && (e.keyCode >= KeyCode.UpArrow && e.keyCode <= KeyCode.LeftArrow)) { anchorRow = row; anchorTrack = track; }
                switch (e.keyCode)
                {
                    case KeyCode.UpArrow: row = (row - 1 + Pattern.rowCount) % Pattern.rowCount; navigated = true; break;
                    case KeyCode.DownArrow: row = (row + 1) % Pattern.rowCount; navigated = true; break;
                    case KeyCode.LeftArrow: if (e.shiftKey) { track = (track - 1 + song.channelCount) % song.channelCount; sub = 0; } else Navigate(-1); navigated = true; break;
                    case KeyCode.RightArrow: if (e.shiftKey) { track = (track + 1) % song.channelCount; sub = 0; } else Navigate(1); navigated = true; break;
                    case KeyCode.Tab: Navigate(e.shiftKey ? -1 : 1); navigated = true; break;
                    case KeyCode.Home: row = 0; navigated = true; break; case KeyCode.End: row = Pattern.rowCount - 1; navigated = true; break;
                    case KeyCode.PageUp: row = Mathf.Max(0, row - 16); navigated = true; break; case KeyCode.PageDown: row = Mathf.Min(Pattern.rowCount - 1, row + 16); navigated = true; break;
                    case KeyCode.KeypadEnter: step = new[] { 0, 1, 2, 4, 8 }[(Array.IndexOf(new[] { 0, 1, 2, 4, 8 }, step) + 1) % 5]; break;
                    case KeyCode.Equals: case KeyCode.KeypadPlus: octave = Mathf.Min(8, octave + 1); break;
                    case KeyCode.Minus: case KeyCode.KeypadMinus: octave = Mathf.Max(0, octave - 1); break;
                    case KeyCode.Delete: case KeyCode.Backspace: Clear(); if (anchorRow < 0) Advance(); break;
                    case KeyCode.BackQuote: case KeyCode.Backslash: if (sub < Notes(track)) { var c = Pattern.GetCell(row, track, song.channelCount); SongEdit("note off", () => c.SetNote(sub, 127)); Advance(); } break;
                    default:
                        if (sub < Notes(track)) { int k = "zsxdcvgbhnjmq2w3er5t6y7u".IndexOf(char.ToLowerInvariant(e.character)); if (k >= 0) { int pitch = octave * 12 + k; EnterNote(pitch); if (entryInstrument < song.instruments.Count && song.instruments[entryInstrument] != null) { instrument = song.instruments[entryInstrument]; Audition(pitch); } } else handled = false; }
                        else { char ch = char.ToUpperInvariant(e.character); string allowed = sub >= Notes(track) + 2 && pending.Length == 0 ? "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ" : "0123456789ABCDEF"; if (allowed.Contains(ch.ToString()) && ch != '\0') { pending += ch; int need = sub >= Notes(track) + 2 ? (pending[0] == 'I' ? 2 : 3) : 2; if (pending.Length == need) { var c = Pattern.GetCell(row, track, song.channelCount); string value = pending; SongEdit("cell entry", () => { if (sub == Notes(track)) { c.instrument = Convert.ToInt32(value, 16); entryInstrument = c.instrument; } else if (sub == Notes(track) + 1) c.volume = Convert.ToInt32(value, 16); else c.SetEffect(sub - Notes(track) - 2, "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ".IndexOf(value[0]), Convert.ToInt32(value.Substring(1), 16)); }); Advance(); } } else handled = false; }
                        break;
                }
            }
            if (navigated) { pending = ""; if (!e.shiftKey) anchorRow = -1; Reveal(); }
            if (handled) { RefreshCells(); BuildCellEditor(); e.StopPropagation(); e.PreventDefault(); }
        }
        void Bounds(out int r0, out int r1, out int t0, out int t1) { r0 = anchorRow < 0 ? row : Mathf.Min(row, anchorRow); r1 = anchorRow < 0 ? row : Mathf.Max(row, anchorRow); t0 = anchorRow < 0 ? track : Mathf.Min(track, anchorTrack); t1 = anchorRow < 0 ? track : Mathf.Max(track, anchorTrack); }
        void Copy() { Bounds(out int r0, out int r1, out int t0, out int t1); clipboardWidth = t1 - t0 + 1; clipboard = new List<ZTrackerCellSerialized>(); for (int r = r0; r <= r1; r++) for (int t = t0; t <= t1; t++) clipboard.Add(Clone(Pattern.GetCell(r, t, song.channelCount))); }
        void Paste() { if (clipboard == null) return; SongEdit("paste cells", () => { for (int i = 0; i < clipboard.Count; i++) { int r = row + i / clipboardWidth, t = track + i % clipboardWidth; if (r < Pattern.rowCount && t < song.channelCount) Pattern.SetCell(r, t, song.channelCount, Clone(clipboard[i])); } }); BuildCellEditor(); }
        void Clear() { if (Pattern == null) return; Bounds(out int r0, out int r1, out int t0, out int t1); SongEdit("clear cells", () => { for (int r = r0; r <= r1; r++) for (int t = t0; t <= t1; t++) { var c = Pattern.GetCell(r, t, song.channelCount); if (anchorRow >= 0) Pattern.SetCell(r, t, song.channelCount, new ZTrackerCellSerialized()); else if (sub < Notes(t)) c.SetNote(sub, -1); else if (sub == Notes(t)) c.instrument = -1; else if (sub == Notes(t) + 1) c.volume = 255; else c.SetEffect(sub - Notes(t) - 2, 0, 0); } }); BuildCellEditor(); }
        void Transpose(int semitones) { Bounds(out int r0, out int r1, out int t0, out int t1); SongEdit("transpose", () => { for (int r = r0; r <= r1; r++) for (int t = t0; t <= t1; t++) { var c = Pattern.GetCell(r, t, song.channelCount); for (int n = 0; n < Notes(t); n++) if (c.GetNote(n) >= 0 && c.GetNote(n) < 127) c.SetNote(n, Mathf.Clamp(c.GetNote(n) + semitones, 0, 126)); } }); }
        void Interpolate() { Bounds(out int r0, out int r1, out int t0, out int t1); if (r0 == r1) return; SongEdit("interpolate", () => { for (int t = t0; t <= t1; t++) { var a = Pattern.GetCell(r0, t, song.channelCount); var b = Pattern.GetCell(r1, t, song.channelCount); for (int r = r0 + 1; r < r1; r++) { var c = Pattern.GetCell(r, t, song.channelCount); float f = (r - r0) / (float)(r1 - r0); if (sub == Notes(t) + 1 && a.volume != 255 && b.volume != 255) c.volume = Mathf.RoundToInt(Mathf.Lerp(a.volume, b.volume, f)); else if (sub >= Notes(t) + 2) { int slot = sub - Notes(t) - 2; c.SetEffect(slot, a.GetEffectCmd(slot), Mathf.RoundToInt(Mathf.Lerp(a.GetEffectParam(slot), b.GetEffectParam(slot), f))); } } } }); }
        void ShiftRow(bool insert) { SongEdit(insert ? "insert row" : "delete row", () => { for (int t = 0; t < song.channelCount; t++) { if (insert) { for (int r = Pattern.rowCount - 1; r > row; r--) Pattern.SetCell(r, t, song.channelCount, Clone(Pattern.GetCell(r - 1, t, song.channelCount))); Pattern.SetCell(row, t, song.channelCount, new ZTrackerCellSerialized()); } else { for (int r = row; r < Pattern.rowCount - 1; r++) Pattern.SetCell(r, t, song.channelCount, Clone(Pattern.GetCell(r + 1, t, song.channelCount))); Pattern.SetCell(Pattern.rowCount - 1, t, song.channelCount, new ZTrackerCellSerialized()); } } }); BuildCellEditor(); }
        void ShowCellMenu(VisualElement anchor) { Z.Menu(anchor).Item("Copy", "Copy selected cells.", Copy).Item("Cut", "Copy and clear selected cells.", () => { Copy(); Clear(); }).Item("Paste", "Paste copied cells at the cursor.", Paste).Item("Clear", "Clear selected cells.", Clear).Item("Semitone up", "Raise selected notes one semitone.", () => Transpose(1)).Item("Semitone down", "Lower selected notes one semitone.", () => Transpose(-1)).Item("Octave up", "Raise selected notes one octave.", () => Transpose(12)).Item("Octave down", "Lower selected notes one octave.", () => Transpose(-12)).Item("Interpolate", "Fill volume or effect parameters between the selection endpoints.", Interpolate).Item("Copy track", "Copy the whole selected track.", () => { anchorRow = 0; row = Pattern.rowCount - 1; anchorTrack = track; Copy(); anchorRow = -1; }).Item("Clear track", "Clear the whole selected track.", () => { anchorRow = 0; row = Pattern.rowCount - 1; anchorTrack = track; Clear(); anchorRow = -1; }).Show(); }
        void ShowCommands()
        {
            var menu = Z.Menu(cellEditor);
            for (int i = 0; i < CommandNames.Length; i++) { int cmd = i; menu.Item(CommandNames[i], CommandTips[i], () => { if (sub < Notes(track) + 2) sub = Notes(track) + 2; var c = Pattern.GetCell(row, track, song.channelCount); SongEdit("effect command", () => c.SetEffect(sub - Notes(track) - 2, cmd, c.GetEffectParam(sub - Notes(track) - 2))); BuildCellEditor(); }); } menu.Show();
        }
    }
}

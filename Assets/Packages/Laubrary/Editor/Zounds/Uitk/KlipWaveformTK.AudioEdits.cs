using System;
using System.Collections.Generic;
using Laubrary.Zui;
using UnityEngine;
using UnityEngine.UIElements;
using Verb = Laubrary.Zounds.Destructive.ZoundsAudioEdits.Verb;
using Edits = Laubrary.Zounds.Destructive.ZoundsAudioEdits;
using WaveMouse = Laubrary.Zounds.ZoundsMachineSettings.WaveMouse;

namespace Laubrary.Zounds.Uitk {

    /// <summary>
    /// The Klip editor waveform's editing (destructive editing 2026-10-09; the shared verbs, the selection menu and the
    /// mouse schemes 2026-10-10): the edit bar over the waveform, the edit marker and selection drawn on the waveform
    /// (apart from the playheads), the right-click menu on a selection, and the keys.
    ///
    /// The mouse follows the Settings tab's "Waveform mouse" (per machine). Click to select (the default): a left click
    /// places the edit marker, a left drag selects (Shift extends), a double-click plays from the pointer, a right-click
    /// plays from the pointer, and a right-click INSIDE a selection opens its menu. Halves: the upper half places the
    /// marker, selects and opens a selection's menu; the lower half plays from the pointer with either button. A press is
    /// only the host's where no trim edge or point of the curve being edited takes it.
    ///
    /// Every verb has one path (<see cref="RunVerb"/>), whether it comes from the bar, the menu or a key: the audio edits
    /// go to the host (<see cref="onAudioEdit"/>, which owns the sound, its file and its playback), the plays and the
    /// trim to their own host actions.
    /// </summary>
    public partial class KlipWaveformTK {

        /// <summary>The host carries out an audio edit (it owns the sound, its playback and its window).</summary>
        internal Action<Verb> onAudioEdit;
        /// <summary>The host plays only this sound from one second of its file to another (again while it sounds: stop).</summary>
        internal Action<float, float> onPlayRange;
        /// <summary>The host trims the sound to a range of seconds of its file (an Undo step; a shared sound goes to a copy).</summary>
        internal Action<double, double> onTrimToSelection;
        /// <summary>Kept check 37 only: receives every verb instead of carrying it out.</summary>
        internal static Action<KlipWaveformTK, EditVerb> verbProbe;
        /// <summary>The selection menu last opened (kept check 37).</summary>
        internal ZuiPopover lastMenu;

        /// <summary>The edit marker and the selection, in seconds of the file; marker below 0 = none placed yet.</summary>
        internal double cursor = -1d, selA, selB;
        internal bool HasSelection => selB > selA + 1e-9;
        VisualElement selBand, editCursor, zoneLine, zoneShade, zoneEdit, zonePlay, menuAnchor;
        readonly Dictionary<EditVerb, Button> editButtons = new Dictionary<EditVerb, Button>();
        Label editReadout;
        string lastEditMessage = "";
        bool selecting; double selAnchor; float selDownX;
        double prevCursor = -1d, prevA, prevB;

        const float EditH = 18f;

        /// <summary>The bar's verbs, in their groups: listen, trim and delete, cut / copy / paste / insert, duplicate.</summary>
        internal static readonly EditVerb[][] BarGroups = {
            new[] { EditVerb.PlayFromMarker, EditVerb.PlaySelection },
            new[] { EditVerb.Trim, EditVerb.Delete },
            new[] { EditVerb.Cut, EditVerb.Copy, EditVerb.Paste, EditVerb.Insert },
            new[] { EditVerb.DuplicateInsert, EditVerb.DuplicatePaste },
        };

        /// <summary>The selection menu's verbs, in order (null: a divider).</summary>
        internal static readonly EditVerb?[] MenuVerbs = {
            EditVerb.PlaySelection, null,
            EditVerb.Cut, EditVerb.Copy, EditVerb.Paste, EditVerb.Insert, EditVerb.Delete, null,
            EditVerb.DuplicateInsert, EditVerb.DuplicatePaste, null,
            EditVerb.Trim, EditVerb.SelectAll,
        };

        internal IReadOnlyDictionary<EditVerb, Button> EditButtons => editButtons;
        static WaveMouse Scheme => ZoundsMachineSettings.WaveMouseScheme;

        // ─────────────────────────── building ───────────────────────────

        partial void AddEditBar() {
            // One fixed row, always all there: what changes is only whether a button is enabled and what the readout says.
            Add(EditBar());
            Add(Space(3f));
        }

        partial void AddEditMarks() {
            // On the surface's marks layer: under the playheads, trim edges and curves.
            zoneShade = Abs(); zoneShade.AddToClassList("zs-klip-waveform__play-zone"); surface.marks.Add(zoneShade);
            zoneLine = Abs(); zoneLine.AddToClassList("zs-klip-waveform__zone-line"); surface.marks.Add(zoneLine);
            // Halves: a small mark in each half saying what it is for (select, play); the waveform's tooltip says more.
            zoneEdit = Z.Icon("cursor-text", 12f) ?? Abs(); zoneEdit.pickingMode = PickingMode.Ignore;
            zoneEdit.AddToClassList("zs-wave-surface__positioned"); zoneEdit.AddToClassList("zs-klip-waveform__zone-icon"); surface.marks.Add(zoneEdit);
            zonePlay = Z.Icon("play", 12f) ?? Abs(); zonePlay.pickingMode = PickingMode.Ignore;
            zonePlay.AddToClassList("zs-wave-surface__positioned"); zonePlay.AddToClassList("zs-klip-waveform__zone-icon"); surface.marks.Add(zonePlay);
            selBand = Abs(); selBand.AddToClassList("zs-klip-waveform__selection"); surface.marks.Add(selBand);
            editCursor = Abs(); editCursor.AddToClassList("zs-klip-waveform__edit-cursor"); surface.marks.Add(editCursor);
            // Where the selection menu opens: a point at the pointer, on the overlay.
            menuAnchor = Abs(); surface.overlay.Add(menuAnchor);
        }

        VisualElement EditBar() {
            var r = new VisualElement();
            r.AddToClassList("zs-klip-waveform__edit-bar");
            r.style.height = EditH;
            for (int g = 0; g < BarGroups.Length; g++) {
                if (g > 0) r.Add(Gap(6f));
                var group = BarGroups[g];
                for (int i = 0; i < group.Length; i++) {
                    var v = group[i];
                    var corners = group.Length == 1 ? ZUICornerMask.All : i == 0 ? ZUICornerMask.Left : i == group.Length - 1 ? ZUICornerMask.Right : ZUICornerMask.None;
                    var b = EditVerbsTK.Button(v, "", () => RunVerb(v), corners, EditH);
                    b.AddToClassList("zs-klip-waveform__edit-button");
                    editButtons[v] = b;
                    r.Add(b);
                }
            }
            r.Add(Gap(8f));
            editReadout = new Label { pickingMode = PickingMode.Position };
            editReadout.AddToClassList("zs-lbl"); editReadout.AddToClassList("zs-greymini");
            editReadout.AddToClassList("zs-klip-waveform__edit-readout");
            r.Add(editReadout);
            SyncEditBar();
            return r;
        }

        // ─────────────────────────── the verbs ───────────────────────────

        static string PlaceMarkerFirst => Scheme == WaveMouse.Halves ? "Place a marker first (click the upper half of the wave)." : "Place a marker first (click the wave).";
        static string SelectFirst => Scheme == WaveMouse.Halves ? "Select some audio first: drag across the upper half of the wave (with no curve being edited)."
                                                                 : "Select some audio first: drag across the wave (with no curve being edited).";

        /// <summary>Whether a verb can run now, and the tooltip that says what it does (or why it cannot).</summary>
        internal bool CanRun(EditVerb v, out string tip) {
            bool sel = HasSelection, clip = Edits.HasClipboard, at = cursor >= 0d, audio = model.OriginalClip != null;
            string kept = clip ? Edits.Seconds(Edits.ClipboardSeconds) + " of audio from '" + Edits.ClipboardFrom + "'" : null;
            const string noKept = "Nothing kept yet: cut or copy some audio first.";
            bool ok;
            switch (v) {
                case EditVerb.PlayFromMarker:
                    ok = at && audio;
                    tip = ok ? "Play only this sound from the edit marker to the end of what it plays (Space when nothing is selected). Again while it sounds: stop." : PlaceMarkerFirst;
                    return ok;
                case EditVerb.PlaySelection:
                    ok = sel && audio;
                    tip = ok ? "Play only the selected audio of this sound, once (Space). Again while it sounds: stop." : SelectFirst;
                    return ok;
                case EditVerb.Trim:
                    ok = sel && audio;
                    tip = ok ? "Trim the sound to the selection (T): only the selected part plays. The audio file is not changed; drag the trim edges to change it again, or switch the curve bar's Trim off to hear the whole recording." : SelectFirst;
                    return ok;
                case EditVerb.Delete:
                    ok = sel;
                    tip = ok ? "Delete the selection (Delete): the audio after it moves up to close the gap. Nothing is kept to paste (Cut keeps it). The trim, the curves and every track's excerpt stay on the same audio." : SelectFirst;
                    return ok;
                case EditVerb.Cut:
                    ok = sel;
                    tip = ok ? "Cut the selection out (Ctrl+X): the audio after it moves up to close the gap, and the cut is kept to paste. The trim, the curves and every track's excerpt stay on the same audio." : SelectFirst;
                    return ok;
                case EditVerb.Copy:
                    ok = sel;
                    tip = ok ? "Keep the selected audio to paste or insert (Ctrl+C). Nothing changes." : SelectFirst;
                    return ok;
                case EditVerb.Paste:
                    ok = clip && at;
                    tip = ok ? "Write the kept " + kept + " over the audio from the edit marker on (Ctrl+V). Nothing moves; the sound gets longer only if it runs past the end." : !at ? PlaceMarkerFirst : noKept;
                    return ok;
                case EditVerb.Insert:
                    ok = clip && at;
                    tip = ok ? "Put the kept " + kept + " in at the edit marker (Ctrl+Shift+V): the audio after it moves on to make room." : !at ? PlaceMarkerFirst : noKept;
                    return ok;
                case EditVerb.DuplicateInsert:
                    ok = sel;
                    tip = ok ? "Put a copy of the selection in right after it (Ctrl+D); the audio after it moves on. The copy is then selected, so pressing again repeats it again." : SelectFirst;
                    return ok;
                case EditVerb.DuplicatePaste:
                    ok = sel;
                    tip = ok ? "Write a copy of the selection over the audio right after it (Ctrl+Shift+D). Nothing moves. The copy is then selected." : SelectFirst;
                    return ok;
                case EditVerb.SelectAll:
                    ok = audio;
                    tip = "Select all the audio this sound plays: its trim, or the whole recording when the trim is off (Ctrl+A).";
                    return ok;
                default:
                    tip = "";
                    return false;
            }
        }

        /// <summary>Runs a verb: the one path for the bar, the selection menu and the keys.</summary>
        internal void RunVerb(EditVerb v) {
            if (!CanRun(v, out _)) return;
            if (verbProbe != null) { verbProbe(this, v); return; }
            switch (v) {
                case EditVerb.PlayFromMarker: {
                    ((IWaveSurfaceHost)this).Heard(out _, out float end);
                    float from = (float)cursor;
                    onPlayRange?.Invoke(from, from < end ? end : model.OriginalClip.length);
                    return;
                }
                case EditVerb.PlaySelection: onPlayRange?.Invoke((float)selA, (float)selB); return;
                case EditVerb.Trim: onTrimToSelection?.Invoke(selA, selB); return;
                case EditVerb.SelectAll: {
                    ((IWaveSurfaceHost)this).Heard(out float a, out float b);
                    selA = a; selB = b; cursor = a;
                    SyncEditBar(); Refresh();
                    return;
                }
                case EditVerb.Delete: onAudioEdit?.Invoke(Verb.Delete); return;
                case EditVerb.Cut: onAudioEdit?.Invoke(Verb.Cut); return;
                case EditVerb.Copy: onAudioEdit?.Invoke(Verb.Copy); return;
                case EditVerb.Paste: onAudioEdit?.Invoke(Verb.Paste); return;
                case EditVerb.Insert: onAudioEdit?.Invoke(Verb.Insert); return;
                case EditVerb.DuplicateInsert: onAudioEdit?.Invoke(Verb.DuplicateInsert); return;
                case EditVerb.DuplicatePaste: onAudioEdit?.Invoke(Verb.DuplicatePaste); return;
            }
        }

        /// <summary>The menu of a selection's edits, opened at the pointer (a right-click inside the selection).</summary>
        internal void ShowSelectionMenu(Vector2 at) {
            Place(menuAnchor, new Rect(at.x, at.y, 1f, 1f));
            var menu = Z.Menu(menuAnchor);
            menu.Width(210f);
            foreach (var v in MenuVerbs) {
                if (!v.HasValue) { menu.Separator(); continue; }
                var verb = v.Value;
                bool on = CanRun(verb, out string tip);
                menu.Item(verb, on, tip, () => RunVerb(verb));
            }
            lastMenu = menu.Show();
        }

        /// <summary>The host's report after an edit: the readout's message, and where the marker and selection now are.</summary>
        internal void EditDone(string message, double newCursor, double newA, double newB) {
            lastEditMessage = message ?? "";
            cursor = newCursor; selA = newA; selB = newB;
            SyncEditBar();
            Refresh();
        }

        internal void SetEditMessage(string message) { lastEditMessage = message ?? ""; SyncEditBar(); }

        (bool, double, double, double, double, string, bool, WaveMouse) editBarKey;
        bool editBarSynced;

        void SyncEditBar() {
            if (editReadout == null) return;
            bool sel = HasSelection, at = cursor >= 0d;
            var key = (Edits.HasClipboard, Edits.ClipboardSeconds, cursor, selA, selB, lastEditMessage, enabledInHierarchy, Scheme);
            if (editBarSynced && key.Equals(editBarKey)) return;
            editBarKey = key; editBarSynced = true;
            foreach (var kv in editButtons) {
                bool on = CanRun(kv.Key, out string tip);
                kv.Value.SetEnabled(on); kv.Value.tooltip = tip;
            }
            string s = sel ? Edits.Seconds(selA) + " – " + Edits.Seconds(selB) + " (" + Edits.Seconds(selB - selA) + ")"
                     : at ? "marker at " + Edits.Seconds(cursor) : "";
            string text = lastEditMessage.Length == 0 ? s : (s.Length > 0 ? s + "   ·   " : "") + lastEditMessage;
            if (editReadout.text != text) editReadout.text = text;
            editReadout.tooltip = text.Length > 0 ? text : "The edit marker, the selection and the result of the last audio edit show here. " +
                                  (Scheme == WaveMouse.Halves ? "Click the upper half of the waveform to place the marker; drag there to select (Shift+click extends)."
                                                              : "Click the waveform to place the marker; drag to select (Shift+click extends).");
        }

        // ─────────────────────────── drawing ───────────────────────────

        partial void PlaceEditMarks(Rect r) {
            var clip = model.OriginalClip;
            bool sel = HasSelection && clip != null;
            selBand.style.display = sel ? DisplayStyle.Flex : DisplayStyle.None;
            if (sel) Place(selBand, Rect.MinMaxRect(Mathf.Clamp(model.TimeToXIn((float)selA, r), r.x, r.xMax), r.y, Mathf.Clamp(model.TimeToXIn((float)selB, r), r.x, r.xMax), r.yMax));
            bool at = cursor >= 0d && clip != null;
            float cx = at ? model.TimeToXIn((float)cursor, r) : 0f;
            at &= cx >= r.x - 0.5f && cx <= r.xMax + 0.5f;
            editCursor.style.display = at ? DisplayStyle.Flex : DisplayStyle.None;
            if (at) Place(editCursor, new Rect(cx - 0.5f, r.y, 1f, r.height));
            // Halves: a faint line through the middle, and a faint shade over the lower (listening) half.
            bool halves = Scheme == WaveMouse.Halves;
            zoneLine.style.display = zoneShade.style.display = zoneEdit.style.display = zonePlay.style.display = halves ? DisplayStyle.Flex : DisplayStyle.None;
            if (halves) {
                float mid = Mathf.Round(r.y + r.height * 0.5f);
                Place(zoneLine, new Rect(r.x, mid, r.width, 1f));
                Place(zoneShade, Rect.MinMaxRect(r.x, mid + 1f, r.xMax, r.yMax));
                // At the left edge, in the middle of each half (the pitch axis labels sit at the top, middle and bottom).
                Place(zoneEdit, new Rect(r.x + 4f, Mathf.Round(r.y + r.height * 0.25f - 6f), 12f, 12f));
                Place(zonePlay, new Rect(r.x + 4f, Mathf.Round(r.y + r.height * 0.75f - 6f), 12f, 12f));
            }
            SyncEditBar();
        }

        // ─────────────────────────── input ───────────────────────────

        partial void EditKey(KeyDownEvent e, ref bool handled) {
            bool ctrl = e.ctrlKey || e.commandKey;
            EditVerb? verb = null;
            if (ctrl && e.keyCode == KeyCode.X) verb = EditVerb.Cut;
            else if (ctrl && e.keyCode == KeyCode.C) verb = EditVerb.Copy;
            else if (ctrl && e.keyCode == KeyCode.V) verb = e.shiftKey ? EditVerb.Insert : EditVerb.Paste;
            else if (ctrl && e.keyCode == KeyCode.D) verb = e.shiftKey ? EditVerb.DuplicatePaste : EditVerb.DuplicateInsert;
            else if (ctrl && e.keyCode == KeyCode.A) verb = EditVerb.SelectAll;
            else if (!ctrl && e.keyCode == KeyCode.Space) verb = HasSelection ? EditVerb.PlaySelection : EditVerb.PlayFromMarker;
            else if (!ctrl && e.keyCode == KeyCode.T && HasSelection) verb = EditVerb.Trim;
            // Delete removes the selected audio unless a curve is being edited (then it removes that curve's selected points).
            else if (!ctrl && (e.keyCode == KeyCode.Delete || e.keyCode == KeyCode.Backspace) && HasSelection && ((IWaveSurfaceHost)this).SelectedCurve < 0) verb = EditVerb.Delete;
            if (verb.HasValue) {
                RunVerb(verb.Value);
                handled = true;
                return;
            }
            if (e.keyCode == KeyCode.Escape && HasSelection) { selA = selB = 0d; SyncEditBar(); Refresh(); handled = true; }
        }

        /// <summary>
        /// A left press nothing else took: by the mouse scheme, the edit marker (a click), a selection (a drag; Shift
        /// extends from the marker), or a play from the pointer (a double-click; in Halves, any click in the lower half).
        /// </summary>
        partial void EditPress(PointerDownEvent e, Vector2 m, Rect r, ref bool handled) {
            var clip = model.OriginalClip;
            if (e.button != 0 || clip == null) return;
            if (Scheme == WaveMouse.Halves && m.y >= r.y + r.height * 0.5f) {
                // The listening half: a click plays from the pointer (a double-click's second press would only stop it again).
                if (e.clickCount < 2) surface.PlayAt(m.x);
                handled = true;
                return;
            }
            if (Scheme == WaveMouse.ClickSelects && e.clickCount == 2) {
                // A double-click plays from the pointer; the marker and selection stay as they were before its first click.
                cursor = prevCursor; selA = prevA; selB = prevB; selecting = false;
                surface.PlayAt(m.x);
                SyncEditBar(); Refresh();
                handled = true;
                return;
            }
            prevCursor = cursor; prevA = selA; prevB = selB;
            double t = Mathf.Clamp(model.XToTimeIn(m.x, r), 0f, clip.length);
            if (e.shiftKey && cursor >= 0d) {
                selAnchor = HasSelection ? (Math.Abs(t - selA) < Math.Abs(t - selB) ? selB : selA) : cursor;
                selA = Math.Min(selAnchor, t); selB = Math.Max(selAnchor, t); cursor = selA;
            }
            else { selAnchor = t; cursor = t; selA = selB = 0d; }
            selecting = true; selDownX = m.x;
            area.CapturePointer(e.pointerId);
            SyncEditBar(); Refresh();
            handled = true;
        }

        partial void EditMove(Vector2 m, ref bool handled) {
            if (!selecting) return;
            handled = true;
            var clip = model.OriginalClip;
            if (clip == null) return;
            double t = Mathf.Clamp(model.XToTimeIn(m.x, surface.AreaRect), 0f, clip.length);
            if (Mathf.Abs(m.x - selDownX) > 3f || HasSelection) { selA = Math.Min(selAnchor, t); selB = Math.Max(selAnchor, t); cursor = selA; Refresh(); }
        }

        partial void EditRelease() {
            if (!selecting) return;
            selecting = false;
            SyncEditBar();
        }

        /// <summary>A right press nothing on the surface took: inside a selection (in Halves, in the upper half) it opens
        /// the selection's menu; anywhere else it is left to play from the pointer.</summary>
        bool ContextPressHere(Vector2 m, Rect r) {
            var clip = model.OriginalClip;
            if (clip == null || !HasSelection) return false;
            if (Scheme == WaveMouse.Halves && m.y >= r.y + r.height * 0.5f) return false;
            double t = model.XToTimeIn(m.x, r);
            if (t < selA || t > selB) return false;
            ShowSelectionMenu(m);
            return true;
        }
    }
}

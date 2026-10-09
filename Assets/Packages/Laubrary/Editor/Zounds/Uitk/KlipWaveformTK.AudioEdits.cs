using System;
using System.Collections.Generic;
using Laubrary.Zui;
using UnityEngine;
using UnityEngine.UIElements;
using Verb = Laubrary.Zounds.Destructive.ZoundsAudioEdits.Verb;
using Edits = Laubrary.Zounds.Destructive.ZoundsAudioEdits;

namespace Laubrary.Zounds.Uitk {

    /// <summary>
    /// The Klip editor waveform's audio editing (destructive editing, 2026-10-09): the edit bar over the waveform (Cut, Copy,
    /// Paste, Insert, Dup insert, Dup paste and a readout), the edit cursor and selection drawn on the waveform (apart from
    /// the playheads), and the keys. A click places the cursor, a drag selects (Shift extends) -- only where no trim handle
    /// or curve being edited takes the press. The verbs themselves are the host's (<see cref="onAudioEdit"/>), which owns
    /// the sound, its file and its playback.
    /// </summary>
    public partial class KlipWaveformTK {

        /// <summary>The host carries out an audio edit (it owns the sound, its playback and its window).</summary>
        internal Action<Verb> onAudioEdit;
        /// <summary>The edit cursor and the selection, in seconds of the file; cursor below 0 = none placed yet.</summary>
        internal double cursor = -1d, selA, selB;
        internal bool HasSelection => selB > selA + 1e-9;
        VisualElement selBand, editCursor;
        readonly Dictionary<Verb, Button> editButtons = new Dictionary<Verb, Button>();
        Label editReadout;
        string lastEditMessage = "";
        bool selecting; double selAnchor; float selDownX;

        const float EditH = 18f;

        // ─────────────────────────── building ───────────────────────────

        partial void AddEditBar() {
            // One fixed row, always all there: what changes is only whether a button is enabled and what the readout says.
            Add(EditBar());
            Add(Space(3f));
        }

        partial void AddEditMarks() {
            // Under the playheads, trim handles and curves.
            selBand = Abs(); selBand.AddToClassList("zs-klip-waveform__selection"); area.Add(selBand);
            editCursor = Abs(); editCursor.AddToClassList("zs-klip-waveform__edit-cursor"); area.Add(editCursor);
        }

        VisualElement EditBar() {
            var r = new VisualElement();
            r.AddToClassList("zs-klip-waveform__edit-bar");
            r.style.height = EditH;
            void B(Verb v, string text, float w, ZUICornerMask corners) {
                var b = ZS.Button(text, "", "RichButton", () => RequestEdit(v), corners, w, EditH);
                b.AddToClassList("zs-klip-waveform__edit-button");
                editButtons[v] = b;
                r.Add(b);
            }
            B(Verb.Cut, "Cut", 34f, ZUICornerMask.Left);
            B(Verb.Copy, "Copy", 40f, ZUICornerMask.None);
            B(Verb.Paste, "Paste", 42f, ZUICornerMask.None);
            B(Verb.Insert, "Insert", 44f, ZUICornerMask.Right);
            r.Add(Gap(6f));
            B(Verb.DuplicateInsert, "Dup insert", 66f, ZUICornerMask.Left);
            B(Verb.DuplicatePaste, "Dup paste", 62f, ZUICornerMask.Right);
            r.Add(Gap(8f));
            editReadout = new Label { pickingMode = PickingMode.Position };
            editReadout.AddToClassList("zs-lbl"); editReadout.AddToClassList("zs-greymini");
            editReadout.AddToClassList("zs-klip-waveform__edit-readout");
            r.Add(editReadout);
            SyncEditBar();
            return r;
        }

        void RequestEdit(Verb v) => onAudioEdit?.Invoke(v);

        /// <summary>The host's report after an edit: the readout's message, and where the cursor and selection now are.</summary>
        internal void EditDone(string message, double newCursor, double newA, double newB) {
            lastEditMessage = message ?? "";
            cursor = newCursor; selA = newA; selB = newB;
            SyncEditBar();
            Refresh();
        }

        internal void SetEditMessage(string message) { lastEditMessage = message ?? ""; SyncEditBar(); }

        (bool, double, double, double, double, string, bool) editBarKey;
        bool editBarSynced;

        void SyncEditBar() {
            if (editReadout == null) return;
            bool sel = HasSelection, clip = Edits.HasClipboard, at = cursor >= 0d;
            var key = (clip, Edits.ClipboardSeconds, cursor, selA, selB, lastEditMessage, enabledInHierarchy);
            if (editBarSynced && key.Equals(editBarKey)) return;
            editBarKey = key; editBarSynced = true;
            string kept = clip ? Edits.Seconds(Edits.ClipboardSeconds) + " of audio from '" + Edits.ClipboardFrom + "'" : null;
            void Set(Verb v, bool on, string tipOn, string tipOff) {
                var b = editButtons[v]; b.SetEnabled(on); b.tooltip = on ? tipOn : tipOff;
            }
            const string noSel = "Select some audio first: drag across the waveform (with no curve being edited).";
            const string noCursor = "Click the waveform to place the edit cursor first.";
            const string noKept = "Nothing kept yet: cut or copy some audio first.";
            Set(Verb.Cut, sel, "Cut the selection out (Ctrl+X): the audio after it moves up to close the gap, and the cut is kept to paste. The trim, the curves and every track's excerpt stay on the same audio.", noSel);
            Set(Verb.Copy, sel, "Keep the selected audio to paste or insert (Ctrl+C). Nothing changes.", noSel);
            Set(Verb.Paste, clip && at, "Write the kept " + kept + " over the audio from the edit cursor on (Ctrl+V). Nothing moves; the sound gets longer only if it runs past the end.", clip ? noCursor : noKept);
            Set(Verb.Insert, clip && at, "Put the kept " + kept + " in at the edit cursor (Ctrl+Shift+V): the audio after it moves on to make room.", clip ? noCursor : noKept);
            Set(Verb.DuplicateInsert, sel, "Put a copy of the selection in right after it (Ctrl+D); the audio after it moves on. The copy is then selected, so pressing again repeats it again.", noSel);
            Set(Verb.DuplicatePaste, sel, "Write a copy of the selection over the audio right after it (Ctrl+Shift+D). Nothing moves. The copy is then selected.", noSel);
            string s = sel ? Edits.Seconds(selA) + " – " + Edits.Seconds(selB) + " (" + Edits.Seconds(selB - selA) + ")"
                     : at ? "cursor at " + Edits.Seconds(cursor) : "";
            string text = lastEditMessage.Length == 0 ? s : (s.Length > 0 ? s + "   ·   " : "") + lastEditMessage;
            if (editReadout.text != text) editReadout.text = text;
            editReadout.tooltip = text.Length > 0 ? text : "The edit cursor, the selection and the result of the last audio edit show here. Click the waveform to place the cursor; drag to select (Shift+click extends).";
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
            SyncEditBar();
        }

        // ─────────────────────────── input ───────────────────────────

        partial void EditKey(KeyDownEvent e, ref bool handled) {
            bool ctrl = e.ctrlKey || e.commandKey;
            Verb? verb = null;
            if (ctrl && e.keyCode == KeyCode.X) verb = Verb.Cut;
            else if (ctrl && e.keyCode == KeyCode.C) verb = Verb.Copy;
            else if (ctrl && e.keyCode == KeyCode.V) verb = e.shiftKey ? Verb.Insert : Verb.Paste;
            else if (ctrl && e.keyCode == KeyCode.D) verb = e.shiftKey ? Verb.DuplicatePaste : Verb.DuplicateInsert;
            if (verb.HasValue) {
                if (editButtons.TryGetValue(verb.Value, out var b) && b.enabledSelf) RequestEdit(verb.Value);
                handled = true;
                return;
            }
            if (e.keyCode == KeyCode.Escape && HasSelection) { selA = selB = 0d; SyncEditBar(); Refresh(); handled = true; }
        }

        /// <summary>A press nothing else took: the edit cursor (a click) or a selection (a drag; Shift extends from the cursor).</summary>
        partial void EditPress(PointerDownEvent e, Vector2 m, Rect r, ref bool handled) {
            var clip = model.OriginalClip;
            if (e.button != 0 || clip == null) return;
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
            double t = Mathf.Clamp(model.XToTimeIn(m.x, AreaRect), 0f, clip.length);
            if (Mathf.Abs(m.x - selDownX) > 3f || HasSelection) { selA = Math.Min(selAnchor, t); selB = Math.Max(selAnchor, t); cursor = selA; Refresh(); }
        }

        partial void EditRelease() {
            if (!selecting) return;
            selecting = false;
            SyncEditBar();
        }
    }
}

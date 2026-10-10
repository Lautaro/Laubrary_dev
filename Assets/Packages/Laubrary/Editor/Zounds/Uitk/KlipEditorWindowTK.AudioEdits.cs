using Laubrary.Zounds.Destructive;
using UnityEngine;

namespace Laubrary.Zounds.Uitk {

    /// <summary>
    /// The Klip editor's audio edits (destructive editing, 2026-10-09): the edit bar's verbs carried out on the sound's file
    /// through <see cref="ZoundsAudioEdits"/>, the view re-read afterwards, the notice shown in the "Tell me" mode, and the
    /// edit cursor and selection kept across a rebuild of the window (an undo rebuilds it).
    /// </summary>
    public partial class KlipEditorWindowTK {

        [System.NonSerialized] double editCursor = -1d, editSelA, editSelB;

        protected override void OnBeforeRebuild() {
            if (waveform != null) { editCursor = waveform.cursor; editSelA = waveform.selA; editSelB = waveform.selB; }
        }

        partial void WireAudioEdits() {
            waveform.onAudioEdit = DoAudioEdit;
            waveform.onPlayRange = PlayRange;
            waveform.onTrimToSelection = TrimToSelection;
            waveform.EditDone("", editCursor, editSelA, editSelB);
            ZoundsAudioEdits.changed -= OnFileChanged;
            ZoundsAudioEdits.changed += OnFileChanged;
        }

        partial void UnwireAudioEdits() { ZoundsAudioEdits.changed -= OnFileChanged; }

        partial void Report(string message) { waveform?.SetEditMessage(message); }

        void DoAudioEdit(ZoundsAudioEdits.Verb verb) {
            if (klip == null || waveform == null) return;
            if (verb != ZoundsAudioEdits.Verb.Copy) audition?.StopAll();
            var view = spectrum != null ? spectrum.ViewSeconds : Vector2.zero;
            var res = ZoundsAudioEdits.Apply(klip, new ZoundsAudioEdits.Request {
                verb = verb, cursor = waveform.cursor, selA = waveform.selA, selB = waveform.selB });
            if (!res.done) { waveform.SetEditMessage(res.message); return; }
            if (res.swap.swapped) FollowCopy(res.swap);
            // The view stays on the part of the file it showed (a new file would otherwise reset it to the whole recording).
            if (verb != ZoundsAudioEdits.Verb.Copy) { spectrum?.InvalidateWaveform(); RefreshSpectrum(); if (spectrum != null) spectrum.ViewSeconds = view; }
            waveform.EditDone(res.message, res.cursor, res.selA, res.selB);
            if (res.notice != null) {
                var sw = res.swap;
                waveform.notice.Show(res.notice, res.editOriginal == null ? null : (System.Action)(() => EditOriginal(sw)));
            }
            SyncBadge();
        }

        /// <summary>
        /// Trim to the selection: the sound plays only the selected part of its file. Not an audio edit (the file stays as
        /// it is): one Undo step, a shared sound goes to a copy first (the same guard as a trim drag), and the curves are
        /// moved onto the file's own seconds first so they stay on their audio.
        /// </summary>
        void TrimToSelection(double a, double b) {
            if (klip == null || waveform == null || b <= a) return;
            if (!GuardSoundEdit()) return;
            var k = klip;
            ZoundsWindow.ModifyAndSaveZoundsProject("trim klip to selection", () => {
                KlipChainEnvelopes.EnsureSourceAnchored(k);
                k.trimEnabled = true;
                k.trimStart = (float)a;
                k.trimEnd = (float)b;
                k.needsRender = true;
                if (k.IsLooper) Dsp.SapVoiceRegistry.PushLoop(k);
            });
            RefreshSpectrum();
            spectrum?.ShowTrim();
            // The selection has become the trim: the marker stays at its start, the selection is cleared.
            waveform.EditDone("Trimmed to " + ZoundsAudioEdits.Seconds(a) + " – " + ZoundsAudioEdits.Seconds(b) + ".", a, 0d, 0d);
        }

        /// <summary>A file changed on disk (an edit here or elsewhere, or an undo putting a version back): re-read the view.</summary>
        void OnFileChanged(string path) {
            if (klip == null || spectrum == null) return;
            var view = spectrum.ViewSeconds;
            spectrum.InvalidateWaveform();
            RefreshSpectrum();
            spectrum.ViewSeconds = view;
            SyncBadge();
        }
    }
}

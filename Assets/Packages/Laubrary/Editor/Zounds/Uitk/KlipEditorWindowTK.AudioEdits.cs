using Laubrary.Zounds.Destructive;

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
            waveform.EditDone("", editCursor, editSelA, editSelB);
            ZoundsAudioEdits.changed -= OnFileChanged;
            ZoundsAudioEdits.changed += OnFileChanged;
        }

        partial void UnwireAudioEdits() { ZoundsAudioEdits.changed -= OnFileChanged; }

        partial void Report(string message) { waveform?.SetEditMessage(message); }

        void DoAudioEdit(ZoundsAudioEdits.Verb verb) {
            if (klip == null || waveform == null) return;
            if (verb != ZoundsAudioEdits.Verb.Copy) audition?.StopAll();
            var res = ZoundsAudioEdits.Apply(klip, new ZoundsAudioEdits.Request {
                verb = verb, cursor = waveform.cursor, selA = waveform.selA, selB = waveform.selB });
            if (!res.done) { waveform.SetEditMessage(res.message); return; }
            if (res.swap.swapped) FollowCopy(res.swap);
            if (verb != ZoundsAudioEdits.Verb.Copy) { spectrum?.InvalidateWaveform(); RefreshSpectrum(); }
            waveform.EditDone(res.message, res.cursor, res.selA, res.selB);
            if (res.notice != null) {
                var sw = res.swap;
                waveform.notice.Show(res.notice, res.editOriginal == null ? null : (System.Action)(() => EditOriginal(sw)));
            }
            SyncBadge();
        }

        /// <summary>A file changed on disk (an edit here or elsewhere, or an undo putting a version back): re-read the view.</summary>
        void OnFileChanged(string path) {
            if (klip == null || spectrum == null) return;
            spectrum.InvalidateWaveform();
            RefreshSpectrum();
            SyncBadge();
        }
    }
}

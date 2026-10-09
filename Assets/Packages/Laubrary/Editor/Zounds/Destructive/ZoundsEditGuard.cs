using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Laubrary.Zounds.Destructive {

    /// <summary>
    /// The moment before an edit would change a shared sound (destructive editing, 2026-10-09; owner's design).
    ///
    /// Changing a shared sound's trim, curves or audio from an editor goes to a copy of it, so the other sounds that use
    /// it, and game code that plays it by name, keep hearing the original. How that is announced is the project's Settings
    /// choice: <b>Ask first</b> (a dialog: edit a copy, edit the original, or cancel), <b>Tell me</b> (the copy is made and
    /// a notice in the editor says so, with "Edit the original instead"), or <b>Silent</b>. "Edit the original" is
    /// remembered for that sound until the editor is reloaded, so you are not asked again on every drag.
    ///
    /// <b>In a sound's own editor</b> the swap keeps the editor's objects: the sound you are editing takes a new id and
    /// the name "&lt;name&gt; (copy)", and an exact copy of it, made just before the edit, takes over its old id, name and
    /// place in the library. Everything that played the original (tracks, game code, routing) now finds that unchanged
    /// twin, while the editor -- and the drag already under way -- carry on on the copy. The swap and the edit that caused
    /// it are one Undo step; undoing it brings the editor back to the original.
    ///
    /// <b>On a Zequence track</b> the track converts to a local copy of the sound (the existing Shared → Local conversion,
    /// named "&lt;name&gt; (copy)"), in one Undo step.
    /// </summary>
    internal static class ZoundsEditGuard {

        public enum Choice { Copy, Cancel, Original }

        public static ZoundsProject.ProjectSettings.ProtectedEditPrompt Mode => ZoundsProject.Instance.projectSettings.protectedEditPrompt;

        static readonly HashSet<int> editOriginal = new HashSet<int>();

        /// <summary>You chose to edit this shared sound itself (until a script reload).</summary>
        public static bool EditsOriginal(Zound z) => z != null && editOriginal.Contains(z.id);
        public static void AllowOriginal(int id) => editOriginal.Add(id);
        internal static void ForgetAllowed(int id) => editOriginal.Remove(id);

        /// <summary>For the kept check: answers the Ask-first dialog instead of showing it (0 copy, 1 cancel, 2 original).</summary>
        internal static Func<string, string, bool, int> dialogOverride;

        /// <summary>The dialog of the "Ask first" mode; the other modes answer "copy" without asking.</summary>
        public static Choice Ask(string title, string message, string copyLabel, bool offerOriginal) {
            if (Mode != ZoundsProject.ProjectSettings.ProtectedEditPrompt.Ask) return Choice.Copy;
            int r;
            if (dialogOverride != null) r = dialogOverride(title, message, offerOriginal);
            else if (offerOriginal) r = EditorUtility.DisplayDialogComplex(title, message, copyLabel, "Cancel", "Edit the original");
            else r = EditorUtility.DisplayDialog(title, message, copyLabel, "Cancel") ? 0 : 1;
            return r == 0 ? Choice.Copy : r == 2 && offerOriginal ? Choice.Original : Choice.Cancel;
        }

        // ─────────────────────────── the sound's own editor ───────────────────────────

        /// <summary>What a guard did, for the editor to follow and to tell.</summary>
        public struct Swap {
            public bool swapped;
            public int originalId, copyId;
            public string originalName, copyName, usedIn;
        }

        /// <summary>
        /// Before an edit of <paramref name="k"/>'s trim, curves or audio in its own editor. False: cancelled, do nothing.
        /// True with <c>swap.swapped</c>: <paramref name="k"/> is now the copy (new id and name) and the edit goes on it.
        /// The swap is an Undo step of its own that the edit's next step joins (see <see cref="ZoundsWindow.JoinNextEdit"/>).
        /// </summary>
        public static bool BeforeSoundEdit(Klip k, out Swap swap) {
            swap = default;
            if (k == null || !ZoundsProtection.IsShared(k) || EditsOriginal(k)) return true;
            string usedIn = ZoundsProtection.UsedIn(k);
            var choice = Ask("'" + k.name + "' is " + usedIn,
                "Changing this sound here would change it everywhere it plays, including from game code that asks for '" + k.name + "'.\n\n" +
                "Edit a copy: this editor goes on with '" + k.name + " (copy)', and everything else keeps the original.",
                "Edit a copy", true);
            if (choice == Choice.Cancel) return false;
            if (choice == Choice.Original) { AllowOriginal(k.id); return true; }
            int group = -1;
            string originalName = k.name; int originalId = k.id;
            ZoundsWindow.ModifyAndSaveZoundsProject("edit a copy of " + originalName, () => { SwapToCopy(k); group = Undo.GetCurrentGroup(); });
            ZoundsWindow.JoinNextEdit(group);
            swap = new Swap { swapped = true, originalId = originalId, copyId = k.id, originalName = originalName, copyName = k.name, usedIn = usedIn };
            return true;
        }

        /// <summary>
        /// The identity swap (call inside a project modification): <paramref name="edited"/> becomes the copy -- a new id,
        /// "&lt;name&gt; (copy)", no render or output file of its own yet -- and an exact copy made now takes its old id, name,
        /// files and place. Returns that unchanged twin.
        /// </summary>
        public static Klip SwapToCopy(Klip edited) {
            var lib = ZoundsProject.Instance.zoundLibrary;
            int idx = lib.klips.IndexOf(edited);
            if (idx < 0) return null;
            var keeper = new Klip(edited.id, edited);
            keeper.name = edited.name;
            keeper.originalId = edited.originalId;
            keeper.parentId = edited.parentId;
            keeper.needsRender = edited.needsRender;
            keeper.renderedClipPath = edited.renderedClipPath;
            keeper.outputClipPath = edited.outputClipPath;
#if ADDRESSABLES_INSTALLED
            keeper.renderedClipRef = edited.renderedClipRef;
            keeper.outputClipRef = edited.outputClipRef;
            edited.renderedClipRef = null;
            edited.outputClipRef = null;
#endif
            lib.klips[idx] = keeper;
            edited.id = ZoundLibrary.GetUniqueZoundId();
            edited.originalId = 0;
            edited.name = ZoundDictionary.EnsureUniqueZoundName(keeper.name + " (copy)", edited);
            edited.renderedClipPath = string.Empty;
            edited.outputClipPath = string.Empty;
            edited.needsRender = true;
            lib.klips.Insert(idx + 1, edited);
            AfterIdentityChange(edited, keeper);
            return keeper;
        }

        /// <summary>"Edit the original instead": the copy takes back the original's id, name, files and place (with every
        /// change made to it so far), and the unchanged twin goes. One Undo step. False when the original is gone.</summary>
        public static bool SwapBack(Klip edited, int originalId) {
            var lib = ZoundsProject.Instance.zoundLibrary;
            var keeper = lib.klips.Find(x => x.id == originalId);
            if (keeper == null || edited == null || ReferenceEquals(keeper, edited)) return false;
            ZoundsWindow.ModifyAndSaveZoundsProject("edit the original " + keeper.name, () => {
                lib.klips.Remove(edited);
                int idx = lib.klips.IndexOf(keeper);
                edited.id = keeper.id;
                edited.originalId = keeper.originalId;
                edited.name = keeper.name;
                edited.needsRender = keeper.needsRender;
                edited.renderedClipPath = keeper.renderedClipPath;
                edited.outputClipPath = keeper.outputClipPath;
#if ADDRESSABLES_INSTALLED
                edited.renderedClipRef = keeper.renderedClipRef;
                edited.outputClipRef = keeper.outputClipRef;
#endif
                lib.klips[idx] = edited;
                AfterIdentityChange(edited, keeper);
            });
            AllowOriginal(originalId);
            return true;
        }

        static void AfterIdentityChange(Klip a, Klip b) {
            ZoundEngine.InvalidateLookups();
            Dsp.ZoundDspPlayback.InvalidateLayout(a);
            Dsp.ZoundDspPlayback.InvalidateLayout(b);
            KlipChainEnvelopes.Touch(a);
            KlipChainEnvelopes.Touch(b);
            ZoundsAssetPostProcessor.RefreshAudioClipsCache();
        }

        // ─────────────────────────── a Zequence track ───────────────────────────

        /// <summary>What a track guard did: the notice to show, and how to undo the swap for "Edit the original instead".</summary>
        public struct TrackSwap {
            public bool swapped;
            public string notice;
            public Action editOriginal;
        }

        /// <summary>
        /// Before an edit of the sound's own trim or curves from a Zequence track. A local track, or a shared sound you
        /// chose to edit itself, goes ahead as it is. A shared one goes to a local copy on this track (per Settings).
        /// False: cancelled.
        /// </summary>
        public static bool BeforeTrackEdit(CompositeZound parent, CompositeZound.ZoundEntry entry, out TrackSwap ts) {
            ts = default;
            if (parent == null || entry == null || entry.local) return true;
            if (!parent.TryGetEntryZound(entry, out var z) || !(z is Klip shared)) return true;
            if (EditsOriginal(shared)) return true;
            string usedIn = ZoundsProtection.UsedIn(shared);
            var choice = Ask("'" + shared.name + "' is a shared sound",
                "It is " + usedIn + " and can be played by name from game code; changing its curves here would change it everywhere.\n\n" +
                "Edit a copy: this track gets its own copy, '" + shared.name + " (copy)', and everything else keeps the original.",
                "Edit a copy on this track", true);
            if (choice == Choice.Cancel) return false;
            if (choice == Choice.Original) { AllowOriginal(shared.id); return true; }
            int index = parent.zoundEntries.IndexOf(entry);
            if (index < 0) return false;
            string saved = JsonUtility.ToJson(entry);
            int sharedId = shared.id;
            ZoundsWindow.ModifyAndSaveZoundsProject("edit a copy of " + shared.name + " on this track", () => {
                CompositeZoundEditing.ConvertEntry(parent, index);
                if (parent.TryGetEntryZound(entry, out var local) && local is Klip lk) lk.name = shared.name + " (copy)";
            });
            int localId = entry.zoundId;
            ts = new TrackSwap {
                swapped = true,
                notice = "'" + shared.name + "' is " + usedIn + ", so this track now plays its own copy, '" + shared.name + " (copy)'; the original is unchanged.",
                editOriginal = () => RevertTrack(parent, entry, saved, localId, sharedId),
            };
            return true;
        }

        /// <summary>Puts the shared sound back on the track (dropping the copy and anything changed on it) and lets you edit
        /// the shared sound from there. One Undo step.</summary>
        static void RevertTrack(CompositeZound parent, CompositeZound.ZoundEntry entry, string saved, int localId, int sharedId) {
            ZoundsWindow.ModifyAndSaveZoundsProject("edit the original sound on this track", () => {
                JsonUtility.FromJsonOverwrite(saved, entry);
                parent.localKlips.RemoveAll(k => k.id == localId);
            });
            AllowOriginal(sharedId);
            ZoundsWindow.RepaintWindow();
        }
    }
}

using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using TMPro;

namespace Laubrary.TextSplash.Editor
{
    // ──────────────────────────────────────────────────────────────────────────────────────────────────────────
    /// <summary>Keeps every splash's committed border font current, on its own, with nothing to press.
    ///
    /// The border pass needs a large-padding twin of the face font, and that twin has to be a COMMITTED asset:
    /// rasterising one needs a `UnityEngine.Font`, TMP nulls `sourceFontFile` on a Static font asset, and only the
    /// AssetDatabase can turn the remaining GUID back into a Font — so a bake succeeds in the Editor and cannot
    /// succeed in a player. A splash whose twin is missing or stale therefore looks perfect while it is authored and
    /// ships with a hairline border. Asking a user to remember a bake button is asking them to remember an invariant
    /// the tool can check itself; this checks it.
    ///
    /// Two triggers, chosen for what they cost:
    ///   • an IMPORT of a splash asset (<see cref="SplashBorderAutoBakeWatcher"/>) — the moment its serialized state
    ///     actually changed on disk, which covers a save, a re-import, a move, and a splash arriving from version
    ///     control. This is the authoritative signal, and it is naturally coarse: it fires once per save rather than
    ///     once per slider frame.
    ///   • SELECTION of a splash — the in-memory catch, for settings changed in the authoring window and not yet
    ///     saved. Clicking a splash is already a deliberate, occasional act, so it makes a good sampling point.
    /// Deliberately NOT a per-change trigger: the padding is derived from the border width, so a dial being dragged
    /// would ask for a different atlas on every frame, and each of those is a full glyph rasterisation. Nothing is
    /// missing in the meantime — while authoring, the runtime baker covers the preview, because in the Editor it can
    /// always resolve the font — and <see cref="SplashBorderBuildBake"/> is the backstop that turns the committed
    /// asset from a hope into a guarantee.
    ///
    /// Everything is queued and drained off <see cref="EditorApplication.update"/>: never inside the import callback
    /// (a bake creates, saves and re-imports assets, which re-enters an AssetDatabase still busy with the batch that
    /// called us), never in play mode, and one splash per tick so a queue cannot present itself as a hung
    /// editor.</summary>
    static class SplashBorderAutoBake
    {
        /// Quiet time before a noticed splash is baked. Long enough that a burst — one save re-importing several
        /// assets, a folder of splashes arriving at once, a click-through of the project window — collapses into a
        /// single pass; short enough that it still feels like part of the edit.
        const double k_Quiet = 0.75;

        /// Extra quiet before the once-per-session sweep, so opening a project is not the moment atlases start
        /// rasterising.
        const double k_LoadQuiet = 4.0;

        /// Failures are remembered in SessionState rather than in a static: a recompile must not hand a hopeless bake
        /// a fresh attempt (that would be an atlas rasterised on every script change), while restarting the editor —
        /// the thing a user does after fixing an import setting — must.
        const string k_FailKey = "Laubrary.TextSplash.BorderBakeFailed:";
        const string k_SweptKey = "Laubrary.TextSplash.BorderBakeSwept";

        static readonly List<string> s_Queue = new List<string>();      // spec GUIDs, in the order they were noticed
        static readonly HashSet<string> s_Waiting = new HashSet<string>();

        static double s_DueAt;
        static bool s_SweepPending;
        static bool s_Pumping;
        static bool s_Baking;

        // ──────────────────────────────────────────────────────────────────────────────────────────────────────
        // Noticing
        // ──────────────────────────────────────────────────────────────────────────────────────────────────────

        [InitializeOnLoadMethod]
        static void Install()
        {
            Selection.selectionChanged += NoticeSelection;

            // Once per EDITOR SESSION, not once per domain reload. A recompile must not cost a sweep, but a freshly
            // opened project deserves one look at everything: a twin can have been deleted, or a splash can have
            // arrived from version control, while nothing was running to notice it.
            if (SessionState.GetBool(k_SweptKey, false)) return;

            s_SweepPending = true;
            Arm(k_LoadQuiet);
        }

        internal static void Notice(string[] imported, string[] deleted, string[] moved)
        {
            // A bake saves and re-imports assets, which lands straight back here. Following that would be a loop.
            if (s_Baking) return;

            bool armed = false;

            for (int i = 0; i < imported.Length; i++) armed |= EnqueuePath(imported[i]);
            for (int i = 0; i < moved.Length; i++) armed |= EnqueuePath(moved[i]);

            // A DELETED asset can no longer be asked what it was, so a twin that has just vanished from under a
            // splash is only findable by looking at every splash. That is what makes a hand-deleted border font grow
            // back — and asset deletions are rare enough for a sweep to be the cheap answer rather than an index.
            if (!s_SweepPending)
            {
                for (int i = 0; i < deleted.Length; i++)
                {
                    if (!IsAssetFile(deleted[i])) continue;
                    s_SweepPending = true;
                    armed = true;
                    break;
                }
            }

            if (armed) Arm(k_Quiet);
        }

        static void NoticeSelection()
        {
            if (s_Baking) return;

            var selection = Selection.objects;
            bool armed = false;

            for (int i = 0; i < selection.Length; i++)
            {
                if (!(selection[i] is TextSplash spec)) continue;
                armed |= Enqueue(AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(spec)));
            }

            if (armed) Arm(k_Quiet);
        }

        static bool EnqueuePath(string path)
        {
            if (!IsAssetFile(path)) return false;
            if (AssetDatabase.GetMainAssetTypeAtPath(path) != typeof(TextSplash)) return false;
            return Enqueue(AssetDatabase.AssetPathToGUID(path));
        }

        static bool Enqueue(string guid)
        {
            if (string.IsNullOrEmpty(guid)) return false;
            if (s_Waiting.Add(guid)) s_Queue.Add(guid);
            return true;                                    // already waiting is still a reason to renew the quiet
        }

        static bool IsAssetFile(string path)
            => !string.IsNullOrEmpty(path) && path.EndsWith(".asset", StringComparison.OrdinalIgnoreCase);

        // ──────────────────────────────────────────────────────────────────────────────────────────────────────
        // Draining
        // ──────────────────────────────────────────────────────────────────────────────────────────────────────

        /// Renewed by every notice, which is what makes this a debounce rather than a delay: a stream of imports
        /// pushes the bake to the END of the stream instead of baking once per file along the way.
        static void Arm(double quiet)
        {
            s_DueAt = EditorApplication.timeSinceStartup + quiet;
            if (s_Pumping) return;
            s_Pumping = true;
            EditorApplication.update += Pump;
        }

        static void Disarm()
        {
            if (!s_Pumping) return;
            s_Pumping = false;
            EditorApplication.update -= Pump;
        }

        static void Pump()
        {
            if (s_Baking) return;

            // Never mid-play (a bake stalls the game, and the running splash is not this pass's to change), never
            // while the editor is compiling or importing (the AssetDatabase is somebody else's for the moment), and
            // never during a build — the build has its own pass and runs it synchronously. Each of these returns
            // WITHOUT disarming, so the queue survives the state that blocked it and drains on the other side.
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling
                || EditorApplication.isUpdating || BuildPipeline.isBuildingPlayer) return;

            if (EditorApplication.timeSinceStartup < s_DueAt) return;

            if (s_SweepPending)
            {
                s_SweepPending = false;
                SessionState.SetBool(k_SweptKey, true);

                string[] guids = AssetDatabase.FindAssets("t:" + nameof(TextSplash));
                for (int i = 0; i < guids.Length; i++) Enqueue(guids[i]);
            }

            if (s_Queue.Count == 0) { Disarm(); return; }

            string guid = s_Queue[0];
            s_Queue.RemoveAt(0);
            s_Waiting.Remove(guid);

            // ONE per tick. A padded atlas is hundreds of milliseconds of rasterising, and a queue drained inside a
            // single frame is indistinguishable from an editor that has locked up.
            Consider(guid);
        }

        static void Consider(string guid)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            if (string.IsNullOrEmpty(path)) return;

            var spec = AssetDatabase.LoadAssetAtPath<TextSplash>(path);
            if (spec == null) return;

            if (!SplashBorderFontBaker.NeedsBake(spec, out TMP_FontAsset source)) return;

            // A bake that cannot succeed must not be attempted again on the next notice, or the same splash would
            // rasterise, fail and warn on every save for the rest of the session. The memory is keyed by what the
            // bake would be made OF, so changing the font, the border width or the atlas size earns a fresh attempt
            // by itself — the back-off holds only for the exact combination that already failed.
            string key = k_FailKey + Signature(spec, source);
            if (SessionState.GetBool(key, false)) return;

            if (TryBake(spec, out string failure)) return;

            SessionState.SetBool(key, true);
            Debug.LogWarning($"[TextSplash] Could not keep \"{spec.name}\"'s border font up to date — {failure}. Not " +
                             "trying that again until its font, border width, atlas padding or atlas size changes " +
                             "(or the editor restarts). Until then its border is capped at the face's own padding, " +
                             "which in a player build is a hairline.", spec);
        }

        /// What a bake would be made of. Two attempts sharing this string would produce the same asset, so a failure
        /// against one is a failure against the other — which is exactly what the back-off needs to know.
        static string Signature(TextSplash spec, TMP_FontAsset source)
        {
            string face = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(source));
            if (string.IsNullOrEmpty(face)) face = source.name;

            string self = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(spec));
            int padding = spec.ResolveBorderPadding(source.faceInfo.pointSize);

            return $"{self}|{face}|{padding}|{spec.borderAtlasSize}";
        }

        // ──────────────────────────────────────────────────────────────────────────────────────────────────────
        // The bake itself
        // ──────────────────────────────────────────────────────────────────────────────────────────────────────

        /// <summary>Bake one splash's twin now, and say whether the splash is satisfied afterwards. Shared by the
        /// editor's maintenance and the build guarantee so the two can never disagree about what "done" means.</summary>
        internal static bool TryBake(TextSplash spec, out string failure)
        {
            failure = null;
            if (spec == null) return false;

            s_Baking = true;
            try
            {
                TMP_FontAsset saved = SplashBorderFontBaker.Bake(spec, null);
                if (saved == null)
                {
                    failure = "the bake itself failed, for the reason logged just above this line";
                    return false;
                }

                // The stamp is an edit to the spec, and an edit that is never written is a bake that has to happen
                // again next session. Only this asset: a maintenance pass nobody asked for has no business flushing
                // whatever else the user happens to have dirty.
                AssetDatabase.SaveAssetIfDirty(spec);

                // The loop-breaker. A bake that "worked" and still left the splash asking for one — a padding the
                // rasteriser would not honour, say — would otherwise be re-noticed by the very import it caused, and
                // baked again, and again. Treating it as a failure is what turns an endless loop into one warning.
                if (SplashBorderFontBaker.NeedsBake(spec, out _))
                {
                    failure = $"the font it produced still does not satisfy \"{spec.name}\" — it came back with less " +
                              "padding than the border asks for";
                    return false;
                }

                return true;
            }
            finally
            {
                s_Baking = false;
            }
        }
    }

    // ──────────────────────────────────────────────────────────────────────────────────────────────────────────
    /// <summary>The import hook. It NOTICES only — creating, saving and re-importing a font asset from inside an
    /// import callback re-enters the AssetDatabase while it is still working through the batch that called us, so
    /// the work is handed to <see cref="SplashBorderAutoBake"/> and done on a later editor tick.</summary>
    class SplashBorderAutoBakeWatcher : AssetPostprocessor
    {
        static void OnPostprocessAllAssets(string[] imported, string[] deleted, string[] moved, string[] movedFrom)
            => SplashBorderAutoBake.Notice(imported, deleted, moved);
    }

    // ──────────────────────────────────────────────────────────────────────────────────────────────────────────
    /// <summary>The guarantee: no build ships a splash whose border font is missing or stale.
    ///
    /// The editor's maintenance is opportunistic — it acts when a splash is saved or selected — and that is enough
    /// for authoring, but "enough for authoring" is precisely how the hairline regression happens: the twin is the
    /// one part of a splash that cannot be produced in a player, so the last chance to make it exist is here. Every
    /// TextSplash in the project is checked, not only the ones some scene happens to reference, because a splash is
    /// data — code plays it by reference, and there is no reliable way to know from the outside which ones a build
    /// will reach.
    ///
    /// A splash that cannot be baked is reported as an ERROR and the build continues. Throwing a
    /// `BuildFailedException` was the alternative and it is the wrong trade: what is at stake is one asset's border
    /// coming out thinner than it was authored — cosmetic, local, and not something a pre-processor can weigh against
    /// whatever the build is for. Refusing to produce a player over it would be a worse failure than the one it
    /// prevents. What the requirement really needs is that a build never proceeds BELIEVING it is fine, and an error
    /// per asset plus a summary line is red in the console and red in any CI log that reads it. A project that wants
    /// the harder rule turns the final `Debug.LogError` into a `throw new BuildFailedException(...)`; nothing else
    /// has to change.</summary>
    public class SplashBorderBuildBake : IPreprocessBuildWithReport
    {
        public int callbackOrder => 0;

        public void OnPreprocessBuild(BuildReport report)
        {
            string[] guids = AssetDatabase.FindAssets("t:" + nameof(TextSplash));
            int baked = 0, failed = 0;

            for (int i = 0; i < guids.Length; i++)
            {
                string path = AssetDatabase.GUIDToAssetPath(guids[i]);
                var spec = AssetDatabase.LoadAssetAtPath<TextSplash>(path);
                if (spec == null) continue;

                // A splash with no font at all resolves no face and needs no twin — an unfinished asset, not a broken
                // one, and not this pass's business.
                if (!SplashBorderFontBaker.NeedsBake(spec, out TMP_FontAsset source)) continue;

                if (SplashBorderAutoBake.TryBake(spec, out string failure)) { baked++; continue; }

                failed++;
                Debug.LogError($"[TextSplash] \"{spec.name}\" ({path}) is going into this build WITHOUT a usable " +
                               $"border font — {failure}. Its border will be capped at \"{source.name}\"'s own " +
                               $"{SplashFontBaker.MaxOutwardEm(source):0.###} em, so it ships as a hairline however " +
                               "wide it was authored. A twin can only be baked from a font file the Editor can " +
                               "reach: check that the face's source .ttf/.otf is still in the project and has " +
                               "\"Include Font Data\" enabled in its import settings.", spec);
            }

            if (baked > 0)
            {
                Debug.Log($"[TextSplash] Baked {baked} border font(s) this build would otherwise have shipped stale " +
                          "or missing. They are committed assets now and travel with the player.");
            }

            if (failed == 0) return;

            Debug.LogError($"[TextSplash] {failed} splash(es) in this build have no usable border font and will draw " +
                           "a hairline instead of the border they were authored with — see the errors above for " +
                           "which, and why. The build was NOT stopped: a thin border is not worth refusing a player " +
                           "over, but it is not something to discover after shipping either.");
        }
    }
}

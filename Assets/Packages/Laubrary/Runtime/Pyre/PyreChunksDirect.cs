// PyreChunksDirect.cs — the Pyre ASSET itself is a Chunks effect source.
//
// WHY THIS EXISTS (do not "tidy it up" as duplication of PyreSpawnSource/PyreChunkAnimation):
//
// Chunks' Pyre Spawn module holds an `Object source` that it casts to IChunkEffectSpawner, and for a long
// time the ONLY implementor was PyreSpawnSource — a wrapper ScriptableObject. In practice ZERO such wrapper
// assets existed anywhere in the project, so a user who opened the Chunks window and clicked the "Blast"
// picker got an EMPTY browser, and the right-click "New" made a blank wrapper whose `spec` could only be
// filled from the raw Inspector (the browser's Edit handler no-ops on a blank source). That is a hard dead
// end for "configure this with no code": you could not get from the Chunks window to a working blast without
// leaving the tool. Making Pyre implement the two Chunks contracts directly removes the wrapper from the
// happy path entirely — the user picks `Assets/Pyre/…/Old School Explo 2 Plus.asset` straight into the Blast
// slot and it plays, because dozens of real Pyre assets already exist to pick from.
//
// THE WRAPPERS STAY, and are still the right answer for the OVERRIDE case. A directly-picked Pyre plays at
// the asset's OWN authored rate (previewFps) and one shot — the same rate the Pyre window previews at and the
// same rate PyreBaker exports at, so one asset means one speed everywhere it is used. What a Pyre asset still
// has nowhere to serialize is a per-USE setting: the same Pyre picked into three different Chunk specs is ONE
// asset, so a "play THIS one at 30 fps, looping for 2 seconds" dial would retune the other two.
// PyreSpawnSource is exactly that per-use asset, and PyreChunkAnimation the same for frame handoff.
// Direct = the zero-friction default; wrapper = per-use overrides. Both, deliberately.
//
// Pyre.cs is `partial` for this file and nothing else — the spawn/animation code does not belong in a
// 1200-line data asset, and keeping it here means Pyre.cs stays a pure serialized-field declaration.
// (Direction of dependency, as always: Pyre references Chunks, so Chunks can never reference Pyre back —
// every Pyre-side implementation of a Chunks interface has to live on this side of the wall.)
using UnityEngine;
using Laubrary.Chunks;
using Laubrary.PreviewKit;

namespace Laubrary.Pyre
{
    public partial class Pyre : IChunkEffectSpawner, IChunkAnimation, IVisualPreview
    {
        /// Playback speed a DIRECTLY-picked Pyre plays at — for spawning, for frame handoff and for the
        /// browser preview alike. It is the asset's own <c>previewFps</c>, because that field IS Pyre's
        /// per-asset rate dial and the whole tool already treats it as authoritative: the Pyre window plays at
        /// it, and PyreBaker exports its sprite sheets and AnimationClips at it. Any second number here would
        /// mean one asset running at two different speeds depending on whether it was baked or spawned live.
        /// A PER-USE rate — "this one place needs it at 30 fps, looping" — is the wrappers' job
        /// (PyreSpawnSource / PyreChunkAnimation); what belongs to the asset stays on the asset.
        public float DirectFps => Mathf.Max(1f, previewFps);

        // ── IChunkEffectSpawner ──────────────────────────────────────────────────
        // Spawn one live blast. The body is NOT re-derived here — it is the shared PyreDirectSpawn.Spawn
        // helper below, the same one PyreSpawnSource calls, so the pooling/Finished/sorting-layer/loop-leak
        // discipline has exactly one implementation to keep right.
        //
        // Defaults for a direct pick: the asset's own authored rate (see DirectFps), no looping (so
        // loopSeconds is irrelevant and passed as 0) — i.e. precisely what a plain one-shot explosion should
        // do when dropped into a Blast slot.

        /// Spawn one blast at worldPos, angled by rotationDeg, at a uniform scale. Returns the spawned
        /// transform so a caller (Chunks' Pyre Movement module) can fly it. A NaN rotation means "no
        /// rotation", matching how an omni-directional burst reports itself.
        public Transform SpawnEffect(Vector3 worldPos, float rotationDeg, float scale,
                                     string sortingLayerName, int sortingOrder)
            => PyreDirectSpawn.Spawn(this, DirectFps, false, 0f,
                                     worldPos, rotationDeg, scale, sortingLayerName, sortingOrder);

        // ── IChunkAnimation ──────────────────────────────────────────────────────
        // Frames for somebody else to play (chunk content, an AmmoDef visual…). Same frame path
        // PyreChunkAnimation uses — PyreRenderer's own cache — so a Pyre picked directly renders identically
        // to, and shares the cache with, the same Pyre picked through a wrapper. Never PyreBaker's exported
        // PNG: that stays a standalone export, not a consumption path.
        //
        // Implicit (not explicit) interface implementation is safe: Pyre declares no member called Fps, Loop
        // or GetFrames. The nearest name is `previewFps` — no clash, and no longer unrelated either: it is
        // exactly the value Fps reports, via DirectFps.

        public Sprite[] GetFrames() => PyreRenderer.GetFrames(this);

        public float Fps => DirectFps;

        /// True, because the animation contract's normal use is chunk CONTENT — a chunk lives until its own
        /// lifetime ends, and freezing its visual on the last frame partway through reads as a broken effect
        /// rather than a finished one. A one-shot-and-freeze visual is the override case: PyreChunkAnimation
        /// with `loop` off. (Note this is the opposite default to SpawnEffect above, and correctly so: a
        /// SPAWNED blast owns its own lifetime and must end to be released, while handed-over FRAMES are
        /// played by an owner who decides when they stop.)
        public bool Loop => true;

        // ── IVisualPreview ───────────────────────────────────────────────────────
        // Now that a Pyre is picked DIRECTLY, it turns up in LauAsset pickers and browsers that previously
        // only ever saw the wrappers — and both wrappers implement IVisualPreview precisely because the UI
        // rulebook forbids a blank thumbnail ("worse than none"). Without this, making Pyre pickable would
        // have traded one UI defect for another: a browser full of grey squares. Same deterministic renderer
        // Pyre's own editing preview uses, never a second render path.

        public Texture2D RenderPreviewTexture()
        {
            int mid = Mathf.Clamp(frameCount / 2, 0, Mathf.Max(0, frameCount - 1));
            return PyreRenderer.RenderFrameTexture(this, mid);
        }

        public bool CanAnimatePreview => frameCount > 1;
        public float PreviewFps => DirectFps;

        public void UpdateAnimatedPreview(Texture2D tex, double time)
        {
            if (frameCount <= 1 || tex == null) return;
            var frames = PyreRenderer.GetFrames(this);
            if (frames == null || frames.Length == 0) return;
            int frame = Mathf.FloorToInt((float)(time * Mathf.Max(1f, DirectFps))) % frames.Length;
            var frameTex = frames[frame]?.texture;
            if (frameTex == null) return;
            tex.SetPixels32(frameTex.GetPixels32());
            tex.Apply();
        }
    }

    /// The one implementation of "spawn a live Pyre blast for Chunks", shared by the direct path
    /// (<see cref="Pyre.SpawnEffect"/>) and the wrapper path (PyreSpawnSource.SpawnEffect). It exists as a
    /// static helper purely so those two cannot drift: every line below is a detail that was got wrong once
    /// and fixed, and duplicating them would mean fixing each future bug twice.
    ///
    /// Lifetime discipline is the same one SpawnPyreFx uses: take a pooled PyreBlastPlayer, configure it,
    /// subscribe Finished ONCE, and in that handler unsubscribe, restore the pooled object to unit scale /
    /// identity rotation, and release it. Nothing outside this helper ever destroys or releases what Spawn
    /// returns — callers only read and move it while it is alive.
    internal static class PyreDirectSpawn
    {
        /// Spawn one blast of `spec`. `fps` <= 0 falls back to 24. `loop` + `loopSeconds` are the wrapper's
        /// override case; a direct pick passes (false, 0f) and takes the plain one-shot path. Returns null
        /// when there is no spec — the interface's documented "could not be spawned" answer.
        public static Transform Spawn(Pyre spec, float fps, bool loop, float loopSeconds,
                                      Vector3 worldPos, float rotationDeg, float scale,
                                      string sortingLayerName, int sortingOrder)
        {
            if (spec == null) return null;

            var bp = PyreBlastPool.Get();   // pooled: pooled=true already set by the pool's factory
            bp.transform.position = worldPos;
            bp.transform.rotation = float.IsNaN(rotationDeg) ? Quaternion.identity
                                                             : Quaternion.Euler(0f, 0f, rotationDeg);
            bp.transform.localScale = Vector3.one * (scale > 0f ? scale : 1f);

            var sr = bp.GetComponent<SpriteRenderer>();
            if (sr != null)
            {
                // Assigning a sorting layer Unity does not know is a warning we must not cause, so an empty
                // or stale name leaves the renderer's existing layer alone. Same round-trip test
                // LayerSpec.IsValidSortingLayer uses — re-derived rather than referenced, because Pyre must
                // not take a dependency on the caller's layering module to spawn one blast.
                if (!string.IsNullOrEmpty(sortingLayerName) && IsValidSortingLayer(sortingLayerName))
                    sr.sortingLayerName = sortingLayerName;
                sr.sortingOrder = sortingOrder;
            }

            bp.spec = spec;
            bp.fps = fps > 0f ? fps : 24f;
            bp.loop = loop;

            System.Action onFinished = null;
            onFinished = () =>
            {
                bp.Finished -= onFinished;
                bp.transform.localScale = Vector3.one;       // hand the pooled blast back at unit scale
                bp.transform.rotation = Quaternion.identity; // …and unrotated
                PyreBlastPool.Release(bp);
            };
            bp.Finished += onFinished;
            bp.Play();

            // A looping blast never raises Finished, so the handler above would never run and the pooled
            // instance would be held for the rest of the session — a slow leak that only shows up as "the
            // pool keeps growing" hours later. Give looping playback an explicit end instead: stop it after
            // loopSeconds, which raises Finished through the player's own path and releases it like any
            // one-shot. loopSeconds == 0 is the deliberate opt-out for a blast the game tears down itself.
            if (loop && loopSeconds > 0f) bp.StartCoroutine(EndLoopAfter(bp, loopSeconds));

            return bp.transform;
        }

        /// Ends a looping blast after roughly a fixed time. It clears `loop` rather than calling Stop(),
        /// because Stop() only sets playing=false — it never raises Finished, so the release handler would
        /// never run and the pooled instance would still be lost. Clearing loop lets the player reach the end
        /// of its sequence normally, which IS the path that raises Finished (and it ends on the last frame
        /// instead of cutting mid-animation). It re-checks the spec first: a pooled instance can have been
        /// released and handed to somebody else by now, and un-looping their blast would be a baffling bug.
        static System.Collections.IEnumerator EndLoopAfter(PyreBlastPlayer bp, float seconds)
        {
            var spec = bp.spec;
            yield return new WaitForSeconds(seconds);
            if (bp == null || bp.spec != spec) yield break;
            bp.loop = false;
        }

        /// SortingLayer has no "does this name exist" query, but the id round-trip is one: an unknown name
        /// maps to the Default layer's id, whose name then does not match what went in.
        static bool IsValidSortingLayer(string name)
            => !string.IsNullOrEmpty(name) && SortingLayer.IDToName(SortingLayer.NameToID(name)) == name;
    }
}

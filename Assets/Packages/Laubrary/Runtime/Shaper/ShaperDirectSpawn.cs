// ShaperDirectSpawn.cs — the one implementation of "spawn a live Shaper animation for Chunks".
//
// Mirrors Runtime/Pyre/PyreChunksDirect.cs's internal PyreDirectSpawn helper exactly, substituting
// ShaperPlayerPool/ShaperPlayer for PyreBlastPool/PyreBlastPlayer. A static helper so a future per-use
// wrapper (the ShaperClip equivalent of PyreSpawnSource, should one ever be authored) shares this one
// implementation instead of duplicating the pooling/Finished/sorting-layer/loop-leak discipline a second
// time — every line below is a detail Pyre's own version got wrong once and fixed.
using UnityEngine;

namespace Laubrary.Shaper
{
    internal static class ShaperDirectSpawn
    {
        /// Spawn one playback of `clip`. `fps` <= 0 falls back to the clip's own authored rate (ShaperPlayer's
        /// own default when fps is left at 0). `loop` + `loopSeconds` are the wrapper's override case; a
        /// direct pick passes (false, 0f) and takes the plain one-shot path. Returns null when there is no
        /// clip — the interface's documented "could not be spawned" answer.
        public static Transform Spawn(ShaperClip clip, float fps, bool loop, float loopSeconds,
                                      Vector3 worldPos, float rotationDeg, float scale,
                                      string sortingLayerName, int sortingOrder)
        {
            if (clip == null) return null;

            var sp = ShaperPlayerPool.Get();   // pooled: pooled=true already set by the pool's factory
            sp.transform.position = worldPos;
            sp.transform.rotation = float.IsNaN(rotationDeg) ? Quaternion.identity
                                                              : Quaternion.Euler(0f, 0f, rotationDeg);
            sp.transform.localScale = Vector3.one * (scale > 0f ? scale : 1f);

            var sr = sp.GetComponent<SpriteRenderer>();
            if (sr != null)
            {
                // Assigning a sorting layer Unity does not know is a warning we must not cause, so an empty
                // or stale name leaves the renderer's existing layer alone. Same round-trip test
                // PyreDirectSpawn.IsValidSortingLayer uses — re-derived rather than shared, because Shaper
                // must not take a dependency on Pyre to spawn one clip.
                if (!string.IsNullOrEmpty(sortingLayerName) && IsValidSortingLayer(sortingLayerName))
                    sr.sortingLayerName = sortingLayerName;
                sr.sortingOrder = sortingOrder;
            }

            sp.clip = clip;
            sp.fps = fps;   // ShaperPlayer itself treats <= 0 as "use the clip's own authored rate"
            sp.loop = loop;

            System.Action onFinished = null;
            onFinished = () =>
            {
                sp.Finished -= onFinished;
                sp.transform.localScale = Vector3.one;       // hand the pooled player back at unit scale
                sp.transform.rotation = Quaternion.identity; // …and unrotated
                ShaperPlayerPool.Release(sp);
            };
            sp.Finished += onFinished;
            sp.Play();

            // A looping player never raises Finished, so the handler above would never run and the pooled
            // instance would be held for the rest of the session — a slow leak that only shows up as "the
            // pool keeps growing" hours later. Give looping playback an explicit end instead: stop it after
            // loopSeconds, which raises Finished through the player's own path and releases it like any
            // one-shot. loopSeconds == 0 is the deliberate opt-out for a player the game tears down itself.
            if (loop && loopSeconds > 0f) sp.StartCoroutine(EndLoopAfter(sp, loopSeconds));

            return sp.transform;
        }

        /// Ends a looping player after roughly a fixed time. It clears `loop` rather than calling Stop(),
        /// because Stop() only sets playing=false — it never raises Finished, so the release handler would
        /// never run and the pooled instance would still be lost. Clearing loop lets the player reach the end
        /// of its sequence normally, which IS the path that raises Finished (and it ends on the last frame
        /// instead of cutting mid-animation). It re-checks the clip first: a pooled instance can have been
        /// released and handed to somebody else by now, and un-looping their playback would be a baffling bug.
        static System.Collections.IEnumerator EndLoopAfter(ShaperPlayer sp, float seconds)
        {
            var clip = sp.clip;
            yield return new WaitForSeconds(seconds);
            if (sp == null || sp.clip != clip) yield break;
            sp.loop = false;
        }

        /// SortingLayer has no "does this name exist" query, but the id round-trip is one: an unknown name
        /// maps to the Default layer's id, whose name then does not match what went in.
        static bool IsValidSortingLayer(string name)
            => !string.IsNullOrEmpty(name) && SortingLayer.IDToName(SortingLayer.NameToID(name)) == name;
    }
}

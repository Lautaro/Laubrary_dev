using System;
using System.Collections.Generic;
using UnityEngine;
using Laubrary.Caching;

namespace Laubrary.Pyre
{
    /// Runtime player that shows a Pyre with no baked assets: it renders every frame to an in-memory Sprite
    /// through the shared BlastRenderer (so it matches the editor preview and any bake) and cycles a SpriteRenderer
    /// through them. Frame arrays are cached per spec so repeated spawns are cheap.
    [RequireComponent(typeof(SpriteRenderer))]
    public class BlastPlayer : MonoBehaviour
    {
        [Tooltip("The explosion to play. If left empty, assign one before Play() or nothing happens.")]
        public Pyre spec;

        [Tooltip("Playback speed in frames per second.")]
        public float fps = 24f;

        [Tooltip("Loop forever, or play once then (optionally) destroy the GameObject.")]
        public bool loop = false;

        [Tooltip("When not looping, destroy this GameObject once the animation finishes.")]
        public bool destroyOnFinish = true;

        [Tooltip("Start playing automatically on Awake.")]
        public bool playOnAwake = true;

        [Tooltip("Set by a pool owner. When true, a finished non-looping playback fires Finished instead of " +
                 "destroying the GameObject — the pool decides its fate, not this component.")]
        public bool pooled;

        /// Fired once when a non-looping playback finishes, ONLY while pooled — the pool's own signal to
        /// reclaim this instance. Never fires when destroyOnFinish already destroyed the object instead.
        public event Action Finished;

        static readonly Dictionary<int, Sprite[]> cache = new();

        // Opts into the general, engine-wide cache-invalidation bus (Laubrary.Caching): whenever ANY editor
        // tool edits a Pyre asset (Pyre's own window, the plain Inspector, doesn't matter which), the
        // Editor-side bridge detects it via Unity's own ObjectChangeEvents and calls Invalidate() — so this
        // cache drops automatically and the NEXT play re-renders with the edited data, including mid-Play-Mode
        // live tuning. First preference, not hard-wired: this is a plain opt-in subscription, not a
        // requirement Pyre imposes on anything.
        static BlastPlayer()
        {
            AssetCacheInvalidation.Invalidated += asset => { if (asset is Pyre spec) ClearCache(spec); };
        }

        SpriteRenderer sr;
        Sprite[] frames;
        float clock;
        bool playing;

        void Awake()
        {
            sr = GetComponent<SpriteRenderer>();
            if (spec != null) frames = GetFrames(spec);
            if (playOnAwake) Play();
        }

        public void Play()
        {
            if (spec == null) return;
            // Awake() (which normally sets sr) isn't guaranteed to have run yet if a caller does
            // AddComponent<BlastPlayer>() then configures + Play()s it in the same call — observed for real
            // via Mirage's previewable realization, both during a domain-reload-triggered OnEnable pass and
            // from a live view-switch. Resolve defensively rather than assume Awake already ran.
            if (sr == null) sr = GetComponent<SpriteRenderer>();
            // ALWAYS re-ask GetFrames here (not frames ??= ...) -- a pooled instance (PyreBlastPool) is
            // reused across many Play()s, possibly for a different spec each time, or the SAME spec
            // re-edited between shots. `??=` only refreshed `frames` the very FIRST time this instance ever
            // played anything; every later Play() kept showing whatever was cached on THIS INSTANCE from
            // its previous use, even though GetFrames' own STATIC cache had already been correctly
            // invalidated by AssetCacheInvalidation. GetFrames itself is already cheap on a cache hit (one
            // dictionary lookup) — there's no real cost to asking fresh every Play().
            frames = GetFrames(spec);
            clock = 0f;
            playing = frames != null && frames.Length > 0;
            if (playing) sr.sprite = frames[0];
        }

        public void Stop() => playing = false;

        void Update()
        {
            if (!playing || frames == null || frames.Length == 0) return;

            clock += Time.deltaTime * Mathf.Max(0.01f, fps);
            int index = Mathf.FloorToInt(clock);

            if (index >= frames.Length)
            {
                if (loop) { clock %= frames.Length; index = Mathf.FloorToInt(clock); }
                else
                {
                    sr.sprite = frames[frames.Length - 1];
                    playing = false;
                    if (pooled) Finished?.Invoke();
                    else if (destroyOnFinish) Destroy(gameObject);
                    return;
                }
            }
            sr.sprite = frames[index];
        }

        /// Build (or reuse) the per-frame Sprite array for a spec. Cached by instance id so the same spec renders once.
        public static Sprite[] GetFrames(Pyre spec)
        {
            if (spec == null) return null;
            int key = spec.GetInstanceID();
            if (cache.TryGetValue(key, out var cached) && cached != null && cached.Length > 0)
                return cached;

            int n = Mathf.Max(1, spec.frameCount);
            var built = new Sprite[n];
            var pivot = spec.origin;
            for (int f = 0; f < n; f++)
            {
                var tex = BlastRenderer.RenderFrameTexture(spec, f);
                built[f] = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), pivot, spec.pixelsPerUnit);
                built[f].name = "blast_" + f;
            }
            cache[key] = built;
            return built;
        }

        /// Drop a spec's cached frames (call after editing a spec at runtime so the next Play() re-renders).
        public static void ClearCache(Pyre spec)
        {
            if (spec != null) cache.Remove(spec.GetInstanceID());
        }
    }
}

using System;
using UnityEngine;
using Laubrary.Caching;

namespace Laubrary.Pyre
{
    /// Runtime player that shows a Pyre with no baked assets: it renders every frame to an in-memory
    /// Sprite through the shared PyreRenderer (so it matches the editor preview and any bake) and cycles a
    /// SpriteRenderer through them. Reuses PyreRenderer.GetFrames' own cache (shared with
    /// PyreChunkAnimation) rather than keeping a separate one. Mirrors Pyre1's own BlastPlayer exactly —
    /// same pooling contract, same Finished-event handshake — so PyreBlastPool is a drop-in swap for
    /// PyreBlastPool wherever a caller (PyreChunksFx, SpawnPyreFx, Mirage's realize switch) needs one.
    [RequireComponent(typeof(SpriteRenderer))]
    public class PyreBlastPlayer : MonoBehaviour
    {
        [Tooltip("The effect to play. If left empty, assign one before Play() or nothing happens.")]
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

        // Opts into the general, engine-wide cache-invalidation bus (Laubrary.Caching): whenever ANY editor
        // tool edits a Pyre asset, the Editor-side bridge detects it via Unity's own
        // ObjectChangeEvents and calls Invalidate() — so PyreRenderer's frame cache drops automatically
        // and the NEXT play re-renders with the edited data, including mid-Play-Mode live tuning.
        static PyreBlastPlayer()
        {
            AssetCacheInvalidation.Invalidated += asset => { if (asset is Pyre spec) PyreRenderer.ClearFrameCache(spec); };
        }

        SpriteRenderer sr;
        Sprite[] frames;
        float clock;
        bool playing;

        void Awake()
        {
            sr = GetComponent<SpriteRenderer>();
            if (spec != null) frames = PyreRenderer.GetFrames(spec);
            if (playOnAwake) Play();
        }

        public void Play()
        {
            if (spec == null) return;
            if (sr == null) sr = GetComponent<SpriteRenderer>();
            // ALWAYS re-ask GetFrames here (not frames ??= ...) — a pooled instance is reused across many
            // Play()s, possibly for a different spec each time, or the SAME spec re-edited between shots.
            frames = PyreRenderer.GetFrames(spec);
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
    }
}

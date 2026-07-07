using System.Collections.Generic;
using UnityEngine;

namespace Laubrary.Pyre
{
    /// Runtime player that shows a BlastSpec with no baked assets: it renders every frame to an in-memory Sprite
    /// through the shared BlastRenderer (so it matches the editor preview and any bake) and cycles a SpriteRenderer
    /// through them. Frame arrays are cached per spec so repeated spawns are cheap.
    [RequireComponent(typeof(SpriteRenderer))]
    public class BlastPlayer : MonoBehaviour
    {
        [Tooltip("The explosion to play. If left empty, assign one before Play() or nothing happens.")]
        public BlastSpec spec;

        [Tooltip("Playback speed in frames per second.")]
        public float fps = 24f;

        [Tooltip("Loop forever, or play once then (optionally) destroy the GameObject.")]
        public bool loop = false;

        [Tooltip("When not looping, destroy this GameObject once the animation finishes.")]
        public bool destroyOnFinish = true;

        [Tooltip("Start playing automatically on Awake.")]
        public bool playOnAwake = true;

        static readonly Dictionary<int, Sprite[]> cache = new();

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
            frames ??= GetFrames(spec);
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
                    if (destroyOnFinish) Destroy(gameObject);
                    return;
                }
            }
            sr.sprite = frames[index];
        }

        /// Build (or reuse) the per-frame Sprite array for a spec. Cached by instance id so the same spec renders once.
        public static Sprite[] GetFrames(BlastSpec spec)
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
        public static void ClearCache(BlastSpec spec)
        {
            if (spec != null) cache.Remove(spec.GetInstanceID());
        }
    }
}

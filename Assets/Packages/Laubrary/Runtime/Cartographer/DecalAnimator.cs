using System.Collections.Generic;
using UnityEngine;

namespace Laubrary.Cartographer
{
    /// Cycles an animated decal's SpriteRenderer through its frames. Attached by LevelInstance to the
    /// decals it builds — derived content, like everything else the instance makes. Frame choice is a pure
    /// function of time so the editor, a probe and the running game all agree on what frame N looks like.
    [ExecuteAlways]
    [AddComponentMenu("")]
    public class DecalAnimator : MonoBehaviour
    {
        public List<Sprite> frames = new();
        public float fps = 6f;

        SpriteRenderer sr;

        /// Show the frame that `time` (seconds) lands on. Deterministic and side-effect-free beyond the
        /// renderer swap, so it is directly verifiable without running a scene clock.
        public void Tick(double time)
        {
            if (frames == null || frames.Count == 0 || fps <= 0f) return;
            if (sr == null) sr = GetComponent<SpriteRenderer>();
            if (sr == null) return;
            sr.sprite = frames[(int)(time * fps) % frames.Count];
        }

        void Update()
        {
#if UNITY_EDITOR
            if (!Application.isPlaying) { Tick(UnityEditor.EditorApplication.timeSinceStartup); return; }
#endif
            Tick(Time.timeAsDouble);
        }
    }
}

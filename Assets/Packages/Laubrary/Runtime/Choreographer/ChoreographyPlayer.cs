using System.Collections.Generic;
using UnityEngine;

namespace Laubrary.Choreographer
{
    /// Drives a set of target Transforms (the Dancers) along a Choreography at runtime. The normalised choreo
    /// is placed into the world through this component's anchor and worldSize, so the same asset fits any scene
    /// or resolution. Count comes from the target list, so N is whatever you hand it — the choreography packs
    /// itself across that many dancers automatically.
    [AddComponentMenu("Laubrary/Choreographer/Choreography Player")]
    public class ChoreographyPlayer : MonoBehaviour
    {
        public Choreography choreography;

        [Tooltip("World origin & orientation for the choreography. Falls back to this transform if empty.")]
        public Transform anchor;
        [Tooltip("Normalised space is scaled by this to reach world units (X, Y).")]
        public Vector2 worldSize = new(10f, 6f);

        [Tooltip("The dancers. Their order is their index, so ni distributes across this list.")]
        public List<Transform> targets = new();

        [Header("Optional anchors")]
        [Tooltip("If set and the choreography uses it, every dancer's journey starts from here.")]
        public Transform launcher;
        [Tooltip("If set and the choreography uses it, every dancer's journey ends on here (e.g. the player).")]
        public Transform target;

        [Tooltip("Freeze the anchors per dancer: sample the launcher when it launches, and the target when it " +
                 "reaches the retarget marker — then hold each. So the choreography does NOT slide around if those " +
                 "transforms keep moving, and each dancer homes onto where the target was when it committed. Off = " +
                 "track both every frame.")]
        public bool freezeAnchors = true;

        [Tooltip("Rotate each dancer to face its travel direction.")]
        public bool applyFacing = false;
        public bool playOnEnable = true;
        [Min(0f)] public float speed = 1f;

        float time;
        bool playing;

        // Per-dancer captured anchor positions (normalised): launcher frozen at launch, target at the marker.
        Vector2[] capLauncher = System.Array.Empty<Vector2>();
        Vector2[] capTarget = System.Array.Empty<Vector2>();
        bool[] capturedL = System.Array.Empty<bool>();
        bool[] capturedT = System.Array.Empty<bool>();

        Transform Anchor => anchor != null ? anchor : transform;

        void OnEnable() { if (playOnEnable) Play(); }

        public bool IsPlaying => playing;
        public void Play() { playing = true; }
        public void Pause() { playing = false; }
        public void Restart() { time = 0f; playing = true; }

        void Update()
        {
            if (!playing || choreography == null || targets.Count == 0) return;
            time += Time.deltaTime * speed;
            Apply(Phase());
        }

        float Phase()
        {
            float cycle = Mathf.Max(0.0001f, choreography.CycleSeconds);
            return choreography.loop ? Mathf.Repeat(time, cycle) / cycle : Mathf.Clamp01(time / cycle);
        }

        void Apply(float phase)
        {
            var a = Anchor;
            int n = targets.Count;
            // Keep all four capture arrays sized to n (each checked independently — a domain reload can leave any
            // one null/stale), so per-dancer indexing below can never go out of bounds.
            if (capLauncher == null || capLauncher.Length != n) capLauncher = new Vector2[n];
            if (capTarget == null || capTarget.Length != n) capTarget = new Vector2[n];
            if (capturedL == null || capturedL.Length != n) capturedL = new bool[n];
            if (capturedT == null || capturedT.Length != n) capturedT = new bool[n];

            bool useL = launcher != null && choreography.useLauncher;
            bool useT = target != null && choreography.useTarget;
            Vector2 curL = useL ? WorldToNorm(a, launcher.position) : default;
            Vector2 curT = useT ? WorldToNorm(a, target.position) : default;

            for (int i = 0; i < n; i++)
            {
                var t = targets[i];
                if (t == null) continue;

                float progress = ChoreographySampler.ProgressAt(choreography, i, n, phase);
                if (freezeAnchors)
                {
                    // Launcher: frozen the instant the dancer leaves the launcher.
                    if (progress <= 0f) capturedL[i] = false;
                    else if (!capturedL[i]) { capLauncher[i] = curL; capturedL[i] = true; }
                    // Target: frozen the instant the dancer reaches the retarget marker (where it commits to home).
                    if (progress < choreography.retargetAt) capturedT[i] = false;
                    else if (!capturedT[i]) { capTarget[i] = curT; capturedT[i] = true; }
                }

                var an = new ChoreoAnchors();
                if (useL) { an.hasLauncher = true; an.launcher = (freezeAnchors && capturedL[i]) ? capLauncher[i] : curL; }
                if (useT) { an.hasTarget = true; an.target = (freezeAnchors && capturedT[i]) ? capTarget[i] : curT; }

                var pose = ChoreographySampler.Evaluate(choreography, i, n, phase, an);
                t.position = a.TransformPoint(new Vector3(pose.position.x * worldSize.x, pose.position.y * worldSize.y, 0f));
                if (applyFacing)
                    t.rotation = a.rotation * Quaternion.Euler(0f, 0f, pose.facingDegrees);
            }
        }

        Vector2 WorldToNorm(Transform a, Vector3 world)
        {
            Vector3 local = a.InverseTransformPoint(world);
            float x = Mathf.Abs(worldSize.x) > 1e-4f ? local.x / worldSize.x : 0f;
            float y = Mathf.Abs(worldSize.y) > 1e-4f ? local.y / worldSize.y : 0f;
            return new Vector2(x, y);
        }
    }
}

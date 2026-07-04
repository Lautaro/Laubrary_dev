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
                // Launcher frozen the instant the dancer leaves the launcher; target frozen the instant it reaches Release.
                if (progress <= 0f) capturedL[i] = false;
                else if (!capturedL[i]) { capLauncher[i] = curL; capturedL[i] = true; }
                if (progress < choreography.releaseAt) capturedT[i] = false;
                else if (!capturedT[i]) { capTarget[i] = curT; capturedT[i] = true; }

                var an = new ChoreoAnchors();
                if (useL) { an.hasLauncher = true; an.launcher = capturedL[i] ? capLauncher[i] : curL; }
                if (useT) { an.hasTarget = true; an.target = capturedT[i] ? capTarget[i] : curT; }

                var pose = ChoreographySampler.Evaluate(choreography, i, n, phase, an);
                t.position = a.TransformPoint(new Vector3(pose.position.x * worldSize.x, pose.position.y * worldSize.y, 0f));
                if (applyFacing)
                    t.rotation = a.rotation * Quaternion.Euler(0f, 0f, pose.facingDegrees);
            }
        }

        // ── read-only accessors for debug views / gameplay hooks ──────────────────
        public int DancerCount => targets.Count;

        /// A dancer's own journey progress (0..1) right now — used to detect wave restarts (progress wrapping to 0).
        public float ProgressOf(int i) => choreography != null ? ChoreographySampler.ProgressAt(choreography, i, Mathf.Max(1, targets.Count), Phase()) : 0f;

        /// Map a normalised choreography position into the world (through this player's anchor + worldSize).
        public Vector3 WorldPoint(Vector2 normalized) => Anchor.TransformPoint(new Vector3(normalized.x * worldSize.x, normalized.y * worldSize.y, 0f));

        /// The anchors a given dancer is flying with right now (frozen values honoured) — so a debug view draws
        /// exactly the committed route.
        public ChoreoAnchors AnchorsForDancer(int i)
        {
            var a = Anchor;
            bool useL = launcher != null && choreography != null && choreography.useLauncher;
            bool useT = target != null && choreography != null && choreography.useTarget;
            var an = new ChoreoAnchors();
            if (useL) { an.hasLauncher = true; an.launcher = i < capturedL.Length && capturedL[i] ? capLauncher[i] : WorldToNorm(a, launcher.position); }
            if (useT) { an.hasTarget = true; an.target = i < capturedT.Length && capturedT[i] ? capTarget[i] : WorldToNorm(a, target.position); }
            return an;
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

using System.Collections.Generic;
using UnityEngine;

namespace Laubrary.Choreographer
{
    /// Debug-only in-game visualiser: draws each dancer's current full route as a LineRenderer, colour-coded by
    /// index, updated in real time. Because it uses LineRenderers (not editor gizmos) it shows in the Scene view,
    /// the Game view AND a build — so it is gated behind a toggle and meant purely for debugging. It reuses
    /// ChoreographySampler + the player's own anchors, so it can never diverge from what the dancers actually fly
    /// or from the editor preview.
    [AddComponentMenu("Laubrary/Choreographer/Choreography Debug View")]
    [RequireComponent(typeof(ChoreographyPlayer))]
    public class ChoreographyDebugView : MonoBehaviour
    {
        /// Master switch for every debug view in the scene (wire a key or UI toggle to it).
        public static bool GlobalEnabled = true;

        public ChoreographyPlayer player;
        public bool show = true;
        [Min(2)] public int segments = 40;
        public float width = 0.06f;
        [Range(0f, 1f)] public float alpha = 0.7f;

        readonly List<LineRenderer> lines = new();
        Material mat;

        void Reset() => player = GetComponent<ChoreographyPlayer>();
        void OnEnable() { if (player == null) player = GetComponent<ChoreographyPlayer>(); }
        void OnDisable() => HideAll();

        void LateUpdate()
        {
            if (!show || !GlobalEnabled || player == null || player.choreography == null) { HideAll(); return; }

            int n = player.DancerCount;
            EnsureLines(n);
            for (int i = 0; i < n; i++)
            {
                var lr = lines[i];
                lr.enabled = true;
                lr.positionCount = segments + 1;
                var an = player.AnchorsForDancer(i);
                for (int k = 0; k <= segments; k++)
                    lr.SetPosition(k, player.WorldPoint(ChoreographySampler.SamplePosition(player.choreography, i, n, k / (float)segments, an)));
                var c = ChoreographySampler.IndexColor(ChoreographySampler.Ni(i, n)); c.a = alpha;
                lr.startColor = lr.endColor = c;
                lr.widthMultiplier = width;
            }
            for (int i = n; i < lines.Count; i++) if (lines[i]) lines[i].enabled = false;
        }

        void EnsureLines(int n)
        {
            if (mat == null) mat = new Material(Shader.Find("Sprites/Default"));
            while (lines.Count < n)
            {
                var go = new GameObject("ChoreoRoute " + lines.Count) { hideFlags = HideFlags.DontSave };
                go.transform.SetParent(transform, false);
                var lr = go.AddComponent<LineRenderer>();
                lr.material = mat;
                lr.useWorldSpace = true;
                lr.numCapVertices = 2;
                lr.sortingOrder = 500;
                lines.Add(lr);
            }
        }

        void HideAll() { foreach (var lr in lines) if (lr) lr.enabled = false; }

        void OnDestroy()
        {
            if (mat) DestroyImmediate(mat);
            foreach (var lr in lines) if (lr) DestroyImmediate(lr.gameObject);
            lines.Clear();
        }
    }
}

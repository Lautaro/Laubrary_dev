using System;
using System.Collections.Generic;
using UnityEngine;

namespace Laubrary.Lattice
{
    /// <summary>
    /// Draws every edge of a <see cref="LatticeGraph"/> as a LineRenderer and
    /// colours it by a consumer-defined integer "state" via a material swap.
    /// The consumer supplies the materials and two functions: the state of a
    /// whole edge (or -1 when the edge must be coloured per chunk) and the
    /// state of an individual chunk. Per-chunk edges are drawn as one pooled
    /// line per run of equal-state chunks.
    ///
    /// Colour is by material only. Per-vertex LineRenderer colours are not
    /// used because URP's Unlit shader ignores them.
    /// </summary>
    public sealed class LatticeLineView : MonoBehaviour
    {
        [Tooltip("Line width in world units.")]
        public float LineWidth = 0.18f;

        /// <summary>One material per state id.</summary>
        public Material[] StateMaterials;

        /// <summary>State id for a whole edge, or -1 if it must be drawn per chunk.</summary>
        public Func<LatticeEdge, int> UniformState;

        /// <summary>State id of chunk i of an edge. Only called for edges whose UniformState is -1.</summary>
        public Func<LatticeEdge, int, int> ChunkState;

        private LatticeGraph _graph;
        private Transform _root, _runRoot;
        private readonly Dictionary<LatticeEdge, LineRenderer> _lineByEdge = new Dictionary<LatticeEdge, LineRenderer>();
        private readonly Dictionary<LatticeEdge, int> _lastState = new Dictionary<LatticeEdge, int>();
        private readonly Dictionary<LatticeEdge, List<LineRenderer>> _runsByEdge = new Dictionary<LatticeEdge, List<LineRenderer>>();

        private const int Unset = -2;
        private const int PerChunk = -1;

        public void Build(LatticeGraph graph)
        {
            _graph = graph;
            if (_root != null) Destroy(_root.gameObject);
            if (_runRoot != null) Destroy(_runRoot.gameObject);
            _root = new GameObject("Edges").transform;
            _root.SetParent(transform, false);
            _runRoot = new GameObject("ChunkRuns").transform;
            _runRoot.SetParent(transform, false);
            _lineByEdge.Clear();
            _lastState.Clear();
            _runsByEdge.Clear();

            foreach (var e in graph.Edges)
            {
                var lr = NewLine(e.ToString(), _root);
                lr.SetPosition(0, graph.CellWorld(e.AX, e.AY));
                lr.SetPosition(1, graph.CellWorld(e.BX, e.BY));
                _lineByEdge[e] = lr;
            }
            Refresh();
        }

        /// <summary>
        /// Re-evaluates every edge's state and updates only the renderers whose
        /// state changed. Per-chunk edges are always rebuilt.
        /// </summary>
        public void Refresh()
        {
            if (_graph == null || UniformState == null) return;
            foreach (var e in _graph.Edges)
            {
                if (!_lineByEdge.TryGetValue(e, out var lr)) continue;
                int state = UniformState(e);
                _lastState.TryGetValue(e, out int last);
                if (!_lastState.ContainsKey(e)) last = Unset;

                if (state >= 0)
                {
                    if (state == last) continue;
                    if (last == PerChunk && _runsByEdge.TryGetValue(e, out var runs)) HideFrom(runs, 0);
                    lr.gameObject.SetActive(true);
                    lr.sharedMaterial = StateMaterials[state];
                    _lastState[e] = state;
                }
                else
                {
                    _lastState[e] = PerChunk;
                    lr.gameObject.SetActive(false);
                    DrawRuns(e);
                }
            }
        }

        private void DrawRuns(LatticeEdge e)
        {
            int n = _graph.ChunkCount;
            Vector3 a = _graph.CellWorld(e.AX, e.AY);
            Vector3 b = _graph.CellWorld(e.BX, e.BY);
            if (!_runsByEdge.TryGetValue(e, out var runs))
            {
                runs = new List<LineRenderer>();
                _runsByEdge[e] = runs;
            }

            int used = 0;
            int runStart = 0;
            int runState = ChunkState(e, 0);
            for (int i = 1; i <= n; i++)
            {
                bool atEnd = i == n;
                int cur = atEnd ? runState : ChunkState(e, i);
                if (!atEnd && cur == runState) continue;

                while (runs.Count <= used) runs.Add(NewLine("Run" + runs.Count, _runRoot));
                var lr = runs[used++];
                lr.gameObject.SetActive(true);
                lr.sharedMaterial = StateMaterials[runState];
                lr.SetPosition(0, Vector3.Lerp(a, b, (float)runStart / n));
                lr.SetPosition(1, Vector3.Lerp(a, b, (float)i / n));

                runStart = i;
                runState = cur;
            }
            HideFrom(runs, used);
        }

        private static void HideFrom(List<LineRenderer> runs, int index)
        {
            for (int i = index; i < runs.Count; i++)
                if (runs[i] != null) runs[i].gameObject.SetActive(false);
        }

        private LineRenderer NewLine(string name, Transform parent)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var lr = go.AddComponent<LineRenderer>();
            lr.useWorldSpace = true;
            lr.positionCount = 2;
            lr.startWidth = LineWidth;
            lr.endWidth = LineWidth;
            lr.numCapVertices = 2;
            lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            lr.receiveShadows = false;
            return lr;
        }

        /// <summary>
        /// A solid-colour unlit material using whichever unlit shader the
        /// project has. With transparent = true the material alpha-blends,
        /// which URP's Unlit shader does not do by default.
        /// </summary>
        public static Material UnlitMaterial(Color c, bool transparent = false)
        {
            Shader s = Shader.Find("Universal Render Pipeline/Unlit")
                    ?? Shader.Find("Sprites/Default")
                    ?? Shader.Find("Unlit/Color");
            var m = new Material(s);
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
            if (m.HasProperty("_Color")) m.SetColor("_Color", c);
            if (transparent) SetTransparent(m);
            return m;
        }

        /// <summary>Writes a colour to a material made by <see cref="UnlitMaterial"/>.</summary>
        public static void SetUnlitColor(Material m, Color c)
        {
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
            if (m.HasProperty("_Color")) m.SetColor("_Color", c);
        }

        private static void SetTransparent(Material m)
        {
            if (m.HasProperty("_Surface"))
            {
                m.SetFloat("_Surface", 1f);
                m.SetFloat("_Blend", 0f);
                m.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
                m.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                m.SetInt("_ZWrite", 0);
                m.DisableKeyword("_ALPHATEST_ON");
                m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                m.SetOverrideTag("RenderType", "Transparent");
            }
            m.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
        }
    }
}

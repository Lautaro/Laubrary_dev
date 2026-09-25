using System.Collections.Generic;
using UnityEngine;

namespace Laubrary.Zounds.Dsp {

    /// <summary>Flattened envelope point, published as an immutable snapshot for the audio thread.</summary>
    public readonly struct EnvPoint {
        public readonly float time, value, exponent;
        public EnvPoint(float time, float value, float exponent) { this.time = time; this.value = value; this.exponent = exponent; }
    }

    /// <summary>
    /// The audio-thread view of a chain: parallel arrays, packed parameters, arena offsets and a
    /// precomputed tail budget. Built on the main thread from authored <see cref="ZoundEffectChain"/>
    /// data and never mutated afterwards; a chain edit produces a new layout and playing voices keep
    /// the one they started with. Nothing here is read from the authored lists on the audio thread.
    /// </summary>
    public sealed class ChainLayout {

        public const int MAX_PARAMS = SourceStageParam.Count + ZoundDspConstants.MAX_NODES * 12;

        // ── nodes ──
        public int nodeCount;
        public readonly ZoundEffectType[] nodeType = new ZoundEffectType[ZoundDspConstants.MAX_NODES];
        public readonly bool[] enabled = new bool[ZoundDspConstants.MAX_NODES];
        public readonly int[] stateOffset = new int[ZoundDspConstants.MAX_NODES];
        public readonly int[] paramOffset = new int[ZoundDspConstants.MAX_NODES];
        public readonly int[] paramCountOf = new int[ZoundDspConstants.MAX_NODES];

        // ── flat parameters (source stage first: pitch, source gain) ──
        public int paramCount;
        public readonly float[] pBase = new float[MAX_PARAMS];
        public readonly float[] pMin = new float[MAX_PARAMS];
        public readonly float[] pMax = new float[MAX_PARAMS];
        /// <summary>Flat indices of parameters that ramp per sample (binding targets). Others hold pBase.</summary>
        public int rampedCount;
        public readonly int[] ramped = new int[MAX_PARAMS];

        // ── modifiers ──
        public int modCount;
        public readonly ZoundModifierType[] modType = new ZoundModifierType[ZoundDspConstants.MAX_MODIFIERS];
        public readonly float[][] modParams = new float[ZoundDspConstants.MAX_MODIFIERS][];
        public readonly int[] modStateOffset = new int[ZoundDspConstants.MAX_MODIFIERS];
        public readonly EnvPoint[][] modCurve = new EnvPoint[ZoundDspConstants.MAX_MODIFIERS][];
        public readonly float[][] modSteps = new float[ZoundDspConstants.MAX_MODIFIERS][];
        /// <summary>Per-modifier extra seconds past the source end over which an Envelope keeps evolving.</summary>
        public readonly float[] modExtraSeconds = new float[ZoundDspConstants.MAX_MODIFIERS];

        // ── bindings ──
        public int bindCount;
        public readonly int[] bindModifier = new int[ZoundDspConstants.MAX_BINDINGS];
        public readonly int[] bindTarget = new int[ZoundDspConstants.MAX_BINDINGS];
        public readonly ModifierOp[] bindOp = new ModifierOp[ZoundDspConstants.MAX_BINDINGS];
        public readonly float[] bindDepth = new float[ZoundDspConstants.MAX_BINDINGS];

        // ── totals ──
        public int stateFloats;
        public bool heavy;
        public float tailSeconds;
        public bool pitchModulated;
        public int sourceVersion;
        public string error;

        public static readonly ChainLayout Empty = Build(null, 48000);

        public bool IsEmptyChain => nodeCount == 0 && modCount == 0;

        /// <summary>Flat parameter index of (node, param); nodeIndex -1 is the source stage.</summary>
        public int FlatIndex(int nodeIndex, int paramIndex) {
            if (nodeIndex < 0) return paramIndex;
            return paramOffset[nodeIndex] + paramIndex;
        }

        public static ChainLayout Build(ZoundEffectChain chain, int sampleRate, List<ChainParamOverride> overrides = null) {
            var L = new ChainLayout();
            // Source stage parameters.
            for (int i = 0; i < SourceStageParam.Count; i++) {
                var d = ZoundEffectDescriptors.SourceStageParams[i];
                L.pBase[i] = d.def; L.pMin[i] = d.min; L.pMax[i] = d.max;
            }
            L.paramCount = SourceStageParam.Count;
            int state = 0;
            if (chain != null) {
                L.sourceVersion = chain.version;
                int nodes = Mathf.Min(chain.nodes.Count, ZoundDspConstants.MAX_NODES);
                if (chain.nodes.Count > nodes) L.error = "Chain has more than " + ZoundDspConstants.MAX_NODES + " nodes; extra nodes are ignored.";
                for (int i = 0; i < nodes; i++) {
                    var n = chain.nodes[i];
                    var d = ZoundEffectDescriptors.Get(n.type);
                    L.nodeType[i] = n.type;
                    L.enabled[i] = n.enabled;
                    L.paramOffset[i] = L.paramCount;
                    L.paramCountOf[i] = d.parameters.Length;
                    for (int k = 0; k < d.parameters.Length; k++) {
                        var pd = d.parameters[k];
                        float v = n.p != null && k < n.p.Length ? n.p[k] : pd.def;
                        int f = L.paramCount + k;
                        L.pBase[f] = Mathf.Clamp(v, pd.min, pd.max);
                        L.pMin[f] = pd.min; L.pMax[f] = pd.max;
                    }
                    L.paramCount += d.parameters.Length;
                    L.stateOffset[i] = state;
                    if (d.isStateful) state += d.stateFloats(n.p, sampleRate);
                    if (d.heavy) L.heavy = true;
                }
                L.nodeCount = nodes;

                if (overrides != null) {
                    for (int i = 0; i < overrides.Count; i++) {
                        var o = overrides[i];
                        if (o.nodeIndex >= nodes || o.nodeIndex < -1) continue;
                        if (o.nodeIndex >= 0 && o.paramIndex >= L.paramCountOf[o.nodeIndex]) continue;
                        if (o.nodeIndex < 0 && o.paramIndex >= SourceStageParam.Count) continue;
                        int f = L.FlatIndex(o.nodeIndex, o.paramIndex);
                        L.pBase[f] = Mathf.Clamp(o.value, L.pMin[f], L.pMax[f]);
                    }
                }

                int mods = Mathf.Min(chain.modifiers.Count, ZoundDspConstants.MAX_MODIFIERS);
                for (int i = 0; i < mods; i++) {
                    var m = chain.modifiers[i];
                    var d = ZoundEffectDescriptors.GetModifier(m.type);
                    L.modType[i] = m.type;
                    var mp = new float[d.parameters.Length];
                    for (int k = 0; k < mp.Length; k++) mp[k] = m.p != null && k < m.p.Length ? m.p[k] : d.parameters[k].def;
                    L.modParams[i] = mp;
                    L.modStateOffset[i] = state;
                    state += d.stateFloats;
                    L.modCurve[i] = Snapshot(m.curve);
                    L.modSteps[i] = m.steps != null && m.steps.Length > 0 ? (float[])m.steps.Clone() : new float[] { 1f };
                    L.modExtraSeconds[i] = m.type == ZoundModifierType.Envelope ? mp[0] : 0f;
                }
                L.modCount = mods;

                int binds = 0;
                for (int i = 0; i < chain.bindings.Count && binds < ZoundDspConstants.MAX_BINDINGS; i++) {
                    var b = chain.bindings[i];
                    if (b.modifierIndex < 0 || b.modifierIndex >= mods) continue;
                    if (!chain.modifiers[b.modifierIndex].enabled) continue;
                    if (b.nodeIndex >= nodes || b.nodeIndex < -1) continue;
                    if (b.nodeIndex >= 0 && (b.paramIndex < 0 || b.paramIndex >= L.paramCountOf[b.nodeIndex])) continue;
                    if (b.nodeIndex < 0 && (b.paramIndex < 0 || b.paramIndex >= SourceStageParam.Count)) continue;
                    int f = L.FlatIndex(b.nodeIndex, b.paramIndex);
                    L.bindModifier[binds] = b.modifierIndex;
                    L.bindTarget[binds] = f;
                    L.bindOp[binds] = b.op;
                    L.bindDepth[binds] = b.depth;
                    binds++;
                    bool already = false;
                    for (int r = 0; r < L.rampedCount; r++) if (L.ramped[r] == f) { already = true; break; }
                    if (!already) L.ramped[L.rampedCount++] = f;
                    if (f == SourceStageParam.Pitch) L.pitchModulated = true;
                }
                L.bindCount = binds;
                L.tailSeconds = ZoundEffectDescriptors.TailBudgetSeconds(chain);
            }
            L.stateFloats = state;
            if (state > ZoundDspConstants.HEAVY_ARENA_FLOATS) {
                L.error = "Chain state (" + state + " floats) exceeds the largest voice arena; the chain is bypassed.";
                L.nodeCount = 0; L.modCount = 0; L.bindCount = 0; L.rampedCount = 0; L.stateFloats = 0; L.heavy = false; L.tailSeconds = 0f;
            }
            else if (state > ZoundDspConstants.LIGHT_ARENA_FLOATS) {
                L.heavy = true;
            }
            return L;
        }

        public static EnvPoint[] Snapshot(Envelope e) {
            if (e == null || e.Count == 0) return new[] { new EnvPoint(0f, 1f, 1f), new EnvPoint(1f, 1f, 1f) };
            var pts = e.GetPointsList();
            var r = new EnvPoint[pts.Count];
            for (int i = 0; i < pts.Count; i++) r[i] = new EnvPoint(pts[i].time, pts[i].value, pts[i].exponent);
            return r;
        }

        /// <summary>Envelope.Evaluate's maths, over the snapshot, with a cached segment index (O(1) amortised).</summary>
        public static float EvaluateEnvelope(EnvPoint[] pts, float time, ref int segment) {
            int count = pts.Length;
            if (count == 0) return 1f;
            if (count == 1) return pts[0].value;
            if (time <= pts[0].time) { segment = 0; return pts[0].value; }
            if (time >= pts[count - 1].time) { segment = count - 2; return pts[count - 1].value; }
            if (segment < 0 || segment >= count - 1) segment = 0;
            while (segment > 0 && pts[segment].time > time) segment--;
            while (segment < count - 2 && pts[segment + 1].time <= time) segment++;
            float x1 = pts[segment].time, x2 = pts[segment + 1].time;
            float t = x2 > x1 ? (time - x1) / (x2 - x1) : 1f;
            float exp = pts[segment + 1].exponent;
            if (exp <= 0f) exp = 0.000001f;
            float a = pts[segment].value, b = pts[segment + 1].value;
            return a + (b - a) * Mathf.Pow(t, exp);
        }
    }

}

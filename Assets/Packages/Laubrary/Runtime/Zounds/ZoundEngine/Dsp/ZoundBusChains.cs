using System.Collections.Generic;

namespace Laubrary.Zounds.Dsp {

    /// <summary>
    /// The Zequences around one play whose own effect chains its sound goes through (2026-10-09): innermost first, up to
    /// three levels. A Zequence's chain is heard on each of its tracks after the track's own Volume, at the end of the
    /// track's voice, so the voices, their per-play values, their ZPOC values and their snapshots stay exactly what they
    /// were. Exact for effects that treat a sum like its parts (gains, equalisers, filters, a delay, a reverb); for an
    /// effect that reacts to its own level (a compressor, a limiter, a distortion) each track is processed on its own
    /// rather than the tracks' sum. A Zequence's modifiers run on each track's own clock.
    /// </summary>
    public struct ZoundBusChains {
        public Zound a, b, c;
        public bool Any => a != null;

        /// <summary>These, with <paramref name="inner"/> (a Zequence whose own chain is not empty) as the new innermost.</summary>
        public ZoundBusChains Inside(Zound inner) => inner == null ? this : new ZoundBusChains { a = inner, b = a, c = b };

        public Zound this[int i] => i == 0 ? a : i == 1 ? b : c;
        public const int Max = 3;
    }

    public static partial class ZoundBus {

        /// <summary>Whether a Zequence's own chain has anything for its tracks to go through.</summary>
        public static bool HasChain(Zound zequence) {
            if (!(zequence is CompositeZound)) return false;
            var chain = ZoundDspPlayback.PlayChain(zequence);
            return chain != null && chain.nodes != null && chain.nodes.Count > 0;
        }

        /// <summary>
        /// The chain a track's voice plays: its sound's chain, then the effects and modifiers of the Zequences around it,
        /// innermost first, with their bindings moved to where their effects and modifiers now sit. A binding of a
        /// Zequence's modifier to the Zequence's own source stage is left out (a Zequence's volume and pitch reach its
        /// tracks as their volume and pitch, not through their voices). The element objects are shared, never copied, so
        /// an edit of a Zequence's effect reaches every track. <paramref name="nodeBase"/>[i] says where owner i's
        /// effects begin (-1: it adds none).
        /// </summary>
        public static ZoundEffectChain Merge(ZoundEffectChain own, in ZoundBusChains bus, int[] nodeBase) {
            var m = new ZoundEffectChain();
            if (own != null) {
                m.nodes.AddRange(own.nodes); m.modifiers.AddRange(own.modifiers); m.bindings.AddRange(own.bindings);
            }
            m.busNodeStart = m.nodes.Count;
            int version = own != null ? own.version : 0;
            for (int i = 0; i < ZoundBusChains.Max; i++) {
                if (nodeBase != null && i < nodeBase.Length) nodeBase[i] = -1;
                var owner = bus[i];
                if (owner == null) continue;
                var zc = ZoundDspPlayback.PlayChain(owner);
                if (zc == null || zc.IsEmpty) continue;
                int nb = m.nodes.Count, mb = m.modifiers.Count;
                if (nodeBase != null && i < nodeBase.Length) nodeBase[i] = nb;
                m.nodes.AddRange(zc.nodes);
                m.modifiers.AddRange(zc.modifiers);
                foreach (var b in zc.bindings) {
                    if (b.nodeIndex < 0) continue;
                    var c = b.DeepCopy();
                    c.modifierIndex += mb; c.nodeIndex += nb;
                    m.bindings.Add(c);
                }
                version = version * 31 + zc.version + 7;
            }
            m.version = version;
            return m;
        }

        sealed class Entry {
            public ZoundEffectChain own, merged;
            public int ownVersion, v0, v1, v2, sampleRate;
            public ZoundEffectChain c0, c1, c2;
            public ChainLayout layout;
            public readonly int[] nodeBase = new int[ZoundBusChains.Max];
        }
        static readonly Dictionary<(Zound, Zound, Zound, Zound), Entry> cache = new Dictionary<(Zound, Zound, Zound, Zound), Entry>();

        public static void Invalidate() => cache.Clear();

        static ZoundEffectChain ChainOf(Zound z) => z != null ? ZoundDspPlayback.PlayChain(z) : null;

        /// <summary>
        /// The merged chain and its layout for a play of <paramref name="sound"/> inside <paramref name="bus"/>, laid out
        /// once and kept while nothing involved changes (main thread). <paramref name="own"/> is the chain the play
        /// decided on (the sound's own, or a converted copy of it).
        /// </summary>
        public static ChainLayout Layout(Zound sound, ZoundEffectChain own, in ZoundBusChains bus, int sampleRate,
                                         out ZoundEffectChain merged, out int[] nodeBase) {
            var key = (sound, bus.a, bus.b, bus.c);
            ZoundEffectChain c0 = ChainOf(bus.a), c1 = ChainOf(bus.b), c2 = ChainOf(bus.c);
            if (!cache.TryGetValue(key, out var e) || e.own != own || e.ownVersion != (own != null ? own.version : 0) || e.sampleRate != sampleRate
                || e.c0 != c0 || e.c1 != c1 || e.c2 != c2
                || e.v0 != (c0 != null ? c0.version : 0) || e.v1 != (c1 != null ? c1.version : 0) || e.v2 != (c2 != null ? c2.version : 0)) {
                e = new Entry { own = own, ownVersion = own != null ? own.version : 0, sampleRate = sampleRate, c0 = c0, c1 = c1, c2 = c2,
                                v0 = c0 != null ? c0.version : 0, v1 = c1 != null ? c1.version : 0, v2 = c2 != null ? c2.version : 0 };
                e.merged = Merge(own, bus, e.nodeBase);
                e.layout = ChainLayout.Build(e.merged, sampleRate);
                if (e.layout.error != null) UnityEngine.Debug.LogWarning("[Zounds] " + (sound != null ? sound.name : "(sound)") + " in a Zequence with effects: " + e.layout.error);
                cache[key] = e;
            }
            merged = e.merged; nodeBase = e.nodeBase;
            return e.layout;
        }
    }
}

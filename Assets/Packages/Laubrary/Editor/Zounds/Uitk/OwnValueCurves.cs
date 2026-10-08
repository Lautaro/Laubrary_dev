using Laubrary.Audio;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Laubrary.Zounds.Dsp;
using UnityEngine;

namespace Laubrary.Zounds.Uitk {

    /// <summary>
    /// Which modifiers are shown in the curve displays (T-0494, the owner's "eye box"): view state, not data -- never saved,
    /// no Undo, kept per modifier OBJECT so it never drifts when modifiers are reordered. A modifier with its eye off is left
    /// out of every combined result drawn, and its own curve, random ellipses and dotted live curve are hidden, except the
    /// editable points of a curve that is currently selected for editing (you cannot edit handles you cannot see).
    /// </summary>
    internal static class CurveView {
        sealed class Flag { public bool hidden; }
        static readonly ConditionalWeakTable<ZoundModifier, Flag> flags = new ConditionalWeakTable<ZoundModifier, Flag>();

        /// <summary>Bumped on every change, so displays and cached measurements know to refresh.</summary>
        public static int Revision { get; private set; }

        public static bool IsVisible(ZoundModifier m) => m == null || !flags.TryGetValue(m, out var f) || !f.hidden;

        public static void SetVisible(ZoundModifier m, bool visible) {
            if (m == null || IsVisible(m) == visible) return;
            flags.GetOrCreateValue(m).hidden = !visible;
            Revision++;
        }
    }

    /// <summary>
    /// What a value on the waveform actually does once EVERY modifier on it has been applied (T-0494): the combined
    /// result, measured from the engine across one reference play (oscillators from the top of their cycle, random draws
    /// at the reference seed, game-code values at rest) and laid out against the SOURCE position, which is what a curve
    /// on the waveform is read by. Drawn lighter than the curve's own editable points, so the envelope you drag stays
    /// the envelope, and you see what an oscillator on top of it does to it.
    ///
    /// Only worth drawing when something besides the curve itself moves the value; with the curve alone the result is
    /// the curve. Measured once per edit (a few milliseconds per second of sound) and cached.
    /// </summary>
    internal static class OwnValueCurves {

        public struct Line { public float[] x, y; }

        sealed class Cache { public string sig; public Measured measured; }
        /// <summary>Holds a measurement (a struct) by reference, so it can live in a weak table keyed by the Zound.</summary>
        sealed class Measured { public EditorTools.ChainSpectrumProbe.Measurement m; }
        static readonly ConditionalWeakTable<Zound, Cache> cache = new ConditionalWeakTable<Zound, Cache>();

        /// <summary>
        /// The combined line for the value the curve <paramref name="curveMod"/> drives, in that curve's own data space
        /// (x: source position 0..1 over the trimmed region; y: the curve's 0..1). False when nothing besides the curve
        /// moves the value, or it cannot be measured.
        /// </summary>
        public static bool TryGet(Zound zound, ZoundModifier curveMod, out Line line) {
            line = default;
            var chain = ZoundDspPlayback.PlayChain(zound);   // as it plays: the sound's own curves laid out with the chain
            if (chain == null || curveMod == null) return false;
            int mi = chain.modifiers.IndexOf(curveMod);
            if (mi < 0) return false;
            ZoundModifierBinding own = null;
            foreach (var b in chain.bindings) if (b.modifierIndex == mi) { own = b; break; }
            if (own == null) return false;

            // Anything else, shown and on, moving the same value?
            int others = 0;
            foreach (var b in chain.bindings) {
                if (b == own || b.nodeIndex != own.nodeIndex || b.paramIndex != own.paramIndex) continue;
                if (b.modifierIndex < 0 || b.modifierIndex >= chain.modifiers.Count) continue;
                var m = chain.modifiers[b.modifierIndex];
                if (m.enabled && CurveView.IsVisible(m)) others++;
            }
            if (others == 0) return false;

            if (!ZoundSapPlayback.TryGetPlayLength(zound, out float play)) play = 1.5f;
            var hidden = new System.Text.StringBuilder();
            for (int i = 0; i < chain.modifiers.Count; i++) hidden.Append(CurveView.IsVisible(chain.modifiers[i]) ? '1' : '0');
            string sig = chain.version + "|" + hidden + "|" + play.ToString("F2") + "|" + chain.modifiers.Count + "|" + chain.bindings.Count;
            var c = cache.GetOrCreateValue(zound);
            if (c.sig != sig || c.measured == null) {
                var copy = chain.DeepCopy();
                // The curve being drawn is always measured (it is what the line is ABOUT); anything with its eye off is left out.
                for (int i = 0; i < copy.modifiers.Count; i++)
                    if (i != mi && !CurveView.IsVisible(chain.modifiers[i])) copy.modifiers[i].enabled = false;
                c.measured = new Measured { m = EditorTools.ChainSpectrumProbe.MeasureModulation(copy, play) };
                c.sig = sig;
            }
            var meas = c.measured.m;
            if (meas.lanes == null || meas.sourceFraction == null) return false;

            if (!ChainModulationCompat.TryParam(chain, own, out var pd, out float setValue)) return false;
            bool ratio = ChainModulationCompat.CombineOf(own) == ModulationCombine.Ratio;
            bool ratioSpaced = ModulationMath.IsRatioSpaced(pd.curve);
            // A source-anchored curve's x is file seconds (T-0501): lay the measured positions out the same way.
            CurveAnchor.Axis axis = default;
            bool sourceAnchored = curveMod.curveAnchor == CurveAnchor.Source && KlipChainEnvelopes.TryAxis(zound, out axis);
            foreach (var lane in meas.lanes) {
                if (lane.nodeIndex != own.nodeIndex || lane.paramIndex != own.paramIndex) continue;
                int n = Mathf.Min(lane.position01.Length, meas.sourceFraction.Length);
                var xs = new List<float>(n); var ys = new List<float>(n);
                for (int i = 0; i < n; i++) {
                    float x = meas.sourceFraction[i];
                    float pos = lane.position01[i];
                    float y = pos;
                    if (ratio) {
                        float value = ModulationMath.FromPosition(pos, pd.min, pd.max, ratioSpaced);
                        y = ModulationMath.PositionFromRatio(setValue > 1e-6f ? value / setValue : 1f);
                    }
                    xs.Add(sourceAnchored ? CurveAnchor.X(curveMod, Mathf.Min(x, 1f), axis) : x); ys.Add(y);
                    if (x >= 1f) break;   // past the source's end the rest is tail, which the waveform does not show
                }
                line = new Line { x = xs.ToArray(), y = ys.ToArray() };
                return xs.Count > 1;
            }
            return false;
        }
    }
}

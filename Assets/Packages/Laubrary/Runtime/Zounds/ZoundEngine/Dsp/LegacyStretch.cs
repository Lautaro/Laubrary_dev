using UnityEngine;

namespace Laubrary.Zounds.Dsp {

    /// <summary>
    /// A Klip's old "Stretch" settings (Uniform, Region, Curve) expressed as what the live stretcher plays (T-0481).
    ///
    /// **Why this exists.** Those settings used to drive an offline stretcher whose output only the editor ever computed:
    /// real-time playback read the original audio and silently ignored them (measured: a Uniform x0.5 and x2 left a play's
    /// length unchanged). The live stretcher can play all three: Uniform is a constant speed, Region a speed that changes
    /// at the region's edges, Curve a speed curve following the waveform. So the old data stays readable and is converted
    /// on the way to the engine at every play -- the same arrangement as the seven old per-sound effect settings -- and
    /// the Klip window can make the conversion permanent (a Speed value and a time curve); there is no second way to edit
    /// the old data.
    ///
    /// All speeds here are the live stretcher's: 1 unchanged, 0.5 half speed (twice as long). The old "factor" was a
    /// length multiplier, so its speed is one over it.
    /// </summary>
    public static class LegacyStretch {

        /// <summary>Whether the Klip carries old stretch settings that change the sound.</summary>
        public static bool IsActive(Klip k) => k != null && k.timeStretch != null && k.timeStretch.IsEffective(0f);

        /// <summary>The constant speed an old Uniform stretch plays at (1 for anything else).</summary>
        public static float UniformSpeed(Klip k) {
            if (!IsActive(k) || k.timeStretch.mode != TimeStretchMode.Uniform) return 1f;
            return Mathf.Clamp(1f / Mathf.Max(k.timeStretch.factor, 1e-3f), 0.25f, 4f);
        }

        /// <summary>
        /// The time curve an old Region or Curve stretch plays as: a curve over the (trimmed) source on the Ratio scale,
        /// for an Envelope bound to Speed that follows the waveform. Null for Uniform or no active stretch.
        /// Region: exact (speed 1 outside, 1/factor inside, with steps at the edges). Curve: the old speed curve sampled
        /// densely, so its shape between points survives the change of interpolation (the old one interpolated the speed
        /// itself, the Ratio scale interpolates evenly in ratio).
        /// </summary>
        public static Envelope ToTimeCurve(Klip k, float trimStartSeconds, float trimEndSeconds) {
            if (!IsActive(k)) return null;
            var ts = k.timeStretch;
            var curve = new Envelope(0f, 1f);
            var pts = curve.GetPointsList();
            pts.Clear();
            if (ts.mode == TimeStretchMode.Region) {
                float len = Mathf.Max(trimEndSeconds - trimStartSeconds, 1e-6f);
                float a = Mathf.Clamp01((ts.regionStart - trimStartSeconds) / len);
                float b = Mathf.Clamp01((ts.regionEnd - trimStartSeconds) / len);
                float v = ModulationMath.PositionFromRatio(Mathf.Clamp(1f / Mathf.Max(ts.factor, 1e-3f), 0.25f, 4f));
                const float eps = 1e-4f;
                void Add(float t, float y) {
                    if (pts.Count > 0 && t <= pts[pts.Count - 1].time) t = pts[pts.Count - 1].time + 1e-6f;
                    pts.Add(new ZUIEnvelopePoint(Mathf.Clamp01(t), y));
                }
                if (a > eps) { Add(0f, 0.5f); Add(a - eps, 0.5f); }
                Add(a, v);
                Add(b, v);
                if (b < 1f - eps) { Add(b + eps, 0.5f); Add(1f, 0.5f); }
                // Pinned ends: a curve always spans 0..1.
                pts[0].time = 0f; pts[pts.Count - 1].time = 1f;
            }
            else if (ts.mode == TimeStretchMode.Envelope && ts.speedEnvelope != null) {
                const int samples = 64;
                for (int i = 0; i <= samples; i++) {
                    float t = i / (float)samples;
                    pts.Add(new ZUIEnvelopePoint(t, ModulationMath.PositionFromRatio(Mathf.Clamp(ts.speedEnvelope.Evaluate(t), 0.25f, 4f))));
                }
            }
            else return null;
            curve.enabled = true;
            return curve;
        }

        /// <summary>A chain modifier and binding for a time curve: an Envelope following the waveform, driving Speed on
        /// the Ratio scale at full depth.</summary>
        public static void AddTimeCurve(ZoundEffectChain chain, Envelope curve, string name) {
            chain.modifiers.Add(new ZoundModifier(ZoundModifierType.Envelope) { name = name, curve = curve });
            chain.bindings.Add(new ZoundModifierBinding {
                modifierIndex = chain.modifiers.Count - 1, nodeIndex = -1, paramIndex = SourceStageParam.Speed,
                combine = ModulationCombine.Ratio, depth = 1f, schema = ChainModulationCompat.CURRENT_SCHEMA });
        }
    }
}

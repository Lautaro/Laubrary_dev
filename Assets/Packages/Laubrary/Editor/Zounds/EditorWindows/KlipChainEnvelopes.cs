using UnityEngine;
using Laubrary.Zounds.Dsp;

namespace Laubrary.Zounds {

    /// <summary>
    /// Locates (or creates) the two chain modifiers that drive a Klip's volume and pitch over its play
    /// length — an Envelope bound to a Gain node for volume, and an Envelope bound to the source stage's
    /// pitch — the same way the waveform overlay's curves are really just ordinary chain modifiers under
    /// the hood.
    ///
    /// The Klip editor's waveform overlay (<see cref="KlipEditorWindow"/> + <see cref="AudioSpectrumView"/>)
    /// reads and writes curves exclusively through this class. That matters because real-time playback
    /// (<see cref="ZoundDspPlayback"/> / <c>ZoundSapPlayback.ResolveChainForPlayback</c>) only converts a
    /// Klip's legacy <c>volumeEnvelope</c>/<c>pitchEnvelope</c> fields into a chain when the Klip has no
    /// chain of its own — the moment any effect is added through the chain editor, those legacy fields stop
    /// being consulted. Editing through this class instead of the legacy fields means a curve affects
    /// playback whether or not the Klip already has other effects.
    ///
    /// The legacy fields themselves are left alone: a one-time project migration still converts them for
    /// Klips nobody has opened in this editor yet, and a handful of older editor-only paths (importing with
    /// a bake, Zequence-entry mixing) still use them directly for their own, unrelated purposes.
    /// </summary>
    internal static class KlipChainEnvelopes {

        /// <summary>A permanently disabled envelope for an overlay that has no modifier yet.</summary>
        public static readonly Envelope Disabled = new Envelope(0f, 1f);

        private static ZoundEffectChain Chain(Zound zound) => ZoundDspPlayback.ResolveChain(zound, out _);

        private static int FindEnvelopeModifier(ZoundEffectChain chain, System.Predicate<ZoundModifierBinding> target) {
            for (int b = 0; b < chain.bindings.Count; b++) {
                var bind = chain.bindings[b];
                if (bind.modifierIndex < 0 || bind.modifierIndex >= chain.modifiers.Count) continue;
                if (chain.modifiers[bind.modifierIndex].type != ZoundModifierType.Envelope) continue;
                if (target(bind)) return bind.modifierIndex;
            }
            return -1;
        }

        // The volume curve: an envelope on the Zound's own Volume (T-0493), or -- as it was saved before that -- on an
        // inserted Gain effect. The second form plays exactly as before until it is edited (see EnsureVolumeOwnValue).
        private static int VolumeModifier(ZoundEffectChain chain) {
            int own = FindEnvelopeModifier(chain, b => b.nodeIndex == -1 && b.paramIndex == SourceStageParam.Volume);
            if (own >= 0) return own;
            return FindEnvelopeModifier(chain, b => b.nodeIndex >= 0 && b.nodeIndex < chain.nodes.Count && chain.nodes[b.nodeIndex].type == ZoundEffectType.Gain && b.paramIndex == 0);
        }

        /// <summary>
        /// Moves a volume curve saved the old way -- an envelope owning an inserted Gain effect -- onto the Zound's own Volume
        /// and removes the Gain (T-0493), so volume stops appearing as an extra effect. Only when that is exactly the same
        /// sound: the Gain is the last effect (Volume acts after every effect, where it did), is on, sits at its default,
        /// and nothing else is bound to it. Otherwise the sound is left as it is. Called at the start of an edit of the
        /// curve, inside that edit's Undo step, never on a whole project at once. Returns whether anything changed.
        /// </summary>
        public static bool EnsureVolumeOwnValue(Zound zound) {
            var chain = Chain(zound);
            if (chain == null) return false;
            int m = VolumeModifier(chain);
            if (m < 0) return false;
            ZoundModifierBinding vb = null;
            foreach (var b in chain.bindings) if (b.modifierIndex == m && b.nodeIndex >= 0) { vb = b; break; }
            if (vb == null) return false;
            int gi = vb.nodeIndex;
            if (gi != chain.nodes.Count - 1) return false;
            var gain = chain.nodes[gi];
            if (gain.type != ZoundEffectType.Gain || !gain.enabled || Mathf.Abs(gain.Param(0) - 1f) > 1e-6f) return false;
            foreach (var b in chain.bindings) if (b != vb && b.nodeIndex == gi) return false;
            if (zound.chainOverrides != null) foreach (var o in zound.chainOverrides) if (o.nodeIndex == gi) return false;
            vb.nodeIndex = -1; vb.paramIndex = SourceStageParam.Volume;
            chain.RemoveNode(gi);
            ZoundDspPlayback.InvalidateLayout(zound);
            return true;
        }

        private static int PitchModifier(ZoundEffectChain chain) =>
            FindEnvelopeModifier(chain, b => b.nodeIndex == -1 && b.paramIndex == SourceStageParam.Pitch);

        public static Envelope VolumeCurve(Zound zound, bool create) {
            var chain = Chain(zound);
            int m = VolumeModifier(chain);
            if (m < 0) {
                if (!create) return null;
                // Seed the new modifier from the Klip's legacy curve, if it drew one, so switching a Klip
                // over to the chain (by giving it its first modifier) doesn't discard existing curve work.
                Envelope seed = zound is Klip legacyKlip && legacyKlip.volumeEnvelope != null
                    ? legacyKlip.volumeEnvelope.DeepCopy()
                    : new Envelope(Zound.MinVolumeRange, Zound.MaxVolumeRange);
                var env = new ZoundModifier(ZoundModifierType.Envelope) { name = "Volume", curve = seed };
                chain.modifiers.Add(env);
                chain.bindings.Add(new ZoundModifierBinding {
                    // On the Zound's own Volume, after every effect (T-0493) -- no Gain effect is added any more.
                    modifierIndex = chain.modifiers.Count - 1, nodeIndex = -1, paramIndex = SourceStageParam.Volume,
                    // The drawn curve owns the level outright, which is what a volume curve has always meant.
                    combine = Dsp.ModulationCombine.Set, depth = 1f, schema = Dsp.ChainModulationCompat.CURRENT_SCHEMA });
                chain.Touch();
                m = chain.modifiers.Count - 1;
            }
            if (chain.modifiers[m].curve == null) chain.modifiers[m].curve = new Envelope(Zound.MinVolumeRange, Zound.MaxVolumeRange);
            return chain.modifiers[m].curve;
        }

        public static Envelope PitchCurve(Zound zound, bool create) {
            var chain = Chain(zound);
            int m = PitchModifier(chain);
            if (m < 0) {
                if (!create) return null;
                // Same carry-over as VolumeCurve: keep the shape of any existing legacy pitch curve. That curve has always
                // been played as Set (a position across the whole pitch range), so it is added that way and converted to
                // the Ratio scale exactly, keeping what it sounds like; a new curve starts flat in the middle, which on
                // the Ratio scale is exactly "no change" (T-0479).
                bool legacy = zound is Klip legacyKlip && legacyKlip.pitchEnvelope != null;
                Envelope seed = legacy ? ((Klip)zound).pitchEnvelope.DeepCopy() : NewRatioCurve();
                var env = new ZoundModifier(ZoundModifierType.Envelope) { name = "Pitch", curve = seed };
                chain.modifiers.Add(env);
                var bind = new ZoundModifierBinding {
                    modifierIndex = chain.modifiers.Count - 1, nodeIndex = -1, paramIndex = SourceStageParam.Pitch,
                    combine = legacy ? Dsp.ModulationCombine.Set : Dsp.ModulationCombine.Ratio, depth = 1f,
                    schema = Dsp.ChainModulationCompat.CURRENT_SCHEMA };
                chain.bindings.Add(bind);
                if (legacy) Dsp.ChainModulationCompat.ConvertSetToRatio(chain, bind, out _);
                chain.Touch();
                m = chain.modifiers.Count - 1;
            }
            if (chain.modifiers[m].curve == null) chain.modifiers[m].curve = NewRatioCurve();
            return chain.modifiers[m].curve;
        }

        // ── the time curve (T-0481/T-0482): an Envelope driving Speed on the Ratio scale, following the waveform ──

        private static int TimeModifier(ZoundEffectChain chain) =>
            FindEnvelopeModifier(chain, b => b.nodeIndex == -1 && b.paramIndex == SourceStageParam.Speed);

        /// <summary>The Klip's time curve (the first envelope bound to Speed), created flat (x1) when asked.</summary>
        public static Envelope TimeCurve(Zound zound, bool create) {
            var chain = Chain(zound);
            if (chain == null) {
                if (!create || !(zound is Klip)) return null;
                zound.effectChain = chain = new ZoundEffectChain();
            }
            int m = TimeModifier(chain);
            if (m < 0) {
                if (!create) return null;
                Dsp.LegacyStretch.AddTimeCurve(chain, NewRatioCurve(), "Time");
                chain.Touch();
                m = chain.modifiers.Count - 1;
            }
            if (chain.modifiers[m].curve == null) chain.modifiers[m].curve = NewRatioCurve();
            return chain.modifiers[m].curve;
        }

        public static void SetTimeEnabled(Zound zound, bool enabled) {
            var chain = Chain(zound);
            var curve = TimeCurve(zound, enabled);
            if (curve == null) return;
            chain = Chain(zound);
            int m = TimeModifier(chain);
            if (m >= 0) chain.modifiers[m].enabled = enabled;
            curve.enabled = enabled;
            chain.Touch();
            ZoundDspPlayback.InvalidateLayout(zound);
        }

        /// <summary>
        /// Makes a Klip's old stretch setting permanent in the current controls (T-0481), so it no longer needs converting
        /// at every play: Uniform becomes the sound's Speed (Live speed on), Region and Curve become its time curve
        /// following the waveform (a separate "Old stretch" curve if the Klip already has a time curve switched on, so
        /// neither is lost). The old setting is then switched off. Plays exactly as it did through the live stretcher.
        /// The caller records Undo first. Returns a line saying what it did, or null when there was nothing to convert.
        /// </summary>
        public static string ConvertLegacyStretch(Klip k) {
            if (!Dsp.LegacyStretch.IsActive(k)) return null;
            var ts = k.timeStretch;
            string done;
            if (ts.mode == TimeStretchMode.Uniform) {
                float s = Dsp.LegacyStretch.UniformSpeed(k);
                ts.liveSpeed = Mathf.Clamp((ts.liveEnabled ? ts.liveSpeed : 1f) * s, Dsp.SapStretch.MinSpeed, Dsp.SapStretch.MaxSpeed);
                ts.liveEnabled = true;
                done = "Speed ×" + ts.liveSpeed.ToString("0.00");
            }
            else {
                var clip = Dsp.ZoundSapPlayback.LoadSourceClip(k);
                float from = k.trimEnabled ? k.trimStart : 0f;
                float to = k.trimEnabled && k.trimEnd > k.trimStart ? k.trimEnd : (clip != null ? clip.length : from + 1f);
                var curve = Dsp.LegacyStretch.ToTimeCurve(k, from, to);
                var chain = Chain(k);
                bool timeOn = chain != null && Dsp.ZoundDspPlayback.HasTimeCurve(chain);
                if (timeOn) {
                    Dsp.LegacyStretch.AddTimeCurve(chain, curve, "Old stretch");
                    chain.Touch();
                    done = "a second time curve, \"Old stretch\"";
                }
                else {
                    var target = TimeCurve(k, true);
                    target.GetPointsList().Clear();
                    target.GetPointsList().AddRange(curve.GetPointsList());
                    SetTimeEnabled(k, true);
                    done = "the time curve";
                }
            }
            ts.enabled = false;
            ZoundDspPlayback.InvalidateLayout(k);
            return done;
        }

        /// <summary>What an old stretch setting does, in words (for the strip's summary line).</summary>
        public static string DescribeLegacyStretch(Klip k) {
            if (!Dsp.LegacyStretch.IsActive(k)) return null;
            var ts = k.timeStretch;
            switch (ts.mode) {
                case TimeStretchMode.Uniform: return "length ×" + ts.factor.ToString("0.00");
                case TimeStretchMode.Region: return "length ×" + ts.factor.ToString("0.00") + " from " + ts.regionStart.ToString("0.00") + " to " + ts.regionEnd.ToString("0.00") + " s";
                default: return "a speed curve";
            }
        }

        /// <summary>At the start of an edit of <paramref name="mod"/>'s curve from the chain card: if it is the Klip's pitch
        /// curve on the old scale, convert it first (inside the edit's Undo step), as the waveform overlay does.</summary>
        public static void EnsurePitchRatioIfPitchCurve(Zound zound, ZoundModifier mod) {
            if (zound == null || mod == null) return;
            var chain = Chain(zound);
            int m = PitchModifier(chain);
            if (m >= 0 && chain.modifiers[m] == mod) EnsurePitchRatio(zound);
            // The same for the volume curve: an old inserted Gain moves onto the Zound's own Volume at its first edit.
            chain = Chain(zound);
            int v = VolumeModifier(chain);
            if (v >= 0 && chain.modifiers[v] == mod) EnsureVolumeOwnValue(zound);
        }

        /// <summary>
        /// What the top, middle and bottom of a modifier's curve mean, for the axis labels on a curve display (T-0479):
        /// on the Ratio scale semitones for a pitch (+24 / 0 / -24 st) and ratios otherwise (x4 / x1 / x1/4); on Set over
        /// a single parameter, that parameter's own ends; otherwise the plain "top" and "bottom". <paramref name="mid"/>
        /// is null where the middle means nothing in particular.
        /// </summary>
        public static void CurveAxis(ZoundEffectChain chain, ZoundModifier mod, out string top, out string mid, out string bottom) {
            top = "top"; bottom = "bottom"; mid = null;
            if (chain == null || mod == null || mod.type != ZoundModifierType.Envelope) return;
            int mi = chain.modifiers.IndexOf(mod);
            ZoundModifierBinding only = null; int n = 0;
            foreach (var b in chain.bindings) if (b.modifierIndex == mi) { only = b; n++; }
            if (n != 1) return;
            var c = Dsp.ChainModulationCompat.CombineOf(only);
            bool pitch = only.nodeIndex == -1 && only.paramIndex == SourceStageParam.Pitch;
            if (c == Dsp.ModulationCombine.Ratio) {
                if (pitch) { top = "+24 st"; mid = "0 st"; bottom = "-24 st"; }
                else { top = "×4"; mid = "×1"; bottom = "×¼"; }
                return;
            }
            // Under Set the curve's 0..1 spans the parameter's range, so its ends are the parameter's ends -- but only for a
            // curve that runs 0..1 (an old-scale pitch curve runs 0.1..2 and its upper half is all the top).
            if (c == Dsp.ModulationCombine.Set && mod.curve != null && mod.curve.yMin == 0f && mod.curve.yMax == 1f
                && Dsp.ChainModulationCompat.TryParam(chain, only, out var pd, out _)) {
                top = Fmt(pd.max, pd); bottom = Fmt(pd.min, pd);
            }
        }

        /// <summary>The waveform overlay's pitch axis: <see cref="CurveAxis"/> for the Klip's pitch curve, plus whether it
        /// is still on the old scale (then no axis is shown, only a warning; see <see cref="OldScaleTip"/>).</summary>
        public static bool PitchAxis(Zound zound, out string top, out string mid, out string bottom, out bool oldScale) {
            top = mid = bottom = null; oldScale = false;
            var chain = Chain(zound);
            int m = PitchModifier(chain);
            if (m < 0) return false;
            oldScale = PitchIsOldScale(zound);
            if (oldScale) return true;
            CurveAxis(chain, chain.modifiers[m], out top, out mid, out bottom);
            return true;
        }

        public const string OldScaleTip =
            "This pitch curve is still on the scale it was saved with: a height across the whole pitch range, where the " +
            "middle of the display is about x4 and everything in its upper half is x4. It plays exactly as it always has. " +
            "The first time you edit it, it moves to the current scale (middle = no change, top +24 semitones, bottom -24) " +
            "without changing how it sounds.";

        static string Fmt(float v, Dsp.ParamDesc pd) =>
            pd.curve == Dsp.ParamCurve.Logarithmic && pd.unit == "" ? "×" + v.ToString("0.##") : v.ToString("0.##") + (string.IsNullOrEmpty(pd.unit) ? "" : " " + pd.unit);

        /// <summary>The index in the Klip's chain of the modifier whose curve is <paramref name="curve"/>, or -1.</summary>
        public static int ModifierIndexOf(Zound zound, Envelope curve) {
            var chain = Chain(zound);
            if (chain == null || curve == null) return -1;
            for (int i = 0; i < chain.modifiers.Count; i++) if (chain.modifiers[i].curve == curve) return i;
            return -1;
        }

        /// <summary>A flat curve on the Ratio scale: range 0..1, both points in the middle, i.e. x1 throughout.</summary>
        public static Envelope NewRatioCurve() {
            var e = new Envelope(0f, 1f);
            foreach (var p in e.GetPointsList()) p.value = 0.5f;
            return e;
        }

        /// <summary>The binding of the Klip's pitch curve (the first envelope bound to the source's pitch), or null.</summary>
        public static ZoundModifierBinding PitchBinding(Zound zound) {
            var chain = Chain(zound);
            int m = PitchModifier(chain);
            if (m < 0) return null;
            foreach (var b in chain.bindings) if (b.modifierIndex == m && b.nodeIndex == -1 && b.paramIndex == SourceStageParam.Pitch) return b;
            return null;
        }

        /// <summary>
        /// Whether the Klip's pitch curve is still on the scale it was saved with before T-0479 (Set: a position across the
        /// whole pitch range, where a flat line in the middle is about x4). It plays exactly as it always has until it is
        /// edited; <see cref="EnsurePitchRatio"/> converts it then.
        /// </summary>
        public static bool PitchIsOldScale(Zound zound) {
            var b = PitchBinding(zound);
            return b != null && Dsp.ChainModulationCompat.CombineOf(b) == Dsp.ModulationCombine.Set;
        }

        /// <summary>
        /// Converts the Klip's pitch curve to the Ratio scale if it is still on the old one, so an edit lands on the new
        /// scale; what it plays is unchanged (see <see cref="Dsp.ChainModulationCompat.ConvertSetToRatio"/>). Called at the
        /// start of every edit of the curve, inside that edit's Undo step. Returns whether anything changed.
        /// </summary>
        public static bool EnsurePitchRatio(Zound zound) {
            var b = PitchBinding(zound);
            if (b == null || Dsp.ChainModulationCompat.CombineOf(b) != Dsp.ModulationCombine.Set) return false;
            var chain = Chain(zound);
            if (!Dsp.ChainModulationCompat.ConvertSetToRatio(chain, b, out int clamped)) return false;
            if (clamped > 0) Debug.LogWarning("[Zounds] " + zound.name + ": " + clamped + " pitch-curve point(s) were beyond two octaves and are now held at x4 or x1/4.");
            ZoundDspPlayback.InvalidateLayout(zound);
            return true;
        }

        public static void SetVolumeEnabled(Zound zound, bool enabled) {
            var chain = Chain(zound);
            var curve = VolumeCurve(zound, enabled);
            if (curve == null) return;
            // Switching it on is an edit: an old inserted Gain moves onto the Zound's own Volume, sounding the same.
            if (enabled) EnsureVolumeOwnValue(zound);
            chain = Chain(zound);
            int m = VolumeModifier(chain);
            if (m >= 0) chain.modifiers[m].enabled = enabled;
            curve.enabled = enabled;
            chain.Touch();
            ZoundDspPlayback.InvalidateLayout(zound);
        }

        public static void SetPitchEnabled(Zound zound, bool enabled) {
            var chain = Chain(zound);
            var curve = PitchCurve(zound, enabled);
            if (curve == null) return;
            // Switching it on is an edit: an old-scale curve moves to the Ratio scale here, sounding the same (T-0479).
            if (enabled) EnsurePitchRatio(zound);
            int m = PitchModifier(chain);
            if (m >= 0) chain.modifiers[m].enabled = enabled;
            curve.enabled = enabled;
            chain.Touch();
            ZoundDspPlayback.InvalidateLayout(zound);
        }

        /// <summary>After an overlay drag mutated a curve in place: the next play rebuilds the layout.</summary>
        public static void Touch(Zound zound) {
            Chain(zound).Touch();
            ZoundDspPlayback.InvalidateLayout(zound);
            UnityEditor.EditorUtility.SetDirty(ZoundsProject.Instance);
        }
    }

}

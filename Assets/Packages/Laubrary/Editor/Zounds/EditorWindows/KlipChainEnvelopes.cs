using Laubrary.Audio;
using UnityEngine;
using Laubrary.Zounds.Dsp;

namespace Laubrary.Zounds {

    /// <summary>
    /// The one place the editor reads and writes a sound's OWN curves: its Volume, Pitch and Time curve, drawn over its
    /// waveform by the curve bar in the Klip editor and on a local track in the Zequence editor. They are properties of
    /// the sound (<see cref="Zound.ownCurves"/>, owner's rule 2026-10-08), not entries of its modifier list, and the engine
    /// hears them as source-stage envelopes laid out with the chain (<see cref="ZoundDspPlayback.PlayChain"/>).
    ///
    /// A sound saved before the slots existed still carries its curves inside its stored chain, as envelope modifiers
    /// bound to the source stage (or, older still, a volume envelope owning an inserted Gain effect). Such a sound moves
    /// its curves into the slots on its first access here (<see cref="ZoundOwnCurves.Adopt"/>), exactly; a curve that
    /// cannot be moved without changing the sound (something else acts on the same value before it) stays in the chain
    /// and is still found and edited from here, so both forms behave alike. A chain linked to a shared preset never
    /// converts (its curves belong to every sound linked to it); a curve switched on there is created on the sound.
    ///
    /// The Klip's legacy <c>volumeEnvelope</c>/<c>pitchEnvelope</c> fields are left alone: a one-time project migration
    /// still converts them for Klips nobody has opened in this editor yet, and a handful of older editor-only paths
    /// (importing with a bake, Zequence-entry mixing) still use them directly for their own, unrelated purposes.
    /// </summary>
    internal static class KlipChainEnvelopes {

        /// <summary>A permanently disabled envelope for an overlay that has no modifier yet.</summary>
        public static readonly Envelope Disabled = new Envelope(0f, 1f);

        /// <summary>An own curve wherever it lives: in the sound's slot, or still inside its stored chain.</summary>
        public struct Own {
            public ZoundModifier mod;
            public ZoundModifierBinding bind;
            /// <summary>Still an entry of the stored chain (a sound that could not be converted, or a shared preset's).</summary>
            public bool inChain;
            public bool Valid => mod != null;
        }

        static ZoundEffectChain Stored(Zound zound) => zound != null ? ZoundDspPlayback.ResolveChain(zound, out _) : null;
        static ZoundEffectChain Play(Zound zound) => zound != null ? ZoundDspPlayback.PlayChain(zound) : null;

        /// <summary>The first-access conversion: a sound saved with its curves in its chain moves them into its slots.</summary>
        static void Settle(Zound zound) {
            if (ZoundOwnCurves.Adopt(zound)) ZoundDspPlayback.InvalidateLayout(zound);
        }

        /// <summary>Everything that caches a laid-out chain forgets it, after an edit of an own curve.</summary>
        static void TouchAll(Zound zound) {
            Stored(zound)?.Touch();
            zound?.ownCurves?.Touch();
            ZoundDspPlayback.InvalidateLayout(zound);
        }

        static int FirstEnvelopeBinding(ZoundEffectChain chain, System.Predicate<ZoundModifierBinding> target) {
            for (int b = 0; b < chain.bindings.Count; b++) {
                var bind = chain.bindings[b];
                if (bind.modifierIndex < 0 || bind.modifierIndex >= chain.modifiers.Count) continue;
                if (chain.modifiers[bind.modifierIndex].type != ZoundModifierType.Envelope) continue;
                if (target(bind)) return b;
            }
            return -1;
        }

        /// <summary>The sound's own curve on a source-stage value (Volume, Pitch or Speed), or an invalid one when it has none.</summary>
        public static Own Find(Zound zound, int sourceParam) {
            if (zound == null) return default;
            Settle(zound);
            var slot = zound.ownCurves?.Of(sourceParam);
            if (slot != null && slot.Has) return new Own { mod = slot.modifier, bind = slot.binding };
            // The Gain curve only ever lives in its slot: an envelope on Drive in a chain is that chain's modifier.
            if (sourceParam == SourceStageParam.Gain) return default;
            var chain = Stored(zound);
            if (chain == null) return default;
            // The forms a sound saved before the slots existed can still carry: the first envelope on the value, or, for
            // volume, an envelope owning an inserted Gain effect (which moves onto the sound's own Volume at its first edit).
            int bi = FirstEnvelopeBinding(chain, b => b.nodeIndex == -1 && b.paramIndex == sourceParam);
            if (bi < 0 && sourceParam == SourceStageParam.Volume)
                bi = FirstEnvelopeBinding(chain, b => b.nodeIndex >= 0 && b.nodeIndex < chain.nodes.Count && chain.nodes[b.nodeIndex].type == ZoundEffectType.Gain && b.paramIndex == 0);
            if (bi < 0) return default;
            var bind = chain.bindings[bi];
            return new Own { mod = chain.modifiers[bind.modifierIndex], bind = bind, inChain = true };
        }

        /// <summary>The chain a curve's binding is indexed against: the stored chain for one still inside it, else the played one.</summary>
        static ZoundEffectChain ChainOf(Zound zound, in Own o) => o.inChain ? Stored(zound) : Play(zound);

        static Own Create(Zound zound, int sourceParam, string name, Envelope seed, ModulationCombine combine) {
            if (zound.ownCurves == null) zound.ownCurves = new ZoundOwnCurves();
            var mod = new ZoundModifier(ZoundModifierType.Envelope) { name = name, curve = seed };
            var bind = new ZoundModifierBinding {
                nodeIndex = -1, paramIndex = sourceParam, combine = combine, depth = 1f, schema = ChainModulationCompat.CURRENT_SCHEMA };
            zound.ownCurves.Set(sourceParam, mod, bind);
            ZoundDspPlayback.InvalidateLayout(zound);
            return new Own { mod = mod, bind = bind };
        }

        /// <summary>
        /// Converts every waveform-following curve of a Klip (its own curves and its chain's) from trim-anchored to
        /// source-anchored (T-0501), so the curves stay on the same audio through any re-trim, split or slip; the sound
        /// plays identically (kept check 30). Called on the first curve edit and before any trim change, inside the same
        /// Undo step. Not done for a chain linked to a shared preset (other sounds with other trims use the same curves),
        /// nor when the file's length is unknown.
        /// </summary>
        public static bool EnsureSourceAnchored(Zound zound) {
            if (!(zound is Klip k) || k.chainPresetId != 0) return false;
            Settle(zound);
            var clip = ZoundSapPlayback.LoadSourceClip(k, out bool alreadyTrimmed);
            if (clip == null || alreadyTrimmed) return false;
            var axis = CurveAnchor.Axis.Of(k, clip.length);
            bool any = false;
            if (k.effectChain != null) foreach (var m in k.effectChain.modifiers) any |= CurveAnchor.ConvertToSource(m, axis);
            if (k.ownCurves != null)
                foreach (int p in ZoundOwnCurves.Params) {
                    var slot = k.ownCurves.Of(p);
                    if (slot.Has) any |= CurveAnchor.ConvertToSource(slot.modifier, axis);
                }
            if (any) TouchAll(zound);
            return any;
        }

        /// <summary>
        /// Moves a volume curve saved the old way -- an envelope owning an inserted Gain effect -- onto the Zound's own Volume
        /// and removes the Gain (T-0493), so volume stops appearing as an extra effect. Only when that is exactly the same
        /// sound: the Gain is the last effect (Volume acts after every effect, where it did), is on, sits at its default,
        /// and nothing else is bound to it. Otherwise the sound is left as it is. Called at the start of an edit of the
        /// curve, inside that edit's Undo step, never on a whole project at once. Returns whether anything changed.
        /// </summary>
        public static bool EnsureVolumeOwnValue(Zound zound) {
            EnsureSourceAnchored(zound);
            var chain = Stored(zound);
            if (chain == null) return false;
            var o = Find(zound, SourceStageParam.Volume);
            if (!o.Valid || !o.inChain || o.bind.nodeIndex < 0) return false;
            int gi = o.bind.nodeIndex;
            if (gi != chain.nodes.Count - 1) return false;
            var gain = chain.nodes[gi];
            if (gain.type != ZoundEffectType.Gain || !gain.enabled || Mathf.Abs(gain.Param(0) - 1f) > 1e-6f) return false;
            foreach (var b in chain.bindings) if (b != o.bind && b.nodeIndex == gi) return false;
            if (zound.chainOverrides != null) foreach (var ov in zound.chainOverrides) if (ov.nodeIndex == gi) return false;
            o.bind.nodeIndex = -1; o.bind.paramIndex = SourceStageParam.Volume;
            chain.RemoveNode(gi);
            Settle(zound);   // now in the form the slot takes
            TouchAll(zound);
            return true;
        }

        /// <summary>
        /// Whether <paramref name="m"/> is one of the sound's OWN curves (its volume, pitch or time curve). Those are
        /// properties of the sound, not entries of its modifier list, so the chain editor keeps one that still sits in a
        /// stored chain (a sound that could not convert, a shared preset's) out of the list.
        /// </summary>
        public static bool IsOwnCurve(Zound zound, ZoundModifier m) {
            if (zound == null || m == null || m.type != ZoundModifierType.Envelope) return false;
            return Find(zound, SourceStageParam.Volume).mod == m || Find(zound, SourceStageParam.Pitch).mod == m || Find(zound, SourceStageParam.Speed).mod == m
                || Find(zound, SourceStageParam.Gain).mod == m;
        }

        public static Envelope VolumeCurve(Zound zound, bool create) {
            var o = Find(zound, SourceStageParam.Volume);
            if (!o.Valid) {
                if (!create || zound == null) return null;
                // Seed the new curve from the Klip's legacy curve, if it drew one, so switching a Klip over to the chain
                // doesn't discard existing curve work. The drawn curve owns the level outright (Set), which is what a volume
                // curve has always meant; it acts on the Zound's own Volume, after every effect (T-0493).
                Envelope seed = zound is Klip legacyKlip && legacyKlip.volumeEnvelope != null
                    ? legacyKlip.volumeEnvelope.DeepCopy()
                    : new Envelope(Zound.MinVolumeRange, Zound.MaxVolumeRange);
                o = Create(zound, SourceStageParam.Volume, "Volume", seed, ModulationCombine.Set);
            }
            if (o.mod.curve == null) o.mod.curve = new Envelope(Zound.MinVolumeRange, Zound.MaxVolumeRange);
            return o.mod.curve;
        }

        public static Envelope PitchCurve(Zound zound, bool create) {
            var o = Find(zound, SourceStageParam.Pitch);
            if (!o.Valid) {
                if (!create || zound == null) return null;
                // Same carry-over as VolumeCurve: keep the shape of any existing legacy pitch curve. That curve has always
                // been played as Set (a position across the whole pitch range), so it is added that way and converted to
                // the Ratio scale exactly, keeping what it sounds like; a new curve starts flat in the middle, which on
                // the Ratio scale is exactly "no change" (T-0479).
                bool legacy = zound is Klip legacyKlip && legacyKlip.pitchEnvelope != null;
                Envelope seed = legacy ? ((Klip)zound).pitchEnvelope.DeepCopy() : NewRatioCurve();
                o = Create(zound, SourceStageParam.Pitch, "Pitch", seed, legacy ? ModulationCombine.Set : ModulationCombine.Ratio);
                if (legacy) { ChainModulationCompat.ConvertSetToRatio(Play(zound), o.bind, out _); TouchAll(zound); }
            }
            if (o.mod.curve == null) o.mod.curve = NewRatioCurve();
            return o.mod.curve;
        }

        // ── the time curve (T-0481/T-0482): an Envelope driving Speed on the Ratio scale, following the waveform ──

        /// <summary>The Klip's time curve (the first envelope bound to Speed), created flat (x1) when asked.</summary>
        public static Envelope TimeCurve(Zound zound, bool create) {
            var o = Find(zound, SourceStageParam.Speed);
            if (!o.Valid) {
                if (!create || !(zound is Klip)) return null;
                o = Create(zound, SourceStageParam.Speed, "Time", NewRatioCurve(), ModulationCombine.Ratio);
            }
            if (o.mod.curve == null) o.mod.curve = NewRatioCurve();
            return o.mod.curve;
        }

        // ── the gain curve (owner, 2026-10-09): an Envelope driving the source's Drive (its level INTO the effects) on the
        // Ratio scale, following the waveform; the waveform is drawn through it ──

        /// <summary>The sound's own Gain curve, created flat (x1, 0 dB) when asked.</summary>
        public static Envelope GainCurve(Zound zound, bool create) {
            var o = Find(zound, SourceStageParam.Gain);
            if (!o.Valid) {
                if (!create || !(zound is Klip)) return null;
                o = Create(zound, SourceStageParam.Gain, "Gain", NewRatioCurve(), ModulationCombine.Ratio);
                // A new curve is on the file's own seconds from the start when the file is known (as every edited curve is).
                if (TryAxis(zound, out var axis) && zound.chainPresetId == 0) CurveAnchor.ConvertToSource(o.mod, axis);
            }
            if (o.mod.curve == null) o.mod.curve = NewRatioCurve();
            return o.mod.curve;
        }

        public static void SetGainEnabled(Zound zound, bool enabled) => SetEnabled(zound, SourceStageParam.Gain, enabled, GainCurve);

        /// <summary>
        /// The factor the sound's own Gain curve gives at <paramref name="sourceSeconds"/> into its file, combined exactly as
        /// the voice combines it (1 without a Gain curve, or with it off). For drawing the waveform as the effects receive it.
        /// <paramref name="axis"/> is the sound's axis (<see cref="TryAxis"/>), read once per drawing by the caller.
        /// </summary>
        public static float GainAt(Zound zound, float sourceSeconds, in CurveAnchor.Axis axis) {
            var slot = zound?.ownCurves?.gain;
            if (slot == null || !slot.Has || !slot.modifier.enabled || slot.modifier.curve == null || !axis.Valid) return 1f;
            var pd = ZoundEffectDescriptors.SourceStageParams[SourceStageParam.Gain];
            bool ratio = ModulationMath.IsRatioSpaced(pd.curve);
            float x = CurveAnchor.XAtSourceSeconds(slot.modifier, sourceSeconds, axis);
            float v = slot.modifier.curve.Evaluate(x);
            return Mathf.Clamp(ModulationMath.Apply(ChainModulationCompat.CombineOf(slot.binding), pd.def, v,
                                                    ChainModulationCompat.DepthOf(slot.binding, pd.min, pd.max, ratio), pd.min, pd.max, ratio), pd.min, pd.max);
        }

        static void SetEnabled(Zound zound, int sourceParam, bool enabled, System.Func<Zound, bool, Envelope> curveOf) {
            var curve = curveOf(zound, enabled);
            if (curve == null) return;
            var o = Find(zound, sourceParam);
            if (o.Valid) o.mod.enabled = enabled;
            curve.enabled = enabled;
            TouchAll(zound);
        }

        public static void SetTimeEnabled(Zound zound, bool enabled) => SetEnabled(zound, SourceStageParam.Speed, enabled, TimeCurve);

        public static void SetVolumeEnabled(Zound zound, bool enabled) {
            // Switching it on is an edit: an old inserted Gain moves onto the Zound's own Volume, sounding the same.
            if (enabled && VolumeCurve(zound, false) != null) EnsureVolumeOwnValue(zound);
            SetEnabled(zound, SourceStageParam.Volume, enabled, VolumeCurve);
        }

        public static void SetPitchEnabled(Zound zound, bool enabled) {
            // Switching it on is an edit: an old-scale curve moves to the Ratio scale here, sounding the same (T-0479).
            if (enabled && PitchCurve(zound, false) != null) EnsurePitchRatio(zound);
            SetEnabled(zound, SourceStageParam.Pitch, enabled, PitchCurve);
        }

        /// <summary>
        /// Makes a Klip's old stretch setting permanent in the current controls (T-0481), so it no longer needs converting
        /// at every play: Uniform becomes the sound's Speed (Live speed on), Region and Curve become its time curve
        /// following the waveform (a separate "Old stretch" curve in the modifier list if the Klip already has a time curve
        /// switched on, so neither is lost). The old setting is then switched off. Plays exactly as it did through the live
        /// stretcher. The caller records Undo first. Returns a line saying what it did, or null when there was nothing to convert.
        /// </summary>
        public static string ConvertLegacyStretch(Klip k) {
            if (!LegacyStretch.IsActive(k)) return null;
            var ts = k.timeStretch;
            string done;
            if (ts.mode == TimeStretchMode.Uniform) {
                float s = LegacyStretch.UniformSpeed(k);
                ts.liveSpeed = Mathf.Clamp((ts.liveEnabled ? ts.liveSpeed : 1f) * s, SapStretch.MinSpeed, SapStretch.MaxSpeed);
                ts.liveEnabled = true;
                done = "Speed ×" + ts.liveSpeed.ToString("0.00");
            }
            else {
                var clip = ZoundSapPlayback.LoadSourceClip(k);
                float from = k.trimEnabled ? k.trimStart : 0f;
                float to = k.trimEnabled && k.trimEnd > k.trimStart ? k.trimEnd : (clip != null ? clip.length : from + 1f);
                var curve = LegacyStretch.ToTimeCurve(k, from, to);
                bool timeOn = ZoundDspPlayback.HasTimeCurve(Play(k));
                if (timeOn) {
                    if (k.effectChain == null) k.effectChain = new ZoundEffectChain();
                    LegacyStretch.AddTimeCurve(k.effectChain, curve, "Old stretch");
                    k.effectChain.Touch();
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
            TouchAll(k);
            return done;
        }

        /// <summary>What an old stretch setting does, in words (for the strip's summary line).</summary>
        public static string DescribeLegacyStretch(Klip k) {
            if (!LegacyStretch.IsActive(k)) return null;
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
            EnsureSourceAnchored(zound);
            if (Find(zound, SourceStageParam.Pitch).mod == mod) EnsurePitchRatio(zound);
            // The same for the volume curve: an old inserted Gain moves onto the Zound's own Volume at its first edit.
            if (Find(zound, SourceStageParam.Volume).mod == mod) EnsureVolumeOwnValue(zound);
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
            var c = ChainModulationCompat.CombineOf(only);
            bool pitch = only.nodeIndex == -1 && only.paramIndex == SourceStageParam.Pitch;
            if (c == ModulationCombine.Ratio) {
                if (pitch) { top = "+24 st"; mid = "0 st"; bottom = "-24 st"; }
                else { top = "×4"; mid = "×1"; bottom = "×¼"; }
                return;
            }
            // Under Set the curve's 0..1 spans the parameter's range, so its ends are the parameter's ends -- but only for a
            // curve that runs 0..1 (an old-scale pitch curve runs 0.1..2 and its upper half is all the top).
            if (c == ModulationCombine.Set && mod.curve != null && mod.curve.yMin == 0f && mod.curve.yMax == 1f
                && ChainModulationCompat.TryParam(chain, only, out var pd, out _)) {
                top = Fmt(pd.max, pd); bottom = Fmt(pd.min, pd);
            }
        }

        /// <summary>
        /// The curve value that leaves what the curve drives unchanged (T-0509: a point's Reset), read from its one binding
        /// the same way <see cref="CurveAxis"/> reads what the top, middle and bottom mean:
        /// Ratio: the middle (x1). Scale: one (x1). Shift: nought (no move). Set: where the parameter is set, so the curve holds
        /// it there. False when there is no single binding to read it from (nothing, or several targets).
        /// </summary>
        public static bool NeutralValue(ZoundEffectChain chain, ZoundModifier mod, out float value) {
            value = 0f;
            if (chain == null || mod == null || mod.curve == null) return false;
            int mi = chain.modifiers.IndexOf(mod);
            ZoundModifierBinding only = null; int n = 0;
            foreach (var b in chain.bindings) if (b.modifierIndex == mi) { only = b; n++; }
            if (n != 1) return false;
            switch (ChainModulationCompat.EffectiveCombine(chain, only)) {
                case ModulationCombine.Ratio: value = 0.5f; break;
                case ModulationCombine.Scale: value = 1f; break;
                case ModulationCombine.Set:
                case ModulationCombine.SetFromZero:
                    if (!ChainModulationCompat.TryParam(chain, only, out var pd, out float set)) return false;
                    value = ModulationMath.ToPosition(set, pd.min, pd.max, ModulationMath.IsRatioSpaced(pd.curve));
                    break;
                case ModulationCombine.ShiftFromCentre: value = 0.5f; break;
                default: value = 0f; break;   // Shift (room-relative or whole range): nought does not move it
            }
            value = Mathf.Clamp(value, mod.curve.yMin, mod.curve.yMax);
            return true;
        }

        /// <summary>The source-stage value of a waveform curve by its number: volume 0, pitch 1, time 2, gain 3.</summary>
        public static int ParamOfWhich(int which) => which == 0 ? SourceStageParam.Volume : which == 1 ? SourceStageParam.Pitch : which == 3 ? SourceStageParam.Gain : SourceStageParam.Speed;

        /// <summary>The neutral value of a Klip's volume (0), pitch (1), time (2) or gain (3) curve (see <see cref="NeutralValue"/>).</summary>
        public static bool WaveformCurveNeutral(Zound zound, int which, out float value) {
            value = 0f;
            var o = Find(zound, ParamOfWhich(which));
            return o.Valid && NeutralValue(ChainOf(zound, o), o.mod, out value);
        }

        /// <summary>The waveform overlay's pitch axis: <see cref="CurveAxis"/> for the Klip's pitch curve, plus whether it
        /// is still on the old scale (then no axis is shown, only a warning; see <see cref="OldScaleTip"/>).</summary>
        public static bool PitchAxis(Zound zound, out string top, out string mid, out string bottom, out bool oldScale) {
            top = mid = bottom = null; oldScale = false;
            var o = Find(zound, SourceStageParam.Pitch);
            if (!o.Valid) return false;
            oldScale = PitchIsOldScale(zound);
            if (oldScale) return true;
            CurveAxis(ChainOf(zound, o), o.mod, out top, out mid, out bottom);
            return true;
        }

        public const string OldScaleTip =
            "This pitch curve is still on the scale it was saved with: a height across the whole pitch range, where the " +
            "middle of the display is about x4 and everything in its upper half is x4. It plays exactly as it always has. " +
            "The first time you edit it, it moves to the current scale (middle = no change, top +24 semitones, bottom -24) " +
            "without changing how it sounds.";

        static string Fmt(float v, ParamDesc pd) =>
            pd.curve == ParamCurve.Logarithmic && pd.unit == "" ? "×" + v.ToString("0.##") : v.ToString("0.##") + (string.IsNullOrEmpty(pd.unit) ? "" : " " + pd.unit);

        /// <summary>A Klip's source as its curves see it (trim and file length, in source seconds); false when not known.</summary>
        public static bool TryAxis(Zound zound, out CurveAnchor.Axis axis) {
            axis = default;
            if (!(zound is Klip k)) return false;
            var clip = ZoundSapPlayback.LoadSourceClip(k, out bool alreadyTrimmed);
            if (clip == null || alreadyTrimmed) return false;
            axis = CurveAnchor.Axis.Of(k, clip.length);
            return axis.Valid;
        }

        /// <summary>The modifier owning <paramref name="curve"/> when that curve is anchored to source seconds (T-0501), else null.</summary>
        public static ZoundModifier SourceAnchoredModifierOf(Zound zound, Envelope curve) {
            var m = ModifierOf(zound, curve);
            return m != null && m.curveAnchor == CurveAnchor.Source && CurveAnchor.FollowsWaveform(m) ? m : null;
        }

        /// <summary>The modifier whose curve is <paramref name="curve"/>, an own curve or a chain's, or null.</summary>
        public static ZoundModifier ModifierOf(Zound zound, Envelope curve) {
            int i = ModifierIndexOf(zound, curve);
            return i >= 0 ? Play(zound).modifiers[i] : null;
        }

        /// <summary>The index of the modifier whose curve is <paramref name="curve"/> in the chain the sound PLAYS (what a
        /// voice's layout and its random draws are indexed by), or -1.</summary>
        public static int ModifierIndexOf(Zound zound, Envelope curve) {
            Settle(zound);
            var chain = Play(zound);
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

        /// <summary>The binding of the Klip's pitch curve, or null.</summary>
        public static ZoundModifierBinding PitchBinding(Zound zound) => Find(zound, SourceStageParam.Pitch).bind;

        /// <summary>
        /// Whether the Klip's pitch curve is still on the scale it was saved with before T-0479 (Set: a position across the
        /// whole pitch range, where a flat line in the middle is about x4). It plays exactly as it always has until it is
        /// edited; <see cref="EnsurePitchRatio"/> converts it then.
        /// </summary>
        public static bool PitchIsOldScale(Zound zound) {
            var b = PitchBinding(zound);
            return b != null && ChainModulationCompat.CombineOf(b) == ModulationCombine.Set;
        }

        /// <summary>
        /// Converts the Klip's pitch curve to the Ratio scale if it is still on the old one, so an edit lands on the new
        /// scale; what it plays is unchanged (see <see cref="ChainModulationCompat.ConvertSetToRatio"/>). Called at the
        /// start of every edit of the curve, inside that edit's Undo step. Returns whether anything changed.
        /// </summary>
        public static bool EnsurePitchRatio(Zound zound) {
            EnsureSourceAnchored(zound);
            var o = Find(zound, SourceStageParam.Pitch);
            if (!o.Valid || ChainModulationCompat.CombineOf(o.bind) != ModulationCombine.Set) return false;
            if (!ChainModulationCompat.ConvertSetToRatio(ChainOf(zound, o), o.bind, out int clamped)) return false;
            if (clamped > 0) Debug.LogWarning("[Zounds] " + zound.name + ": " + clamped + " pitch-curve point(s) were beyond two octaves and are now held at x4 or x1/4.");
            TouchAll(zound);
            return true;
        }

        /// <summary>
        /// The start of an edit of one of the sound's own waveform curves (volume 0, pitch 1, time 2, gain 3), inside that edit's Undo
        /// step: the conversions that keep the sound as it was (curves onto the source's own seconds; a volume curve off an
        /// inserted Gain; a pitch curve off its old scale). The one start for every place that edits these curves.
        /// </summary>
        public static void BeginCurveEdit(Zound zound, int which) {
            if (which == 0) EnsureVolumeOwnValue(zound);
            else if (which == 1) EnsurePitchRatio(zound);
            else EnsureSourceAnchored(zound);
        }

        /// <summary>After an overlay drag mutated a curve in place: the next play rebuilds the layout.</summary>
        public static void Touch(Zound zound) {
            TouchAll(zound);
            UnityEditor.EditorUtility.SetDirty(ZoundsProject.Instance);
        }

        /// <summary>
        /// The sound's own values as the newest play of it hears them right now, curves and modulators included, for the
        /// curve bar's readout: "Vol 0.73 · Pitch +2.1 st · Time ×1.00", only the curves that are on. Empty when nothing is
        /// playing it or no curve is on.
        /// </summary>
        public static string LiveReadout(Zound zound) {
            if (zound == null) return "";
            var sb = new System.Text.StringBuilder();
            void Part(int param, string label, System.Func<float, string> fmt) {
                var o = Find(zound, param);
                if (!o.Valid || !o.mod.enabled) return;
                if (!SapVoiceRegistry.TryReadLiveParam(zound, -1, param, out float v)) return;
                if (sb.Length > 0) sb.Append("  ·  ");
                sb.Append(label).Append(' ').Append(fmt(v));
            }
            Part(SourceStageParam.Volume, "Vol", v => v.ToString("0.00"));
            Part(SourceStageParam.Pitch, "Pitch", v => (12f * Mathf.Log(Mathf.Max(v, 1e-4f), 2f)).ToString("+0.0;-0.0;0") + " st");
            Part(SourceStageParam.Speed, "Time", v => "×" + v.ToString("0.00"));
            Part(SourceStageParam.Gain, "Gain", v => (20f * Mathf.Log10(Mathf.Max(v, 1e-5f))).ToString("+0.0;-0.0;0") + " dB");
            return sb.ToString();
        }
    }

}

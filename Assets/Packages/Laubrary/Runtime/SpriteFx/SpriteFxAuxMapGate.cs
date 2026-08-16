// SpriteFxAuxMapGate.cs
// A gate attached to a WHOLE modifier list (not a modifier itself, and never in the list) that scales how
// strongly the list's OWN pixel modifiers affect each pixel, driven by a content-derived auxiliary map (see
// SpriteFxAuxMap) baked from the sprite's own pixel content. Where the map reads 1 the pixel modifiers' full
// chained output wins; where it reads 0 the pixel is left exactly as the list found it; in between the two are
// blended. It never recolours a pixel on its own — see SpriteFxStack.RunStack for the mechanism this drives.
using System;
using UnityEngine;

namespace Laubrary.SpriteFx
{
    /// How a gate judges "the picture" it bakes its map from, when the modifier list it drives belongs to a
    /// SWARM of many small particles composited one after another onto a shared canvas (PyrePlus only — a
    /// SpriteFx Stack, and a non-swarm PyrePlus layer, only ever have one such picture, so this choice is moot
    /// there). APPEND-ONLY: serialized as an int on every authored gate, so an existing gate's scope must never
    /// change index.
    public enum AuxMapScope
    {
        /// Each particle is baked and gated against ONLY ITS OWN finished silhouette, before it composites onto
        /// the shared canvas — cheap (one small picture per particle, no cross-particle awareness needed) and
        /// available now. A content-aware generator (Edge/Detail/the distance-field family) reads as "the edge
        /// of THIS particle", not the swarm's combined outline — an accepted limitation of this mode, not a bug.
        PerParticle,

        /// Bakes from the swarm's own COMBINED finished silhouette after every particle has composited — the
        /// meaning Edge/Detail/distance-field generators actually want for a swarm. NOT YET IMPLEMENTED: it
        /// needs RenderSwarm's per-particle-then-composite loop restructured to defer gated modifiers until
        /// after compositing, with "last particle wins" identity for pixels more than one particle touches.
        /// Queued as a follow-up; selecting it today behaves exactly like PerParticle (see the editor UI, which
        /// currently doesn't expose it for that reason).
        WholeSwarm
    }

    [Serializable]
    public class SpriteFxAuxMapGate
    {
        [Tooltip("Turns the gate on for this modifier list. While on, every enabled pixel modifier in the list " +
                 "runs as one chain and the result is blended against the untouched pixel by the map below, " +
                 "instead of recolouring the whole picture uniformly.")]
        public bool enabled;

        [Tooltip("PyrePlus swarms only: whether the map judges each particle on its own silhouette (Per Particle, " +
                 "cheap, available now) or the swarm's combined finished silhouette (Whole Swarm, not yet built — " +
                 "see AuxMapScope). Irrelevant for a SpriteFx Stack and for a non-swarm PyrePlus layer, which only " +
                 "ever have one picture to judge either way.")]
        public AuxMapScope scope = AuxMapScope.PerParticle;

        [Tooltip("How the map is derived from the sprite's own pixel content: brightness/darkness, saturation, " +
                 "alpha, proximity to a chosen hue, Sobel edges, high-pass detail (painted seams/highlights), or " +
                 "distance from the silhouette's edges (inward, outward, or solid interior only).")]
        public AuxMapGenerator generator = AuxMapGenerator.Luma;

        [ZUIShowIf("generator", "EdgeDistanceIn", "EdgeDistanceOut", "Interior")]
        [Range(0f, 1f)]
        [Tooltip("How strong a Sobel edge (or alpha-silhouette edge) has to be to seed the distance transform " +
                 "the edge-distance/interior generators are built on. Lower catches fainter painted lines too.")]
        public float edgeThreshold = 0.18f;

        [ZUIShowIf("generator", "EdgeDistanceIn", "EdgeDistanceOut", "Interior")]
        [Range(1f, 32f)]
        [Tooltip("How many pixels the edge-distance/interior falloff spans before reaching its full value. " +
                 "Smaller keeps the gate hugging close to the silhouette's edges; larger reaches deeper in.")]
        public float distanceRadius = 8f;

        [ZUIShowIf("generator", "HueProximity")]
        [Range(0f, 360f)]
        [Tooltip("The hue (in degrees) the map opens the gate around, e.g. 200 for blue.")]
        public float hueCenter = 200f;

        [ZUIShowIf("generator", "HueProximity")]
        [Range(1f, 180f)]
        [Tooltip("How wide a band of hues around the centre counts as a match — narrower picks out one colour " +
                 "more precisely, wider catches a broader range of nearby hues.")]
        public float hueWidth = 45f;

        [Range(0f, 4f)]
        [Tooltip("Multiplies the raw map before shaping — turns a faint map up (or a strong one down). Animatable.")]
        public ZUIValue gain = new ZUIValue(1f);

        [Range(-1f, 1f)]
        [Tooltip("Adds a flat offset to the map before shaping — opens or closes the gate everywhere at once, " +
                 "including areas the generator left at zero. Animatable.")]
        public ZUIValue bias = new ZUIValue(0f);

        [Range(0.15f, 4f)]
        [Tooltip("Contrast curve applied to the map. Below 1 flattens it (more of the sprite is at least " +
                 "partly gated open); above 1 sharpens it toward fully open / fully closed. Animatable.")]
        public ZUIValue power = new ZUIValue(1f);

        [Range(0f, 1f)]
        [Tooltip("Cuts off map values below this and rescales what remains — carves away the faint end so only " +
                 "the strongest areas gate the effect open at all. 0 = no cutoff. Animatable.")]
        public ZUIValue threshold = new ZUIValue(0f);

        [Range(0, 4)]
        [Tooltip("Box-blur passes softening the map's edges. Not animatable — a shifting pass COUNT flickers " +
                 "rather than reading as motion, the same reason Posterize's band count is a plain int.")]
        public int blur = 0;

        [Tooltip("Flips the map: what was gated open becomes gated shut and vice versa.")]
        public bool invertMap = false;

        [Tooltip("Forces fully transparent source pixels to always gate to zero, regardless of any gain/bias/" +
                 "invert above, so the effect never opens up over empty space around the sprite.")]
        public bool respectAlpha = true;

        [Range(0f, 2f)]
        [Tooltip("Overall multiplier on the gate's strength — animate this over the effect's life to open or " +
                 "close the whole gate over time (a hit flash, a charge-up glow). Animatable.")]
        public ZUIValue strength = new ZUIValue(1f);

        [Tooltip("PREVIEW ONLY — shows the baked map itself as a plain grayscale image in the live preview " +
                 "instead of the composited result, so you can see exactly what this gate is targeting. Never " +
                 "affects the actual bake, playback, or the asset's saved data.")]
        public bool previewHeatmap = false;

        float gainV, biasV, powerV, thresholdV, strengthV;

        /// Resolves the animatable fields for the current frame — call once per RunStack invocation, before Bake.
        public void Prepare(Func<ZUIValue, int, float> eval)
        {
            gainV = eval(gain, 0);
            biasV = eval(bias, 1);
            powerV = eval(power, 2);
            thresholdV = eval(threshold, 3);
            strengthV = eval(strength, 4);
        }

        /// Bakes the shaped 0..1 map over the whole `w`x`h` picture in `srcPixels`, already multiplied by the
        /// resolved Strength (clamped 0..2, matching the field's own range). Call <see cref="Prepare"/> first.
        public float[] Bake(Color32[] srcPixels, int w, int h)
        {
            var map = SpriteFxAuxMap.BuildMap(srcPixels, w, h, generator, edgeThreshold, distanceRadius,
                hueCenter, hueWidth, gainV, biasV, powerV, thresholdV, blur, invertMap, respectAlpha);
            float s = Mathf.Clamp(strengthV, 0f, 2f);
            if (s != 1f)
                for (int i = 0; i < map.Length; i++) map[i] = Mathf.Clamp01(map[i] * s);
            return map;
        }

        /// Deep copy — for a layer/spec "Duplicate". The ZUIValue fields are independent objects; everything
        /// else is a value type, so MemberwiseClone already copies it.
        public SpriteFxAuxMapGate Clone()
        {
            var g = (SpriteFxAuxMapGate)MemberwiseClone();
            g.gain = Sfx.CloneVal(gain);
            g.bias = Sfx.CloneVal(bias);
            g.power = Sfx.CloneVal(power);
            g.threshold = Sfx.CloneVal(threshold);
            g.strength = Sfx.CloneVal(strength);
            return g;
        }
    }
}

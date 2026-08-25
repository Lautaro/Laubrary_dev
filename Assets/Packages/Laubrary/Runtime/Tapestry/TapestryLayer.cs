// TapestryLayer — one layer in a Tapestry stack: a plug-in TapestryGenerator (its own algorithm + auto-drawn
// UI, mirrors Pyre's Form/PyreForm) plus a per-layer modifier stack, composited with the layers below it
// via a blend mode + opacity. Unlike Pyre's layers (straight-alpha Over only — confirmed by reading
// PyreLayer/FrameComposer, no blend-mode field exists there at all), Tapestry layers DO carry a blend
// mode: the panel/line composition this tool targets explicitly needs Multiply (grooves that darken what's
// below) and Add (highlight strokes) as first-class options, not just Normal-over.
using System;
using System.Collections.Generic;
using UnityEngine;

namespace Laubrary.Tapestry
{
    public enum TapestryBlendMode { Normal, Add, Multiply, Screen }

    [Serializable]
    public class TapestryLayer
    {
        public string name = "Layer";
        public bool enabled = true;
        [SerializeReference] public TapestryGenerator generator;
        [SerializeReference] public List<TapestryLayerModifier> modifiers = new List<TapestryLayerModifier>();
        public TapestryBlendMode blendMode = TapestryBlendMode.Normal;
        [Range(0f, 1f)] public float opacity = 1f;

        // Static transform — always available, useful even with no animation (e.g. offsetting the Lines
        // layer's grid against Panels, or rotating one layer 45° against another).
        public Vector2 position = Vector2.zero;
        [Range(0f, 360f)] public float rotation = 0f;
        public Vector2 scale = Vector2.one;

        // Animate Transform — OFF by default, same avoid-bloat gating every other optional feature in this
        // package uses. Drifts position/rotation/scale over the canvas's animation loop (TapestrySpec.
        // frameCount), same "speed * animT" model as Lathe's Animate Texture. Deliberately allowed to break
        // tileability — rotation and scale especially — since that's the user's own call to make, not a
        // constraint this tool enforces.
        public bool animateTransform = false;
        public Vector2 positionSpeed = new Vector2(0.2f, 0f);   // UV units drifted per full loop
        [Range(-4f, 4f)] public float rotationTurns = 0f;         // full 360° turns drifted per full loop
        public Vector2 scaleSpeed = Vector2.zero;                 // scale delta drifted per full loop

        public TapestryLayer Clone()
        {
            var c = new TapestryLayer
            {
                name = name,
                enabled = enabled,
                generator = generator?.Clone(),
                blendMode = blendMode,
                opacity = opacity,
                position = position,
                rotation = rotation,
                scale = scale,
                animateTransform = animateTransform,
                positionSpeed = positionSpeed,
                rotationTurns = rotationTurns,
                scaleSpeed = scaleSpeed,
                modifiers = new List<TapestryLayerModifier>(),
            };
            if (modifiers != null)
                foreach (var m in modifiers)
                    if (m != null) c.modifiers.Add(m.Clone());
            return c;
        }
    }
}

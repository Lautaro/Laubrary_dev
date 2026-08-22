// TapestryLayer — one layer in a Tapestry stack: a plug-in TapestryGenerator (its own algorithm + auto-drawn
// UI, mirrors PyrePlus's Form/PlusForm) plus a per-layer modifier stack, composited with the layers below it
// via a blend mode + opacity. Unlike PyrePlus's layers (straight-alpha Over only — confirmed by reading
// PyrePlusLayer/FrameComposer, no blend-mode field exists there at all), Tapestry layers DO carry a blend
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

        public TapestryLayer Clone()
        {
            var c = new TapestryLayer
            {
                name = name,
                enabled = enabled,
                generator = generator?.Clone(),
                blendMode = blendMode,
                opacity = opacity,
                modifiers = new List<TapestryLayerModifier>(),
            };
            if (modifiers != null)
                foreach (var m in modifiers)
                    if (m != null) c.modifiers.Add(m.Clone());
            return c;
        }
    }
}

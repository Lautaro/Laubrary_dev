// TapestryLayerModifier — a post-process stage over ONE layer's own rendered pixel buffer, or (in the spec-
// wide globalModifiers list) over the whole finished composite — the same dual-scope role PyrePlus's
// PyreModifier/PostModifier plays for spec.layers[i].modifiers vs spec.globalModifiers (confirmed precedent:
// PyrePlusSpec.cs's globalModifiers list + FrameComposer.Finish running PostModifiers over the composited
// frame). One base class serves both scopes here, and both pickers (layer-local, global) reuse the same
// catalog against different target lists.
using System;
using UnityEngine;

namespace Laubrary.Tapestry
{
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
    public sealed class TapestryModifierInfoAttribute : Attribute
    {
        public string DisplayName { get; }
        public string Group { get; }
        public TapestryModifierInfoAttribute(string displayName, string group = "Modifiers")
        {
            DisplayName = displayName;
            Group = group;
        }
    }

    [Serializable]
    public abstract class TapestryLayerModifier
    {
        // [HideInInspector] so ZuiReflect's field-drawer skips this one — the editor draws it itself as the
        // card's header toggle, not a reflected row. Same convention as LatheMeshModifier.enabled.
        [HideInInspector] public bool enabled = true;

        public abstract string DisplayName { get; }
        public virtual string Description => DisplayName + " modifier.";

        /// Mutate `buf` (width*height, row 0 = bottom) in place. Must keep the result tileable.
        public abstract void Apply(Color32[] buf, int width, int height);

        public virtual TapestryLayerModifier Clone() => (TapestryLayerModifier)MemberwiseClone();
    }
}

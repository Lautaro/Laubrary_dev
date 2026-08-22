// LatheMeshModifier — a post-process stage over a generated solid's mesh data. Mirrors PyrePlus's
// modifier stack (PyreModifier), applied to geometry instead of pixels: discovered by assembly scan
// (LatheWindow's "+ Add modifier" picker), drawn with zero editor code via ZuiReflect.
using System;

namespace Laubrary.Lathe
{
    /// The shared X/Y/Z axis choice every axis-aware modifier (Mirror, Taper, Radial Array, Twist…) picks
    /// from — one enum instead of each modifier declaring its own near-identical copy.
    public enum LatheAxis { X, Y, Z }

    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
    public sealed class LatheModifierInfoAttribute : Attribute
    {
        public string DisplayName { get; }
        public string Group { get; }
        public LatheModifierInfoAttribute(string displayName, string group = "Modifiers")
        {
            DisplayName = displayName;
            Group = group;
        }
    }

    [Serializable]
    public abstract class LatheMeshModifier
    {
        // [HideInInspector] so ZuiReflect.FlowFields (used to auto-draw every OTHER field on the card)
        // skips this one — the editor draws it itself as the card's header toggle, not a reflected row.
        [UnityEngine.HideInInspector] public bool enabled = true;

        public abstract string DisplayName { get; }
        public virtual string Description => DisplayName + " modifier.";

        /// Mutate `data` in place — append/remove verts and triangles as needed.
        public abstract void Apply(LatheMeshData data);

        public virtual LatheMeshModifier Clone()
        {
            var c = (LatheMeshModifier)MemberwiseClone();
            LatheReflectionUtil.CloneListFields(this, c);
            return c;
        }
    }
}

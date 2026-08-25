// TapestryGenerator — the plug-in "one generator = one layer's content" model Tapestry uses in place of
// Pyre's Form (PyreForm): each concrete generator is its own self-contained algorithm, discovered by
// assembly scan (TapestryWindow's "+ Add Generator"/"Change…" picker) and drawn with zero editor code via
// ZuiReflect over its own public fields — same "a form needs zero editor code" convention PyreForm and
// LatheModule both already rely on (confirmed by reading Pyre's PyreForm.cs + FormCatalog()).
using System;
using UnityEngine;

namespace Laubrary.Tapestry
{
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
    public sealed class TapestryGeneratorInfoAttribute : Attribute
    {
        public string DisplayName { get; }
        public string Group { get; }
        public TapestryGeneratorInfoAttribute(string displayName, string group = "Generators")
        {
            DisplayName = displayName;
            Group = group;
        }
    }

    /// What a generator is handed to draw with. `compositeSoFar` is the fully-composited result of every
    /// layer BELOW this one (already through its own modifiers and blend) — read-only unless a generator
    /// explicitly wants to warp/etch it (see TapestryLinesGenerator's Etch mode), in which case it copies
    /// from it into `target` itself rather than mutating it in place. Every generator MUST treat coordinates
    /// as wrapping (u/v mod 1, grid indices mod a grid size) — Tapestry textures are always tileable by
    /// construction, with no per-generator opt-out (confirmed decision: a hardcoded-seamless algorithm beats
    /// a "make tileable" toggle, since that's the entire point of the tool).
    public readonly struct TapestryGenCtx
    {
        public readonly int width, height, seed;
        public readonly Color32[] compositeSoFar;
        public TapestryGenCtx(int width, int height, int seed, Color32[] compositeSoFar)
        {
            this.width = width; this.height = height; this.seed = seed; this.compositeSoFar = compositeSoFar;
        }
    }

    [Serializable]
    public abstract class TapestryGenerator
    {
        public abstract string DisplayName { get; }
        public virtual string Description => DisplayName + " generator.";

        /// Paint into `target` (width*height, row 0 = bottom, already cleared to transparent black). Must
        /// produce a seamlessly tileable result — see the class doc above.
        public abstract void Generate(in TapestryGenCtx ctx, Color32[] target);

        public virtual TapestryGenerator Clone() => (TapestryGenerator)MemberwiseClone();
    }
}

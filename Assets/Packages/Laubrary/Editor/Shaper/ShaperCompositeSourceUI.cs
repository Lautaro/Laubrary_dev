// ShaperCompositeSourceUI — how a shape source brings its OWN card, instead of a reflected dial dump (T-0183).
//
// ── the problem this closes ──────────────────────────────────────────────────────────────────────────────
// Every composite generator's dials are drawn one way today: ZuiReflect.FlowFields over the source object
// (ShaperWindow.Sections.cs, BuildCompositeBody). That is the right default and must stay the default — the
// nine hosted PyreForms declare ~775 authored fields between them, and a hand-listed card for those would be
// wrong within a release. But it is only a default: a reflected dump cannot group a light rig into a Light
// box, cannot hide a dial that does not apply to the form the author picked, and cannot show a 2D pad where
// the object stores two floats. Some generators genuinely have a designed card, and the owner's requirement
// for the hosted Pyre layers is precisely that: "a legacy Pyre generator could look quite like it did in
// Pyre's Shape section."
//
// ── why an interface and a registry, not a branch ────────────────────────────────────────────────────────
// The alternative is an `if (source is PyreLayerCompositeSource)` inside the window, which makes the window
// know about one generator family by name — the same coupling the shape PICKER already refused (see
// ShaperShapePicker's header). So a generator family that has a designed card registers a drawer for it, in
// its own assembly, and the window asks the registry rather than asking what type it is holding. Shaper's
// core editor assembly gains no reference to Pyre's editor assembly; the bridge assembly that already knows
// both registers the pairing.
//
// ── why a context struct rather than the window ──────────────────────────────────────────────────────────
// A drawer lives in another assembly, so it cannot reach ShaperWindow's internal Change/Val/Rebuild. Handing
// it a small record of exactly what a card legitimately needs — where to draw, what to record for Undo, how
// to say "I changed something" — keeps the window's edit contract (one Change per gesture, always undoable)
// on the window's side of the line where it belongs.
using System;
using System.Collections.Generic;
using UnityEngine.UIElements;

namespace Laubrary.Shaper.Editor
{
    /// <summary>Everything a source's own card is allowed to use from the window that hosts it.</summary>
    public struct ShaperSourceUIContext
    {
        /// Where the card is added.
        public VisualElement Body;

        /// The document, for <c>Undo.RegisterCompleteObjectUndo</c> before a control that mutates a value in
        /// place. Complete-object, never <c>RecordObject</c>: a document is a graph of
        /// <c>[SerializeReference]</c>s and RecordObject does not snapshot them, so undo nulls them (T-0198).
        public UnityEngine.Object UndoTarget;

        /// The document's frame count — what a Curve envelope draws its frame markers against, so an authored
        /// curve shows where each baked frame lands.
        public int FrameCount;

        /// The larger canvas edge in pixels, the natural upper bound for a positional or size dial.
        public float CanvasExtent;

        /// One authored edit: record Undo, apply, mark dirty, refresh the preview. Every mutation a card makes
        /// goes through this — it is the window's single Undo contract, not a convenience.
        public Action<Action> Change;

        /// A value was mutated in place by a control that owns its own object (a fill, an envelope): mark the
        /// document dirty and repaint the preview.
        public Action Touch;

        /// The card's own shape changed (a toggle that reveals more dials), so the section must be rebuilt.
        public Action Rebuild;
    }

    /// <summary>A designed card for one family of composite sources.</summary>
    public interface IShaperCompositeSourceUI
    {
        /// Whether this drawer is the one for that source. Asked in registration order; the first yes wins.
        bool CanDraw(IShaperCompositeSource source);

        /// Draw the source's dials. Called INSTEAD of the reflected dial dump, not beside it.
        void Build(ShaperSourceUIContext ctx, IShaperCompositeSource source);
    }

    /// <summary>
    /// The drawers a tool has registered. Empty by default, which is exactly the reflected-dump behaviour every
    /// generator had before this existed.
    ///
    /// <b>Extension point.</b> Register from an <c>[InitializeOnLoadMethod]</c>, the same way a shape-picker
    /// provider is registered (<see cref="ShaperShapeCatalog.Register"/>), so a family joins with no edit to
    /// the window.
    /// </summary>
    public static class ShaperCompositeSourceUICatalog
    {
        static readonly List<IShaperCompositeSourceUI> Drawers = new List<IShaperCompositeSourceUI>();

        public static void Register(IShaperCompositeSourceUI drawer)
        {
            if (drawer == null || Drawers.Contains(drawer)) return;
            Drawers.Add(drawer);
        }

        /// The drawer for this source, or null when it should take the reflected dial dump.
        public static IShaperCompositeSourceUI For(IShaperCompositeSource source)
        {
            if (source == null) return null;
            foreach (var d in Drawers)
            {
                // A broken third-party drawer must cost its own card, never the whole Shape section — the same
                // posture ShaperShapeCatalog.All takes for a provider that throws.
                try { if (d.CanDraw(source)) return d; }
                catch (Exception e) { UnityEngine.Debug.LogException(e); }
            }
            return null;
        }
    }
}

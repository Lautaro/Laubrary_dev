// IZuiRamp — what the ZUI ramp control (ZuiRampControl, reached through ZuiReflect or Z.Ramp) needs from a colour
// ramp: N stops over t in 0..1, each a position + a colour whose alpha is the opacity there, blended by the ramp's
// OWN rule. The data type that owns the ramp (Pyre's PyreRamp) implements this so the toolkit can draw and edit it
// without referencing the owner's assembly — the same reason ZUIValue / ZuiFill / the old IZuiBands live here
// rather than beside their consumers. The Zui toolkit asmdef references only ZuiRuntime, so an interface in this
// folder is the ONLY way it can draw a Pyre type without inverting the dependency.
//
// Contract: positions live in 0..1 and ascend with the index (Insert keeps that; SetPos is expected to be called
// with a value already clamped between the stop's neighbours, which is what the control does). Count == 0 is a
// LEGAL, meaningful state — an empty ramp means "no colour here at all" (Pyre's soot ramp), so an implementer must
// never auto-seed one and Eval must not throw on it.
//
// Undo contract for the control that draws this: OnBeforeMutate fires ONCE per gesture (a drag, a colour pick, an
// insert) BEFORE the first mutation, OnChanged after every mutation.
using UnityEngine;

public interface IZuiRamp
{
    /// How many stops the ramp has. 0 is legal and means an empty ramp (draw the bare checker, offer a way in).
    int Count { get; }

    /// Stop i's position along the ramp, in 0..1.
    float GetPos(int i);

    /// Move stop i to `pos`. The caller clamps between the neighbours, so the index order never changes.
    void SetPos(int i, float pos);

    /// Stop i's colour. Its alpha is the opacity at that position, not a separate key.
    Color GetColor(int i);

    /// Recolour stop i (alpha included).
    void SetColor(int i, Color c);

    /// Add a stop at `pos` with colour `c`, keeping the list sorted by position. Returns the NEW stop's index —
    /// the control needs it to keep dragging the marker it just created.
    int Insert(float pos, Color c);

    /// Drop stop i. Removing the last stop is allowed (see the Count == 0 note above).
    void RemoveAt(int i);

    /// The ramp's OWN evaluation at t in 0..1 — whatever blend space, clamping and transforms it applies. The
    /// control paints its strip from this, so the strip can never drift from what the renderer draws.
    Color Eval(float t);

    /// The ramp's own non-destructive Adjust knobs (hue / saturation / brightness / contrast / phase / quantise /
    /// cycle / reverse), or NULL when the ramp has none of its own — a ZuiGradient returns null because its knobs
    /// are animatable over life and its own editor already draws them, so the ramp control must not draw a second,
    /// non-animatable set beside them. The control edits this object IN PLACE, under the same OnBeforeMutate /
    /// OnChanged gesture contract as every other edit here.
    ZuiRampAdjust RampAdjust { get; }

    /// Labels for the ramp's blend-mode choices, drawn as a segmented row (never a dropdown). Null or empty means
    /// the ramp has no mode to choose and the control draws no mode control at all.
    string[] BlendModeNames { get; }

    /// The selected index into BlendModeNames.
    int BlendMode { get; set; }
}

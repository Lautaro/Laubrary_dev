using UnityEngine;

namespace Laubrary.Cabinets
{
    /// The extension point for everything about a cabinet that is COLOUR rather than GEOMETRY — a palette
    /// clamp, scanlines, a CRT curve, a colour-bleed pass. A Cabinet holds one of these optionally, and a
    /// project supplies its own subclass.
    ///
    /// Laubrary deliberately ships NO implementation. Every real look is pipeline-bound (URP volumes, a
    /// blit pass, a fullscreen shader graph), and a package that must stay render-pipeline agnostic cannot
    /// pick one without dragging that pipeline into every consumer. So the mechanism lives here and the
    /// implementation lives in the game.
    ///
    /// This is the "wired but empty" slot: the plumbing is real and tested, the paint is not shipped. A
    /// Cabinet with no look renders in its native colours and still controls resolution and framing — the
    /// look is an addition to a working cabinet, never a prerequisite for one.
    public abstract class CabinetLook : ScriptableObject
    {
        /// Called when the cabinet is applied to a camera, and again whenever it is re-applied in the editor.
        /// Must be idempotent: it will be called repeatedly on the same camera, including in edit mode.
        public abstract void Apply(Camera cam);

        /// Called when this look stops being in force — a different cabinet took over, or the stage was
        /// disabled. Undo whatever Apply did; a look that leaks its state onto a camera will follow the
        /// player into the next scene and be extremely confusing to track down.
        public virtual void Remove(Camera cam) { }

        /// One line for the Cabinet inspector, so a look is not an opaque object reference. Say what it
        /// DOES to the picture.
        public virtual string Describe() => name;
    }
}

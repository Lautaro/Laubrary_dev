using FOW;

namespace Laubrary.Fov
{
    /// Dev/debug switches for the fog. RevealAll is the classic "lift the fog" toggle: it forces the
    /// fog's APPEARANCE off through the asset's own global effect-strength shader knob
    /// (FogOfWarWorld.SetFowEffectStrength — the cleanest runtime-safe path: nothing is torn down, the
    /// world keeps simulating, exploration keeps accumulating, and FovQuery/FovReveal answers are
    /// unaffected), and FovHideRenderers additionally shows fog-hidden actors while it is on — so the
    /// toggle shows EVERYTHING, and dropping it restores normal fog and hiding on the next frame.
    ///
    /// Editor- and runtime-safe; flip it from any dev hook, e.g. with the new Input System:
    ///     if (Keyboard.current != null && Keyboard.current.f3Key.wasPressedThisFrame)
    ///         FovDebug.RevealAll = !FovDebug.RevealAll;
    public static class FovDebug
    {
        static bool revealAll;

        /// Force the fog appearance off (see the class comment for exactly what that does and does not
        /// change). Safe to set at any time, including before a fog world exists — FovRoomWorld
        /// re-asserts it after every room (re)build.
        public static bool RevealAll
        {
            get => revealAll;
            set { revealAll = value; Apply(); }
        }

        /// Re-push the current flag onto the fog shader. The asset resets its strength global to 1
        /// whenever a FogOfWarWorld (re)initializes, so FovRoomWorld.ConfigureOrCreate calls this after
        /// each build; call it yourself only if something else re-initialized the world.
        public static void Apply()
        {
            if (FogOfWarWorld.instance != null)
                FogOfWarWorld.SetFowEffectStrength(revealAll ? 0f : 1f);
        }
    }
}

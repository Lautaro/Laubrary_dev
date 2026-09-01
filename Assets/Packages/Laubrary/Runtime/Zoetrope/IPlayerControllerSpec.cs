using UnityEngine;

namespace Laubrary.Zoetrope
{
    /// <summary>
    /// A pluggable player-input rig for a character: attaches whatever reads a gamepad/keyboard and drives
    /// movement/aim/fire to the spawned GameObject. The concrete implementation lives in an OPTIONAL bridge
    /// (<c>Zoetrope.ZoeCharacter</c> attaches the real <c>TopDownMotionDriver</c> stack), so Zoetrope core stays
    /// Combat2D-only and input-stack-agnostic — same pattern as <see cref="IBrainSpec"/>.
    ///
    /// Its presence on a <see cref="Zoe"/> (<c>Zoe.playerController != null</c>) IS the answer to "does this Zoe
    /// have a player controller configured": any spawner, including Mirage, uses that one signal to decide
    /// whether a character can be driven by real input instead of (or alongside) a hand-control rig.
    /// </summary>
    public interface IPlayerControllerSpec
    {
        /// Attach the input-reading components to the freshly-spawned character.
        void Attach(GameObject host);
    }
}

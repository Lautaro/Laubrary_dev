using UnityEngine;
using Laubrary.Zoetrope;
using Laubrary.ZoeCharacter;

namespace Laubrary.ZoetropeZoeCharacter
{
    /// <summary>
    /// Answers the motion drivers' <see cref="IMovementLock"/> from the Zoe: walking is locked while a move
    /// marked "Hold still" plays. Attached by the player controller specs, so a character authored in the
    /// Zoe window gets the behaviour with no scene wiring.
    /// </summary>
    [DisallowMultipleComponent]
    public class ReactionMovementLock : MonoBehaviour, IMovementLock
    {
        ReactionFxPlayer _reactions;
        ReactionFxPlayer Reactions => _reactions != null ? _reactions : (_reactions = GetComponent<ReactionFxPlayer>());

        public bool MovementLocked => Reactions != null && Reactions.HoldingStill;
    }
}

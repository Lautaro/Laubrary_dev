using UnityEngine;
using UnityEngine.InputSystem;

namespace Laubrary.Monolith.Samples
{
    public class Demo2_AltGameOnState_SubState1 : MonolithStateBase<MonolithDemo2, MonolithDemo2States>
    {
        public override MonolithDemo2States UpdateState()
        {
            gameManager.message = "ALT SUBSTATE 1 \n(2)SubState 2\nEsc back to AltGameOn";

            if (Keyboard.current != null && Keyboard.current.digit2Key.wasPressedThisFrame)
                return MonolithDemo2States.AlternativeGameOn_SubState2;

            if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
                return MonolithDemo2States.AlternativeGameOn;

            return assignedGameState;
        }
    }
}
using UnityEngine;
using UnityEngine.InputSystem;

namespace Laubrary.Monolith.Samples
{
    public class Demo2_AltGameOnState_SubState2 : MonolithStateBase<MonolithDemo2, MonolithDemo2States>
    {
        public override MonolithDemo2States UpdateState()
        {
            gameManager.message = "ALT SUBSTATE 2 \n(1)SubState 1\n(Q) back MAIN";

            if (Keyboard.current != null && Keyboard.current.digit1Key.wasPressedThisFrame)
                return MonolithDemo2States.AlternativeGameOn_SubState1;

            if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
                return MonolithDemo2States.AlternativeGameOn;

            if (Keyboard.current != null && Keyboard.current.qKey.wasPressedThisFrame)
                return MonolithDemo2States.Main;

            return assignedGameState;
        }
    }
}
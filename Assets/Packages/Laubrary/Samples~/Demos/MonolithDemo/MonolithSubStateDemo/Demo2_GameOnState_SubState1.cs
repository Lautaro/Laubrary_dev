using UnityEngine;
using UnityEngine.InputSystem;

namespace Laubrary.Monolith.Samples
{
    public class Demo2_GameOnState_SubState1 : MonolithStateBase<MonolithDemo2, MonolithDemo2States>
    {
        public override MonolithDemo2States UpdateState()
        {
            gameManager.message = "SUBSTATE 1 \n(2)SubState 2\nEsc back to *GameOn";

            if (Keyboard.current != null && Keyboard.current.digit2Key.wasPressedThisFrame)
                return MonolithDemo2States.GameOn_SubState2;

            if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
                return MonolithDemo2States.GameOn;

            return assignedGameState;
        }
    }
}
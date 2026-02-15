using UnityEngine;
using UnityEngine.InputSystem;

namespace Laubrary.Monolith.Samples
{
    public class Demo2_GameOnState_SubState2 : MonolithStateBase<MonolithDemo2, MonolithDemo2States>
    {
        public override MonolithDemo2States UpdateState()
        {
            gameManager.message = "SUBSTATE 2 \n(1)SubState 1\nEsc back to GameOn\nW Jump to Alternative GameOnSub2";

            if (Keyboard.current != null && Keyboard.current.digit1Key.wasPressedThisFrame)
                return MonolithDemo2States.GameOn_SubState1;

            if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
                return MonolithDemo2States.GameOn;

            if (Keyboard.current != null && Keyboard.current.wKey.wasPressedThisFrame)
                return MonolithDemo2States.AlternativeGameOn_SubState2;


            return assignedGameState;
        }
    }
}
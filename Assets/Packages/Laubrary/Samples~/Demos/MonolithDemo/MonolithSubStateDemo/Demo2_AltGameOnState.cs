using UnityEngine;
using UnityEngine.InputSystem;

namespace Laubrary.Monolith.Samples
{
    public class Demo2_AltGameOnState: MonolithStateBase<MonolithDemo2, MonolithDemo2States>
    {
        public override MonolithDemo2States UpdateState()
        {
            gameManager.message = "ALTERNATIVE game IS ON!\n(1)SubState 1\n(2)SubState 2\nEsc to quit";

            if (Keyboard.current != null && Keyboard.current.digit1Key.wasPressedThisFrame)
                return MonolithDemo2States.AlternativeGameOn_SubState1;

            if (Keyboard.current != null && Keyboard.current.digit2Key.wasPressedThisFrame)
                return MonolithDemo2States.AlternativeGameOn_SubState2;

            if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
                return MonolithDemo2States.Main;

            return assignedGameState;
        }
    }
}
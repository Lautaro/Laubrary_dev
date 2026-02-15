using UnityEngine;
using UnityEngine.InputSystem;

namespace Laubrary.Monolith.Samples
{
    public class Demo2_GameOnState: MonolithStateBase<MonolithDemo2, MonolithDemo2States>
    {
        public override MonolithDemo2States UpdateState()
        {
            gameManager.message = "GAME IS ON!\n(1)SubState 1\nEsc to quit";

            if (Keyboard.current != null && Keyboard.current.digit1Key.wasPressedThisFrame)
                return MonolithDemo2States.GameOn_SubState1;

            if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
                return MonolithDemo2States.Main;

            return assignedGameState;
        }
    }
}
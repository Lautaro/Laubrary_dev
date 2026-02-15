using UnityEngine;
using UnityEngine.InputSystem;

namespace Laubrary.Monolith.Samples
{
    public class Demo2_MainState : MonolithStateBase<MonolithDemo2, MonolithDemo2States>
    {
        public override MonolithDemo2States UpdateState()
        {
            gameManager.message = "Main state. Press A or B to start.";
            
            if (Keyboard.current != null && Keyboard.current.aKey.wasPressedThisFrame)
                return MonolithDemo2States.GameOn;

            if (Keyboard.current != null && Keyboard.current.bKey.wasPressedThisFrame)
                return MonolithDemo2States.AlternativeGameOn;

            return assignedGameState;
        }
    }
}
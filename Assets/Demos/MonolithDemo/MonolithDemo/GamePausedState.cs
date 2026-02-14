using Laubrary.Monolith.Samples;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Laubrary.Monolith.Samples
{
    public class Demo1_GamePausedState : MonolithStateBase<MonolithDemo1, MonolithDemo1States>
    {
        public override MonolithDemo1States UpdateState()
        {
            gameManager.message = "GAME IS PAUSED\nP to Unpause";

            if (Keyboard.current != null && Keyboard.current.pKey.wasPressedThisFrame)
                return MonolithDemo1States.GameOn;

            return assignedGameState;
        }

        public override void EnterState(MonolithDemo1States previousStateEnum)
        {
            Debug.Log("Entering PAUSE state");
        }

        public override void ExitState(MonolithDemo1States nextStateEnum)
        {
            Debug.Log("UNPAUSING! next state :" + nextStateEnum.ToString());
        }
    }
}
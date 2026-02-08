using Laubrary.Monolith.Samples;
using UnityEngine;

namespace Laubrary.Monolith.Samples
{
    public class Demo1_GameOnState : MonolithStateBase<MonolithDemo1, MonolithDemo1States>
    {
        public override MonolithDemo1States UpdateState()
        {
            gameManager.message = "GAME IS ON!\n(P)ause\nEsc to quit";

            if (Input.GetKeyDown(KeyCode.P))
                return MonolithDemo1States.GamePaused;

            if (Input.GetKeyDown(KeyCode.Escape))
                return MonolithDemo1States.Start;

            return assignedGameState;
        }

        public override void EnterState(MonolithDemo1States previousStateEnum)
        {
            Debug.Log("Entering GAME ON state. Coming from : " + previousStateEnum.ToString());
            if (previousStateEnum.Equals(MonolithDemo1States.Start))
                Debug.Log("Setting up new GAME!");


            if (previousStateEnum.Equals(MonolithDemo1States.GamePaused))
                Debug.Log("Resuming game!");
        }

        public override void ExitState(MonolithDemo1States nextStateEnum)
        {
            Debug.Log("Exiting GAME ON state");

            if (nextStateEnum.Equals(MonolithDemo1States.Start))
                Debug.Log("Finishing game");

            if (nextStateEnum.Equals(MonolithDemo1States.GamePaused))
                Debug.Log("Just pausing");
        }
    }
}
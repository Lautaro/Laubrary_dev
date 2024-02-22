using Laubrary.Monolith;
using UnityEngine;

public class GamePausedState : MonolithStateBase<MonolithTest, MonolithTestStates>
{
    public override MonolithTestStates UpdateState()
    {
        gameManager.message = "GAME IS PAUSED\nP to Unpause";

        if (Input.GetKeyDown(KeyCode.P))
            return MonolithTestStates.GameOn;

        return assignedGameState;
    }

    public override void EnterState(MonolithTestStates previousStateEnum)
    {
        Debug.Log("Entering PAUSE state");
    }

    public override void ExitState(MonolithTestStates nextStateEnum)
    {
        Debug.Log("UNPAUSING! next state :" + nextStateEnum.ToString());
    }
}

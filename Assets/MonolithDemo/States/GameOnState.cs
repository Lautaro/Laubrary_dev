using Laubrary.Monolith;
using UnityEngine;

public class GameOnState : MonolithStateBase<MonolithTest, MonolithTestStates>
{
    public override MonolithTestStates UpdateState()
    {
        gameManager.message = "GAME IS ON!\n(P)ause\nEsc to quit";

        if (Input.GetKeyDown(KeyCode.P))
            return MonolithTestStates.GamePaused;

        if (Input.GetKeyDown(KeyCode.Escape))
            return MonolithTestStates.Start;

        return assignedGameState;
    }

    public override void EnterState(MonolithTestStates previousStateEnum)
    {
        Debug.Log("Entering GAME ON state. Coming from : " + previousStateEnum.ToString());
        if (previousStateEnum.Equals(MonolithTestStates.Start))
            Debug.Log("Setting up new GAME!");


        if (previousStateEnum.Equals(MonolithTestStates.GamePaused))
            Debug.Log("Resuming game!");
    }

    public override void ExitState(MonolithTestStates nextStateEnum)
    {
        Debug.Log("Exiting GAME ON state");

        if (nextStateEnum.Equals(MonolithTestStates.Start))
            Debug.Log("Finishing game");

        if (nextStateEnum.Equals(MonolithTestStates.GamePaused))
            Debug.Log("Just pausing");
    }
}

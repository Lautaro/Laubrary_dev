using Laubrary.Monolith;
using UnityEngine;


public class StartState : MonolithStateBase<MonolithTest, MonolithTestStates>
{
    string gameName;

    public StartState(string gameName)
    {
        this.gameName = gameName;
    }
    public override MonolithTestStates UpdateState()
    {
        gameManager.message = $"Welcome to {gameName} \nThis is the START STATE\nSpace to start game";
        if (Input.GetKeyDown(KeyCode.Space))
            return MonolithTestStates.GameOn;

        return assignedGameState;
    }

    public override void EnterState(MonolithTestStates previousStateEnum)
    {
        if (previousStateEnum == assignedGameState)
            Debug.Log("Booting up!....");

        Debug.Log("Entering START state. Previous: " + previousStateEnum.ToString());
    }

    public override void ExitState(MonolithTestStates nextStateEnum)
    {
        Debug.Log("Exiting START state " + nextStateEnum.ToString());
    }
}

using Laubrary.Monolith;
using Laubrary.Monolith.Samples;
using UnityEngine;

namespace Laubrary.Monolith.Samples
{
    public class Demo1_StartState : MonolithStateBase<MonolithDemo1, MonolithDemo1States>
    {
        string gameName;

        public Demo1_StartState(string gameName)
        {
            this.gameName = gameName;
        }
        public override MonolithDemo1States UpdateState()
        {
            gameManager.message = $"Welcome to {gameName} \nThis is the START STATE\nSpace to start game";
            if (Input.GetKeyDown(KeyCode.Space))
                return MonolithDemo1States.GameOn;

            return assignedGameState;
        }

        public override void EnterState(MonolithDemo1States previousStateEnum)
        {
            if (previousStateEnum == assignedGameState)
                Debug.Log("Booting up!....");

            Debug.Log("Entering START state. Previous: " + previousStateEnum.ToString());
        }

        public override void ExitState(MonolithDemo1States nextStateEnum)
        {
            Debug.Log("Exiting START state " + nextStateEnum.ToString());
        }
    }
}
using UnityEngine;
namespace Laubrary.Monolith.Samples
{
    public class Demo2_GameOnState: MonolithStateBase<MonolithDemo2, MonolithDemo2States>
    {
        public override MonolithDemo2States UpdateState()
        {
            gameManager.message = "GAME IS ON!\n(1)SubState 1\nEsc to quit";

            if (Input.GetKeyDown(KeyCode.Alpha1))
                return MonolithDemo2States.GameOn_SubState1;

            if (Input.GetKeyDown(KeyCode.Escape))
                return MonolithDemo2States.Main;

            return assignedGameState;
        }
    }
}
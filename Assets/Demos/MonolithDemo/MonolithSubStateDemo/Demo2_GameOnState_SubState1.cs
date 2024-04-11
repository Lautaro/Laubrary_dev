using UnityEngine;
namespace Laubrary.Monolith.Demo2
{
    public class Demo2_GameOnState_SubState1 : MonolithStateBase<MonolithSubStateTest, MonolithDemo2States>
    {
        public override MonolithDemo2States UpdateState()
        {
            gameManager.message = "SUBSTATE 1 \n(2)SubState 2\nEsc back to *GameOn";

            if (Input.GetKeyDown(KeyCode.Alpha2))
                return MonolithDemo2States.GameOn_SubState2;

            if (Input.GetKeyDown(KeyCode.Escape))
                return MonolithDemo2States.GameOn;

            return assignedGameState;
        }
    }
}
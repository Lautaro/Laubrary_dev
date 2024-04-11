using UnityEngine;

namespace Laubrary.Monolith.Demo2
{
    public class Demo2_AltGameOnState_SubState2 : MonolithStateBase<MonolithSubStateTest, MonolithDemo2States>
    {
        public override MonolithDemo2States UpdateState()
        {
            gameManager.message = "ALT SUBSTATE 2 \n(1)SubState 1\n(Q) back MAIN";

            if (Input.GetKeyDown(KeyCode.Alpha1))
                return MonolithDemo2States.AlternativeGameOn_SubState1;

            if (Input.GetKeyDown(KeyCode.Escape))
                return MonolithDemo2States.AlternativeGameOn;

            if (Input.GetKeyDown(KeyCode.Q))
                return MonolithDemo2States.Main;

            return assignedGameState;
        }
    }
}
using UnityEngine;

namespace Laubrary.Monolith.Demo2
{
    public class Demo2_GameOnState_SubState2 : MonolithStateBase<MonolithSubStateTest, MonolithDemo2States>
    {
        public override MonolithDemo2States UpdateState()
        {
            gameManager.message = "SUBSTATE 2 \n(1)SubState 1\nEsc back to GameOn\nW Jump to Alternative GameOnSub2";

            if (Input.GetKeyDown(KeyCode.Alpha1))
                return MonolithDemo2States.GameOn_SubState1;

            if (Input.GetKeyDown(KeyCode.Escape))
                return MonolithDemo2States.GameOn;

            if (Input.GetKeyDown(KeyCode.W))
                return MonolithDemo2States.AlternativeGameOn_SubState2;


            return assignedGameState;
        }
    }
}
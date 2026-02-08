using UnityEngine;
namespace Laubrary.Monolith.Samples
{
    public class Demo2_AltGameOnState_SubState1 : MonolithStateBase<MonolithDemo2, MonolithDemo2States>
    {
        public override MonolithDemo2States UpdateState()
        {
            gameManager.message = "ALT SUBSTATE 1 \n(2)SubState 2\nEsc back to AltGameOn";

            if (Input.GetKeyDown(KeyCode.Alpha2))
                return MonolithDemo2States.AlternativeGameOn_SubState2;

            if (Input.GetKeyDown(KeyCode.Escape))
                return MonolithDemo2States.AlternativeGameOn;

            return assignedGameState;
        }
    }
}
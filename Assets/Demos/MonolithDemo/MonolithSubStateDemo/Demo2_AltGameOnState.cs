using UnityEngine;
namespace Laubrary.Monolith.Samples
{
    public class Demo2_AltGameOnState: MonolithStateBase<MonolithDemo2, MonolithDemo2States>
    {
        public override MonolithDemo2States UpdateState()
        {
            gameManager.message = "ALTERNATIVE game IS ON!\n(1)SubState 1\n(2)SubState 2\nEsc to quit";

            if (Input.GetKeyDown(KeyCode.Alpha1))
                return MonolithDemo2States.AlternativeGameOn_SubState1;

            if (Input.GetKeyDown(KeyCode.Alpha2))
                return MonolithDemo2States.AlternativeGameOn_SubState2;

            if (Input.GetKeyDown(KeyCode.Escape))
                return MonolithDemo2States.Main;

            return assignedGameState;
        }
    }
}
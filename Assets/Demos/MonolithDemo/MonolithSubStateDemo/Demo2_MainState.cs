using UnityEngine;

namespace Laubrary.Monolith.Demo2
{
    public class Demo2_MainState : MonolithStateBase<MonolithSubStateTest, MonolithDemo2States>
    {
        public override MonolithDemo2States UpdateState()
        {
            gameManager.message = "Main state. Press A or B to start.";
            
            if (Input.GetKeyDown(KeyCode.A))
                return MonolithDemo2States.GameOn;

            if (Input.GetKeyDown(KeyCode.B))
                return MonolithDemo2States.AlternativeGameOn;

            return assignedGameState;
        }
    }
}
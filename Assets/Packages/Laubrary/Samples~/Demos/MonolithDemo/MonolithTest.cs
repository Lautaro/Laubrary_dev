using Laubrary.Monolith;
using UnityEngine;

namespace Laubrary.Monolith.Samples
{
    public class MonolithDemo1 : MonoBehaviour
    {
        public MonolithStateMachine<MonolithDemo1, MonolithDemo1States> monolith;
        public string gameName;
        public string message;

        void Start()
        {
            monolith = new(this);
            monolith.AddState(MonolithDemo1States.GameOn, new Demo1_GameOnState());
            monolith.AddState(MonolithDemo1States.GamePaused, new Demo1_GamePausedState());
            monolith.AddState(MonolithDemo1States.Start, new Demo1_StartState(gameName), true);
        }

        void Update()
        {
            monolith.UpdateState();
        }
    }

    public enum MonolithDemo1States
    {
        GameOn,
        GamePaused,
        Start
    }
}
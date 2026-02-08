using Sirenix.OdinInspector;
using Unity.VisualScripting;
using UnityEngine;

namespace Laubrary.Monolith.Samples
{
    public class MonolithDemo2 : MonoBehaviour
    {
        public MonolithStateMachine<MonolithDemo2, MonolithDemo2States> monolith;

        public bool LogTransitions = false;

        [ReadOnly, Multiline(10)]
        public string message;

        void Start()
        {
            monolith = new(this);
            monolith.logTransitions = LogTransitions;

            monolith.AddState(MonolithDemo2States.Main, new Demo2_MainState(), true);

            var gameOnState = monolith.AddState(MonolithDemo2States.GameOn, new Demo2_GameOnState()); 
            gameOnState.AddChildState(MonolithDemo2States.GameOn_SubState1, new Demo2_GameOnState_SubState1());
            gameOnState.AddChildState(MonolithDemo2States.GameOn_SubState2, new Demo2_GameOnState_SubState2());

            var altGameOnState = monolith.AddState(MonolithDemo2States.AlternativeGameOn, new Demo2_AltGameOnState());
            altGameOnState.AddChildState(MonolithDemo2States.AlternativeGameOn_SubState1, new Demo2_AltGameOnState_SubState1());
            altGameOnState.AddChildState(MonolithDemo2States.AlternativeGameOn_SubState2, new Demo2_AltGameOnState_SubState2());
        }

        void Update()
        {
            monolith.UpdateState();
        }
    }
}
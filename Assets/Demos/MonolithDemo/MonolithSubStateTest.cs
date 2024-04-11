using Sirenix.OdinInspector;
using Unity.VisualScripting;
using UnityEngine;

namespace Laubrary.Monolith.Demo2
{
    public class MonolithSubStateTest : MonoBehaviour
    {
        public MonolithStateMachine<MonolithSubStateTest, MonolithDemo2States> monolith;

        public bool LogTransitions = false;

        [ReadOnly, Multiline(10)]
        public string message;

        void Start()
        {
            monolith = new(this);
            monolith.logTransitions = LogTransitions;

            // THIS IS THE HIERARCHY WE CONSTRUCT
            /*
            Main
            GameOn
                SubState1
                SubState2
            AlternativeGameOn
                AlternativeSubState1
                AlternativeSubState2
            */

            monolith.AddState(MonolithDemo2States.Main, new Demo2_MainState(), true);

            var gameOnState = monolith.AddState(MonolithDemo2States.GameOn, new Demo2_GameOnState()); 
            gameOnState.AddChildState(MonolithDemo2States.GameOn_SubState1, new Demo2_GameOnState_SubState1());
            gameOnState.AddChildState(MonolithDemo2States.GameOn_SubState2, new Demo2_GameOnState_SubState2());

            var altGameOnState = monolith.AddState(MonolithDemo2States.AlternativeGameOn, new Demo2_AltGameOnState());
            altGameOnState.AddChildState(MonolithDemo2States.AlternativeGameOn_SubState1, new Demo2_AltGameOnState_SubState1());
            altGameOnState.AddChildState(MonolithDemo2States.AlternativeGameOn_SubState2, new Demo2_AltGameOnState_SubState2());
        }

        // Update is called once per frame
        void Update()
        {
            monolith.UpdateState();
        }
    }

    public enum MonolithDemo2States
    {
        Main,
        GameOn,
        GameOn_SubState1,
        GameOn_SubState2,
        AlternativeGameOn,
        AlternativeGameOn_SubState1,
        AlternativeGameOn_SubState2
    }
}
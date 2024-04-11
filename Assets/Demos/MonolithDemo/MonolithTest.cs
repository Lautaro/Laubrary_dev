using Laubrary.Monolith;
using Laubrary.Monolith.Demo;
using Sirenix.OdinInspector;
using UnityEngine;

public class MonolithTest : MonoBehaviour
{
    public MonolithStateMachine<MonolithTest, MonolithTestStates> monolith;

    public string gameName;
    [ReadOnly, Multiline]
    public string message;
    void Start()
    {
        monolith = new(this);

        monolith.AddState(MonolithTestStates.GameOn, new GameOnState());
        monolith.AddState(MonolithTestStates.GamePaused, new GamePausedState());
        monolith.AddState(MonolithTestStates.Start, new StartState(gameName),true);

       // monolith.CurrentStateEnum = MonolithTestStates.Start;
    }

    void Update()
    {
        monolith.UpdateState();
    }
}

public enum MonolithTestStates
{
     GameOn, GamePaused, Start

}

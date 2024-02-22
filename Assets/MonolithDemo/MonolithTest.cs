using Laubrary.Monolith;
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
        monolith.AddState(MonolithTestStates.Start, new StartState (gameName));
        monolith.AddState(MonolithTestStates.GameOn, new GameOnState());
        monolith.AddState(MonolithTestStates.GamePaused, new GamePausedState());

        monolith.CurrentStateEnum = MonolithTestStates.Start;
    }

    void Update()
    {
        monolith.UpdateState();
    }
}

public enum MonolithTestStates
{
    Start, GameOn, GamePaused

}

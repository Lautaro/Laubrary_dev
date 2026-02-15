using UnityEngine;

namespace Laubrary.SimpleMenu
{
    public enum GameDifficulty
    {
        Easy,
        Normal,
        Hard,
        Expert
    }

    public enum Edges
    {
        None, 
        Bounce,
        Wrap
    }

    [SimpleMenu("Edges")]
    public class EdgeSettings : SimpleMenuBase
    {
        [SimpleMenuDropdown("Difficulty",persistenceId:"difficulty")]
        private GameDifficulty difficulty = GameDifficulty.Normal;

        [SimpleMenuDropdown("Anti-Aliasing", onValueChanged: nameof(OnEdgesChanged),persistenceId:"antiAliasing")]
        private Edges antiAliasing = Edges.None;

        [SimpleMenuDropdown("Debris Wrap Behaviour", getOptionsMethod: nameof(GetDebrisWrapBehaviourOptions), onValueChanged: nameof(OnEdgesChanged),persistenceId:"debrisWrapBehaviour")]
        private string debrisWrapBehaviour = "Same as player";

        private string[] GetDebrisWrapBehaviourOptions()
        {
            return new string[] { "Same as player", "Always Wrap", "Never Wrap" };
        }
        private void OnEdgesChanged(Edges newValue)
        {
            Debug.Log($"Edges changed to: {newValue}");
        }
    }
}

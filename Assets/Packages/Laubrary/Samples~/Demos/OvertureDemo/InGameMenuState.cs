using UnityEngine;
using UnityEngine.UI;

namespace Laubrary.Overture.Demo
{
    /// <summary>
    /// Substate of GameState. Shows the in-game pause menu.
    /// The Back button transitions back to GameState (the superState).
    /// </summary>
    public class InGameMenuState : OvertureState
    {
        public void ResumeGame() => manager.TransitionTo("GameState");
    }
}

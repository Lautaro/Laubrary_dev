using Laubrary.SimpleMenu;
using UnityEngine;

namespace Laubrary.Overture.Demo
{
    /// <summary>
    /// Main menu for the Overture demo. Attach anywhere inside the MainMenuState hierarchy.
    /// Set <see cref="startGameStateName"/> to match the target state's GameObject name.
    /// </summary>
    [SimpleMenu("Main Menu")]
    public class DemoMainMenu : SimpleMenuBase
    {
        [SerializeField] private string startGameStateName = "StartGameState";

        private OvertureManager _manager;

        protected override void Awake()
        {
            base.Awake();
            _manager = GetComponentInParent<OvertureManager>();
        }

        [SimpleMenuButton("Start Game")]
        private void StartGame()
        {
            _manager.TransitionTo(startGameStateName);
        }

        [SimpleSubMenu("Settings")]
        private DemoSettingsMenu Settings;
    }
}

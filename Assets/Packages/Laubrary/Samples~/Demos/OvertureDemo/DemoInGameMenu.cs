using UnityEngine;
using Laubrary.SimpleMenu;
using Laubrary.Overture.Demo;

namespace Laubrary.Demos
{
    [SimpleMenu(label:"InGame Menu")]
    public class DemoInGameMenu : SimpleMenuBase
    {
        public InGameMenuState inGameMenuState;

        [SimpleMenuButton("Resume")]
        void Resume() => inGameMenuState.ResumeGame();

        [SimpleMenuButton("Back To Main Menu")]
        void BackToMainMenu() => inGameMenuState.Manager.TransitionTo("MainMenuState");

    }
}

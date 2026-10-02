using UnityEngine;

namespace Laubrary.SimpleMenu.Samples
{
    /// <summary>
    /// Main menu for the SimpleMenu transition demo.
    /// Showcases alpha, position, and scale enter/exit animations.
    /// Configure transitionAnimationType and related fields in the Inspector.
    /// </summary>
    [SimpleMenu("Transition Demo")]
    public class TransitionDemoMainMenu : SimpleMenuBase
    {
        [SimpleMenuTextBox("Welcome to the SimpleMenu\nTransition Demo!\nEach menu animates in and out.")]
        private int _infoDummy;

        [SimpleSubMenu("Settings")]
        private TransitionDemoSettingsMenu _settings;

        [SimpleSubMenu("Display")]
        private TransitionDemoDisplayMenu _display;

        [SimpleSubMenu("About")]
        private TransitionDemoAboutMenu _about;

        [SimpleMenuButton("Quit")]
        private void Quit()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }
    }
}

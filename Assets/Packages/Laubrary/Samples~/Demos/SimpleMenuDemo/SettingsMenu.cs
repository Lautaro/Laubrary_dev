using UnityEngine;

namespace Laubrary.SimpleMenu
{
    [SimpleMenuLayout(useHorizontalLayout: true)]
    public class SettingsMenu : SimpleMenuBase
    {
        [SubMenu(typeof(AudioMenu), "Audio Settings")]
        private void OpenAudioSettings()
        {
        }

        [SubMenu(typeof(VideoMenu), "Video Settings")]
        private void OpenVideoSettings()
        {
        }

        [SimpleMenuButton("Reset to Defaults")]
        private void ResetSettings()
        {
            Debug.Log("Resetting all settings to defaults");
        }
    }
}

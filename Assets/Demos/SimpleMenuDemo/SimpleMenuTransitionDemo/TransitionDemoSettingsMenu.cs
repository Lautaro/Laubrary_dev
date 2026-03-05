using UnityEngine;

namespace Laubrary.SimpleMenu.Samples
{
    /// <summary>
    /// Settings submenu for the transition demo.
    /// </summary>
    [SimpleMenu("Settings")]
    public class TransitionDemoSettingsMenu : SimpleMenuBase
    {
        [SimpleMenuSlider("Master Volume", 0f, 100f, showValueLabel: true, persistenceId: "td_masterVol")]
        private float masterVolume = 80f;

        [SimpleMenuSlider("SFX Volume", 0f, 100f, showValueLabel: true, persistenceId: "td_sfxVol")]
        private float sfxVolume = 80f;

        [SimpleMenuToggle("Mute All", nameof(OnMuteChanged), persistenceId: "td_mute")]
        private bool muteAll = false;

        [SimpleMenuDropdown("Language", nameof(GetLanguageOptions))]
        private string language = "English";

        private string[] GetLanguageOptions() => new[] { "English", "Spanish", "French", "German" };

        private void OnMuteChanged(bool muted)
        {
            Debug.Log($"[TransitionDemo] Mute all: {muted}");
        }
    }
}

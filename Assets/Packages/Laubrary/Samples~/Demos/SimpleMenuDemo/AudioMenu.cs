using UnityEngine;

namespace Laubrary.SimpleMenu
{
    [SimpleMenu("Audio Stuff")]
    public class AudioMenu : SimpleMenuBase
    {
        [SimpleMenuSlider("SFX Volume", 0f, 100f)]
        private float sfxVolume = 80f;

        [SimpleMenuSlider("Music Volume", 0f, 100f)]
        private float musicVolume = 80f;

        [SimpleMenuToggle("Enable Sound", nameof(OnSoundToggled))]
        private bool soundEnabled = true;

        private void OnSoundToggled(bool enabled)
        {
            Debug.Log($"Sound Enabled: {enabled}");
        }

        [SimpleMenuSlider("SFX Volume", 0f, 1f, showValueLabel: true)]
        private float masterVolume = 1f;
    }
}

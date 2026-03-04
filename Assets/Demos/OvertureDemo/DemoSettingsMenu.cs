using Laubrary.SimpleMenu;
using UnityEngine;

namespace Laubrary.Overture.Demo
{
    /// <summary>Simulated settings sub-menu shown from the Overture demo main menu.</summary>
    [SimpleMenu("Settings")]
    public class DemoSettingsMenu : SimpleMenuBase
    {
        [SimpleMenuSlider("Master Volume", 0f, 100f, persistenceId: "demo_masterVolume")]
        private float masterVolume = 80f;

        [SimpleMenuSlider("SFX Volume", 0f, 100f, persistenceId: "demo_sfxVolume")]
        private float sfxVolume = 80f;

        [SimpleMenuToggle("Fullscreen", nameof(OnFullscreenToggled), persistenceId: "demo_fullscreen")]
        private bool fullscreen = true;

        [SimpleMenuDropdown("Quality", persistenceId: "demo_quality")]
        private DemoQuality quality = DemoQuality.High;

        [SimpleMenuButton("Reset to Defaults")]
        private void ResetDefaults()
        {
            masterVolume = 80f;
            sfxVolume    = 80f;
            fullscreen   = true;
            quality      = DemoQuality.High;
            Debug.Log("Settings reset to defaults.");
        }

        private void OnFullscreenToggled(bool value)
        {
            Screen.fullScreen = value;
        }
    }

    public enum DemoQuality { Low, Medium, High, Ultra }
}

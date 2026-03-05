using UnityEngine;

namespace Laubrary.SimpleMenu.Samples
{
    /// <summary>
    /// Display submenu for the transition demo. Showcases dropdown and toggle controls.
    /// </summary>
    [SimpleMenu("Display")]
    public class TransitionDemoDisplayMenu : SimpleMenuBase
    {
        public enum QualityLevel { Low, Medium, High, Ultra }

        [SimpleMenuDropdown("Quality", persistenceId: "td_quality")]
        private QualityLevel quality = QualityLevel.High;

        [SimpleMenuDropdown("Resolution", nameof(GetResolutionOptions))]
        private string resolution = "1920x1080";

        private string[] GetResolutionOptions() => new[] { "1280x720", "1920x1080", "2560x1440", "3840x2160" };

        [SimpleMenuToggle("Fullscreen", nameof(OnFullscreenChanged), persistenceId: "td_fullscreen")]
        private bool fullscreen = true;

        [SimpleMenuToggle("VSync", persistenceId: "td_vsync")]
        private bool vsync = true;

        [SimpleMenuSlider("Brightness", 0f, 1f, showValueLabel: true, persistenceId: "td_brightness")]
        private float brightness = 1f;

        private void OnFullscreenChanged(bool value)
        {
            Debug.Log($"[TransitionDemo] Fullscreen: {value}");
        }
    }
}

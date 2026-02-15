using UnityEngine;

namespace Laubrary.SimpleMenu
{
    //[SimpleMenu("Video Settings")] 
    public class VideoMenu : SimpleMenuBase
    {
        private int currentResolutionIndex = 0;
        private readonly string[] resolutions = { "1920x1080", "1280x720", "800x600" };

        [SimpleMenuButton("Toggle Fullscreen")]
        private void ToggleFullscreen()
        {
            Screen.fullScreen = !Screen.fullScreen;
            Debug.Log($"Fullscreen: {Screen.fullScreen}");
        }

        [SimpleMenuButton("Next Resolution")]
        private void NextResolution()
        {
            currentResolutionIndex = (currentResolutionIndex + 1) % resolutions.Length;
            Debug.Log($"Resolution: {resolutions[currentResolutionIndex]}");
        }

        [SimpleMenuButton("Toggle VSync")]
        private void ToggleVSync()
        {
            QualitySettings.vSyncCount = QualitySettings.vSyncCount == 0 ? 1 : 0;
            Debug.Log($"VSync: {(QualitySettings.vSyncCount > 0 ? "On" : "Off")}");
        }
    }
}

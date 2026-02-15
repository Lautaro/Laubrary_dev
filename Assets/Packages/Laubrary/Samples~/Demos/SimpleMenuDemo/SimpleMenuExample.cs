using UnityEditor;
using UnityEngine;

namespace Laubrary.SimpleMenu
{
    [SimpleMenu("Example Menu")]
    public class SimpleMenuExample : SimpleMenuBase
    {
        [SimpleMenuToggle("Enable Feature X")]
        [SerializeField]private bool enableFeatureX = false;

        [SimpleMenuButton("Start Game")]
        private void StartGame()
        {
            Debug.Log("Starting game...");
        }

        [SimpleMenuTextBox("This is info text for the player.\n<color=yellow>Important:</color>\nThis is a dummy field.")]
        private int dummyField;

        [SimpleSubMenu("Audio Settings")]
        private AudioMenu AudioSettings;

        [SimpleMenuInputField("Write Something")]
        public string someField;

        [SimpleSubMenu("Video Settings")]
        private VideoMenu VideoSettings;

        [SimpleSubMenu("Edges")]
        private EdgeSettings EdgeSettings;
    }
}

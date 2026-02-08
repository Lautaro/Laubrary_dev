using UnityEngine;
using Laubrary.Lau_StatefulUI;

namespace Laubrary.Lau_StatefulUI.Demo
{
    public class StatefulUITester : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private StatefulUI targetUI;

        [Header("Test Controls")]
        [SerializeField] private KeyCode navigateKey = KeyCode.N;
        [SerializeField] private KeyCode toggleKey = KeyCode.T;
        [SerializeField] private KeyCode focusKey = KeyCode.F;

        private bool _isNavigated = false;
        private bool _isToggled = false;
        private bool _isFocused = false;

        private void Update()
        {
            if (targetUI == null) return;

            if (Input.GetKeyDown(navigateKey))
            {
                _isNavigated = !_isNavigated;
                targetUI.SetNavigated(_isNavigated);
                Debug.Log($"Navigated: {_isNavigated}");
            }

            if (Input.GetKeyDown(toggleKey))
            {
                _isToggled = !_isToggled;
                targetUI.SetToggled(_isToggled);
                Debug.Log($"Toggled: {_isToggled}");
            }

            if (Input.GetKeyDown(focusKey))
            {
                _isFocused = !_isFocused;
                targetUI.SetFocused(_isFocused);
                Debug.Log($"Focused: {_isFocused}");
            }
        }

        private void OnGUI()
        {
            if (targetUI == null) return;

            GUILayout.BeginArea(new Rect(10, 10, 300, 200));
            GUILayout.Label("StatefulUI Tester", GUI.skin.box);
            GUILayout.Label($"Press {navigateKey} to toggle Navigated: {_isNavigated}");
            GUILayout.Label($"Press {toggleKey} to toggle Toggled: {_isToggled}");
            GUILayout.Label($"Press {focusKey} to toggle Focused: {_isFocused}");
            GUILayout.Space(10);
            GUILayout.Label($"Current Interaction State: {targetUI.CurrentInteractionState}");
            GUILayout.EndArea();
        }
    }
}

using System.Collections;
using UnityEngine;

namespace Laubrary.SimpleMenu
{
    /// <summary>
    /// Activates a target GameObject after a delay on startup.
    /// Place this on any always-active GameObject in the scene, assign the menu's
    /// root GameObject (which should start inactive in the Inspector), and set the delay.
    /// </summary>
    public class SimpleMenuStartupDelay : MonoBehaviour
    {
        [SerializeField, Tooltip("The menu root GameObject to activate. Must start inactive in the scene.")]
        private GameObject targetMenu;

        [SerializeField, Tooltip("Seconds to wait before activating the menu."), Min(0f)]
        private float delay = 1f;

        private void Start()
        {
            StartCoroutine(ActivateAfterDelay());
        }

        private IEnumerator ActivateAfterDelay()
        {
            targetMenu.SetActive(false); // Ensure it's inactive at start
            yield return new WaitForSeconds(delay);
            targetMenu.SetActive(true);
        }
    }
}

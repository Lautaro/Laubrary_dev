using System;
using UnityEngine;

namespace Laubrary.Overture.Demo
{
    /// <summary>
    /// Detects mouse clicks on the Interactable cube and notifies the game controller.
    /// Requires a Collider on this GameObject.
    /// </summary>
    [RequireComponent(typeof(Collider))]
    public class ClickableCube : MonoBehaviour
    {
        public event Action OnClicked;

        private void OnMouseDown() => OnClicked?.Invoke();
    }
}

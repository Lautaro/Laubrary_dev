using Laubrary.Overture;
using UnityEngine;

namespace Laubrary.Overture.Demo
{
    /// <summary>Triggers an OvertureManager state transition when called. Wire to a UI Button.onClick.</summary>
    public class OvertureTransitionButton : MonoBehaviour
    {
        [SerializeField] private OvertureManager manager;
        [SerializeField] private string targetStateId;

        /// <summary>Requests the manager to transition to the configured target state.</summary>
        public void Transition()
        {
            if (manager == null)
            {
                Debug.LogError($"OvertureTransitionButton on '{gameObject.name}': no OvertureManager assigned.", this);
                return;
            }

            manager.TransitionTo(targetStateId);
        }
    }
}

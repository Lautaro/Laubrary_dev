using System;
using System.Threading.Tasks;
using Laubrary.Overture;
using UnityEngine;
using UnityEngine.Events;

namespace Laubrary.Overture.Demo
{
    /// <summary>
    /// An OvertureState that remains active for a fixed duration.
    /// Exposes a normalized Progress value (0–1) and fires OnProgressChanged each frame.
    /// Optionally auto-transitions to another state when the duration elapses.
    /// Use for boot screens, loading screens, or any timed state.
    /// </summary>
    public class OvertureTimedState : OvertureState
    {
        [SerializeField] private float duration = 2f;

        [Space]
        [Tooltip("When enabled, automatically transitions to the next state after the duration.")]
        [SerializeField] private bool autoTransition = false;
        [SerializeField] private string nextStateId;

        [Space]
        [Tooltip("Fires each frame with the current normalized progress (0–1).")]
        public UnityEvent<float> onProgressChanged;

        /// <summary>Normalized progress of the current timed state (0 = start, 1 = complete).</summary>
        public float Progress { get; private set; }

        /// <summary>Ticks progress each frame, fires OnProgressChanged, then optionally transitions.</summary>
        public override async Task OnState()
        {
            Progress = 0f;
            onProgressChanged?.Invoke(Progress);

            float elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                Progress = Mathf.Clamp01(elapsed / duration);
                onProgressChanged?.Invoke(Progress);
                await Task.Yield();
            }

            Progress = 1f;
            onProgressChanged?.Invoke(Progress);

            if (autoTransition)
            {
                if (string.IsNullOrEmpty(nextStateId))
                {
                    Debug.LogWarning($"OvertureTimedState on '{gameObject.name}': autoTransition is enabled but nextStateId is empty.", this);
                    return;
                }

                manager.TransitionTo(nextStateId);
            }
        }
    }
}

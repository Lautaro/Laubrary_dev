using UnityEngine;

namespace Lautaro.Stats.Engine
{
    /// <summary>
    /// Internal component that handles automatic stat registration for classes marked with [AutoRegisterStats].
    /// Do not add this component manually - it's automatically added by the editor processor when needed.
    /// Registers stats in Awake and unregisters them in OnDestroy.
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(-100)]
    public class AutoRegisterStatsBehaviour : MonoBehaviour
    {
        void Awake()
        {
            foreach (var behaviour in GetComponents<MonoBehaviour>())
            {
                if (behaviour.GetType().GetCustomAttributes(typeof(AutoRegisterStatsAttribute), true).Length > 0)
                    behaviour.RegisterAllStats();
            }
        }

        void OnDestroy()
        {
            foreach (var behaviour in GetComponents<MonoBehaviour>())
            {
                if (behaviour.GetType().GetCustomAttributes(typeof(AutoRegisterStatsAttribute), true).Length > 0)
                    behaviour.UnregisterAllStats();
            }
        }
    }
}

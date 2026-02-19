using System.Linq;
using UnityEngine;

namespace Lautaro.Stats.Engine
{
    /// <summary>
    /// Internal component that handles automatic stat registration for classes marked with [AutoRegisterStats].
    /// Do not add this component manually - it's automatically added by Unity when needed.
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(-100)]
    public class AutoRegisterStatsBehaviour : MonoBehaviour
    {
        void Awake()
        {
            var targetBehaviour = GetComponents<MonoBehaviour>()
                .FirstOrDefault(c => c.GetType().GetCustomAttributes(typeof(AutoRegisterStatsAttribute), true).Length > 0);

            if (targetBehaviour != null)
            {
                targetBehaviour.RegisterAllStats();
            }
        }
    }
}

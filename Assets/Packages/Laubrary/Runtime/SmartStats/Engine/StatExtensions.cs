using System.Reflection;
using UnityEngine;

namespace Lautaro.Stats.Engine
{
    /// <summary>
    /// Extension methods for SmartStats auto-registration and management.
    /// Provides convenient methods to register/unregister stats with UpdateEngine.
    /// </summary>
    public static class StatExtensions
    {
        /// <summary>
        /// Automatically discovers and registers all IUpdatable stats on this MonoBehaviour.
        /// Searches all public instance fields for FloatStat, BoolStat, IntStat, and any custom IUpdatable types.
        /// 
        /// Usage in MonoBehaviour:
        /// <code>
        /// void Awake()
        /// {
        ///     this.RegisterAllStats();
        /// }
        /// </code>
        /// 
        /// This is useful when:
        /// - You want explicit control over when stats are registered (vs automatic registration)
        /// - You need stats to register at a specific point in the lifecycle (e.g., after other Awake logic)
        /// - You prefer a single-line solution over manually calling Init() on each stat
        /// 
        /// Note: If SmartStatsConfig.AutoRegisterStats is true (default), stats auto-register on deserialization,
        /// making this method redundant (but harmless to call).
        /// </summary>
        public static void RegisterAllStats(this MonoBehaviour behaviour)
        {
            var fields = behaviour.GetType().GetFields(BindingFlags.Public | BindingFlags.Instance);
            
            foreach (var field in fields)
            {
                var stat = field.GetValue(behaviour) as IUpdatable;
                if (stat != null)
                {
                    UpdateEngine.Register(stat);
                }
            }
        }

        /// <summary>
        /// Registers a single stat with the UpdateEngine.
        /// Convenience method for explicit individual registration.
        /// 
        /// Usage:
        /// <code>
        /// void Awake()
        /// {
        ///     health.RegisterStat();
        ///     stamina.RegisterStat();
        /// }
        /// </code>
        /// 
        /// Note: If SmartStatsConfig.AutoRegisterStats is true (default), this is redundant.
        /// </summary>
        public static void RegisterStat(this IUpdatable stat)
        {
            UpdateEngine.Register(stat);
        }

        /// <summary>
        /// Legacy method: Registers multiple stats with the UpdateEngine.
        /// 
        /// Usage:
        /// <code>
        /// void Awake()
        /// {
        ///     gameObject.RegisterStat(health, mana, stamina);
        /// }
        /// </code>
        /// 
        /// Note: Consider using RegisterAllStats() or individual RegisterStat() calls instead.
        /// </summary>
        public static void RegisterStat(this GameObject gameObject, params IUpdatable[] stats)
        {
            foreach (var stat in stats)
            {
                UpdateEngine.Register(stat);
            }
        }

        /// <summary>
        /// Manually unregisters all IUpdatable stats on this MonoBehaviour.
        /// Useful if you need to stop updates without destroying the GameObject.
        /// 
        /// Note: Cleanup is automatic when the owning GameObject is destroyed (UpdateEngine checks for null).
        /// You only need this for special cases like disabling a character temporarily.
        /// </summary>
        public static void UnregisterAllStats(this MonoBehaviour behaviour)
        {
            var fields = behaviour.GetType().GetFields(BindingFlags.Public | BindingFlags.Instance);
            
            foreach (var field in fields)
            {
                var stat = field.GetValue(behaviour) as IUpdatable;
                if (stat != null)
                {
                    UpdateEngine.UnRegister(stat);
                }
            }
        }

        /// <summary>
        /// Unregisters a single stat from the UpdateEngine.
        /// Use this to stop updates without destroying the GameObject.
        /// 
        /// Note: Cleanup is automatic when the owning GameObject is destroyed.
        /// </summary>
        public static void UnregisterStat(this IUpdatable stat)
        {
            UpdateEngine.UnRegister(stat);
        }

        /// <summary>
        /// Legacy method: Unregisters multiple stats from the UpdateEngine.
        /// Note: Cleanup is automatic when GameObject is destroyed.
        /// </summary>
        public static void UnregisterStat(this GameObject gameObject, params IUpdatable[] stats)
        {
            foreach (var stat in stats)
            {
                UpdateEngine.UnRegister(stat);
            }
        }
    }
}

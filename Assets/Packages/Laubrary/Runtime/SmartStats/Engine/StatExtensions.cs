using System.Reflection;
using UnityEngine;
using Laubrary.LaubraryTicker;

namespace Lautaro.Stats.Engine
{
    /// <summary>
    /// Extension methods for SmartStats registration and management via Ticker.
    /// </summary>
    public static class StatExtensions
    {
        /// <summary>
        /// Automatically discovers and registers all ITickable stats on this MonoBehaviour with Ticker.
        /// Searches all public instance fields for FloatStat, BoolStat, IntStat, and any custom ITickable types.
        /// Call this in Awake and pair it with UnregisterAllStats() in OnDestroy.
        ///
        /// Usage:
        /// <code>
        /// void Awake()   => this.RegisterAllStats();
        /// void OnDestroy() => this.UnregisterAllStats();
        /// </code>
        /// </summary>
        public static void RegisterAllStats(this MonoBehaviour behaviour)
        {
            var fields = behaviour.GetType().GetFields(BindingFlags.Public | BindingFlags.Instance);

            foreach (var field in fields)
            {
                if (field.GetValue(behaviour) is ITickable tickable)
                    Ticker.Register(tickable);
            }
        }

        /// <summary>
        /// Registers a single stat with Ticker.
        /// </summary>
        public static void RegisterStat(this ITickable stat)
        {
            Ticker.Register(stat);
        }

        /// <summary>
        /// Registers multiple stats with Ticker.
        /// </summary>
        public static void RegisterStat(this GameObject gameObject, params ITickable[] stats)
        {
            foreach (var stat in stats)
                Ticker.Register(stat);
        }

        /// <summary>
        /// Unregisters all ITickable stats on this MonoBehaviour from Ticker.
        /// Call this in OnDestroy. Stats are plain C# objects — they will NOT be
        /// null-checked out of Ticker when their parent MonoBehaviour is destroyed,
        /// so explicit unregistration is required to prevent leaking ticks.
        /// </summary>
        public static void UnregisterAllStats(this MonoBehaviour behaviour)
        {
            var fields = behaviour.GetType().GetFields(BindingFlags.Public | BindingFlags.Instance);

            foreach (var field in fields)
            {
                if (field.GetValue(behaviour) is ITickable tickable)
                    Ticker.Unregister(tickable);
            }
        }

        /// <summary>
        /// Unregisters a single stat from Ticker.
        /// </summary>
        public static void UnregisterStat(this ITickable stat)
        {
            Ticker.Unregister(stat);
        }

        /// <summary>
        /// Unregisters multiple stats from Ticker.
        /// </summary>
        public static void UnregisterStat(this GameObject gameObject, params ITickable[] stats)
        {
            foreach (var stat in stats)
                Ticker.Unregister(stat);
        }
    }
}

using UnityEngine;

namespace Lautaro.Stats.Engine
{
    public static class SmartStatsConfig
    {
        private static bool? _autoRegisterStats;

        /// <summary>
        /// Controls whether stats automatically register with Ticker when deserialized.
        /// Default: true
        ///
        /// Set to false if you want full manual control over registration.
        /// You can still use RegisterStat() or RegisterAllStats() extension methods.
        /// </summary>
        public static bool AutoRegisterStats
        {
            get => _autoRegisterStats ?? true;
            set => _autoRegisterStats = value;
        }
    }
}

using Laubrary.LaubraryTicker;

namespace Lautaro.Stats.Engine
{
    /// <summary>
    /// Compatibility subclass of Ticker.Timer.
    /// Use Ticker.Timer directly in new code.
    /// </summary>
    [System.Serializable]
    public class StatModifierTimer : Ticker.Timer
    {
        /// <summary>Alias for RemainingNormalized.</summary>
        public float RemainingPercentage => RemainingNormalized;

        public StatModifierTimer(float duration) : base(duration) { }
    }
}

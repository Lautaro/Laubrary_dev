namespace Lautaro.Stats.Engine
{
    /// <summary>
    /// Defines how a timed modifier's value changes over its duration.
    /// </summary>
    public enum TimerMode
    {
        /// <summary>
        /// No timer - modifier lasts until manually removed (default).
        /// </summary>
        None = 0,
        
        /// <summary>
        /// Constant value for the entire duration, then auto-removes.
        /// Example: +10 health for 5 seconds (stays +10 the whole time).
        /// </summary>
        Timer = 1,
        
        /// <summary>
        /// Value decreases from full to zero over the duration.
        /// Example: +10 health over 10 seconds → +10 at t=0s, +5 at t=5s, 0 at t=10s.
        /// </summary>
        TimerDecreasing = 2,
        
        /// <summary>
        /// Value increases from zero to full over the duration.
        /// Example: +10 health over 10 seconds → 0 at t=0s, +5 at t=5s, +10 at t=10s.
        /// </summary>
        TimerIncreasing = 3
    }
}

using Laubrary.LaubraryTicker;

namespace Lautaro.Stats.Engine
{
    /// <summary>
    /// Compatibility shim. Forwards all calls to Ticker.
    /// Use Ticker.Register() and Ticker.Unregister() directly in new code.
    /// </summary>
    public static class UpdateEngine
    {
        public static void Register(ITickable tickable) => Ticker.Register(tickable);
        public static void UnRegister(ITickable tickable) => Ticker.Unregister(tickable);
    }
}

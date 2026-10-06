using Laubrary.Zounds;

namespace Laubrary.ZTracker.Engine
{
    /// <summary>The physical units accepted by the engine's explicit source-effect parameter mappings.</summary>
    public static class TrackerEffectMetadata
    {
        public static string ParameterUnits(ZoundEffectType type,int parameter)=>TrackerFxCompiler.ParameterUnits(type,parameter);
    }
}

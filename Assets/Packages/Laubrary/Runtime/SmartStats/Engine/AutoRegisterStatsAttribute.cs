using System;
using UnityEngine;

namespace Lautaro.Stats.Engine
{
    /// <summary>
    /// Marks a MonoBehaviour as containing SmartStats that should be auto-registered.
    /// This attribute serves as documentation and can trigger automatic registration in Awake.
    /// 
    /// Usage:
    /// <code>
    /// [AutoRegisterStats]
    /// public class PlayerController : MonoBehaviour
    /// {
    ///     public FloatStat health;
    ///     public BoolStat canJump;
    ///     public IntStat coins;
    /// }
    /// </code>
    /// 
    /// THREE REGISTRATION OPTIONS:
    /// 
    /// 1. AUTOMATIC (Default, Recommended):
    ///    - Stats auto-register when deserialized (ISerializationCallbackReceiver)
    ///    - Enabled by default via SmartStatsConfig.AutoRegisterStats = true
    ///    - No code needed in MonoBehaviour
    ///    - Works immediately when scene loads or prefab instantiates
    ///    - This attribute is just documentation in this mode
    /// 
    /// 2. ATTRIBUTE-BASED:
    ///    - Add [AutoRegisterStats] to your class
    ///    - Stats register in Awake via reflection
    ///    - Good for clarity and explicit documentation
    ///    - Useful when SmartStatsConfig.AutoRegisterStats = false
    /// 
    /// 3. MANUAL:
    ///    - Call this.RegisterAllStats() in Awake/Start
    ///    - Or call health.RegisterStat() for each stat individually
    ///    - Most explicit control over timing
    ///    - No reflection overhead with individual registration
    /// 
    /// Note: All three methods are compatible and safe to use together.
    /// UpdateEngine prevents duplicate registrations automatically.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, Inherited = true, AllowMultiple = false)]
    public class AutoRegisterStatsAttribute : Attribute
    {
    }
}

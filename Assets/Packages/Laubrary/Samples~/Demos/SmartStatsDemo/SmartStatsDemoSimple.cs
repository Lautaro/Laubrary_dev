using UnityEngine;
using Lautaro.Stats;
using Lautaro.Stats.Engine;

namespace Laubrary.SmartStats.Demo
{
    public class SmartStatsDemoSimple : MonoBehaviour
    {
        [Header("Character Stats")]
        public IntStat health = new (100, 0, 100);
        public FloatStat moveSpeed = new (5f, 0f, 20f);

        [Header("Demo Settings")]
        [SerializeField] private float healthBoostDuration = 5f;
        [SerializeField] private float speedBoostDuration = 3f;

        void Awake()
        {
            this.RegisterAllStats();
        }

        void Update()
        {
            if (Input.GetKeyDown(KeyCode.Alpha1))
            {
                AddHealthModifier();
            }
            
            if (Input.GetKeyDown(KeyCode.Alpha2))
            {
                AddSpeedModifier();
            }
            
            if (Input.GetKeyDown(KeyCode.Alpha3))
            {
                AddTimedHealthBoost();
            }
            
            if (Input.GetKeyDown(KeyCode.Alpha4))
            {
                AddTimedSpeedBoost();
            }
        }

        void AddHealthModifier()
        {
            var healthBoost = new IntAdditionModifier(20, "Health Boost");
            health.AddModifier(healthBoost);
            
            Debug.Log($"<color=green>Added Health Modifier!</color> Base: {health.baseValue} → Total: {(int)health}");
        }

        void AddSpeedModifier()
        {
            var speedBoost = new FloatMultiplierModifier(0.5f, "Speed Boost (+50%)");
            moveSpeed.AddModifier(speedBoost);
            
            Debug.Log($"<color=cyan>Added Speed Modifier!</color> Base: {moveSpeed.baseValue} → Total: {(float)moveSpeed}");
        }

        void AddTimedHealthBoost()
        {
            var tempHealthBoost = new IntAdditionModifier(30, "Temporary Health");
            health.AddTimedModifier(tempHealthBoost, healthBoostDuration);
            
            Debug.Log($"<color=yellow>Added Timed Health Boost!</color> +30 for {healthBoostDuration}s. Total: {(int)health}");
        }

        void AddTimedSpeedBoost()
        {
            var tempSpeedBoost = new FloatMultiplierModifier(1.0f, "Haste (+100%)");
            moveSpeed.AddTimedModifier(tempSpeedBoost, speedBoostDuration);
            
            Debug.Log($"<color=magenta>Added Timed Speed Boost!</color> +100% for {speedBoostDuration}s. Total: {(float)moveSpeed}");
        }

        void OnDestroy()
        {
            this.UnregisterAllStats();
        }

        /// <summary>Flags all modifiers on all stats for removal on the next Update cycle.</summary>
        public void ClearAllModifiers()
        {
            foreach (var mod in health.intStatMods) mod.Remove();
            foreach (var mod in moveSpeed.floatStatMods) mod.Remove();
            Debug.Log("<color=white>Simple: Cleared all modifiers</color>");
        }
    }
}

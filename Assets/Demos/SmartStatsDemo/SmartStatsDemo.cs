using UnityEngine;
using Lautaro.Stats;
using Lautaro.Stats.Engine;

namespace Laubrary.SmartStats.Demo
{
    public class SmartStatsDemo : MonoBehaviour
{
    [Header("Character Stats")]
    public FloatStat health = new FloatStat(100, 0, 100);
    public FloatStat damage = new FloatStat(10, 0, 1000);
    public FloatStat moveSpeed = new FloatStat(5f, 0f, 20f);
    
    [Header("Resource Stats")]
    public IntStat gold = new IntStat(50, 0, 999999);
    public IntStat experience = new IntStat(0, 0, int.MaxValue);
    
    [Header("State Stats")]
    public BoolStat isInvincible = new BoolStat(false);
    public BoolStat canAttack = new BoolStat(true);

    [Header("Demo Controls")]
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
        var healthBoost = new FloatAdditionModifier(20f, "Health Boost");
        health.AddModifier(healthBoost);
        
        Debug.Log($"<color=green>Added Health Modifier!</color> Base: {health.baseValue} → Total: {(float)health}");
    }

    void AddSpeedModifier()
    {
        var speedBoost = new FloatMultiplierModifier(0.5f, "Speed Boost (+50%)");
        moveSpeed.AddModifier(speedBoost);
        
        Debug.Log($"<color=cyan>Added Speed Modifier!</color> Base: {moveSpeed.baseValue} → Total: {(float)moveSpeed}");
    }

    void AddTimedHealthBoost()
    {
        var tempHealthBoost = new FloatAdditionModifier(30f, "Temporary Health");
        health.AddTimedModifier(tempHealthBoost, healthBoostDuration);
        
        Debug.Log($"<color=yellow>Added Timed Health Boost!</color> +30 for {healthBoostDuration}s. Total: {(float)health}");
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
    }
}

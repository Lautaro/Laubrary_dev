using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using Lautaro.Stats;
using Lautaro.Stats.Engine;

namespace Laubrary.SmartStats.Demo
{
    /// <summary>
    /// Demonstrates: FloatStatConditionalModifier, FloatStatSwitchModifier,
    /// priority ordering, and TimerMode.TimerDecreasing / TimerIncreasing.
    ///
    /// Controls:
    ///   5 - Toggle conditional modifier (Rage Bonus on/off)
    ///   6 - Toggle switch modifier backing condition
    ///   7 - Add priority-ordered modifiers (+100 at P:-10, *1.5 at P:10)
    ///   8 - Add TimerDecreasing modifier (+150 fading out over timerDuration)
    ///   9 - Add TimerIncreasing modifier (+150 building up over timerDuration)
    /// </summary>
    public class SmartStatsDemoAdvancedFloat : MonoBehaviour
    {
        [Header("Stat")]
        public FloatStat power = new FloatStat(100f, 0f, 1000f);

        [Header("Settings")]
        [SerializeField] private float timerDuration = 6f;

        private bool isRageActive = false;
        private bool isSwitchActive = false;

        private FloatStatConditionalModifier conditionalMod;
        private FloatStatSwitchModifier switchMod;
        private readonly List<FloatStatModifier> dynamicMods = new List<FloatStatModifier>();

        void Awake()
        {
            this.RegisterAllStats();

            conditionalMod = new FloatStatConditionalModifier(50f, () => isRageActive, "Rage Bonus");
            conditionalMod.isPaused = true; // sync with isRageActive = false before first Update()
            power.AddModifier(conditionalMod);

            switchMod = new FloatStatSwitchModifier(30f, () => isSwitchActive, "Switch Bonus");
            power.AddModifier(switchMod);
        }

        void Update()
        {
            var kb = Keyboard.current;
            if (kb == null) return;

            if (kb.digit5Key.wasPressedThisFrame) ToggleRageMode();
            if (kb.digit6Key.wasPressedThisFrame) ToggleSwitchModifier();
            if (kb.digit7Key.wasPressedThisFrame) AddPriorityDemo();
            if (kb.digit8Key.wasPressedThisFrame) AddTimerDecreasing();
            if (kb.digit9Key.wasPressedThisFrame) AddTimerIncreasing();
        }

        /// <summary>Flips isRageActive and immediately syncs isPaused so the inspector updates this frame.</summary>
        void ToggleRageMode()
        {
            isRageActive = !isRageActive;
            conditionalMod.isPaused = !isRageActive;
            Debug.Log($"<color=red>Rage Mode {(isRageActive ? "ON" : "OFF")}</color> | Power: {(float)power:F1}");
        }

        /// <summary>Flips isSwitchActive — the switch modifier evaluates this on every stat calculation.</summary>
        void ToggleSwitchModifier()
        {
            isSwitchActive = !isSwitchActive;
            Debug.Log($"<color=yellow>Switch Bonus {(isSwitchActive ? "Active (+30)" : "Inactive (0)")}</color> | Power: {(float)power:F1}");
        }

        /// <summary>
        /// Adds +100 at priority -10 (fires before the multiplier) and *1.5 at priority 10 (fires after).
        /// Result: (100 + 100) * 1.5 = 300, not (100 * 1.5) + 100 = 250.
        /// </summary>
        void AddPriorityDemo()
        {
            var earlyBonus = new FloatAdditionModifier(100f, "Early Bonus");
            power.AddModifier(earlyBonus, priority: -10);
            dynamicMods.Add(earlyBonus);

            var lateMultiplier = new FloatMultiplierModifier(0.5f, "Late Multiplier");
            power.AddModifier(lateMultiplier, priority: 10);
            dynamicMods.Add(lateMultiplier);

            Debug.Log($"<color=cyan>Priority Demo</color> — +100 at P:-10, x1.5 at P:10 | Power: {(float)power:F1}");
        }

        /// <summary>+150 that fades from full to 0 over timerDuration seconds.</summary>
        void AddTimerDecreasing()
        {
            var mod = new FloatAdditionModifier(150f, "Fading Boost");
            power.AddTimedModifier(mod, timerDuration, TimerMode.TimerDecreasing);
            dynamicMods.Add(mod);
            Debug.Log($"<color=magenta>TimerDecreasing</color> — +150 fading to 0 over {timerDuration}s | Power: {(float)power:F1}");
        }

        /// <summary>+150 that builds from 0 to full over timerDuration seconds.</summary>
        void AddTimerIncreasing()
        {
            var mod = new FloatAdditionModifier(150f, "Building Boost");
            power.AddTimedModifier(mod, timerDuration, TimerMode.TimerIncreasing);
            dynamicMods.Add(mod);
            Debug.Log($"<color=green>TimerIncreasing</color> — 0 building to +150 over {timerDuration}s | Power: {(float)power:F1}");
        }

        /// <summary>Removes all dynamically added modifiers and resets toggle states.</summary>
        public void ClearAllModifiers()
        {
            foreach (var mod in dynamicMods)
                mod.Remove();
            dynamicMods.Clear();

            isRageActive = false;
            isSwitchActive = false;
            conditionalMod.isPaused = true;

            Debug.Log("<color=white>AdvancedFloat: Cleared all modifiers</color>");
        }

        void OnDestroy()
        {
            this.UnregisterAllStats();
        }
    }
}


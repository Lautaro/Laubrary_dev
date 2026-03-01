using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Lautaro.Stats.Engine;
using Laubrary.LaubraryTicker;

namespace Lautaro.Stats
{
    /// <summary>
    /// A serializable numeric stat that supports temporary modifiers with a priority-based sequential calculation system.
    /// 
    /// Key Concepts:
    /// 
    /// BASE VALUE:
    /// - The permanent, unmodified stat value (e.g., character's base health)
    /// - Only changed via direct assignment: stat.baseValue = 100
    /// - Modifiers NEVER change the base value
    /// 
    /// MODIFIERS:
    /// - Temporary effects that adjust the calculated value (buffs, debuffs, equipment bonuses)
    /// - Applied sequentially in order based on priority level and insertion order
    /// 
    /// PRIORITY SYSTEM (Sequential Model):
    /// - Modifiers are sorted by priority level (lower priority applies first)
    /// - Within same priority level: modifiers apply in order they were added
    /// - Each modifier operates on the result of the previous modifier
    /// - Multipliers can be merged using MergeModifier() to combine their percentages additively
    /// 
    /// Example Calculation:
    /// Base: 100
    /// 
    /// Priority 0:
    ///   +20 (equipment) → 120
    ///   +30 (buff) → 150
    ///   *0.5 (50% talent) → 225
    /// 
    /// Priority 10:
    ///   +50 (shield) → 275
    /// 
    /// Final: 275 (clamped between minValue and maxValue)
    /// 
    /// Merged Multipliers:
    ///   multiplierA.MergeModifier(multiplierB); // Combines their percentages additively
    ///   // If A=50%, B=25%, result is A=75%, B is marked as merged
    /// 
    /// Usage Examples:
    /// 
    /// Simple case (all default priority 0):
    ///   stat.AddModifier(new FloatAdditionModifier(20, "Ring"));
    ///   stat.AddModifier(new FloatMultiplierModifier(0.5f, "Talent"));
    /// 
    /// Custom priority:
    ///   stat.AddModifier(new FloatAdditionModifier(50, "Shield"), priority: 10);
    /// 
    /// Helper methods:
    ///   stat.AddFirst(modifier);  // Applies before everything
    ///   stat.AddLast(modifier);   // Applies after everything
    ///   stat.AddBefore(newMod, existingMod);  // Insert relative to another modifier
    /// </summary>
    [Serializable]
    public class FloatStat : ITickable, ISerializationCallbackReceiver
    {
        private const int DEFAULT_PRIORITY = 0;
        private const int PRIORITY_SPACING = 10;

        /// <summary>
        /// The base value of this stat. Only modified via direct assignment (e.g., levelUp, stat reset).
        /// Modifiers do NOT change this value.
        /// </summary>
        [SerializeField] public float baseValue;

        /// <summary>
        /// Minimum allowed value after all modifiers are applied. Clamps the final result.
        /// </summary>
        [SerializeField] public float minValue = float.MinValue;

        /// <summary>
        /// Maximum allowed value after all modifiers are applied. Clamps the final result.
        /// </summary>
        [SerializeField] public float maxValue = float.MaxValue;

        /// <summary>
        /// Cached total value after applying all modifiers and clamping. Updated each frame.
        /// </summary>
        [SerializeField] public float totalValue;

        /// <summary>
        /// All modifiers currently affecting this stat (includes paused modifiers).
        /// </summary>
        [SerializeReference] public List<FloatStatModifier> floatStatMods = new List<FloatStatModifier>();

        /// <summary>
        /// Cached list of active (non-paused) modifiers sorted by priority and insertion order.
        /// </summary>
        [SerializeReference] public List<FloatStatModifier> activeFloatStatMods = new List<FloatStatModifier>();

#if UNITY_EDITOR
        /// <summary>
        /// Debug data for inspector: stores intermediate calculation results for each modifier.
        /// Index corresponds to modifier index in activeFloatStatMods.
        /// </summary>
        [System.NonSerialized]
        public List<float> debugIntermediateValues = new List<float>();
#endif

        private bool isRegistered = false;

        public FloatStat()
        {
            totalValue = baseValue;
        }

        public FloatStat(float baseValue, float minValue = float.MinValue, float maxValue = float.MaxValue)
        {
            this.baseValue = baseValue;
            this.minValue = minValue;
            this.maxValue = maxValue;
            this.totalValue = baseValue;
        }

        /// <summary>
        /// Registers this stat with Ticker for automatic updates.
        /// If using [AutoRegisterStats] or RegisterAllStats(), this is called automatically.
        /// For runtime-created stats, call this manually after construction.
        /// </summary>
        public void Init()
        {
            if (!isRegistered)
            {
                Ticker.Register(this);
                isRegistered = true;
            }
        }

        /// <summary>
        /// Unregisters this stat from Ticker. Call this in OnDestroy.
        /// FloatStat is a plain C# object — it will NOT be null-checked out of Ticker
        /// when its parent MonoBehaviour is destroyed, so explicit cleanup is required.
        /// </summary>
        public void Cleanup()
        {
            if (isRegistered)
            {
                Ticker.Unregister(this);
                isRegistered = false;
            }
        }

        void ISerializationCallbackReceiver.OnAfterDeserialize()
        {
            // Safe: field assignment only. Do NOT call Ticker.Register() or any
            // other Unity API here — OnAfterDeserialize can run off the main thread
            // or mid-serialization. Registration is handled by [AutoRegisterStats],
            // RegisterAllStats(), or manual Init() calls.
            totalValue = baseValue;
        }

        void ISerializationCallbackReceiver.OnBeforeSerialize()
        {
        }

#if UNITY_EDITOR
        void OnValidate()
        {
            if (floatStatMods == null) return;
            RefreshActiveModifiers();
        }
#endif

        /// <summary>
        /// Implicit conversion to float returns the current calculated value.
        /// Allows using FloatStat directly in arithmetic: float damage = player.attack * 2;
        /// </summary>
        public static implicit operator float(FloatStat floatStat)
        {
            return floatStat.CalculateCurrentValue();
        }

        /// <summary>
        /// Calculates the current stat value using sequential priority-based application.
        /// 
        /// Process:
        /// 1. Start with baseValue
        /// 2. Sort modifiers by priority level, then by insertion order
        /// 3. Apply each modifier sequentially to the running total
        /// 4. Clamp final result to [minValue, maxValue]
        /// 
        /// In editor, also populates debugIntermediateValues for inspector visualization.
        /// </summary>
        float CalculateCurrentValue()
        {
#if UNITY_EDITOR
            debugIntermediateValues.Clear();
            debugIntermediateValues.Add(baseValue);
#endif

            float runningValue = baseValue;

            for (int i = 0; i < activeFloatStatMods.Count; i++)
            {
                var mod = activeFloatStatMods[i];
                
                if (mod is FloatMultiplierModifier multiplier && multiplier.IsMergedIntoAnother)
                {
#if UNITY_EDITOR
                    debugIntermediateValues.Add(runningValue);
#endif
                    continue;
                }

                runningValue = mod.ApplyTo(runningValue);

#if UNITY_EDITOR
                debugIntermediateValues.Add(runningValue);
#endif
            }

            totalValue = Mathf.Clamp(runningValue, minValue, maxValue);
            return totalValue;
        }

        /// <summary>
        /// Adds a modifier with default priority (0).
        /// Most common method - use this unless you need custom priority.
        /// </summary>
        /// <param name="modifier">The modifier to add.</param>
        public void AddModifier(FloatStatModifier modifier)
        {
            modifier.priority ??= DEFAULT_PRIORITY;
            floatStatMods.Add(modifier);
            RefreshActiveModifiers();
        }

        /// <summary>
        /// Adds a modifier with explicit priority.
        /// Lower priority values apply first.
        /// </summary>
        /// <param name="modifier">The modifier to add.</param>
        /// <param name="priority">Priority level (lower applies first).</param>
        public void AddModifier(FloatStatModifier modifier, int priority)
        {
            modifier.priority = priority;
            floatStatMods.Add(modifier);
            RefreshActiveModifiers();
        }

        /// <summary>
        /// Adds a timed modifier that will auto-remove after the specified duration.
        /// </summary>
        /// <param name="modifier">The modifier to add.</param>
        /// <param name="duration">How long the modifier lasts in seconds.</param>
        /// <param name="mode">How the timer affects the modifier's value (Timer = constant, TimerDecreasing = decreases to 0, TimerIncreasing = increases from 0).</param>
        public void AddTimedModifier(FloatStatModifier modifier, float duration, TimerMode mode = TimerMode.Timer)
        {
            modifier.EnableTimer(duration, mode);
            AddModifier(modifier);
        }

        /// <summary>
        /// Adds a timed modifier with explicit priority.
        /// </summary>
        /// <param name="modifier">The modifier to add.</param>
        /// <param name="duration">How long the modifier lasts in seconds.</param>
        /// <param name="priority">Priority level (lower applies first).</param>
        /// <param name="mode">How the timer affects the modifier's value.</param>
        public void AddTimedModifier(FloatStatModifier modifier, float duration, int priority, TimerMode mode = TimerMode.Timer)
        {
            modifier.EnableTimer(duration, mode);
            AddModifier(modifier, priority);
        }

        /// <summary>
        /// Adds a modifier that will apply BEFORE all existing modifiers.
        /// Sets priority to (current lowest - spacing).
        /// </summary>
        /// <param name="modifier">The modifier to add.</param>
        /// <param name="leaveSpace">If true, uses PRIORITY_SPACING (10) gap. If false, uses gap of 1.</param>
        public void AddFirst(FloatStatModifier modifier, bool leaveSpace = false)
        {
            int lowestPriority = floatStatMods.Any()
                ? floatStatMods.Min(m => m.priority ?? DEFAULT_PRIORITY)
                : DEFAULT_PRIORITY;

            modifier.priority = lowestPriority - (leaveSpace ? PRIORITY_SPACING : 1);
            floatStatMods.Add(modifier);
            RefreshActiveModifiers();
        }

        /// <summary>
        /// Adds a modifier that will apply AFTER all existing modifiers.
        /// Sets priority to (current highest + spacing).
        /// </summary>
        /// <param name="modifier">The modifier to add.</param>
        /// <param name="leaveSpace">If true, uses PRIORITY_SPACING (10) gap. If false, uses gap of 1.</param>
        public void AddLast(FloatStatModifier modifier, bool leaveSpace = false)
        {
            int highestPriority = floatStatMods.Any()
                ? floatStatMods.Max(m => m.priority ?? DEFAULT_PRIORITY)
                : DEFAULT_PRIORITY;

            modifier.priority = highestPriority + (leaveSpace ? PRIORITY_SPACING : 1);
            floatStatMods.Add(modifier);
            RefreshActiveModifiers();
        }

        /// <summary>
        /// Adds a modifier immediately BEFORE an existing modifier in priority order.
        /// If leaveSpace=true or no room exists, shifts target and later modifiers to make space.
        /// </summary>
        /// <param name="newModifier">The modifier to add.</param>
        /// <param name="targetModifier">The existing modifier to insert before.</param>
        /// <param name="leaveSpace">If true, always creates PRIORITY_SPACING gap by shifting modifiers.</param>
        public void AddBefore(FloatStatModifier newModifier, FloatStatModifier targetModifier, bool leaveSpace = false)
        {
            if (!floatStatMods.Contains(targetModifier))
                throw new ArgumentException("Target modifier not found in stat");

            int targetPriority = targetModifier.priority ?? DEFAULT_PRIORITY;

            int previousPriority = floatStatMods
                .Where(m => (m.priority ?? DEFAULT_PRIORITY) < targetPriority)
                .Select(m => m.priority ?? DEFAULT_PRIORITY)
                .DefaultIfEmpty(targetPriority - PRIORITY_SPACING)
                .Max();

            if (leaveSpace || previousPriority >= targetPriority - 1)
            {
                foreach (var mod in floatStatMods.Where(m => (m.priority ?? DEFAULT_PRIORITY) >= targetPriority))
                {
                    mod.priority = (mod.priority ?? DEFAULT_PRIORITY) + PRIORITY_SPACING;
                }
                newModifier.priority = targetPriority;
            }
            else
            {
                newModifier.priority = (previousPriority + targetPriority) / 2;
            }

            floatStatMods.Add(newModifier);
            RefreshActiveModifiers();
        }

        /// <summary>
        /// Adds a modifier immediately AFTER an existing modifier in priority order.
        /// If leaveSpace=true or no room exists, shifts later modifiers to make space.
        /// </summary>
        /// <param name="newModifier">The modifier to add.</param>
        /// <param name="targetModifier">The existing modifier to insert after.</param>
        /// <param name="leaveSpace">If true, always creates PRIORITY_SPACING gap by shifting modifiers.</param>
        public void AddAfter(FloatStatModifier newModifier, FloatStatModifier targetModifier, bool leaveSpace = false)
        {
            if (!floatStatMods.Contains(targetModifier))
                throw new ArgumentException("Target modifier not found in stat");

            int targetPriority = targetModifier.priority ?? DEFAULT_PRIORITY;

            int nextPriority = floatStatMods
                .Where(m => (m.priority ?? DEFAULT_PRIORITY) > targetPriority)
                .Select(m => m.priority ?? DEFAULT_PRIORITY)
                .DefaultIfEmpty(targetPriority + PRIORITY_SPACING)
                .Min();

            if (leaveSpace || nextPriority <= targetPriority + 1)
            {
                foreach (var mod in floatStatMods.Where(m => (m.priority ?? DEFAULT_PRIORITY) > targetPriority))
                {
                    mod.priority = (mod.priority ?? DEFAULT_PRIORITY) + PRIORITY_SPACING;
                }
                newModifier.priority = targetPriority + PRIORITY_SPACING;
            }
            else
            {
                newModifier.priority = (targetPriority + nextPriority) / 2;
            }

            floatStatMods.Add(newModifier);
            RefreshActiveModifiers();
        }

        /// <summary>
        /// Legacy method name for backwards compatibility.
        /// Use AddModifier() instead.
        /// </summary>
        public void AddFloatStatModifier(FloatStatModifier modifier)
        {
            AddModifier(modifier);
        }

        /// <summary>
        /// Called every frame by Ticker. Removes expired modifiers and recalculates the stat value.
        /// </summary>
        public void Tick()
        {
            var remove = floatStatMods.Where(mod => mod.FlaggedForRemoval).ToList();
            foreach (var removeItem in remove)
                floatStatMods.Remove(removeItem);

            RefreshActiveModifiers();
        }

        /// <summary>
        /// Refreshes the active modifiers list and recalculates the current value.
        /// Called automatically when modifiers are added/removed or during Update().
        /// </summary>
        void RefreshActiveModifiers()
        {
            activeFloatStatMods = floatStatMods
                .Where(mod => mod.isPaused == false)
                .OrderBy(m => m.priority ?? DEFAULT_PRIORITY)
                .ToList();
            
            CalculateCurrentValue();
        }
    }
}
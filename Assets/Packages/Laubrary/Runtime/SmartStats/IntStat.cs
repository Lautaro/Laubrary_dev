using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Lautaro.Stats.Engine;
using Laubrary.LaubraryTicker;

namespace Lautaro.Stats
{
    /// <summary>
    /// A serializable integer stat that supports temporary modifiers with a priority-based sequential calculation system.
    /// Works identically to FloatStat but ensures all intermediate and final calculations are rounded to integers.
    /// 
    /// See FloatStat documentation for detailed information about the modifier system.
    /// </summary>
    [Serializable]
    public class IntStat : ITickable, ISerializationCallbackReceiver
    {
        private const int DEFAULT_PRIORITY = 0;
        private const int PRIORITY_SPACING = 10;

        [SerializeField] public int baseValue;
        [SerializeField] public int minValue = int.MinValue;
        [SerializeField] public int maxValue = int.MaxValue;
        [SerializeField] public int totalValue;

        [SerializeReference] public List<IntStatModifier> intStatMods = new List<IntStatModifier>();
        [SerializeReference] public List<IntStatModifier> activeIntStatMods = new List<IntStatModifier>();

#if UNITY_EDITOR
        [System.NonSerialized]
        public List<int> debugIntermediateValues = new List<int>();
#endif

        private bool isRegistered = false;

        public IntStat()
        {
            totalValue = baseValue;
        }

        public IntStat(int baseValue, int minValue = int.MinValue, int maxValue = int.MaxValue)
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
        /// IntStat is a plain C# object — it will NOT be null-checked out of Ticker
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
            if (intStatMods == null) return;
            RefreshActiveModifiers();
        }
#endif

        public static implicit operator int(IntStat intStat)
        {
            return intStat.CalculateCurrentValue();
        }

        int CalculateCurrentValue()
        {
#if UNITY_EDITOR
            debugIntermediateValues.Clear();
            debugIntermediateValues.Add(baseValue);
#endif

            int runningValue = baseValue;

            for (int i = 0; i < activeIntStatMods.Count; i++)
            {
                var mod = activeIntStatMods[i];
                
                if (mod is IntMultiplierModifier multiplier && multiplier.IsMergedIntoAnother)
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

        public void AddModifier(IntStatModifier modifier)
        {
            modifier.priority ??= DEFAULT_PRIORITY;
            intStatMods.Add(modifier);
            RefreshActiveModifiers();
        }

        public void AddModifier(IntStatModifier modifier, int priority)
        {
            modifier.priority = priority;
            intStatMods.Add(modifier);
            RefreshActiveModifiers();
        }

        public void AddTimedModifier(IntStatModifier modifier, float duration, TimerMode mode = TimerMode.Timer)
        {
            modifier.EnableTimer(duration, mode);
            AddModifier(modifier);
        }

        public void AddTimedModifier(IntStatModifier modifier, float duration, int priority, TimerMode mode = TimerMode.Timer)
        {
            modifier.EnableTimer(duration, mode);
            AddModifier(modifier, priority);
        }

        public void AddFirst(IntStatModifier modifier, bool leaveSpace = false)
        {
            int lowestPriority = intStatMods.Any()
                ? intStatMods.Min(m => m.priority ?? DEFAULT_PRIORITY)
                : DEFAULT_PRIORITY;

            modifier.priority = lowestPriority - (leaveSpace ? PRIORITY_SPACING : 1);
            intStatMods.Add(modifier);
            RefreshActiveModifiers();
        }

        public void AddLast(IntStatModifier modifier, bool leaveSpace = false)
        {
            int highestPriority = intStatMods.Any()
                ? intStatMods.Max(m => m.priority ?? DEFAULT_PRIORITY)
                : DEFAULT_PRIORITY;

            modifier.priority = highestPriority + (leaveSpace ? PRIORITY_SPACING : 1);
            intStatMods.Add(modifier);
            RefreshActiveModifiers();
        }

        public void AddBefore(IntStatModifier newModifier, IntStatModifier targetModifier, bool leaveSpace = false)
        {
            if (!intStatMods.Contains(targetModifier))
                throw new ArgumentException("Target modifier not found in stat");

            int targetPriority = targetModifier.priority ?? DEFAULT_PRIORITY;

            int previousPriority = intStatMods
                .Where(m => (m.priority ?? DEFAULT_PRIORITY) < targetPriority)
                .Select(m => m.priority ?? DEFAULT_PRIORITY)
                .DefaultIfEmpty(targetPriority - PRIORITY_SPACING)
                .Max();

            if (leaveSpace || previousPriority >= targetPriority - 1)
            {
                foreach (var mod in intStatMods.Where(m => (m.priority ?? DEFAULT_PRIORITY) >= targetPriority))
                {
                    mod.priority = (mod.priority ?? DEFAULT_PRIORITY) + PRIORITY_SPACING;
                }
                newModifier.priority = targetPriority;
            }
            else
            {
                newModifier.priority = (previousPriority + targetPriority) / 2;
            }

            intStatMods.Add(newModifier);
            RefreshActiveModifiers();
        }

        public void AddAfter(IntStatModifier newModifier, IntStatModifier targetModifier, bool leaveSpace = false)
        {
            if (!intStatMods.Contains(targetModifier))
                throw new ArgumentException("Target modifier not found in stat");

            int targetPriority = targetModifier.priority ?? DEFAULT_PRIORITY;

            int nextPriority = intStatMods
                .Where(m => (m.priority ?? DEFAULT_PRIORITY) > targetPriority)
                .Select(m => m.priority ?? DEFAULT_PRIORITY)
                .DefaultIfEmpty(targetPriority + PRIORITY_SPACING)
                .Min();

            if (leaveSpace || nextPriority <= targetPriority + 1)
            {
                foreach (var mod in intStatMods.Where(m => (m.priority ?? DEFAULT_PRIORITY) > targetPriority))
                {
                    mod.priority = (mod.priority ?? DEFAULT_PRIORITY) + PRIORITY_SPACING;
                }
                newModifier.priority = targetPriority + PRIORITY_SPACING;
            }
            else
            {
                newModifier.priority = (targetPriority + nextPriority) / 2;
            }

            intStatMods.Add(newModifier);
            RefreshActiveModifiers();
        }

        /// <summary>
        /// Called every frame by Ticker. Removes expired modifiers and recalculates the stat value.
        /// </summary>
        public void Tick()
        {
            var remove = intStatMods.Where(mod => mod.FlaggedForRemoval).ToList();
            foreach (var removeItem in remove)
                intStatMods.Remove(removeItem);

            RefreshActiveModifiers();
        }

        void RefreshActiveModifiers()
        {
            activeIntStatMods = intStatMods
                .Where(mod => mod.isPaused == false)
                .OrderBy(m => m.priority ?? DEFAULT_PRIORITY)
                .ToList();
            
            CalculateCurrentValue();
        }
    }
}

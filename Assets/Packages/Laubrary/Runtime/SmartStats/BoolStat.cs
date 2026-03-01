using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Lautaro.Stats.Engine;
using Laubrary.LaubraryTicker;

namespace Lautaro.Stats
{
    [Serializable]
    public class BoolStat : ITickable, ISerializationCallbackReceiver
    {
        [SerializeField] public bool baseValue;
        [SerializeReference] public List<BoolStatModifierBase> boolStatMods = new List<BoolStatModifierBase>();

        private bool isRegistered = false;

        public BoolStat()
        {
        }

        public BoolStat(bool baseValue)
        {
            this.baseValue = baseValue;
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
        /// BoolStat is a plain C# object — it will NOT be null-checked out of Ticker
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
            // Safe: intentionally empty. Do NOT call Ticker.Register() or any
            // other Unity API here — OnAfterDeserialize can run off the main thread
            // or mid-serialization. Registration is handled by [AutoRegisterStats],
            // RegisterAllStats(), or manual Init() calls.
        }

        void ISerializationCallbackReceiver.OnBeforeSerialize()
        {
        }

        public static implicit operator bool(BoolStat boolStat)
        {
            return boolStat.CalculateCurrentValue();
        }

        /// <summary>
        /// Calculates the current boolean value based on active modifiers.
        /// Only the modifier with the LOWEST priority number is applied.
        /// If multiple modifiers share the lowest priority, the first added wins.
        /// If no modifiers exist, returns baseValue.
        /// </summary>
        public bool CalculateCurrentValue()
        {
            var priorityModifer = boolStatMods
                .Where(m => m.isPaused == false)
                .OrderBy(m => m.priority)
                .FirstOrDefault();

            if (priorityModifer == null)
                return baseValue;
            else
            {
                switch (priorityModifer.modifierType)
                {
                    case BoolStatModifierBase.BoolStatModifierType.Flip:
                        return !baseValue;
                    case BoolStatModifierBase.BoolStatModifierType.AlwaysTrue:
                        return true;
                    case BoolStatModifierBase.BoolStatModifierType.AlwaysFalse:
                        return false;
                    default:
                        return !baseValue;
                }
            }
        }

        public void AddModifier(BoolStatModifierBase modifier)
        {
            boolStatMods.Add(modifier);
        }

        /// <summary>
        /// Called every frame by Ticker. Removes expired modifiers.
        /// </summary>
        public void Tick()
        {
            var remove = boolStatMods.Where(mod => mod.FlaggedForRemoval).ToList();
            foreach (var removeItem in remove)
                boolStatMods.Remove(removeItem);
        }
    }
}
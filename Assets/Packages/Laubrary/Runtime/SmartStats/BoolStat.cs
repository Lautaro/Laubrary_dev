using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Lautaro.Stats.Engine;

namespace Lautaro.Stats
{
    [Serializable]
    public class BoolStat : IUpdatable, ISerializationCallbackReceiver
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

        public void Init()
        {
            if (!isRegistered)
            {
                UpdateEngine.Register(this);
                isRegistered = true;
            }
        }

        public void Cleanup()
        {
            if (isRegistered)
            {
                UpdateEngine.UnRegister(this);
                isRegistered = false;
            }
        }

        void ISerializationCallbackReceiver.OnAfterDeserialize()
        {
            // Cannot use Application.isPlaying during deserialization
            // Auto-registration is handled by attribute-based system or manual registration
            // This method is kept for potential future use with safe checks
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

        public void Update()
        {
            foreach (var mod in boolStatMods)
            {
                if (mod is IUpdatable)
                {
                    (mod as IUpdatable).Update();
                }
            }

            var remove = boolStatMods.Where(mod => mod.FlaggedForRemoval == true).ToList();
            foreach (var removeItem in remove)
            {
                boolStatMods.Remove(removeItem);
            }
        }
    }
}
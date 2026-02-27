using System;
using Lautaro.Stats.Engine;

namespace Lautaro.Stats
{
    [Serializable]
    public abstract class BoolStatModifierBase : StatModifierBase
    {
        public int priority = 0;

        public BoolStatModifierType modifierType;

        public BoolStatModifierBase(string description) : base(description)
        {
        }

        public enum BoolStatModifierType
        {
            Flip,
            AlwaysTrue,
            AlwaysFalse
        }
    }
}
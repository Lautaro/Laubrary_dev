using System;

namespace Lautaro.Stats
{
    [Serializable]
    public class BoolStatModifier : BoolStatModifierBase
    {
        public BoolStatModifier(string description, BoolStatModifierType modifierType = BoolStatModifierType.Flip) : base(description)
        {
            this.modifierType = modifierType;
        }
    }
}

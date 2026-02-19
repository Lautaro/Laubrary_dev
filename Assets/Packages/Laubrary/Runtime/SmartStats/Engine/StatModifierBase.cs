using System;

namespace Lautaro.Stats.Engine
{
    [Serializable]
    public abstract class StatModifierBase<T>
    {   
        public string description;
        public bool IsPaused;
        protected bool remove = false;
        public bool FlaggedForRemoval => remove;

        public StatModifierBase(string description = "")
        {   
            this.description = description;
        }

        public void Remove()
        {
            remove = true;
            if (this is IUpdatable)
            {
                UpdateEngine.UnRegister((IUpdatable)this);
            }
        }
    }
}

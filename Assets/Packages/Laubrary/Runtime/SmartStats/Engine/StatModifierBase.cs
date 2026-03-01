using System;
using Laubrary.LaubraryTicker;

namespace Lautaro.Stats.Engine
{
    /// <summary>
    /// Non-generic base for all stat modifiers (Float, Int, Bool).
    /// Provides shared identity fields, removal logic, and pause state.
    /// </summary>
    [Serializable]
    public abstract class StatModifierBase
    {
        public string description;
        public bool isPaused;
        protected bool remove = false;
        public bool FlaggedForRemoval => remove;

        public StatModifierBase(string description = "")
        {
            this.description = description;
        }

        /// <summary>
        /// Flags this modifier for removal and unregisters it from Ticker if it implements ITickable.
        /// </summary>
        public void Remove()
        {
            remove = true;
            if (this is ITickable tickable)
                Ticker.Unregister(tickable);
        }
    }
}

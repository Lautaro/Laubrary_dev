using System;

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
        /// Flags this modifier for removal and unregisters it from UpdateEngine if it implements IUpdatable.
        /// </summary>
        public void Remove()
        {
            remove = true;
            if (this is IUpdatable updatable)
            {
                UpdateEngine.UnRegister(updatable);
            }
        }
    }
}

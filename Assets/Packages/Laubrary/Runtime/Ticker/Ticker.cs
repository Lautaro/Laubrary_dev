using System;
using Laubrary.Cookbook;
namespace Laubrary.LaubraryTicker
{
    /// <summary>
    /// Creates a MonoSingleton GameObject that can update non MonoBehaviour types in the update loop. Make sure to unregister from update when destroyed or the custom type wont be garbage collected.
    /// </summary>
    public class Ticker : MonoSingleton<Ticker>
    {
        /// <summary>Custom types subscribing to the OnUpdate must implement a method to unsubscribe when their GameObject is destroyed.The reference will otherwise keep it from being garbage collected</summary>
        public static Action OnUpdate
        {
            get => I._onUpdate;
            set => I._onUpdate = value;
        }
        private Action _onUpdate;

        void Update()
        {
            if (_onUpdate != null)
                _onUpdate();
        }
    }
}

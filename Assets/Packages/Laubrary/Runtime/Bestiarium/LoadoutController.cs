using System.Collections.Generic;
using UnityEngine;

namespace Laubrary.Bestiarium
{
    /// <summary>
    /// Runtime holder for a character's loadout of <see cref="IActivatable"/>s (weapons + abilities). Tracks a
    /// per-slot cooldown so the TRIGGER — a Daemon behaviour for an enemy, or input for the player — just calls
    /// <see cref="TryActivate"/> and the gate is handled here. Attached by <c>Bestiary.SpawnCharacter</c> when the
    /// def has a loadout. Cooldown state lives here (not in the activatable), so activatables stay stateless data
    /// that can be shared across every character spawned from one def.
    /// </summary>
    public class LoadoutController : MonoBehaviour
    {
        readonly List<IActivatable> _items = new List<IActivatable>();
        float[] _cooldownLeft;

        public int Count => _items.Count;
        public IActivatable Get(int i) => (i >= 0 && i < _items.Count) ? _items[i] : null;

        /// Install the loadout (from the CharacterDef). Nulls are skipped.
        public void Set(IReadOnlyList<IActivatable> items)
        {
            _items.Clear();
            if (items != null)
                foreach (var it in items) if (it != null) _items.Add(it);
            _cooldownLeft = new float[_items.Count];
        }

        void Update()
        {
            if (_cooldownLeft == null) return;
            float dt = Time.deltaTime;
            for (int i = 0; i < _cooldownLeft.Length; i++)
                if (_cooldownLeft[i] > 0f) _cooldownLeft[i] -= dt;
        }

        /// True if slot <paramref name="i"/> exists and is off cooldown.
        public bool Ready(int i) => i >= 0 && i < _items.Count && (_cooldownLeft == null || _cooldownLeft[i] <= 0f);

        /// Activate slot <paramref name="i"/> toward <paramref name="aimDir"/> if ready; returns true if it fired.
        public bool TryActivate(int i, Vector2 aimDir)
        {
            if (!Ready(i)) return false;
            _items[i].Activate(gameObject, aimDir);
            if (_cooldownLeft != null && i < _cooldownLeft.Length) _cooldownLeft[i] = Mathf.Max(0f, _items[i].Cooldown);
            return true;
        }
    }
}

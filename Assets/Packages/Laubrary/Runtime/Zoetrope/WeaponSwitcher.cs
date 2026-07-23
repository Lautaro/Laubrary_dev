using System.Collections.Generic;
using UnityEngine;
using Laubrary.Combat2D;

namespace Laubrary.Zoetrope
{
    /// <summary>
    /// Toggles between pre-built weapon slot GameObjects (each already carrying its own
    /// <see cref="Combat2D.ProjectileWeapon"/>/<see cref="WeaponMuzzleCue"/>/muzzle/projectile template) —
    /// deactivating the old slot and activating the new one is enough on its own: <see cref="WeaponMuzzleCue"/>'s
    /// OnEnable/OnDisable already handles registering/unregistering its cue as a side effect of SetActive, so
    /// this class only owns which slot is active, not any cue bookkeeping.
    /// </summary>
    [AddComponentMenu("Laubrary/Zoetrope/Weapon Switcher")]
    public class WeaponSwitcher : MonoBehaviour
    {
        public List<GameObject> slots = new List<GameObject>();
        public int ActiveIndex { get; private set; } = -1;

        /// The currently active slot's ProjectileWeapon, or null if nothing's active — lets a trigger
        /// mechanism that only knows "fire whatever this character has equipped" (e.g. MirageSubject's
        /// auto-fire-on-MetaLayer preview) reach the real, currently-switched weapon without needing its own
        /// separate, redundant weapon reference.
        public ProjectileWeapon ActiveWeapon =>
            ActiveIndex >= 0 && ActiveIndex < slots.Count && slots[ActiveIndex] != null
                ? slots[ActiveIndex].GetComponent<ProjectileWeapon>() : null;

        public void SwitchTo(int index)
        {
            if (index < 0 || index >= slots.Count || index == ActiveIndex) return;
            if (ActiveIndex >= 0 && ActiveIndex < slots.Count && slots[ActiveIndex] != null)
                slots[ActiveIndex].SetActive(false);
            if (slots[index] != null) slots[index].SetActive(true);
            ActiveIndex = index;
        }
    }
}

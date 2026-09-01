using UnityEngine;
using Laubrary.Zoetrope;

namespace Laubrary.Demos.ProtoGuyDemo
{
    /// <summary>
    /// Demo-only: presses 1 and 2 (and Tab to cycle) pick which of the character's equipped weapon slots is live.
    ///
    /// This is game code deciding WHEN to switch, which is the correct side of the line — Laubrary already owns
    /// WHAT a weapon slot is and how switching works (<see cref="WeaponSwitcher"/>, built by
    /// <c>ZoeSpawner.EquipWeaponSlots</c> from the Zoe's own weapons list). It lives here rather than in the
    /// package because the shared input asset deliberately has no weapon-switch action: making switching a
    /// Laubrary-level capability would mean adding an action to every project's control scheme, which is the
    /// owner's call, not a demo's.
    ///
    /// Finds the switcher by search rather than by reference because the character is spawned at play time and
    /// therefore cannot be wired in the saved scene.
    /// </summary>
    public class DemoWeaponSwitch : MonoBehaviour
    {
        WeaponSwitcher _switcher;

        void Update()
        {
            if (_switcher == null)
            {
                _switcher = FindAnyObjectByType<WeaponSwitcher>();
                if (_switcher == null) return;                 // character not spawned yet
            }

            if (Input.GetKeyDown(KeyCode.Alpha1)) _switcher.SwitchTo(0);
            else if (Input.GetKeyDown(KeyCode.Alpha2)) _switcher.SwitchTo(1);
            else if (Input.GetKeyDown(KeyCode.Tab) && _switcher.slots.Count > 0)
                _switcher.SwitchTo((_switcher.ActiveIndex + 1) % _switcher.slots.Count);
        }
    }
}

using UnityEngine;

namespace Laubrary.Zoetrope
{
    /// <summary>
    /// Optional capability a spawned character's view MAY provide (discovered via GetComponent, same pattern
    /// as <see cref="IMotionPoseHost"/>/<see cref="IAnimatedView"/>) — lets a caller find a named composite
    /// body part's transform without core Zoetrope depending on the Launimator bridge that actually builds
    /// composite bodies. A single-part Zoe host has no component implementing this, so a lookup by name
    /// silently finds nothing — the caller (<see cref="ZoeSpawner.EquipWeaponSlots"/>) falls back to the
    /// character's own root transform in that case, same as before this interface existed.
    /// </summary>
    public interface IPartLookup
    {
        /// The named part's transform, or null if this host isn't composite or has no part by that name.
        Transform FindPartTransform(string partName);
    }
}

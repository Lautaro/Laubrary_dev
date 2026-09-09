using UnityEngine;
using Laubrary.Combat2D;

namespace Laubrary.Zoetrope
{
    /// <summary>
    /// The <see cref="IAimSpec"/> that shoots along the muzzle PAINTED on the animation: whichever of
    /// <see cref="MuzzleVectorTracker"/> (a Vector MetaLayer — origin and direction both authored) or
    /// <see cref="MuzzleTracker"/> (a Point MetaLayer — origin only) matches what is actually painted under the
    /// slot's muzzle layer. Both follow the drawing frame by frame, so the shot leaves the barrel exactly where
    /// and how the artist drew it — at the cost of the spread being only as even as the painting is.
    ///
    /// <para>This is what a <see cref="Zoe"/> with no aiming technique set uses, so it is the unchanged
    /// long-standing behaviour rather than a new default. It exists as a named module so all techniques are
    /// peers on one shelf and a character can name this one explicitly (e.g. to sit alongside an alternative in
    /// a demo's character switch).</para>
    /// </summary>
    [System.Serializable]
    public class PaintedMuzzleAimSpec : IAimSpec
    {
        public void Attach(GameObject weaponSlot, Transform muzzle, Combatant owner, string muzzleLayerId)
        {
            // Nothing painted/reachable leaves the muzzle exactly where the weapon's own muzzleOffset put it —
            // the same graceful "optional capability, quiet no-op" rule every other consumer of these
            // interfaces follows.
            if (string.IsNullOrEmpty(muzzleLayerId)) return;

            // GetComponentInParent mirrors WeaponMuzzleCue's own ICueSink lookup — for a composite Zoe this
            // only reaches the right PART's view when the slot is actually parented under that part (see
            // ZoeSpawner.EquipWeaponSlots' attachToPartName handling); a single-body Zoe's root IS the view.
            var animatedView = weaponSlot.GetComponentInParent<IAnimatedView>();
            if (animatedView == null) return;

            // Ask the DATA which kind of layer this is, never the live view. This runs inside Start(), before
            // any clip has been resolved — sampling the screen here meant asking "does the clip that happened
            // to load first have something painted on its current frame", which for a composite Zoe is the
            // version's first animation and is virtually never the one carrying the muzzle. The answer was no,
            // so NO tracker was attached, permanently, and the muzzle stayed pinned to the weapon's fixed
            // muzzleOffset no matter what was painted.
            switch (animatedView.GetMetaLayerKind(muzzleLayerId))
            {
                case MetaLayerKind.Vector:
                {
                    var t = weaponSlot.GetComponent<MuzzleVectorTracker>();
                    if (t == null) t = weaponSlot.AddComponent<MuzzleVectorTracker>();
                    t.Configure(muzzle, muzzleLayerId);
                    break;
                }
                case MetaLayerKind.Point:
                {
                    var t = weaponSlot.GetComponent<MuzzleTracker>();
                    if (t == null) t = weaponSlot.AddComponent<MuzzleTracker>();
                    t.Configure(muzzle, muzzleLayerId);
                    break;
                }
            }
        }
    }
}

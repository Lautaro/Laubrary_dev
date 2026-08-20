using UnityEngine;
using Laubrary.Combat2D;

namespace Laubrary.Zoetrope
{
    /// <summary>
    /// The Vector-layer sibling of <see cref="MuzzleTracker"/>: keeps a weapon's muzzle transform on the point
    /// a VECTOR MetaLayer authors AND exposes the direction it authors, so a weapon can fire along an
    /// animation-drawn trajectory instead of (or in addition to) <c>Combatant.aimDirection</c>.
    ///
    /// <para>This is the WEAPON-side half of the vector-muzzle feature — it calls Launimator's generic
    /// <see cref="IAnimatedView.TryGetMetaVectorNearest"/> itself and assigns the meaning "origin = spawn
    /// position, direction = fire direction, length = ignored" (per the user's own spec for this feature).
    /// Launimator/<see cref="MetaLayer"/> have no idea a "muzzle" exists; this component is where that meaning
    /// gets attached, same as <see cref="MuzzleTracker"/> already does for Point layers.</para>
    ///
    /// <para>Like <see cref="MuzzleTracker"/>, this only tracks — it does not itself trigger a shot. WHEN to
    /// fire is the game's business; a caller reads <see cref="CurrentDirection"/> (guarded by
    /// <see cref="HasDirection"/>) and passes it to <see cref="Combat2D.ProjectileWeapon.TryFireFromVector"/>
    /// when it decides to shoot.</para>
    /// </summary>
    [AddComponentMenu("")]
    public class MuzzleVectorTracker : MonoBehaviour, IVectorAimSource
    {
        [Tooltip("The transform a weapon spawns its shots from — moved to the authored origin each frame.")]
        public Transform muzzle;
        [Tooltip("Which Vector-mode MetaLayer on the body's laumination marks the muzzle origin + fire direction.")]
        public string layerId = "Muzzle";

        IAnimatedView _view;
        Vector3 _restLocalPos;
        bool _hasRest;

        /// The last resolved WORLD-space fire direction, valid only when <see cref="HasDirection"/> is true.
        public Vector2 CurrentDirection { get; private set; } = Vector2.up;
        /// Whether a vector was actually resolved this frame (current or nearest-fallback). False means the
        /// layer has no authored data at all right now — a caller should fall back to its own aim source.
        public bool HasDirection { get; private set; }

        /// Wire it up right after AddComponent — OnEnable has already run by the time a caller could assign
        /// fields the normal way (the same trap MuzzleTracker.Configure exists for).
        public void Configure(Transform muzzle, string layerId)
        {
            this.muzzle = muzzle;
            this.layerId = string.IsNullOrEmpty(layerId) ? "Muzzle" : layerId;
            CaptureRest();
        }

        void OnEnable() => CaptureRest();

        // Where the weapon's own offset put the muzzle. Kept so an unauthored laumination RESTORES it rather
        // than leaving the muzzle stuck wherever the last resolved frame happened to be.
        void CaptureRest()
        {
            if (muzzle == null || _hasRest) return;
            _restLocalPos = muzzle.localPosition;
            _hasRest = true;
        }

        void LateUpdate()
        {
            if (muzzle == null) return;
            // Resolved lazily and re-resolved while missing: the view lives on the spawned body, which the
            // spawner may add AFTER the weapon slots (and a Mirage rebuild replaces it wholesale) — same
            // reasoning as MuzzleTracker's own lazy lookup.
            if (_view == null) _view = GetComponentInParent<IAnimatedView>();
            if (_view == null) return;

            if (_view.TryGetMetaVectorNearest(layerId, out Vector2 worldOrigin, out Vector2 worldDirection, out _))
            {
                muzzle.position = worldOrigin;
                CurrentDirection = worldDirection;
                HasDirection = true;
            }
            else
            {
                if (_hasRest) muzzle.localPosition = _restLocalPos;
                HasDirection = false;
            }
        }
    }
}

using UnityEngine;
using Laubrary.Combat2D;

namespace Laubrary.Zoetrope
{
    /// <summary>
    /// An <see cref="IAimSpec"/> that aims FREELY — any angle, not a ring of authored ones — by taking the
    /// drawn facing nearest the aim and rotating that art the rest of the way. The shot leaves along the exact
    /// aim, and its origin is still the muzzle PAINTED on that facing's frame, carried around by the same
    /// rotation so it stays on the drawn barrel.
    ///
    /// <para>The third of three techniques on one shelf. <see cref="PaintedMuzzleAimSpec"/> can only ever fire
    /// along the directions someone painted; <see cref="EvenDirectionsAimSpec"/> spreads a fixed ring
    /// perfectly evenly but is still a ring, and gives up the painted origin to do it. This one keeps the
    /// painted origin AND has no ring at all — the cost is that a sprite drawn for one direction is being
    /// shown turned, which is a lie the eye can catch (see <see cref="maxRotationDeg"/>).</para>
    ///
    /// <para>It never derives its own direction table: the part reports which facing it resolved
    /// (<see cref="IResolvedFacing"/>, provided by the directional animator that picked the frame), and the
    /// residual is the difference. Re-bake the sheet with 32 directions instead of 16 and this follows with no
    /// edit. A character whose weapon part has no directional animator gets no rotation at all — it degrades
    /// to "painted muzzle origin, continuous fire direction" rather than guessing.</para>
    /// </summary>
    [System.Serializable]
    public class FreeAimRotateAimSpec : IAimSpec
    {
        [Tooltip("Quantise the aim to this many evenly-spaced directions before anything else — 0 (the " +
                 "default) leaves it fully continuous. Use it to try a finer ring than the artwork has " +
                 "(e.g. 32 or 64) without going free.")]
        [Range(0, 128)] public int directionCount = 0;

        [Tooltip("Largest angle the art may be turned by, in degrees. 0 (the default) means no limit, which " +
                 "for a nearest-facing pick is at most half the gap between facings anyway. Lower it to keep " +
                 "the drawing honest — but past the limit the shot still leaves along the true aim, so the " +
                 "barrel and the bullet visibly disagree.")]
        [Min(0f)] public float maxRotationDeg = 0f;

        [Tooltip("Snap the turn to multiples of this many degrees. 0 (the default) turns freely. A few " +
                 "coarse steps stop a slowly-turning pixel sprite shimmering as its pixels re-sample every " +
                 "frame, at the cost of the barrel lagging the aim by up to half a step.")]
        [Min(0f)] public float rotationSnapDeg = 0f;

        [Tooltip("The point the art turns around, relative to the part's own origin. Zero (the default) " +
                 "turns it around that origin — where the part is joined to the body, so the seam stays " +
                 "shut. Move it to swing the drawing around the shoulder or the hips instead. Ignored for a " +
                 "one-piece character, whose part IS the character and must not be moved.")]
        public Vector2 pivotOffset = Vector2.zero;

        public void Attach(GameObject weaponSlot, Transform muzzle, Combatant owner, string muzzleLayerId)
        {
            var aimer = weaponSlot.GetComponent<FreeAimRotateAimer>();
            if (aimer == null) aimer = weaponSlot.AddComponent<FreeAimRotateAimer>();
            aimer.Configure(muzzle, owner, muzzleLayerId, directionCount, maxRotationDeg, rotationSnapDeg, pivotOffset);
        }
    }

    /// <summary>
    /// The live half of <see cref="FreeAimRotateAimSpec"/>: each frame it turns the part its weapon hangs off
    /// to cover the gap between the facing that part drew and where the character is actually aiming, then
    /// reads the painted muzzle back out of the (now turned) drawing as the shot's origin.
    ///
    /// <para>Runs LATE on purpose — after the animator that writes the part's facing rotation, and after the
    /// composite body re-joins its parts — so the turn is applied to a settled pose instead of racing it. The
    /// rotation it writes is ABSOLUTE (the facing's own rotation plus the residual), never a delta on top of
    /// whatever it finds, so a frame where nothing else wrote cannot make the turn accumulate.</para>
    /// </summary>
    [AddComponentMenu("")]
    [DefaultExecutionOrder(100)]
    public class FreeAimRotateAimer : MonoBehaviour, IVectorAimSource
    {
        [Tooltip("The transform the weapon spawns its shots from — moved onto the painted muzzle each frame.")]
        public Transform muzzle;
        [Tooltip("The firing character, whose raw aim is used as-is.")]
        public Combatant owner;
        [Tooltip("Which Vector MetaLayer carries the painted muzzle. Empty leaves the muzzle at the weapon's own offset.")]
        public string muzzleLayerId = "";
        [Tooltip("Quantise the aim to this many directions first. 0 = fully continuous.")]
        [Range(0, 128)] public int directionCount = 0;
        [Tooltip("Largest angle the art may be turned by. 0 = no limit.")]
        [Min(0f)] public float maxRotationDeg = 0f;
        [Tooltip("Snap the turn to multiples of this many degrees. 0 = free.")]
        [Min(0f)] public float rotationSnapDeg = 0f;
        [Tooltip("The point the art turns around, relative to the part's own origin.")]
        public Vector2 pivotOffset = Vector2.zero;

        IResolvedFacing _facing;
        Transform _part;
        IAnimatedView _view;
        Vector3 _restLocalPos;
        bool _hasRest;

        /// The last resolved WORLD-space fire direction — the character's own aim, quantised only if
        /// <see cref="directionCount"/> asks for it. Never the direction the art ended up drawn at.
        public Vector2 CurrentDirection { get; private set; } = Vector2.up;
        /// True once an aim has been resolved at all. The last answer is held while aim is momentarily zero,
        /// so this only reads false before the first resolve.
        public bool HasDirection { get; private set; }

        /// How far the art is currently turned from the facing it was drawn for, in degrees. Read by probes
        /// and by anything checking how far this technique is stretching the artwork right now.
        public float ResidualDeg { get; private set; }

        /// Wire it up right after AddComponent — OnEnable has already run by the time a caller could assign
        /// fields the normal way (the same trap MuzzleVectorTracker.Configure exists for).
        public void Configure(Transform muzzle, Combatant owner, string muzzleLayerId, int directionCount,
                              float maxRotationDeg, float rotationSnapDeg, Vector2 pivotOffset)
        {
            this.muzzle = muzzle;
            this.owner = owner;
            this.muzzleLayerId = muzzleLayerId ?? "";
            this.directionCount = Mathf.Clamp(directionCount, 0, 128);
            this.maxRotationDeg = Mathf.Max(0f, maxRotationDeg);
            this.rotationSnapDeg = Mathf.Max(0f, rotationSnapDeg);
            this.pivotOffset = pivotOffset;
            CaptureRest();
        }

        void OnEnable() => CaptureRest();

        // Where the weapon's own offset put the muzzle. Kept so a frame with nothing painted RESTORES it
        // rather than leaving the muzzle stuck wherever the last resolved frame happened to be.
        void CaptureRest()
        {
            if (muzzle == null || _hasRest) return;
            _restLocalPos = muzzle.localPosition;
            _hasRest = true;
        }

        void LateUpdate()
        {
            // Lazy, re-fetched while missing — a slot is built before the character's own components are all
            // in place, the same reasoning MuzzleVectorTracker's own lazy view lookup uses.
            if (owner == null) owner = GetComponentInParent<Combatant>();

            Vector2 raw = owner != null ? owner.aimDirection : Vector2.zero;
            if (raw.sqrMagnitude > 1e-6f)
            {
                CurrentDirection = Quantise(raw);
                HasDirection = true;
            }
            else if (!HasDirection) return;   // nothing aimed yet, and no previous answer to hold

            // Screen convention throughout: 0° = up, increasing clockwise — the same one the facing is
            // reported in, and the one a part's transform rotation is the negation of.
            float aimDeg = Mathf.Atan2(CurrentDirection.x, CurrentDirection.y) * Mathf.Rad2Deg;

            if (_facing == null)
            {
                // The part this weapon hangs off IS the part to turn — a slot is parented under the composite
                // part it was authored for (ZoeSpawner.EquipWeaponSlots), so the torso a gun is held by is
                // reached by looking up, with no part name to pass around and get wrong.
                _facing = GetComponentInParent<IResolvedFacing>();
                _part = (_facing as Component) != null ? ((Component)_facing).transform : null;
            }

            if (_facing != null && _part != null && _facing.TryGetResolvedFacing(out float facingDeg, out float baseRotDeg))
            {
                float residual = Mathf.DeltaAngle(facingDeg, aimDeg);
                if (rotationSnapDeg > 0f) residual = Mathf.Round(residual / rotationSnapDeg) * rotationSnapDeg;
                if (maxRotationDeg > 0f) residual = Mathf.Clamp(residual, -maxRotationDeg, maxRotationDeg);
                ResidualDeg = residual;

                _part.rotation = Quaternion.Euler(0f, 0f, -(baseRotDeg + residual));

                // Turning around a point other than the part's own origin means moving the origin to
                // compensate. Skipped entirely for a one-piece character, where the part transform IS the
                // character and moving it would drive the whole body around the screen.
                if (pivotOffset != Vector2.zero && owner != null && _part != owner.transform)
                {
                    Vector2 turned = (Vector2)(Quaternion.Euler(0f, 0f, -residual) * (Vector3)pivotOffset);
                    _part.position += (Vector3)(pivotOffset - turned);
                }
            }

            if (muzzle == null) return;

            // Read AFTER the turn: the painted origin is computed through the part's own transform, so it
            // comes back already carried around by the rotation just written — which is the whole point.
            if (!string.IsNullOrEmpty(muzzleLayerId))
            {
                if (_view == null) _view = GetComponentInParent<IAnimatedView>();
                if (_view != null && _view.TryGetMetaVectorNearest(muzzleLayerId, out Vector2 worldOrigin, out _, out _))
                {
                    muzzle.position = worldOrigin;
                    return;
                }
            }
            if (_hasRest) muzzle.localPosition = _restLocalPos;
        }

        Vector2 Quantise(Vector2 raw)
        {
            if (directionCount < 2) return raw.normalized;
            float step = 360f / directionCount;
            // Measured from East, matching EvenDirectionsAimer, so a count that is a multiple of 4 contains
            // N, S, E and W exactly.
            float rad = Mathf.Round(Mathf.Atan2(raw.y, raw.x) * Mathf.Rad2Deg / step) * step * Mathf.Deg2Rad;
            return new Vector2(Mathf.Cos(rad), Mathf.Sin(rad));
        }
    }
}

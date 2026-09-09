using UnityEngine;
using Laubrary.Combat2D;

namespace Laubrary.Zoetrope
{
    /// <summary>
    /// An <see cref="IAimSpec"/> that fires along a fixed ring of EVENLY-SPACED directions instead of a
    /// direction painted on the animation: the character's raw aim is snapped to the nearest of
    /// <see cref="directionCount"/> directions spread equally around the circle. At the default 16 that is one
    /// every 22.5°, and because the ring is measured from East it contains N, S, E and W exactly with two steps
    /// between each — the same bins a 16-facing body already picks its sprite from, so a shot leaves along the
    /// facing the character is drawn in.
    ///
    /// <para>The alternative to <see cref="PaintedMuzzleAimSpec"/>, whose direction is only as evenly spread as
    /// the vectors someone painted frame by frame. This one is pure geometry, so the spread is exact by
    /// construction and needs no authoring at all — the trade is that the ORIGIN is geometric too and has to be
    /// tuned once per character with the three dials below, rather than following the drawn barrel per frame.</para>
    ///
    /// <para>The origin model is what a top-down character holding a gun actually does on screen: the muzzle
    /// swings around a point on the body (<see cref="originCentre"/>), further sideways than it does up and
    /// down (<see cref="originReach"/>, because the view foreshortens depth), and sits off to one shoulder when
    /// the character faces towards or away from the camera (<see cref="sideOffset"/>). Tuned that way it lands
    /// on the drawn muzzle at North, South, East and West and stays close in between. The defaults are sized
    /// for a roughly 2-unit-tall character and are a starting point, not a fit for any particular one.</para>
    /// </summary>
    [System.Serializable]
    public class EvenDirectionsAimSpec : IAimSpec
    {
        [Tooltip("How many directions the ring is divided into. 16 is one every 22.5°. Keep it a multiple of " +
                 "4 so North, South, East and West stay exactly on the ring.")]
        [Range(4, 64)] public int directionCount = 16;

        [Tooltip("The point on the body the muzzle swings around, relative to the character's own position — " +
                 "roughly where the gun is held.")]
        public Vector2 originCentre = new Vector2(0f, 1f);

        [Tooltip("How far the muzzle swings out from that centre: X when firing East or West, Y when firing " +
                 "North or South. The two differ because a top-down view foreshortens depth — the barrel " +
                 "covers less screen height pointing away than it covers width pointing sideways.")]
        public Vector2 originReach = new Vector2(1.2f, 0.9f);

        [Tooltip("How far the muzzle sits to the shooter's own right, for a gun held at one shoulder. Only " +
                 "shifts the origin when firing towards or away from the camera; firing East or West the " +
                 "barrel already lies along that axis and the hold moves the muzzle nowhere.")]
        public float sideOffset = 0.25f;

        public void Attach(GameObject weaponSlot, Transform muzzle, Combatant owner, string muzzleLayerId)
        {
            // muzzleLayerId is deliberately unused: the whole point of this technique is that the origin is
            // geometric (the three dials above), not read off whatever was painted.
            var aimer = weaponSlot.GetComponent<EvenDirectionsAimer>();
            if (aimer == null) aimer = weaponSlot.AddComponent<EvenDirectionsAimer>();
            aimer.Configure(muzzle, owner, directionCount, originCentre, originReach, sideOffset);
        }
    }

    /// <summary>
    /// The live half of <see cref="EvenDirectionsAimSpec"/> — snaps the owner's aim onto the even ring each
    /// frame and parks the weapon's muzzle transform at the matching origin, so a
    /// <see cref="ProjectileWeapon"/> on the same GameObject picks both up with no call-site change (it already
    /// prefers a sibling <see cref="IVectorAimSource"/>, and already spawns from <c>muzzle</c>).
    /// </summary>
    [AddComponentMenu("")]
    public class EvenDirectionsAimer : MonoBehaviour, IVectorAimSource
    {
        [Tooltip("The transform the weapon spawns its shots from — moved to the computed origin each frame.")]
        public Transform muzzle;
        [Tooltip("The firing character, whose raw aim gets snapped onto the ring.")]
        public Combatant owner;
        [Tooltip("How many directions the ring is divided into.")]
        [Range(4, 64)] public int directionCount = 16;
        [Tooltip("The point on the body the muzzle swings around, relative to the character.")]
        public Vector2 originCentre = new Vector2(0f, 1f);
        [Tooltip("How far the muzzle swings out from that centre — X firing East/West, Y firing North/South.")]
        public Vector2 originReach = new Vector2(1.2f, 0.9f);
        [Tooltip("How far the muzzle sits to the shooter's own right when facing towards or away from the camera.")]
        public float sideOffset = 0.25f;

        public Vector2 CurrentDirection { get; private set; } = Vector2.up;
        /// True once a direction has been resolved at all. The ring always has an answer — the last snapped
        /// direction is held while aim is momentarily zero — so this only reads false before the first resolve.
        public bool HasDirection { get; private set; }

        /// Wire it up right after AddComponent — OnEnable has already run by the time a caller could assign
        /// fields the normal way (the same trap MuzzleTracker/MuzzleVectorTracker's Configure exist for).
        public void Configure(Transform muzzle, Combatant owner, int directionCount,
                              Vector2 originCentre, Vector2 originReach, float sideOffset)
        {
            this.muzzle = muzzle;
            this.owner = owner;
            this.directionCount = Mathf.Clamp(directionCount, 4, 64);
            this.originCentre = originCentre;
            this.originReach = originReach;
            this.sideOffset = sideOffset;
            Resolve();
        }

        void OnEnable() => Resolve();

        // LateUpdate, matching MuzzleVectorTracker: the aim this reads is published during Update by whatever
        // drives the character, and the muzzle must land after that and before anything renders.
        void LateUpdate() => Resolve();

        void Resolve()
        {
            // Lazy, re-fetched while missing — a slot is built before the character's own components are all in
            // place, the same reasoning MuzzleVectorTracker's own lazy view lookup uses.
            if (owner == null) owner = GetComponentInParent<Combatant>();

            Vector2 raw = owner != null ? owner.aimDirection : Vector2.zero;
            float rad;
            if (raw.sqrMagnitude > 1e-6f)
            {
                int n = Mathf.Max(4, directionCount);
                float step = 360f / n;
                // Measured from East (+X) so the ring contains 0/90/180/270 — E, N, W, S — exactly whenever n
                // is a multiple of 4. That is the "start with N, S, W, E, then add steps between them" set.
                rad = Mathf.Round(Mathf.Atan2(raw.y, raw.x) * Mathf.Rad2Deg / step) * step * Mathf.Deg2Rad;
                CurrentDirection = new Vector2(Mathf.Cos(rad), Mathf.Sin(rad));
                HasDirection = true;
            }
            else if (HasDirection)
            {
                rad = Mathf.Atan2(CurrentDirection.y, CurrentDirection.x);   // hold the last answer
            }
            else return;   // nothing to aim along yet, and no previous answer to hold

            if (muzzle == null) return;
            // The pivot is the CHARACTER, not the weapon slot's own parent: a slot hangs off an animated body
            // part whose transform moves with the drawing, and an origin measured from that would inherit
            // exactly the per-frame wobble this technique exists to replace.
            Transform pivot = owner != null ? owner.transform : muzzle.parent;
            if (pivot == null) return;

            float c = Mathf.Cos(rad), s = Mathf.Sin(rad);
            muzzle.position = (Vector2)pivot.position + originCentre
                            + new Vector2(originReach.x * c + sideOffset * s, originReach.y * s);
        }
    }
}

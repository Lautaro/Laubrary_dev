using System.Collections.Generic;
using Laubrary.Zoetrope;

namespace Laubrary.ZoetropeLaunimator
{
    /// <summary>
    /// What a <see cref="Zoe"/> can be ASKED to do — aim, move, fire, play named clips — derived from the
    /// authoring it already has. Nothing here is authored twice: aiming comes from the direction sets its
    /// MotionPose resolves against, moving from whether it has a Moving rule at all, firing from its equipped
    /// weapon slots, clips from its version's animations.
    ///
    /// This is the contract that lets ONE Zoe be driven by any of three things without caring which: the
    /// player's input, an AI brain, or Mirage's manual controls. A driver asks "what can this thing do", gets
    /// a list, and offers exactly that — so a preview panel for an ENEMY is generated the same way as for the
    /// player, and testing an enemy by hand needs no special case. A capability the Zoe can't do simply isn't
    /// in the list, so no UI is generated for it and no driver tries to invoke it.
    ///
    /// Deliberately DERIVED, not a new authored asset: a capability that could disagree with the thing it
    /// describes is worse than no capability list at all. Re-derive after an edit rather than caching.
    /// </summary>
    public readonly struct ZoeCapabilities
    {
        /// The Zoe this describes. Null means nothing was resolvable.
        public readonly Zoe Zoe;

        /// Can it be pointed somewhere — i.e. does any part resolve a direction? False for a Zoe whose art has
        /// exactly one facing (a hovering disc), in which case a direction control is meaningless.
        public readonly bool CanAim;

        /// The distinct directions its art actually supports, in degrees (0 = up, clockwise). For ProtoGuy's
        /// 16-way sheets this is the 16 compass angles; for a 3-member mirrored walk set it is however many
        /// the resolver can actually reach. A control should offer THESE, not an arbitrary dial, so the user
        /// can only ask for poses that exist.
        public readonly IReadOnlyList<float> AimAngles;

        /// Does it have a Moving rule — i.e. is "walk" a thing it can be asked to do at all?
        public readonly bool CanMove;

        /// Does it have at least one weapon slot equipped?
        public readonly bool CanFire;

        /// Display names of its weapon slots, in slot order — what a "which weapon" control offers.
        public readonly IReadOnlyList<string> WeaponNames;

        /// Named clips its view can play (hit/death reactions, custom events). What a "play this" control offers.
        public readonly IReadOnlyList<string> ClipNames;

        public ZoeCapabilities(Zoe zoe, bool canAim, IReadOnlyList<float> aimAngles, bool canMove,
                               bool canFire, IReadOnlyList<string> weaponNames, IReadOnlyList<string> clipNames)
        {
            Zoe = zoe; CanAim = canAim; AimAngles = aimAngles; CanMove = canMove;
            CanFire = canFire; WeaponNames = weaponNames; ClipNames = clipNames;
        }

        static readonly float[] NoAngles = new float[0];
        static readonly string[] NoNames = new string[0];

        public static ZoeCapabilities None => new ZoeCapabilities(null, false, NoAngles, false, false, NoNames, NoNames);

        /// <summary>Read a Zoe's capabilities off its existing authoring. Safe on a partially-configured Zoe —
        /// every capability independently reports false rather than throwing, so a half-built character still
        /// previews with whatever controls it has earned so far (which is the state it spends most of its life
        /// in while being built).</summary>
        public static ZoeCapabilities Derive(Zoe zoe)
        {
            if (zoe == null) return None;

            // Aim + move come from the same place: the pose catalog already answers "which distinct poses can
            // this Zoe actually show", per bucket, by probing the real resolver. Reuse it rather than
            // re-deriving from sets — it already handles mirroring, per-rule channel overrides and composites.
            var poses = MotionPoseCatalog.Derive(zoe);
            var angles = new List<float>();
            bool canMove = false;
            if (poses != null)
            {
                foreach (var p in poses)
                {
                    if (p.bucket == MotionCondition.Moving) canMove = true;
                    if (!angles.Contains(p.angleDeg)) angles.Add(p.angleDeg);
                }
            }
            angles.Sort();

            var weaponNames = new List<string>();
            if (zoe.weapons != null)
                foreach (var slot in zoe.weapons)
                    if (slot != null && slot.weapon != null)
                        weaponNames.Add(string.IsNullOrEmpty(slot.weapon.displayName)
                            ? slot.weapon.name : slot.weapon.displayName);

            var clipNames = new List<string>();
            if (zoe.view is CompositeLauminaryView composite && composite.parts != null)
            {
                foreach (var part in composite.parts)
                    CollectClipNames(part?.view as ZonedLauminaryView, clipNames);
            }
            else CollectClipNames(zoe.view as ZonedLauminaryView, clipNames);

            // More than one distinct reachable direction is what makes an aim control meaningful; a single
            // direction means the art has one facing and there is nothing to steer.
            bool canAim = angles.Count > 1;

            return new ZoeCapabilities(zoe, canAim, angles, canMove, weaponNames.Count > 0, weaponNames, clipNames);
        }

        static void CollectClipNames(ZonedLauminaryView view, List<string> into)
        {
            var version = view != null ? view.version : null;
            if (version == null || version.animations == null) return;
            foreach (var a in version.animations)
                if (a != null && !string.IsNullOrEmpty(a.name) && !into.Contains(a.name)) into.Add(a.name);
        }

        public override string ToString()
            => Zoe == null ? "ZoeCapabilities(none)"
             : $"ZoeCapabilities({Zoe.name}: aim={CanAim}({(AimAngles != null ? AimAngles.Count : 0)} dirs) " +
               $"move={CanMove} fire={CanFire}({(WeaponNames != null ? WeaponNames.Count : 0)}) " +
               $"clips={(ClipNames != null ? ClipNames.Count : 0)})";
    }
}

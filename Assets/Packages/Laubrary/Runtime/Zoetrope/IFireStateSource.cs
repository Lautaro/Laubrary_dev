using UnityEngine;
using Laubrary.Combat2D;

namespace Laubrary.Zoetrope
{
    /// <summary>Everything about the shot that just happened, handed to whoever gets to choose which state
    /// the shooter shows for it. Read-only in-params: answering the question is the point, changing the shot
    /// is not.</summary>
    public readonly struct FireShot
    {
        /// <summary>The weapon that fired. Never null when the shot came from a weapon.</summary>
        public readonly ProjectileWeapon weapon;
        /// <summary>The weapon's authored data — its name, its muzzle look, its stats. This is what a chooser
        /// usually branches on ("the lazer rifle shows Shooting Lazer").</summary>
        public readonly WeaponDef def;
        /// <summary>Where the shot came out, in world space — the LIVE muzzle, tracked to the animation when a
        /// Muzzle meta-layer is painted and at the weapon's muzzleOffset when it is not.</summary>
        public readonly Vector2 origin;
        /// <summary>Which way the shot went, as a direction vector. The SHOT's aim, not any one pellet's, so a
        /// shotgun's five-way spread still yields one honest facing.</summary>
        public readonly Vector2 direction;
        /// <summary>Was this an instant-hit shot (no projectile travelled)? Some games show a different state
        /// for a beam than for a lobbed round; most will ignore it.</summary>
        public readonly bool hitscan;

        public FireShot(ProjectileWeapon weapon, WeaponDef def, Vector2 origin, Vector2 direction, bool hitscan)
        {
            this.weapon = weapon;
            this.def = def;
            this.origin = origin;
            this.direction = direction;
            this.hitscan = hitscan;
        }
    }

    /// <summary>
    /// GAME CODE's seat in the "what does the character show when it fires?" question. Put a component
    /// implementing this on the character (or any parent of the weapon), and every shot asks it for the name
    /// of the state to show; without one, the weapon asks for the reserved <c>"Fire"</c> exactly as it always
    /// has, so nothing that exists today changes.
    ///
    /// <para>This is the Zoe-palette split made concrete for weapons: the character DECLARES what it can show
    /// (its named states, authored in the Zoe window), Laubrary decides the mechanics (when a shot happens,
    /// where the muzzle is, how the reaction plays and arbitrates), and game code answers only "which of the
    /// declared looks, this time" — a lazer rifle showing <c>"Shooting Lazer"</c>, a powered-up shot showing
    /// something else. A hardcoded <c>"Fire"</c> could never express that, and the alternative games reach for
    /// is playing a clip on the character directly, which goes around the palette entirely.</para>
    ///
    /// <para>Discovered via <c>GetComponentInParent</c>, matching <see cref="ICueSink"/> /
    /// <c>IVectorAimSource</c> / <c>IActiveWeaponSource</c> — the capability-discovery pattern this module
    /// already uses everywhere. A game that would rather write a lambda wraps one in a three-line
    /// MonoBehaviour; a delegate could not be seen in the Inspector and would have no clean place to be
    /// installed, since the weapon components are built at runtime by ZoeSpawner.</para>
    /// </summary>
    public interface IFireStateSource
    {
        /// <summary>The name of the state the shooter should show for this shot.
        ///
        /// <para>Returning null or an empty string means <b>ask for nothing at all</b> — no state is raised,
        /// and there is deliberately NO fallback to <c>"Fire"</c>. Once game code has installed a chooser,
        /// game code is in charge; a silent fallback would be exactly the "something played on its own"
        /// this whole model exists to prevent, and it would be undebuggable (the character would show a state
        /// nobody asked for, in response to a decision to ask for nothing).</para>
        ///
        /// <para>A name the character does not declare is NOT silently ignored: it warns, once, listing what
        /// the character does declare — because unlike the built-in <c>"Fire"</c>, this name was deliberately
        /// chosen, and a deliberate choice that misses is a bug.</para></summary>
        string FireStateFor(in FireShot shot);
    }
}

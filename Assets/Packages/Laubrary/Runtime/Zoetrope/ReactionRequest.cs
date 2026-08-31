using UnityEngine;
using Laubrary.Combat2D;

namespace Laubrary.Zoetrope
{
    /// <summary>
    /// Everything a request to show a named state carries WITH it: where it happened, which way it faced, how
    /// big it was, and who asked. The "in-params" half of a raise — <c>ReactionFxPlayer.Raise</c> turns one of
    /// these into the <see cref="EventContext"/> every effect on the reaction then reads.
    ///
    /// <para>It exists because a raise used to carry a <see cref="DamageInfo"/> — and a fire pose is not a
    /// blow. <c>Raise</c>'s own doc comment apologised for the mismatch in prose ("a custom event has NO
    /// ATTACKER"), and the practical cost was real: every game-raised state arrived with a zero position and
    /// no facing, so its effects spawned at the character's anchor pointing nowhere, whatever actually caused
    /// them. A gun's muzzle flash is the worked case — it happens at the muzzle, aimed along the shot, and
    /// neither of those facts fits in a damage event.</para>
    ///
    /// <para>Deliberately NOT a widening of <see cref="DamageInfo"/>: that struct lives in Combat2D, means
    /// "one damage event" (amount, source, faction, crit), and is passed around by every consumer project.
    /// Cutting the seam here instead keeps damage meaning damage, and gives the later "which override does
    /// this request select" capability one obvious place to land — one more field on this struct, and no
    /// signature in the chain moves again.</para>
    /// </summary>
    public readonly struct ReactionRequest
    {
        /// <summary>Where the state happens, in world space. <c>null</c> = "wherever the character is" — the
        /// effect's HitPosition placement then resolves to the character's own anchor, exactly as an
        /// un-positioned raise always has.
        ///
        /// <para>Nullable rather than a zero sentinel because zero is a REAL position. The old
        /// <c>DamageInfo.point != Vector2.zero</c> test could not tell "no position was supplied" from "this
        /// genuinely happened at the world origin", so a deliberate request at (0,0) silently snapped to the
        /// character instead. Rare, but unfixable from the caller's side, and free to get right here.</para></summary>
        public readonly Vector2? Position;

        /// <summary>Which way the state faces, as a direction vector. <see cref="Vector2.zero"/> = no facing,
        /// which resolves to NaN degrees downstream and fires directional effects omni-directionally — the
        /// same answer an un-aimed raise has always produced.</summary>
        public readonly Vector2 Direction;

        /// <summary>How big this is, for effects that size themselves by the event's scalar param. Zero when
        /// the state has no natural magnitude, which is the common case for a deliberately-raised state (a
        /// taunt has no "amount").</summary>
        public readonly float Amount;

        /// <summary>Who asked, when that is meaningful (the attacker for a hit, the shooter's weapon for a
        /// fire state). Informational — nothing in the core reaction path branches on it. May be null.</summary>
        public readonly GameObject Source;

        public ReactionRequest(Vector2? position = null, Vector2 direction = default,
                               float amount = 0f, GameObject source = null)
        {
            Position = position;
            Direction = direction;
            Amount = amount;
            Source = source;
        }

        /// <summary>The request a damage event makes — how Hit and Death reach the same code path a
        /// game-raised state does.
        ///
        /// <para>The <c>point != zero</c> test is reproduced here EXACTLY, and that is the one subtle
        /// correctness requirement of the whole struct: every hit and death effect authored against the old
        /// behaviour has to keep spawning at the byte-identical world point. A hit that recorded no point
        /// still falls back to the character's anchor; the nullable only adds an answer that used to be
        /// inexpressible, it re-interprets nothing that was already stored.</para></summary>
        public static ReactionRequest From(in DamageInfo info) => new ReactionRequest(
            position: info.point != Vector2.zero ? info.point : (Vector2?)null,
            direction: info.direction,
            amount: info.amount,
            source: info.source);
    }
}

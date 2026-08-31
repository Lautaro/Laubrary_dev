using Laubrary.Combat2D;

namespace Laubrary.Zoetrope
{
    /// <summary>
    /// Laubrary's two built-in questions — "which hurt look?" and "which death look?" — answered by GAME
    /// CODE, never by Laubrary itself. This is the round-3/round-4 "ask, don't default" mechanism
    /// (ZOE_PALETTE_TAKE.md "List B — moments where Laubrary stops and asks you"; ZOE_PALETTE_BUILD_PLAN.md
    /// item 2: "no state ever plays without gameplay code deciding it should — no exceptions, not even a
    /// list of one").
    ///
    /// An optional component (on the character or any parent — <see cref="ReactionFxPlayer"/> discovers it
    /// via <c>GetComponentInParent</c>, same convention <see cref="IFireStateSource"/> already uses). With
    /// NO such component, or one that answers null/empty, the character does EXACTLY what it does today: the
    /// built-in <c>Zoe.hit</c>/<c>Zoe.death</c> reaction plays, untouched. That is the honest answer to
    /// "nobody has answered yet" (round 3: "the answer is: it does exactly what it does today") — not a
    /// hidden default-picker, because nothing here CHOOSES on the game's behalf; an unanswered question
    /// simply falls back to the single reaction this character has always had.
    ///
    /// An answer must name a row carrying the matching <see cref="ReactionRole"/> chip
    /// (<see cref="Zoe.FindEvent"/>) — a name that doesn't exist, or exists but isn't chipped for this
    /// question, is treated as no legal answer (warned once, same fallback as no answerer at all), never
    /// played anyway. Laubrary still decides WHETHER a hurt or death happens — that is Health reaching zero /
    /// taking damage, entirely untouched by this interface — this only ever picks the APPEARANCE.
    /// </summary>
    public interface IReactionLookAnswerer
    {
        /// The name of a declared row (role-chipped Hurt) to show instead of the built-in Hit reaction, or
        /// null/empty to use the built-in one.
        string HurtLookFor(in DamageInfo info);

        /// The name of a declared row (role-chipped Death) to show instead of the built-in Death reaction, or
        /// null/empty to use the built-in one.
        string DeathLookFor(in DamageInfo info);
    }
}

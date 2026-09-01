using UnityEngine;

namespace Laubrary.Zoetrope
{
    /// <summary>Which of Laubrary's two built-in questions ("which hurt look?" / "which death look?") a
    /// declared row is a LEGAL answer to. Purely a filter on what an answerer may pick — it never decides
    /// WHETHER or WHEN a hurt or death happens (that stays entirely Health reaching zero / taking damage,
    /// untouched by this). Defaults to <see cref="None"/>, so nothing already authored changes meaning.
    ///
    /// This is the round-3/round-4 "role chip": ZOE_PALETTE_TAKE.md ("A role chip — nothing special / this is
    /// a hurt look / this is a death look... defaults to 'nothing special', so nothing you already have
    /// changes meaning") and ZOE_PALETTE_BUILD_PLAN.md task 6. It is NOT a category on the event in the sense
    /// the round-2 condition system was — it never gates or conditions playback, it only narrows a picker.</summary>
    public enum ReactionRole
    {
        /// An ordinary custom event — not eligible to answer either built-in question.
        None,
        /// A legal answer to "which hurt look?".
        Hurt,
        /// A legal answer to "which death look?".
        Death,
    }

    /// A reaction the character plays under a name of your choosing — "teleport-in", "spawn", "taunt".
    ///
    /// Deliberately IN PARALLEL to Zoe.hit and Zoe.death, not replacing them — SAME storage as before the
    /// palette model, not restructured (ZOE_PALETTE_TAKE.md: "Restructuring hurt and death into one saved
    /// list — Gone... Hurt and death stay exactly where they are; they're just DRAWN as the top two rows of
    /// the same list"). This class carries an old note, written before that round, arguing hit and death
    /// can't become named-list entries because they carry typed combat params, gate ZoeState.CanAct and drive
    /// deathDisposal — so folding them in would mean special-casing two entries anyway, and a typo could
    /// silently disable dying. That argument is answered, not dodged: hit and death still carry all of that,
    /// still on Laubrary's side, never name-driven — WHETHER/WHEN a hurt or death happens is never touched by
    /// a name. What became name-driven is only the APPEARANCE (see ReactionRole/IReactionLookAnswerer), and a
    /// typo there is bounded exactly as promised: a character that dies looking wrong, never one that fails
    /// to die. The special-casing half stands — hit/death ARE special, by being fixed fields with their role
    /// chip drawn rather than stored — and the honest claim is only that this is more visible than the old
    /// "special because of where they live, and unreachable by name at all" version.
    ///
    /// Everything else, though, has no typed semantics — spawn and shoot are just "play this, here" — so
    /// they belong here as conventional ids rather than as new fixed fields. Otherwise the model grows a tail
    /// of half-special cases, each needing its own plumbing.
    [System.Serializable]
    public class NamedReaction
    {
        [Tooltip("The name this reaction is raised by. Typed ONCE, here; every consumer picks from the "+
                 "declared list instead of retyping it. Keep it stable — renaming it breaks whatever raises it.")]
        public string id = "";

        [Tooltip("The role chip — is this row a legal answer to Laubrary's built-in \"which hurt look?\" or " +
                 "\"which death look?\" question. Nothing special = an ordinary custom event, raised by name " +
                 "like any other. This never decides WHETHER or WHEN a hurt or death happens — only whether " +
                 "this row may be PICKED as its appearance, by whatever answers the question.")]
        public ReactionRole role = ReactionRole.None;

        [Tooltip("What playing it does: the clip, an optional SpriteFx on the body, and the effect list — "+
                 "the same shape hit and death use.")]
        public ReactionFx reaction = new ReactionFx();
    }
}

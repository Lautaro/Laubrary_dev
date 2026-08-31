using System;

namespace Laubrary.Zoetrope
{
    /// <summary>
    /// Runtime half of the palette's honesty surface (ZOE_PALETTE_BUILD_PLAN.md task 7 / ZOE_PALETTE_TAKE.md
    /// "the bypass question" — "each row tells you whether anything in your project actually asks for it").
    ///
    /// Same shape as <see cref="Laubrary.Chunks.ChunkTimelineEvents.HasListeners"/> — "not needed to fire (an
    /// unheard event is harmless) — it exists so a tool can honestly report" — reused here for the mirror
    /// question: not "is anyone listening", but "was this specific declared name ever actually asked for, and
    /// did it resolve". <see cref="ReactionFxPlayer"/> reports every attempt to raise a name (custom event or a
    /// role-chipped hurt/death answer) here, whether it matched a declared row or not.
    ///
    /// Deliberately no UnityEditor reference anywhere in this file — this assembly stays Editor-free, same as
    /// every other Runtime/Zoetrope file. The Editor-only listener that PERSISTS what it hears
    /// (<c>ZoePaletteUsageLog</c>, Editor/Zoetrope) subscribes to <see cref="Requested"/> from outside; this
    /// class does not know it exists, has no dependency on it, and works (harmlessly, to nobody) with zero
    /// subscribers, exactly like ChunkTimelineEvents.
    /// </summary>
    public static class ZoeReactionTelemetry
    {
        /// <summary>Raised every time <see cref="ReactionFxPlayer"/> resolves a name against a character's
        /// declared palette — a custom event <c>Raise</c>/<c>TryRaise</c> call, or an
        /// <see cref="IReactionLookAnswerer"/>'s hurt/death answer. Args: the character asked, the name that
        /// was requested, and whether it matched a legal declared row (true) or missed (false) — a miss still
        /// fires this, because "requested but nothing declares it" is exactly the fact the honesty surface
        /// exists to surface.</summary>
        public static event Action<Zoe, string, bool> Requested;

        /// <summary>Report one resolution attempt. No-op (and cheap) with nobody listening — same "harmless
        /// when unheard" contract as ChunkTimelineEvents.Raise.</summary>
        public static void Report(Zoe def, string id, bool matched)
        {
            if (string.IsNullOrEmpty(id)) return;
            Requested?.Invoke(def, id, matched);
        }
    }
}

namespace Laubrary.Zoetrope
{
    /// <summary>
    /// A pluggable effect fired on a Zoe event (Hit, Death, …). It reads what it needs from the typed
    /// <see cref="EventContext"/> the trigger fills — the target Zoe's live data plus the resolved
    /// position / direction / scalar its picked params came out to — and does its thing. This is the general
    /// form of <see cref="ICombatFx"/> (which STAYS as the "spawn VFX at a world point" effect kind, now simply
    /// recognised as one <c>IEffect</c>). Other kinds (SpriteFx, Pushback, Play-Lauminary, …) plug in the same way
    /// (<c>[SerializeReference]</c>), each shipping in the bridge module that owns its tool, so core Zoetrope
    /// stays free of any specific presentation module. Assigned via <c>[SerializeReference]</c> on the reaction
    /// data, exactly as <see cref="ICombatFx"/> is today.
    /// </summary>
    public interface IEffect
    {
        /// True when there's nothing to do (so callers can skip it), same contract as <see cref="ICombatFx.IsEmpty"/>.
        bool IsEmpty { get; }

        /// <summary>Run this effect against the event context. The context carries the target Zoe's live data
        /// plus the resolved <see cref="EventContext.Position"/> / <see cref="EventContext.DirectionDeg"/> /
        /// <see cref="EventContext.Scalar"/> that the effect's picked params resolved to. An
        /// <see cref="ICombatFx"/> applies by spawning at the resolved position + direction (its default bridge);
        /// other effect kinds read whichever params they care about.</summary>
        void Apply(EventContext ctx);
    }
}

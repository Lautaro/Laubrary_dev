using UnityEngine;
using Laubrary.Combat2D;

namespace Laubrary.Zoetrope
{
    /// <summary>
    /// An OCCASION that raises a <see cref="ZoeEvent"/> without anyone naming it — "when this character is
    /// damaged", "when it dies". Optional: an event with no trigger is raised BY REFERENCE and nothing else,
    /// which is the primary path (game code knows whether a hit should make this character flinch; the
    /// character asset does not).
    ///
    /// A trigger is a convenience, not the definition of an event. It exists so a Zoe with no bespoke game
    /// code still does something sensible in Mirage, in a demo, or as a simple enemy.
    ///
    /// Pluggable (<c>[SerializeReference]</c>) because the kinds carry genuinely different data and are
    /// mutually exclusive per event — unlike CONSEQUENCES, which are plain optional fields on the event
    /// because they must COMBINE (a taunt may stun; a teleport may grant invulnerability).
    /// </summary>
    public interface IZoeEventTrigger
    {
        /// A short human description of when this fires, for the authoring UI's folded header.
        string Describe();
    }

    /// <summary>Raised when the character takes a non-killing hit. The filter is what lets one character
    /// react differently to different attacks; an unfiltered OnDamaged matches every hit, so the simple case
    /// stays a single event.</summary>
    [System.Serializable]
    public class OnDamagedTrigger : IZoeEventTrigger
    {
        [Tooltip("Only react to this KIND of damage. None = react to any hit. A hit whose DamageInfo carries " +
                 "no type never matches a filter that names one, so adding types to a project is additive.")]
        public DamageType damageType;

        [Tooltip("Only react when the hit did at least this much damage. 0 = any amount. Lets a light graze " +
                 "and a heavy blow play different reactions without any code.")]
        [Min(0f)] public float minAmount;

        [Tooltip("Only react to critical hits.")]
        public bool requireCrit;

        public bool Matches(in DamageInfo info)
        {
            if (damageType != null && info.type != damageType) return false;
            if (minAmount > 0f && info.amount < minAmount) return false;
            if (requireCrit && !info.crit) return false;
            return true;
        }

        public string Describe()
        {
            var bits = new System.Text.StringBuilder("when damaged");
            if (damageType != null) bits.Append(" by ").Append(string.IsNullOrEmpty(damageType.displayName) ? damageType.name : damageType.displayName);
            if (minAmount > 0f) bits.Append(" for ").Append(minAmount.ToString("0.##")).Append("+");
            if (requireCrit) bits.Append(", crits only");
            return bits.ToString();
        }
    }

    /// <summary>Raised by the killing blow. Same filter shape as <see cref="OnDamagedTrigger"/>, so a
    /// character can burn away when killed by fire and fall over when killed by anything else.</summary>
    [System.Serializable]
    public class OnDiedTrigger : IZoeEventTrigger
    {
        [Tooltip("Only answer a killing blow of this KIND. None = answer any death.")]
        public DamageType damageType;

        [Tooltip("Only critical killing blows.")]
        public bool requireCrit;

        public bool Matches(in DamageInfo info)
        {
            if (damageType != null && info.type != damageType) return false;
            if (requireCrit && !info.crit) return false;
            return true;
        }

        public string Describe()
            => damageType != null
                ? "when killed by " + (string.IsNullOrEmpty(damageType.displayName) ? damageType.name : damageType.displayName)
                : "when killed";
    }

    /// <summary>Raised once, the moment the character is spawned — a materialise flash, a drop-in puff.</summary>
    [System.Serializable]
    public class OnSpawnedTrigger : IZoeEventTrigger
    {
        public string Describe() => "when spawned";
    }

    /// <summary>Raised when the character's ANIMATION reaches a point — a painted MetaLayer, or an authored
    /// FrameEvent — whatever laumination happens to be playing. Footstep dust on every walk cycle, a sparkle
    /// as a cast pose peaks.
    ///
    /// <para><b>This absorbs the old <see cref="CueBinding"/> list.</b> A cue was "when the animation reaches
    /// this point, spawn this effect there, or raise this event" — which is a TRIGGER wearing a payload. The
    /// trigger half is real and nothing else expresses it (an event's own effects only fire while THAT event
    /// plays; a cue fires off whatever is playing). The payload half was a weaker copy of the event's: one
    /// <c>ICombatFx</c>, no body SpriteFx, no consequences, no duration — which is why the cue grew a
    /// "raise this event" field to escape its own limits. Made a trigger kind, the payload question stops
    /// existing: the event IS the payload, and there is one list to read instead of two.</para></summary>
    [System.Serializable]
    public class OnCueTrigger : IZoeEventTrigger
    {
        [Tooltip("Painted MetaLayer id whose point raises this. Good for anything needing several pixels or " +
                 "several frames. Ignored when a Frame event is named below.")]
        public string layerId = "";

        [Tooltip("Alternative to the MetaLayer — an authored FrameEvent name (one pixel on one frame), the " +
                 "lighter signal. Takes priority over the MetaLayer when set.")]
        public string eventName = "";

        [Tooltip("Only raise it while THIS laumination is playing. Empty = any laumination that reaches the " +
                 "point, which is what an always-on cue (footstep dust) wants.")]
        public string onlyDuring = "";

        /// The signal this listens for, or empty when nothing is picked yet (in which case it never fires —
        /// silently doing nothing is better than firing on every point at once).
        public bool HasPoint => !string.IsNullOrEmpty(eventName) || !string.IsNullOrEmpty(layerId);

        public bool Matches(string signal, bool isFrameEvent, string playingLaumination)
        {
            if (!HasPoint) return false;
            if (!string.IsNullOrEmpty(onlyDuring) &&
                !string.Equals(onlyDuring, playingLaumination, System.StringComparison.OrdinalIgnoreCase))
                return false;
            string want = !string.IsNullOrEmpty(eventName) ? eventName : layerId;
            bool wantFrameEvent = !string.IsNullOrEmpty(eventName);
            return wantFrameEvent == isFrameEvent &&
                   string.Equals(want, signal, System.StringComparison.OrdinalIgnoreCase);
        }

        public string Describe()
        {
            string point = !string.IsNullOrEmpty(eventName) ? "frame event \"" + eventName + "\""
                         : !string.IsNullOrEmpty(layerId) ? "layer \"" + layerId + "\""
                         : "no point picked";
            return string.IsNullOrEmpty(onlyDuring) ? "on " + point : $"on {point} during \"{onlyDuring}\"";
        }
    }
}

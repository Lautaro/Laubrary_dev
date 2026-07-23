using System.Collections.Generic;

namespace Laubrary.Zoetrope
{
    /// <summary>
    /// Core-side seam for "this character's animation can cue an effect at a point" — same shape as
    /// <see cref="ICharacterView"/>/<see cref="IBrainSpec"/>: a core interface, a bridge-module implementation
    /// (<c>Laubrary.ZoetropeLaunimator.CueRelay</c>) that actually knows about MetaLayers/FrameEvents.
    /// Zoetrope core stays Combat2D-only; only projects that include the Launimator bridge get a working sink.
    /// </summary>
    public interface ICueSink
    {
        /// Replaces the character's own always-on cues (from <see cref="Zoe.cues"/>), seeded once at spawn.
        void Seed(IReadOnlyList<CueBinding> cues);

        /// Adds/replaces a cue under a stable key (e.g. an equipped weapon's muzzle) — independent of Seed.
        /// <paramref name="eventName"/> (optional) triggers off a FrameEvent's authored pixel instead of a
        /// MetaLayer, taking priority over <paramref name="layerId"/> when non-empty — see CueBinding's own
        /// doc comment for when to reach for which.
        void Register(string key, string layerId, ICombatFx fx, string eventName = "");

        /// Removes a previously registered cue (e.g. on weapon unequip/switch).
        void Unregister(string key);
    }
}

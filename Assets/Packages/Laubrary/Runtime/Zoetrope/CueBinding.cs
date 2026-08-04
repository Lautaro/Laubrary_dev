using UnityEngine;

namespace Laubrary.Zoetrope
{
    /// <summary>
    /// One "when this animation signal is reached, play this effect at that point" binding. Two mutually
    /// exclusive trigger sources: a MetaLayer id (<see cref="layerId"/>, the original mechanism — good for
    /// anything needing multiple pixels or multiple frames, e.g. hit detection) or a FrameEvent name
    /// (<see cref="eventName"/>, lighter — a single authored pixel on a single frame, e.g. a muzzle/spawn
    /// point). <see cref="eventName"/> wins when both are set. No Launimator dependency here — just plain
    /// strings and the already-core <see cref="ICombatFx"/> — so it's safe to live on core <see cref="Zoe"/>
    /// data even though only a bridge module can actually act on it.
    /// </summary>
    [System.Serializable]
    public class CueBinding
    {
        public string layerId = "";
        [Tooltip("Alternative to layerId — a FrameEvent name (with an authored pixel position) that triggers " +
                 "this cue at its point instead. Takes priority over layerId when non-empty.")]
        public string eventName = "";
        [SerializeReference] public ICombatFx fx;

        [Tooltip("Raise this named Zoe event when the cue fires, instead of (or as well as) spawning the FX "+
                 "above. This is how an animation frame triggers a full reaction — a teleport's flash, a "+
                 "spawn puff — including a SpriteFx on the body, which the ICombatFx slot alone cannot express.")]
        public string raiseEvent = "";

        /// True when this binding would do SOMETHING if it fired. Both halves are optional, so a binding that
        /// only raises an event is valid and must not be skipped by an fx-only guard.
        public bool IsActionable => (fx != null && !fx.IsEmpty) || !string.IsNullOrEmpty(raiseEvent);
    }
}

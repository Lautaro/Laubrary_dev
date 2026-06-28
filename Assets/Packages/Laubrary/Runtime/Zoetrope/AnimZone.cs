using System;
using UnityEngine;

namespace Laubrary.Zoetrope
{
    /// <summary>How a <see cref="AnimZone"/> is traversed at runtime (see repo ANIMATION_CONTROLLER.md).</summary>
    public enum ZoneBehavior
    {
        /// <summary>Play once, then auto-advance to the next zone at its end. (e.g. Start, Land.)</summary>
        PlayThrough,
        /// <summary>Loop within this zone until the controller calls Advance()/EndIn(). (e.g. Air, Fall.)</summary>
        Loop,
    }

    /// <summary>
    /// A named, contiguous frame range inside ONE animation strip — the "zone" that turns a single clip into a
    /// phased move (a Jump's Start/Air/Fall/Land). Frame indices are inclusive, into the animation's baked
    /// <see cref="AnimationDef.frames"/> sequence. Behaviour ({PlayThrough, Hold}) is what the runtime acts on;
    /// the name is for humans + EnterAt(name).
    /// </summary>
    [Serializable]
    public class AnimZone
    {
        public string name = "Zone";
        public int startFrame;   // inclusive
        public int endFrame;     // inclusive
        public ZoneBehavior behavior = ZoneBehavior.PlayThrough;

        public int Length => Mathf.Max(1, endFrame - startFrame + 1);

        public bool Contains(int frame) => frame >= startFrame && frame <= endFrame;
    }
}

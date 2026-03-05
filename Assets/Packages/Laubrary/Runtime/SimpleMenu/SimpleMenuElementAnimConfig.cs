using System;
using UnityEngine;

namespace Laubrary.SimpleMenu
{
    public enum ElementAnimationDirection { TopBottom, BottomTop }
    public enum ElementPickOrder { Sequential, EveryOther, Ripple }

    /// <summary>
    /// One complete animation configuration used by SimpleMenuVisual for per-element transitions.
    /// Shared between primary and alternate stagger slots.
    /// </summary>
    [Serializable]
    public class SimpleMenuElementAnimConfig
    {
        public SimpleMenuAnimationType animationType = SimpleMenuAnimationType.Alpha;
        [Range(0f, 1f)] public float fromAlpha = 0f;
        public Vector2 fromPositionOffset = new Vector2(-30f, 0f);
        public Vector3 fromScale = Vector3.zero;
        public float duration = 0.2f;
        public AnimationCurve curve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);
        [Tooltip("Exit speed relative to enter duration. 2 = twice as fast.")]
        public float exitSpeedMultiplier = 2f;

        /// <summary>Shallow-copies this config. AnimationCurve reference is shared (read-only in practice).</summary>
        public SimpleMenuElementAnimConfig Clone() => new SimpleMenuElementAnimConfig
        {
            animationType      = animationType,
            fromAlpha          = fromAlpha,
            fromPositionOffset = fromPositionOffset,
            fromScale          = fromScale,
            duration           = duration,
            curve              = curve,
            exitSpeedMultiplier = exitSpeedMultiplier,
        };
    }
}

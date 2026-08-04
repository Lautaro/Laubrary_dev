using System.Collections.Generic;
using UnityEngine;
using Laubrary.Launimator;
using Laubrary.Zounds;

namespace Laubrary.LaunimatorZounds
{
    /// <summary>
    /// Auto-plays a Zound when a frame event fires carrying a non-empty <see cref="FrameEvent.zoundName"/>, for
    /// whichever Launimator player (<see cref="ZonedAnimationPlayer"/> or <see cref="LauminaryPlayer"/>) is on the
    /// same GameObject. Add this component alongside either player to get "author a Zound on a frame event, it
    /// just plays" for free — gameplay code doesn't need to manually wire "on Shot event, play the gunshot
    /// sound." Launimator's core (ZonedAnimationPlayer, FrameEvent, etc.) stays completely unaware Zounds
    /// exists — only this bridge does.
    /// </summary>
    [DisallowMultipleComponent]
    public class LaunimatorZoundBridge : MonoBehaviour
    {
        ZonedAnimationPlayer _zoned;
        LauminaryPlayer _lauminary;

        void Awake()
        {
            _zoned = GetComponent<ZonedAnimationPlayer>();
            _lauminary = GetComponent<LauminaryPlayer>();
            if (_zoned != null) _zoned.OnFrameEvent += OnZonedFrameEvent;
            if (_lauminary != null) _lauminary.OnFrameEvent += OnLauminaryFrameEvent;
        }

        void OnDestroy()
        {
            if (_zoned != null) _zoned.OnFrameEvent -= OnZonedFrameEvent;
            if (_lauminary != null) _lauminary.OnFrameEvent -= OnLauminaryFrameEvent;
        }

        void OnZonedFrameEvent(string name, int frame) => PlayIfZound(_zoned.CurrentEvents, name, frame);
        void OnLauminaryFrameEvent(string name, int frame) => PlayIfZound(_lauminary.CurrentAnim?.events, name, frame);

        static void PlayIfZound(IReadOnlyList<FrameEvent> events, string name, int frame)
        {
            if (events == null) return;
            for (int i = 0; i < events.Count; i++)
            {
                var ev = events[i];
                if (ev != null && ev.frame == frame && ev.name == name && !string.IsNullOrEmpty(ev.zoundName))
                {
                    ZoundEngine.PlayZound(ev.zoundName);
                    return;
                }
            }
        }
    }
}

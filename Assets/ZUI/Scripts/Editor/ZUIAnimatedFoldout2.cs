// ZUIAnimatedFoldout2.cs
// Experimental animated foldout — "measure first, animate second" approach.
//
// State machine:
//   Closed    → (open requested) → Measuring → Opening → Open
//   Open      → (close requested) → Closing  → Closed
//
// Measuring: draws content at full size for ONE Repaint frame (invisible,
//   alpha=0) to capture the natural height.
//
// Opening/Closing: content is drawn at full size using normal EditorGUILayout
//   (so IMGUI controls render correctly). After drawing, the excess space
//   beyond the animated height is reclaimed with negative GUILayout.Space,
//   and EditorGUI.DrawRect paints over the visual overflow.
//   Uses cubic ease-out for smooth, even timing.

using UnityEditor;
using UnityEngine;

public static partial class ZUI
{
    public enum FoldoutState
    {
        Closed,
        Measuring,
        Opening,
        Open,
        Closing,
    }

    public class AnimatedFoldout2
    {
        public readonly string key;
        public float contentHeight;
        public FoldoutState state = FoldoutState.Closed;

        /// <summary>Duration of the open/close animation in seconds.</summary>
        public float duration;

        private bool _requestedOpen;
        private bool _firstFrame = true;

        // Time-based animation state
        private double _animStartTime;
        private float _animFrom;  // 0 or 1
        private float _animTo;    // 1 or 0
        internal float _animValue; // current 0-1 progress (eased)

        public AnimatedFoldout2(string key, float duration = 0.25f)
        {
            this.key = key;
            this.duration = duration;
        }

        /// <summary>Returns the current 0-1 animation value (eased).</summary>
        public float AnimT => _animValue;

        /// <summary>Cubic ease-out: fast start, gentle finish. f(0)=0, f(1)=1.</summary>
        private static float EaseOutCubic(float t)
        {
            t = 1f - t;
            return 1f - t * t * t;
        }

        internal void StartAnim(float from, float to)
        {
            _animFrom = from;
            _animTo = to;
            _animValue = from;
            _animStartTime = EditorApplication.timeSinceStartup;
            // Use a dummy AnimatedFloat to keep the shared repaint loop alive
            // during our time-based animation.
            var keepAlive = GetOrCreateAnimFloat(key + "_keepalive", 0f);
            keepAlive.SnapTo(0f);
            keepAlive.SetTarget(1f, 1f / Mathf.Max(duration, 0.01f));
        }

        private void AdvanceAnim()
        {
            float elapsed = (float)(EditorApplication.timeSinceStartup - _animStartTime);
            float linear = Mathf.Clamp01(elapsed / duration);
            float eased = EaseOutCubic(linear);
            _animValue = Mathf.Lerp(_animFrom, _animTo, eased);
        }

        public FoldoutScope2 Begin(bool open)
        {
            if (_firstFrame)
            {
                _requestedOpen = open;
                _firstFrame = false;
                if (open)
                {
                    state = FoldoutState.Open;
                    _animValue = 1f;
                }
            }
            else if (open != _requestedOpen)
            {
                _requestedOpen = open;
                if (open)
                {
                    state = FoldoutState.Measuring;
                }
                else
                {
                    if (contentHeight > 1f)
                    {
                        state = FoldoutState.Closing;
                        StartAnim(1f, 0f);
                    }
                    else
                    {
                        state = FoldoutState.Closed;
                        _animValue = 0f;
                    }
                }
            }

            if (state == FoldoutState.Opening || state == FoldoutState.Closing)
            {
                AdvanceAnim();
                float linear = Mathf.Clamp01((float)(EditorApplication.timeSinceStartup - _animStartTime) / duration);
                if (linear >= 1f)
                {
                    _animValue = _animTo;
                    state = (_animTo >= 1f) ? FoldoutState.Open : FoldoutState.Closed;
                }
            }

            return new FoldoutScope2(this);
        }
    }

    public struct FoldoutScope2 : System.IDisposable
    {
        public readonly bool visible;

        private readonly AnimatedFoldout2 _foldout;
        private readonly FoldoutState _stateAtBegin;
        private readonly Color _prevGuiColor;
        private readonly bool _modifiedColor;
        private readonly Rect _clipRect;

        public FoldoutScope2(AnimatedFoldout2 foldout)
        {
            _foldout = foldout;
            _stateAtBegin = foldout.state;
            _prevGuiColor = GUI.color;
            _modifiedColor = false;
            _clipRect = Rect.zero;
            visible = false;

            switch (_stateAtBegin)
            {
                case FoldoutState.Closed:
                    visible = false;
                    break;

                case FoldoutState.Measuring:
                    // Draw at full size but invisible. Measure on Repaint.
                    visible = true;
                    _modifiedColor = true;
                    GUI.color = new Color(_prevGuiColor.r, _prevGuiColor.g, _prevGuiColor.b, 0f);
                    break;

                case FoldoutState.Opening:
                case FoldoutState.Closing:
                {
                    // Content draws normally (full size via EditorGUILayout).
                    // In Dispose we reclaim excess space and paint over overflow.
                    visible = true;

                    // Peek at the current layout Y position (zero-height rect).
                    // We store this so Dispose knows where the content starts.
                    _clipRect = GUILayoutUtility.GetRect(0f, 0f, GUILayout.ExpandWidth(true));
                    break;
                }

                case FoldoutState.Open:
                    visible = true;
                    break;
            }
        }

        public void Dispose()
        {
            switch (_stateAtBegin)
            {
                case FoldoutState.Measuring:
                {
                    // Capture the height the content occupied
                    var r = GUILayoutUtility.GetLastRect();

                    // Reclaim ALL the space the invisible content took, so
                    // following content stays at its original position (no blink).
                    if (r.height > 1f)
                        GUILayout.Space(-r.height);

                    if (Event.current.type == EventType.Repaint && r.height > 1f)
                    {
                        _foldout.contentHeight = r.height;
                        _foldout.StartAnim(0f, 1f);
                        _foldout.state = FoldoutState.Opening;
                    }
                    break;
                }

                case FoldoutState.Opening:
                case FoldoutState.Closing:
                {
                    float fullH = _foldout.contentHeight;
                    float animatedH = fullH * _foldout._animValue;
                    float excess = fullH - animatedH;

                    // Reclaim excess layout space — this pulls following content up
                    if (excess > 0.5f)
                        GUILayout.Space(-excess);

                    // Clip visual overflow: paint over the area below animatedH
                    // with the window background color. This is the simplest
                    // reliable way to hide overflow in IMGUI without affecting
                    // layout or event processing.
                    if (Event.current.type == EventType.Repaint && excess > 0.5f)
                    {
                        // The content ended at _clipRect.y + fullH.
                        // We want to hide from _clipRect.y + animatedH downward.
                        Rect coverRect = new Rect(
                            _clipRect.x,
                            _clipRect.y + animatedH,
                            _clipRect.width,
                            excess + 2f // +2 to avoid sub-pixel gaps
                        );
                        // Use the editor background color
                        Color bgColor = EditorGUIUtility.isProSkin
                            ? new Color(0.22f, 0.22f, 0.22f, 1f)
                            : new Color(0.76f, 0.76f, 0.76f, 1f);
                        EditorGUI.DrawRect(coverRect, bgColor);
                    }
                    break;
                }

                case FoldoutState.Open:
                    if (Event.current.type == EventType.Repaint)
                    {
                        var r = GUILayoutUtility.GetLastRect();
                        if (r.height > 1f)
                            _foldout.contentHeight = r.height;
                    }
                    break;
            }

            if (_modifiedColor)
                GUI.color = _prevGuiColor;
        }
    }
}

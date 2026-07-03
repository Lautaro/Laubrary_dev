// ZUIAnimation.cs
// Shared animation driver for all ZUI animation systems (Pulse, Tween, Flash, AnimatedFloat).
//
// Each subsystem (ZUIPulse, ZUITween, flash) registers itself via MarkAnimationActive().
// A single EditorApplication.update callback drives repaint for all of them. The loop
// stops automatically when no animations are active, restarting on demand.
//
// ZUI.AnimatedFloat: a float animator that replaces UnityEditor.AnimFloat.
//   - Create/get:  ZUI.GetOrCreateAnimFloat(key, initial)
//   - Animate:     af.SetTarget(value)   — exponential ease-out toward target
//   - Snap:        af.SnapTo(value)      — instant, no animation
//   - Read:        af.value
//   - Destroy:     ZUI.RemoveAnimFloat(key)
//
// The repaint-all-windows pattern is intentional for editor-only code.

using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

public static partial class ZUI
{
    // ===== Shared update loop =================================================

    private static bool _animUpdateRunning = false;

    /// <summary>
    /// Called by any animation subsystem when it becomes active.
    /// Starts the shared EditorApplication.update loop if not already running.
    /// </summary>
    internal static void EnsureAnimUpdateRunning()
    {
        if (_animUpdateRunning) return;
        _animUpdateRunning = true;
        EditorApplication.update += OnAnimUpdate;
    }

    private static void OnAnimUpdate()
    {
        double now = EditorApplication.timeSinceStartup;

        // --- Pulses ---
        if (_pulses.Count > 0)
        {
            var toRemove = new List<string>();
            foreach (var kv in _pulses)
                if (now >= kv.Value.endTime) toRemove.Add(kv.Key);
            foreach (var k in toRemove) _pulses.Remove(k);
        }

        // --- AnimatedFloats (named + standalone) ---
        bool anyFloatMoving = false;
        anyFloatMoving |= AdvanceAnimFloats(_animFloats.Values, now);
        anyFloatMoving |= AdvanceAnimFloats(_standaloneFloats, now);

        // --- Tweens ---
        bool anyTweenActive = false;
        foreach (var kv in _tweens)
        {
            var e = kv.Value;
            if (e.def == null) continue;
            bool hoverActive = e.def.hoverAnimEnabled &&
                               ((e.hoverForward && e.hoverT < 1f) || (!e.hoverForward && e.hoverT > 0f));
            bool clickActive = e.def.clickAnimEnabled &&
                               ((e.clickForward && e.clickT < 1f) || (!e.clickForward && e.clickT > 0f));
            if (hoverActive || clickActive) { anyTweenActive = true; break; }
        }

        // --- Flash ---
        bool flashActive = (!string.IsNullOrEmpty(_flashStyleName) && now <= _flashEndTime)
                         || (_spaceFlashActive && now <= _spaceFlashEndTime);

        bool microRadioActive = HasActiveMicroRadioAnim();
        bool anyActive = _pulses.Count > 0 || anyFloatMoving || anyTweenActive || flashActive || microRadioActive;

        if (!anyActive)
        {
            _animUpdateRunning = false;
            EditorApplication.update -= OnAnimUpdate;
            return;
        }

        foreach (var w in Resources.FindObjectsOfTypeAll<EditorWindow>())
            w.Repaint();
    }

    // Advances all AnimatedFloats in a collection. Returns true if any are still moving.
    private static bool AdvanceAnimFloats(IEnumerable<AnimatedFloat> floats, double now)
    {
        bool anyMoving = false;
        foreach (var af in floats)
        {
            if (Mathf.Approximately(af.value, af.target)) continue;
            float dt = (float)(now - af.lastUpdateTime);
            dt = Mathf.Min(dt, 0.1f);
            float t = 1f - Mathf.Exp(-af.speed * dt);
            af.value = Mathf.Lerp(af.value, af.target, t);
            if (Mathf.Abs(af.value - af.target) < 0.001f) af.value = af.target;
            af.lastUpdateTime = now;
            anyMoving = true;
        }
        return anyMoving;
    }

    // ===== AnimatedFloat ======================================================

    /// <summary>
    /// A named, smoothly-animated float value. Replaces UnityEditor.AnimFloat
    /// for inspector panel open/close animations without requiring UnityEvent wiring.
    /// </summary>
    public class AnimatedFloat
    {
        public float  value;
        public float  target;
        /// <summary>Speed as a fraction of remaining distance per second (exponential ease-out). Default 4.</summary>
        public float  speed = 4f;
        internal double lastUpdateTime;

        public AnimatedFloat(float initial)
        {
            value = initial;
            target = initial;
            lastUpdateTime = EditorApplication.timeSinceStartup;
            // Auto-register so the shared update loop can advance standalone floats.
            // Floats created via GetOrCreateAnimFloat are tracked in _animFloats instead.
            _standaloneFloats.Add(this);
        }

        /// <summary>Sets the target and starts animation. Returns this for chaining.</summary>
        public AnimatedFloat SetTarget(float newTarget, float newSpeed = -1f)
        {
            target = newTarget;
            if (newSpeed >= 0f) speed = newSpeed;
            lastUpdateTime = EditorApplication.timeSinceStartup;
            EnsureAnimUpdateRunning();
            return this;
        }

        /// <summary>Snaps to target immediately without animation.</summary>
        public AnimatedFloat SnapTo(float v) { value = v; target = v; return this; }

        /// <summary>Returns 0-1 animation progress (value / target). Safe when target is 0.</summary>
        public float progress => target > 0.001f ? Mathf.Clamp01(value / target) : (value < 0.001f ? 0f : 1f);
    }

    // Standalone AnimatedFloat instances (created via new rather than GetOrCreateAnimFloat).
    private static readonly List<AnimatedFloat> _standaloneFloats = new List<AnimatedFloat>();

    private static readonly Dictionary<string, AnimatedFloat> _animFloats = new Dictionary<string, AnimatedFloat>();

    /// <summary>
    /// Returns the AnimatedFloat for this key, creating it at <paramref name="initial"/> if it doesn't exist.
    /// </summary>
    public static AnimatedFloat GetOrCreateAnimFloat(string key, float initial = 0f)
    {
        if (!_animFloats.TryGetValue(key, out var af))
        {
            af = new AnimatedFloat(initial);
            _standaloneFloats.Remove(af); // keyed floats are tracked in _animFloats, not _standaloneFloats
            _animFloats[key] = af;
        }
        return af;
    }

    /// <summary>Removes the AnimatedFloat for this key.</summary>
    public static void RemoveAnimFloat(string key) => _animFloats.Remove(key);

    // ===== Animated Foldout ===================================================
    //
    // Space+Fade animated foldout. Works reliably inside nested ZUI.Box()
    // containers without IMGUI layout conflicts.
    //
    // How it works:
    //   - Closed:  nothing drawn
    //   - Opening: GUILayout.Space(animatedH) grows — pushes content below
    //   - Open:    content drawn normally, height measured
    //   - Closing: content drawn with fading alpha (layout stays consistent)
    //
    // State changes happen only during EventType.Layout to guarantee Layout
    // and Repaint passes see identical control structure.
    //
    // Usage:
    //   // field:
    //   private ZUI.AnimatedFoldout _foldout = new ZUI.AnimatedFoldout("MyKey");
    //
    //   // in OnGUI:
    //   using (var fold = _foldout.Begin(isOpen))
    //   {
    //       if (fold.visible)
    //       {
    //           // draw your content normally (GUILayout calls)
    //       }
    //   }

    // FoldoutState enum is defined in ZUIAnimatedFoldout2.cs

    // Internal sub-states for the two-phase animation.
    // Opening:  SpaceGrow → FadeIn → Open
    // Closing:  FadeOut → SpaceShrink → Closed
    internal enum FoldoutPhase { Closed, SpaceGrow, FadeIn, Open, FadeOut, SpaceShrink }

    /// <summary>
    /// Animated foldout state. Caches content height and drives the animation.
    /// One instance per foldable section.
    /// </summary>
    public class AnimatedFoldout
    {
        public readonly string key;

        /// <summary>Duration of the space grow/shrink phase in seconds.</summary>
        public float spaceDuration;

        /// <summary>Duration of the fade in/out phase in seconds.</summary>
        public float fadeDuration;

        /// <summary>Measured natural height of the content. Updated when fully open.</summary>
        public float contentHeight;

        internal FoldoutPhase _phase = FoldoutPhase.Closed;
        internal double _phaseStartTime;
        internal float _phaseT;    // 0-1 progress within current phase (frozen per OnGUI)
        private bool _lastOpen;
        private bool _firstFrame = true;

        public AnimatedFoldout(string key, float spaceDuration = 0.07f, float fadeDuration = 0.11f)
        {
            this.key           = key;
            this.spaceDuration = spaceDuration;
            this.fadeDuration  = fadeDuration;
        }

        /// <summary>Returns the current state for debug display.</summary>
        public FoldoutState state
        {
            get
            {
                switch (_phase)
                {
                    case FoldoutPhase.SpaceGrow:
                    case FoldoutPhase.FadeIn:    return FoldoutState.Opening;
                    case FoldoutPhase.FadeOut:
                    case FoldoutPhase.SpaceShrink: return FoldoutState.Closing;
                    case FoldoutPhase.Open:      return FoldoutState.Open;
                    default:                     return FoldoutState.Closed;
                }
            }
        }

        private float PhaseDuration
        {
            get
            {
                switch (_phase)
                {
                    case FoldoutPhase.SpaceGrow:
                    case FoldoutPhase.SpaceShrink: return spaceDuration;
                    case FoldoutPhase.FadeIn:
                    case FoldoutPhase.FadeOut:     return fadeDuration;
                    default: return 0f;
                }
            }
        }

        private void StartPhase(FoldoutPhase phase)
        {
            _phase = phase;
            _phaseStartTime = EditorApplication.timeSinceStartup;
            _phaseT = 0f;
            EnsureAnimUpdateRunning();
            var keepAlive = GetOrCreateAnimFloat(key + "_keepalive", 0f);
            keepAlive.SnapTo(0f);
            keepAlive.SetTarget(1f, 1f / Mathf.Max(PhaseDuration, 0.01f));
        }

        /// <summary>
        /// Opens the foldout scope. Dispose the returned value (use 'using').
        /// Check <see cref="FoldoutScope.visible"/> to know whether to draw content.
        /// </summary>
        public FoldoutScope Begin(bool open)
        {
            // Only update state on Layout to guarantee Layout/Repaint consistency.
            if (Event.current.type == EventType.Layout)
            {
                if (_firstFrame)
                {
                    _firstFrame = false;
                    _lastOpen = open;
                    _phase = open ? FoldoutPhase.Open : FoldoutPhase.Closed;
                    _phaseT = 0f;
                }
                else if (open != _lastOpen)
                {
                    _lastOpen = open;
                    if (open)
                        StartPhase(FoldoutPhase.SpaceGrow);
                    else
                        StartPhase(FoldoutPhase.FadeOut);
                }

                // Advance current phase
                float dur = PhaseDuration;
                if (dur > 0f && (_phase == FoldoutPhase.SpaceGrow || _phase == FoldoutPhase.FadeIn
                              || _phase == FoldoutPhase.FadeOut   || _phase == FoldoutPhase.SpaceShrink))
                {
                    float elapsed = (float)(EditorApplication.timeSinceStartup - _phaseStartTime);
                    _phaseT = Mathf.Clamp01(elapsed / dur);
                    if (_phaseT >= 1f)
                    {
                        // Transition to next phase
                        switch (_phase)
                        {
                            case FoldoutPhase.SpaceGrow:   StartPhase(FoldoutPhase.FadeIn); break;
                            case FoldoutPhase.FadeIn:      _phase = FoldoutPhase.Open; _phaseT = 0f; break;
                            case FoldoutPhase.FadeOut:     StartPhase(FoldoutPhase.SpaceShrink); break;
                            case FoldoutPhase.SpaceShrink: _phase = FoldoutPhase.Closed; _phaseT = 0f; break;
                        }
                    }
                }
            }

            return new FoldoutScope(this);
        }
    }

    /// <summary>
    /// Disposable scope returned by <see cref="AnimatedFoldout.Begin"/>.
    /// </summary>
    public struct FoldoutScope : System.IDisposable
    {
        /// <summary>True when content should be drawn.</summary>
        public readonly bool visible;

        private readonly AnimatedFoldout _foldout;
        private readonly FoldoutPhase _phaseAtBegin;
        private readonly Color _prevColor;
        private readonly bool _modifiedColor;

        public FoldoutScope(AnimatedFoldout foldout)
        {
            _foldout = foldout;
            _phaseAtBegin = foldout._phase;
            _prevColor = GUI.color;
            _modifiedColor = false;
            visible = false;

            switch (_phaseAtBegin)
            {
                case FoldoutPhase.Closed:
                    break;

                case FoldoutPhase.SpaceGrow:
                {
                    float h = (foldout.contentHeight > 1f ? foldout.contentHeight : 100f) * foldout._phaseT;
                    GUILayout.Space(h);
                    break;
                }

                case FoldoutPhase.FadeIn:
                    visible = true;
                    _modifiedColor = true;
                    GUI.color = new Color(_prevColor.r, _prevColor.g, _prevColor.b, _prevColor.a * foldout._phaseT);
                    EditorGUILayout.BeginVertical();
                    break;

                case FoldoutPhase.Open:
                    visible = true;
                    EditorGUILayout.BeginVertical();
                    break;

                case FoldoutPhase.FadeOut:
                    visible = true;
                    _modifiedColor = true;
                    GUI.color = new Color(_prevColor.r, _prevColor.g, _prevColor.b, _prevColor.a * (1f - foldout._phaseT));
                    EditorGUILayout.BeginVertical();
                    break;

                case FoldoutPhase.SpaceShrink:
                {
                    float h = (foldout.contentHeight > 1f ? foldout.contentHeight : 100f) * (1f - foldout._phaseT);
                    GUILayout.Space(h);
                    break;
                }
            }
        }

        public void Dispose()
        {
            if (_modifiedColor)
                GUI.color = _prevColor;

            switch (_phaseAtBegin)
            {
                case FoldoutPhase.Open:
                    EditorGUILayout.EndVertical();
                    if (Event.current.type == EventType.Repaint)
                    {
                        var r = GUILayoutUtility.GetLastRect();
                        if (r.height > 1f)
                            _foldout.contentHeight = r.height;
                    }
                    break;

                case FoldoutPhase.FadeIn:
                case FoldoutPhase.FadeOut:
                    EditorGUILayout.EndVertical();
                    break;
            }
        }
    }

}

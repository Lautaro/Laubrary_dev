// ZUITween.cs
// Opt-in hover-in / hover-out / click color animation for ZUI buttons and toggles.
//
// How it works:
//   - Each ZUIButtonDef can opt in via hoverAnimEnabled / clickAnimEnabled fields.
//   - DrawManualButton / DrawManualToggle call ZUITween.UpdateHover / UpdateClick
//     on every frame to drive the tween state.
//   - GetAnimatedFill(key, baseFill) returns the interpolated fill color for the
//     current frame. Pass this to DrawVisual instead of the raw state gradient.
//   - The tween dictionary is keyed by control ID so each button instance is
//     independent. EditorApplication.update drives repaint when tweens are active.
//
// Opt-in fields on ZUIButtonDef:
//   bool  hoverAnimEnabled     — enable hover-in / hover-out gradient transition
//   float hoverInDuration      — seconds for hover-in  (default 0.12)
//   float hoverOutDuration     — seconds for hover-out (default 0.20)
//   bool  clickAnimEnabled     — enable click-in / click-out gradient transition
//   float clickInDuration      — seconds for click-in  (default 0.06)
//   float clickOutDuration     — seconds for click-out (default 0.20)
//
// Usage in DrawManualButton:
//   ZUI.TweenNotifyHover(id, isHover, def);
//   ZUI.TweenNotifyActive(id, isActive, def);
//   var animTint = ZUITween.GetTint(id);    // Color to multiply over the visual

using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

public static partial class ZUI
{
    // ===== ZUITween — per-control hover/click animation =======================

    /// <summary>
    /// Notifies the tween system of the current hover state for a control.
    /// Call every frame in the button draw loop before the repaint block.
    /// Does nothing if <paramref name="def"/> has hoverAnimEnabled = false.
    /// </summary>
    public static void TweenNotifyHover(int controlId, bool isHover, ZUIButtonDef def)
    {
        if (!def.hoverAnimEnabled) return;

        // Only update hover state on events where mouse position is meaningful.
        // Layout events fire with stale/wrong mouse positions, causing enter/exit oscillation
        // that resets hoverStart every frame and keeps hoverT at 0.
        var evType = Event.current.type;
        if (evType == EventType.Layout || evType == EventType.Used) return;

        if (!_tweens.TryGetValue(controlId, out var entry))
        {
            entry = new TweenEntry();
            _tweens[controlId] = entry;
        }

        bool wasHover = entry.isHover;
        entry.isHover = isHover;
        entry.def     = def;

        if (isHover && !wasHover)
        {
            // Hover enter
            entry.hoverT       = entry.hoverT; // continue from current position
            entry.hoverStart   = EditorApplication.timeSinceStartup - entry.hoverT * def.hoverInDuration;
            entry.hoverForward = true;
            EnsureAnimUpdateRunning();
        }
        else if (!isHover && wasHover)
        {
            // Hover exit
            entry.hoverStart   = EditorApplication.timeSinceStartup - (1f - entry.hoverT) * def.hoverOutDuration;
            entry.hoverForward = false;
            EnsureAnimUpdateRunning();
        }
    }

    /// <summary>
    /// Notifies the tween system of the current active (mouse-down) state.
    /// Works like hover: animates in on mouse-down, animates out on mouse-up.
    /// </summary>
    public static void TweenNotifyActive(int controlId, bool isActive, ZUIButtonDef def)
    {
        if (!def.clickAnimEnabled) return;

        var evType = Event.current.type;
        if (evType == EventType.Layout || evType == EventType.Used) return;

        if (!_tweens.TryGetValue(controlId, out var entry))
        {
            entry = new TweenEntry();
            _tweens[controlId] = entry;
        }

        bool wasActive = entry.isActive;
        entry.isActive = isActive;
        entry.def      = def;

        if (isActive && !wasActive)
        {
            // Click-in: animate clickT toward 1
            entry.clickStart   = EditorApplication.timeSinceStartup - entry.clickT * def.clickInDuration;
            entry.clickForward = true;
            EnsureAnimUpdateRunning();
        }
        else if (!isActive && wasActive)
        {
            // Click-out: animate clickT toward 0
            entry.clickStart   = EditorApplication.timeSinceStartup - (1f - entry.clickT) * def.clickOutDuration;
            entry.clickForward = false;
            EnsureAnimUpdateRunning();
        }
    }

    /// <summary>
    /// Returns the current hover tween value (0=normal, 1=fully hovered) for a control.
    /// </summary>
    public static float TweenGetHoverT(int controlId)
    {
        if (!_tweens.TryGetValue(controlId, out var entry)) return 0f;
        if (entry.def == null || !entry.def.hoverAnimEnabled) return 0f;
        double now = EditorApplication.timeSinceStartup;
        float dur = entry.hoverForward ? entry.def.hoverInDuration : entry.def.hoverOutDuration;
        if (dur > 0f)
        {
            float elapsed = (float)(now - entry.hoverStart);
            float rawT = Mathf.Clamp01(elapsed / dur);
            var ease = entry.hoverForward ? entry.def.hoverInEase : entry.def.hoverOutEase;
            float easedT = ZUIEasing.Evaluate(ease, rawT);
            entry.hoverT = entry.hoverForward ? easedT : (1f - easedT);
        }
        return entry.hoverT;
    }

    /// <summary>
    /// Returns the current click tween value (0=normal, 1=fully active).
    /// </summary>
    public static float TweenGetClickT(int controlId)
    {
        if (!_tweens.TryGetValue(controlId, out var entry)) return 0f;
        if (entry.def == null || !entry.def.clickAnimEnabled) return 0f;
        double now = EditorApplication.timeSinceStartup;
        float dur = entry.clickForward ? entry.def.clickInDuration : entry.def.clickOutDuration;
        if (dur > 0f)
        {
            float elapsed = (float)(now - entry.clickStart);
            float rawT = Mathf.Clamp01(elapsed / dur);
            var ease = entry.clickForward ? entry.def.clickInEase : entry.def.clickOutEase;
            float easedT = ZUIEasing.Evaluate(ease, rawT);
            entry.clickT = entry.clickForward ? easedT : (1f - easedT);
        }
        return entry.clickT;
    }

    // ===== Internals ==========================================================

    private class TweenEntry
    {
        public ZUIButtonDef def;
        public bool   isHover;
        public bool   hoverForward;
        public float  hoverT;         // 0=normal, 1=fully hovered
        public double hoverStart;
        public bool   isActive;
        public bool   clickForward;
        public float  clickT;         // 0=normal, 1=fully active
        public double clickStart;
    }

    private static readonly Dictionary<int, TweenEntry> _tweens = new Dictionary<int, TweenEntry>();
}

// ZUIPulse.cs
// Animated overlay system for ZUI controls.
//
// How it works:
//   - Call StartPulse(key, params) when something starts (e.g. audio begins playing).
//   - Call StopPulse(key) when it ends (or let it expire via totalDuration).
//   - In your repaint block, call DrawPulse(key, rect) BEFORE drawing your control
//     so the overlay sits behind it.
//   - Use GetPulseIntensity(key) to read the current 0-1 envelope value and apply
//     secondary effects (e.g. darkening button text while the bg is brightening).
//   - The update loop drives Repaint() on all editor windows automatically.
//
// Modes:
//   Fill          — full rect fill
//   Border        — inset border rects only
//   FillAndBorder — both, with separate colors for each
//
// Blend modes (applied per layer):
//   Multiply — lerps GUI.color from white toward color at peak intensity.
//              Tints already-rendered pixels. Best for subtle "glow" on dark UIs.
//   Alpha    — draws a flat color rect at alpha = intensity * color.a.
//              Sits on top as a translucent wash.
//
// Envelope:
//   Each cycle uses sin(π * phase) — 0→1→0. Fully smooth, no sharp edges.
//   Set totalDuration = float.PositiveInfinity for a permanent loop; call StopPulse to end.

using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

public static partial class ZUI
{
    // ===== Public API types ===================================================

    public enum PulseMode  { Fill, Border, FillAndBorder }
    public enum PulseBlend { Multiply, Alpha }

    public struct PulseParams
    {
        public Color      color;
        /// <summary>Border color. Only used when mode == FillAndBorder. Defaults to color if not set.</summary>
        public Color?     borderColor;
        public PulseMode  mode;
        public PulseBlend blend;
        /// <summary>Seconds for one rise-fall cycle.</summary>
        public float      cycleDuration;
        /// <summary>Total seconds the pulse runs. Use float.PositiveInfinity for a permanent loop.</summary>
        public float      totalDuration;
        /// <summary>Border thickness in pixels. Used when mode == Border or FillAndBorder.</summary>
        public float      borderWidth;

        public static PulseParams Default => new PulseParams
        {
            color         = Color.white,
            mode          = PulseMode.Fill,
            blend         = PulseBlend.Multiply,
            cycleDuration = 0.4f,
            totalDuration = 1.2f,
            borderWidth   = 2f,
        };
    }

    // ===== Public API =========================================================

    /// <summary>
    /// Registers a pulse for the given key. If a pulse with this key already exists it is replaced.
    /// Call DrawPulse(key, rect) every repaint BEFORE drawing the control so the overlay is behind it.
    /// </summary>
    public static void StartPulse(string key, PulseParams p)
    {
        _pulses[key] = new PulseEntry
        {
            color         = p.color,
            borderColor   = p.borderColor ?? p.color,
            mode          = p.mode,
            blend         = p.blend,
            cycleDuration = Mathf.Max(p.cycleDuration, 0.05f),
            startTime     = EditorApplication.timeSinceStartup,
            endTime       = float.IsPositiveInfinity(p.totalDuration)
                                ? double.PositiveInfinity
                                : EditorApplication.timeSinceStartup + p.totalDuration,
            borderWidth   = Mathf.Max(p.borderWidth, 1f),
        };

        EnsureAnimUpdateRunning();
    }

    /// <summary>Convenience overload with individual parameters.</summary>
    public static void StartPulse(string key, Color color,
        PulseMode mode = PulseMode.Fill, PulseBlend blend = PulseBlend.Multiply,
        float cycleDuration = 0.4f, float totalDuration = 1.2f, float borderWidth = 2f,
        Color? borderColor = null)
        => StartPulse(key, new PulseParams {
            color = color, borderColor = borderColor, mode = mode, blend = blend,
            cycleDuration = cycleDuration, totalDuration = totalDuration, borderWidth = borderWidth,
        });

    /// <summary>Stops a named pulse immediately.</summary>
    public static void StopPulse(string key) => _pulses.Remove(key);

    /// <summary>Returns true if a pulse with this key is currently active.</summary>
    public static bool IsPulsing(string key) => _pulses.ContainsKey(key);

    /// <summary>
    /// Returns the current envelope intensity (0-1) for the given pulse key.
    /// Returns 0 if no pulse is active. Use this to apply secondary effects in sync
    /// with the pulse (e.g. darkening button text while the background brightens).
    /// </summary>
    public static float GetPulseIntensity(string key)
    {
        if (!_pulses.TryGetValue(key, out var entry)) return 0f;
        double now = EditorApplication.timeSinceStartup;
        if (now >= entry.endTime) return 0f;
        float phase = (float)((now - entry.startTime) % entry.cycleDuration / entry.cycleDuration);
        return Mathf.Sin(Mathf.PI * phase);
    }

    /// <summary>
    /// Draws the pulse overlay for this key onto rect.
    /// Call every repaint BEFORE drawing your control so the overlay is behind it.
    /// Safe to call every frame — does nothing if no active pulse exists for this key.
    /// </summary>
    public static void DrawPulse(string key, Rect rect, int cornerRadius = 0)
    {
        if (Event.current.type != EventType.Repaint) return;
        if (!_pulses.TryGetValue(key, out var entry)) return;

        double now = EditorApplication.timeSinceStartup;

        if (now >= entry.endTime)
        {
            _pulses.Remove(key);
            return;
        }

        // Envelope: sin(π * phase) → smoothly 0→1→0 per cycle
        float phase     = (float)((now - entry.startTime) % entry.cycleDuration / entry.cycleDuration);
        float intensity = Mathf.Sin(Mathf.PI * phase);

        var prev = GUI.color;

        if (entry.mode == PulseMode.Fill || entry.mode == PulseMode.FillAndBorder)
        {
            GUI.color = BlendColor(entry.color, entry.blend, intensity);
            DrawFillShape(rect, cornerRadius);
        }

        if (entry.mode == PulseMode.Border || entry.mode == PulseMode.FillAndBorder)
        {
            GUI.color = BlendColor(entry.borderColor, entry.blend, intensity);
            DrawBorderRects(rect, entry.borderWidth);
        }

        GUI.color = prev;
    }

    // ===== Internals ==========================================================

    private static Color BlendColor(Color color, PulseBlend blend, float intensity)
    {
        switch (blend)
        {
            case PulseBlend.Multiply:
                return Color.Lerp(Color.white, color, intensity);
            case PulseBlend.Alpha:
                return new Color(color.r, color.g, color.b, color.a * intensity);
            default:
                return color;
        }
    }

    private class PulseEntry
    {
        public Color      color;
        public Color      borderColor;
        public PulseMode  mode;
        public PulseBlend blend;
        public double     cycleDuration;
        public double     startTime;
        public double     endTime;
        public float      borderWidth;
    }

    private static readonly Dictionary<string, PulseEntry> _pulses = new Dictionary<string, PulseEntry>();

    private static void DrawFillShape(Rect rect, int cornerRadius)
    {
#if UNITY_2021_2_OR_NEWER
        if (cornerRadius > 0)
        {
            float cr    = Mathf.Min(cornerRadius, rect.width * 0.5f, rect.height * 0.5f);
            var   crVec = new Vector4(cr, cr, cr, cr);
            GUI.DrawTexture(rect, Texture2D.whiteTexture,
                ScaleMode.StretchToFill, true, 0f, Color.white, Vector4.zero, crVec);
            return;
        }
#endif
        GUI.DrawTexture(rect, EditorGUIUtility.whiteTexture);
    }

    private static void DrawBorderRects(Rect r, float bw)
    {
        var t = EditorGUIUtility.whiteTexture;
        GUI.DrawTexture(new Rect(r.x,         r.y,         r.width, bw),      t);
        GUI.DrawTexture(new Rect(r.x,         r.yMax - bw, r.width, bw),      t);
        GUI.DrawTexture(new Rect(r.x,         r.y,         bw, r.height),     t);
        GUI.DrawTexture(new Rect(r.xMax - bw, r.y,         bw, r.height),     t);
    }
}

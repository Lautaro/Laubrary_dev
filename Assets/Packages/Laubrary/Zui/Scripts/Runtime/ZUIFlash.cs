// ZUIFlash — shared flash-highlight state for the runtime rendering path. The editor toolkit's own
// flash (ZUI.StartFlash/DrawFlashOverlayIfNeeded in Editor/ZUI.cs, triggered by the flash-icon buttons in
// the Style Editor) only ever highlighted editor-drawn controls. This gives ZUISheet.DrawBox/Button
// (runtime) the same highlight, so flashing a style in the Zeditor also highlights a Play-mode HUD using
// that sheet — not just editor windows.
//
// Editor-only (wrapped in UNITY_EDITOR): this is a diagnostic aid, not a shipped-build feature, so it
// compiles out of a real build entirely. ZUISheet's call sites are wrapped the same way. ZUI.StartFlash
// forwards into Start() below alongside its own existing editor-only state — nothing about the editor
// toolkit's own flash behavior changes.

#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

public enum ZUIFlashKind { Button, Box }

public static class ZUIFlash
{
    /// <summary>When true, flash overlay drawing is suppressed (mirrors ZUI.SuppressFlash).</summary>
    public static bool Suppress { get; set; }

    static string _styleName;
    static ZUIFlashKind _kind;
    static ZUIStyleSheetAsset _sheet;
    static double _endTime;
    static float _interval = 0.12f;

    /// <summary>Called from ZUI.StartFlash — not meant to be started independently.</summary>
    public static void Start(string styleName, ZUIFlashKind kind, ZUIStyleSheetAsset sheet, float interval, int count)
    {
        _styleName = styleName;
        _kind = kind;
        _sheet = sheet;
        _interval = interval;
        _endTime = EditorApplication.timeSinceStartup + count * interval;
    }

    /// <summary>Call right after drawing a runtime box/button's visual. No-ops instantly unless this
    /// exact (name, kind, sheet) is the one currently flashing.</summary>
    public static void DrawOverlayIfNeeded(Rect rect, string defName, ZUIFlashKind kind, ZUIStyleSheetAsset owningSheet)
    {
        if (Suppress) return;
        if (string.IsNullOrEmpty(_styleName) || defName != _styleName || kind != _kind) return;
        if (_sheet != null && owningSheet != null && _sheet != owningSheet) return;

        double t = EditorApplication.timeSinceStartup;
        if (t > _endTime) { _styleName = null; return; }

        int phase = (int)((t % (_interval * 2)) / _interval);
        var color = phase == 0 ? Color.white : Color.black;

        var prev = GUI.color;
        GUI.color = color;
        float bw = 2f;
        GUI.DrawTexture(new Rect(rect.x, rect.y, rect.width, bw), Texture2D.whiteTexture);
        GUI.DrawTexture(new Rect(rect.x, rect.yMax - bw, rect.width, bw), Texture2D.whiteTexture);
        GUI.DrawTexture(new Rect(rect.x, rect.y, bw, rect.height), Texture2D.whiteTexture);
        GUI.DrawTexture(new Rect(rect.xMax - bw, rect.y, bw, rect.height), Texture2D.whiteTexture);
        GUI.color = prev;
    }
}
#endif

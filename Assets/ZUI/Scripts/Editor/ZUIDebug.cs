// ZUIDebug.cs
// Right-click any ZUI control while StyleDebugMode is on to inspect its style info.
// Toggle via the menu: ZUI / Toggle Style Debug Mode
//
// How it works:
//   ContextClick frame: every ZUI control checks rect.Contains(mousePosition) directly.
//   ALL hit controls call RegisterCandidate — the event is NOT consumed, so every control
//   in the frame sees the same live ContextClick with correct local-space coordinates.
//   RegisterCandidate queues a delayCall Repaint on the first hit.
//   On the next Repaint: TryShowPendingMenu (called at the top of each window's
//   OnGUI) picks the winner — highest priority (Button > Label > Box), then
//   smallest area — and shows the GenericMenu.
//   TryShowPendingMenu must be called from each window's OnGUI, not from individual
//   draw helpers, to avoid premature firing on intermediate Repaints.

using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

public static partial class ZUI
{
    // ── Public toggle ─────────────────────────────────────────────────────────

    public static bool StyleDebugMode { get; private set; }

    [MenuItem("Tools/ZUI/Toggle Style Debug Mode")]
    static void ToggleStyleDebugMode()
    {
        StyleDebugMode = !StyleDebugMode;
        Menu.SetChecked("ZUI/Toggle Style Debug Mode", StyleDebugMode);
        foreach (var w in Resources.FindObjectsOfTypeAll<EditorWindow>())
            w.Repaint();
    }

    // ── Internal access (used by ZUIStyleDef.cs) ──────────────────────────────

    internal static string _pendingBoxStyle    = ZUIStyle.Default;
    internal static bool   _pendingBoxStyleSet;

    // ── Core hook registration ────────────────────────────────────────────────
    // ZUIBoxDef now lives in the Runtime assembly and can't call back into this
    // editor-only ZUI static directly. It invokes delegates on ZUICore instead;
    // we wire them up here on load so box flash + style-debug keep working in-editor.
    [InitializeOnLoadMethod]
    static void RegisterCoreHooks()
    {
        ZUICore.BoxDebugHook = (def, styleName, rect) =>
        {
            if (CheckDebugBoxClick(rect))
            {
                // Prefer the caller-provided style key (via BoxScope) when available,
                // otherwise fall back to the def's own name — never a generic "Default".
                var debugStyle = _pendingBoxStyleSet ? _pendingBoxStyle.ToString() : styleName;
                _pendingBoxStyleSet = false;
                CollectBoxDebugInfo(def, debugStyle, rect);
            }
            else _pendingBoxStyleSet = false;
        };
        ZUICore.BoxFlashHook = (rect, styleName, cr) =>
            DrawFlashOverlayIfNeeded(rect, styleName, cr, FlashDefType.Box);
    }

    // ── Candidate system ──────────────────────────────────────────────────────

    enum DebugHitPriority { Box = 0, Label = 1, Button = 2 }

    internal struct DebugEntry { public string label; public string value; }

    struct DebugCandidate
    {
        public DebugHitPriority priority;
        public float            area;
        public List<DebugEntry> entries;
    }

    static List<DebugCandidate> _candidates;
    static bool                 _flushQueued;

    // Register a candidate. On first hit per frame: queues a repaint.
    // The ContextClick event is never consumed — every control sees it with correct local coords.
    static void RegisterCandidate(DebugHitPriority priority, Rect rect, List<DebugEntry> entries)
    {
        if (!_flushQueued)
        {
            _flushQueued = true;
            EditorApplication.delayCall += () =>
            {
                foreach (var w in Resources.FindObjectsOfTypeAll<EditorWindow>())
                    w.Repaint();
            };
        }

        if (_candidates == null) _candidates = new List<DebugCandidate>();
        _candidates.Add(new DebugCandidate
        {
            priority = priority,
            area     = rect.width * rect.height,
            entries  = entries,
        });
    }

    // Called at the top of each ZUI draw method every frame.
    // Shows the menu on the Repaint that follows the click frame.
    public static void TryShowPendingMenu()
    {
        if (!_flushQueued) return;
        if (Event.current.type != EventType.Repaint) return;

        _flushQueued = false;
        var candidates = _candidates;
        _candidates    = null;

        if (candidates == null || candidates.Count == 0) return;

        // Pick best: highest priority, then smallest area.
        var winner = candidates[0];
        for (int i = 1; i < candidates.Count; i++)
        {
            var c = candidates[i];
            if (c.priority > winner.priority) { winner = c; continue; }
            if (c.priority == winner.priority && c.area < winner.area) winner = c;
        }

        var entries = winner.entries;
        if (entries == null || entries.Count == 0) return;

        var menu = new GenericMenu();
        menu.AddDisabledItem(new GUIContent("── ZUI Style Inspector ──"));
        menu.AddSeparator("");
        foreach (var e in entries)
        {
            var captured = e;
            menu.AddItem(new GUIContent($"{captured.label}:  {captured.value}   [copy]"),
                false, () => EditorGUIUtility.systemCopyBuffer = captured.value);
        }
        menu.AddSeparator("");
        menu.AddItem(new GUIContent("Copy All"), false, () =>
        {
            var sb = new System.Text.StringBuilder();
            foreach (var e in entries) sb.AppendLine($"{e.label}: {e.value}");
            EditorGUIUtility.systemCopyBuffer = sb.ToString().TrimEnd();
        });
        menu.AddSeparator("");
        menu.AddItem(new GUIContent("Exit Style Debug Mode"), false, () =>
        {
            StyleDebugMode = false;
            Menu.SetChecked("ZUI/Toggle Style Debug Mode", false);
            foreach (var w in Resources.FindObjectsOfTypeAll<EditorWindow>())
                w.Repaint();
        });
        menu.ShowAsContext();
    }

    // ── Check helpers ─────────────────────────────────────────────────────────
    // Returns true if a ContextClick landed inside rect this frame.

    static bool IsDebugHit(Rect rect)
    {
        if (!StyleDebugMode) return false;
        if (Event.current.type != EventType.ContextClick) return false;
        return rect.Contains(Event.current.mousePosition);
    }

    internal static bool CheckDebugBoxClick(Rect rect)     => IsDebugHit(rect);
    internal static bool CheckDebugContextClick(Rect rect) => IsDebugHit(rect);

    // ── Record helpers called from Collect* ───────────────────────────────────

    static void RecordBoxHit(Rect rect, List<DebugEntry> entries)
        => RegisterCandidate(DebugHitPriority.Box, rect, entries);

    static void RecordButtonHit(Rect rect, List<DebugEntry> entries)
        => RegisterCandidate(DebugHitPriority.Button, rect, entries);

    static void RecordLabelHit(Rect rect, List<DebugEntry> entries)
        => RegisterCandidate(DebugHitPriority.Label, rect, entries);

    // ── Entry helpers ─────────────────────────────────────────────────────────

    static List<DebugEntry> MakeEntries() => new List<DebugEntry>();

    static void Add(List<DebugEntry> list, string label, string value)
        => list.Add(new DebugEntry { label = label, value = value });

    // ── Collect helpers (called from draw code, pass rect for area comparison) ─

    internal static void CollectButtonDebugInfo(ZUIButtonDef def, string styleName, Rect rect)
    {
        var e = MakeEntries();
        Add(e, "Button style", styleName);
        RecordButtonHit(rect, e);
    }

    internal static void CollectBoxDebugInfo(ZUIBoxDef def, string styleName, Rect rect)
    {
        var e = MakeEntries();
        Add(e, "Box style", styleName);
        RecordBoxHit(rect, e);
    }

    internal static void CollectTextDebugInfo(ZUITextStyleDef def, ZTextStyle style, Rect rect)
    {
        var e = MakeEntries();
        Add(e, "Text style", style.ToString());
        RecordLabelHit(rect, e);
    }

    internal static void CollectTextDebugInfo(ZUITextStyleDef styleDef, ZTextStyle style, ZUITextDef fallbackTextDef, Rect rect)
    {
        if (styleDef != null) { CollectTextDebugInfo(styleDef, style, rect); return; }
        var e = MakeEntries();
        Add(e, "Text style", style.ToString() + " (no sheet def — fallback)");
        RecordLabelHit(rect, e);
    }

    internal static void CollectTextDebugInfo(ZUITextDef textDef, ZTextStyle style, Rect rect)
    {
        var e = MakeEntries();
        Add(e, "Text style", style.ToString());
        RecordLabelHit(rect, e);
    }

    internal static void CollectSliderDebugInfo(ZUISliderDef def, string styleName, Rect rect, bool isRange)
    {
        var e = MakeEntries();
        Add(e, "Slider style", styleName);
        Add(e, "Type",         isRange ? "Range (min/max)" : "Single value");
        Add(e, "Track",        def.track?.name     ?? "(none)");
        Add(e, "Track fill",   def.trackFill?.name ?? "(none)");
        Add(e, "Thumb",        def.thumb?.name     ?? "(none)");
        RecordButtonHit(rect, e);
    }

}

// ZUIMultiSelect.cs
// Multi-selection controls for ZUI: CycleButton, CycleButtonWithArrows, MiniRadio.
//
// CycleButton:   single button that cycles through options on click, sized to longest label.
// CycleArrows:   same as CycleButton but with left/right arrow buttons flanking.
// MiniRadio:     tight inline labels (horizontal or vertical), selected one is highlighted.

using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

public static partial class ZUI
{
    // ===== CycleButton ========================================================
    // A single button that displays the current selection. Clicking cycles forward.
    // Right-clicking cycles backward. Sized to the widest label so it doesn't jump.

    public static int CycleButton(int selected, string[] labels,
                                   string style = Style.Default,
                                   params GUILayoutOption[] options)
    {
        var sheet = ActiveSheet;
        var def   = sheet?.FindButton(style);
        if (def == null)
        {
            // Fallback: Unity popup
            return EditorGUILayout.Popup(selected, labels, options);
        }

        // Measure widest label to keep button stable
        var labelStyle = def.GetLabelStyle();
        float maxW = 0f;
        for (int i = 0; i < labels.Length; i++)
        {
            float w = labelStyle.CalcSize(new GUIContent(labels[i])).x;
            if (w > maxW) maxW = w;
        }
        // Add padding for the button chrome
        float padH = def.padding.PadLeft + def.padding.PadRight;
        float minW = maxW + padH + 4f;

        var content = new GUIContent(labels[Mathf.Clamp(selected, 0, labels.Length - 1)]);
        var rect = GUILayoutUtility.GetRect(content, labelStyle,
            AppendMinWidth(options, minW));

        return DrawCycleButton(rect, selected, labels, content, def, style);
    }

    public static int CycleButton(Rect rect, int selected, string[] labels,
                                   string style = Style.Default)
    {
        var sheet = ActiveSheet;
        var def   = sheet?.FindButton(style);
        if (def == null)
            return EditorGUI.Popup(rect, selected, labels);

        var content = new GUIContent(labels[Mathf.Clamp(selected, 0, labels.Length - 1)]);
        return DrawCycleButton(rect, selected, labels, content, def, style);
    }

    static int DrawCycleButton(Rect rect, int selected, string[] labels,
                                GUIContent content, ZUIButtonDef def, string debugStyle)
    {
        if (CheckDebugContextClick(rect))
        {
            CollectButtonDebugInfo(def, debugStyle, rect);
            return selected;
        }

        int id = GUIUtility.GetControlID(FocusType.Passive, rect);
        var ev = Event.current;
        bool isHover  = rect.Contains(ev.mousePosition);
        bool isActive = GUIUtility.hotControl == id;

        TweenNotifyHover(id, isHover, def);
        TweenNotifyActive(id, isActive, def);

        switch (ev.type)
        {
            case EventType.MouseDown:
                if (isHover && (ev.button == 0 || ev.button == 1))
                {
                    GUIUtility.hotControl = id;
                    ev.Use();
                }
                break;

            case EventType.MouseUp:
                if (isActive)
                {
                    GUIUtility.hotControl = 0;
                    ev.Use();
                    if (isHover)
                    {
                        int dir = ev.button == 1 ? -1 : 1;
                        selected = (selected + dir + labels.Length) % labels.Length;
                        GUI.changed = true;
                    }
                }
                break;
        }

        if (ev.type == EventType.Repaint)
        {
            int r = SimulateLegacyCorners ? 0 : def.GetResolvedCornerRadius();
            var s = isActive ? ZUIButtonDrawState.Active : (isHover ? ZUIButtonDrawState.Hover : ZUIButtonDrawState.Normal);
            DrawButtonCellVisual(rect, id, def, s, r, content);
        }

        return selected;
    }

    // ===== CycleArrows ========================================================
    // Left arrow | current label | right arrow — all in one row.

    public static int CycleArrows(int selected, string[] labels,
                                   string style = Style.Default,
                                   string arrowStyle = "",
                                   params GUILayoutOption[] options)
    {
        var sheet = ActiveSheet;
        var def   = sheet?.FindButton(style);
        if (def == null)
            return EditorGUILayout.Popup(selected, labels, options);

        string arrStyle = string.IsNullOrEmpty(arrowStyle) ? style : arrowStyle;

        // Measure widest label
        var labelStyle = def.GetLabelStyle();
        float maxW = 0f;
        for (int i = 0; i < labels.Length; i++)
        {
            float w = labelStyle.CalcSize(new GUIContent(labels[i])).x;
            if (w > maxW) maxW = w;
        }
        float padH = def.padding.PadLeft + def.padding.PadRight;
        float arrowW = 20f;
        float totalMinW = maxW + padH + 4f + arrowW * 2f + 2f;

        var totalRect = GUILayoutUtility.GetRect(GUIContent.none, GUIStyle.none,
            AppendMinWidth(options, totalMinW));

        return DrawCycleArrows(totalRect, selected, labels, def, arrStyle);
    }

    public static int CycleArrows(Rect rect, int selected, string[] labels,
                                   string style = Style.Default,
                                   string arrowStyle = "")
    {
        var sheet = ActiveSheet;
        var def   = sheet?.FindButton(style);
        if (def == null)
            return EditorGUI.Popup(rect, selected, labels);

        string arrStyle = string.IsNullOrEmpty(arrowStyle) ? style : arrowStyle;
        return DrawCycleArrows(rect, selected, labels, def, arrStyle);
    }

    static int DrawCycleArrows(Rect rect, int selected, string[] labels,
                                ZUIButtonDef def, string arrowStyle)
    {
        float arrowW = 20f;
        float gap = 1f;
        var leftRect  = new Rect(rect.x, rect.y, arrowW, rect.height);
        var rightRect = new Rect(rect.xMax - arrowW, rect.y, arrowW, rect.height);
        var centerRect = new Rect(rect.x + arrowW + gap, rect.y,
                                   rect.width - arrowW * 2f - gap * 2f, rect.height);

        // Draw center label as a cycle button (click to cycle)
        var content = new GUIContent(labels[Mathf.Clamp(selected, 0, labels.Length - 1)]);
        selected = DrawCycleButton(centerRect, selected, labels, content, def, def.name);

        // Arrow buttons
        if (Button(leftRect, "\u25C0", arrowStyle))
        {
            selected = (selected - 1 + labels.Length) % labels.Length;
            GUI.changed = true;
        }
        if (Button(rightRect, "\u25B6", arrowStyle))
        {
            selected = (selected + 1) % labels.Length;
            GUI.changed = true;
        }

        return selected;
    }

    // ===== MiniRadio ==========================================================
    // Tight inline labels — selected one draws with Active state, others with Normal.
    // Horizontal by default. Use MiniRadioVertical for vertical layout.

    public static int MiniRadio(int selected, string[] labels,
                                 string style = Style.Default,
                                 bool shaped = true,
                                 params GUILayoutOption[] options)
    {
        var sheet = ActiveSheet;
        var def   = sheet?.FindButton(style);

        // Measure widest label for uniform button width
        float uniformW = 0f;
        if (def != null)
        {
            var measureStyle = def.GetLabelStyle();
            float padH = def.padding.PadLeft + def.padding.PadRight + 4f;
            for (int i = 0; i < labels.Length; i++)
            {
                float w = measureStyle.CalcSize(new GUIContent(labels[i])).x + padH;
                if (w > uniformW) uniformW = w;
            }
        }

        // Zero-margin style for shaped mode (no gaps between buttons)
        GUIStyle noMargin = shaped ? new GUIStyle { margin = new RectOffset(0,0,0,0) } : GUIStyle.none;

        GUILayout.BeginHorizontal();
        for (int i = 0; i < labels.Length; i++)
        {
            bool isSel = (i == selected);
            if (def != null)
            {
                var labelContent = new GUIContent(labels[i]);
                var labelStyle = def.GetLabelStyle(isSel ? ZUIButtonDrawState.ToggleOn : ZUIButtonDrawState.Normal);
                var sz = labelStyle.CalcSize(labelContent);
                // Per-cell layout options: caller options first (so GUILayout.Height takes effect),
                // then Width last so it overrides any caller-supplied GUILayout.Width (the radio
                // is width-uniform across cells).
                var cellOpts = new GUILayoutOption[options.Length + 1];
                for (int o = 0; o < options.Length; o++) cellOpts[o] = options[o];
                cellOpts[options.Length] = GUILayout.Width(uniformW);
                var rect = GUILayoutUtility.GetRect(uniformW, sz.y, noMargin, cellOpts);

                Rect drawRect = rect;

                int id = GUIUtility.GetControlID(FocusType.Passive, rect);
                bool hover = rect.Contains(Event.current.mousePosition);
                TweenNotifyHover(id, hover, def);

                if (Event.current.type == EventType.MouseDown && hover && Event.current.button == 0)
                {
                    selected = i;
                    GUI.changed = true;
                    Event.current.Use();
                }

                if (Event.current.type == EventType.Repaint)
                {
                    var state = isSel ? ZUIButtonDrawState.ToggleOn
                              : hover ? ZUIButtonDrawState.Hover
                              : ZUIButtonDrawState.Normal;
                    if (shaped)
                    {
                        int pillR = Mathf.RoundToInt(drawRect.height / 2f);
                        bool isFirst = (i == 0), isLast = (i == labels.Length - 1);
                        var mask = isFirst && isLast ? ZUICornerMask.All
                                 : isFirst          ? ZUICornerMask.Left
                                 : isLast           ? ZUICornerMask.Right
                                 :                    ZUICornerMask.Square;
                        DrawButtonCellVisual(drawRect, id, def, state, pillR, labelContent, cornerMask: mask);
                    }
                    else
                    {
                        int r = SimulateLegacyCorners ? 0 : def.GetResolvedCornerRadius();
                        DrawButtonCellVisual(drawRect, id, def, state, r, labelContent);
                    }
                }
            }
            else
            {
                // Fallback
                var s = isSel ? EditorStyles.miniButtonMid : EditorStyles.miniButton;
                if (GUILayout.Toggle(isSel, labels[i], s, options) && !isSel)
                    selected = i;
            }
        }
        GUILayout.EndHorizontal();
        return selected;
    }

    public static int MiniRadio(Rect rect, int selected, string[] labels,
                                 string style = Style.Default)
    {
        var sheet = ActiveSheet;
        var def   = sheet?.FindButton(style);
        float itemW = rect.width / labels.Length;

        for (int i = 0; i < labels.Length; i++)
        {
            var itemRect = new Rect(rect.x + i * itemW, rect.y, itemW, rect.height);
            bool isSel = (i == selected);

            if (def != null)
            {
                int id = GUIUtility.GetControlID(FocusType.Passive, itemRect);
                bool hover = itemRect.Contains(Event.current.mousePosition);
                TweenNotifyHover(id, hover, def);

                if (Event.current.type == EventType.MouseDown && hover && Event.current.button == 0)
                {
                    selected = i;
                    GUI.changed = true;
                    Event.current.Use();
                }

                if (Event.current.type == EventType.Repaint)
                {
                    int r = SimulateLegacyCorners ? 0 : def.GetResolvedCornerRadius();
                    var state = isSel ? ZUIButtonDrawState.ToggleOn
                              : hover ? ZUIButtonDrawState.Hover
                              : ZUIButtonDrawState.Normal;
                    DrawButtonCellVisual(itemRect, id, def, state, r, new GUIContent(labels[i]));
                }
            }
            else
            {
                if (GUI.Toggle(itemRect, isSel, labels[i], EditorStyles.miniButton) && !isSel)
                    selected = i;
            }
        }
        return selected;
    }

    public static int MiniRadioVertical(int selected, string[] labels,
                                         string style = Style.Default,
                                         bool shaped = true,
                                         params GUILayoutOption[] options)
    {
        // Measure widest label for uniform button width
        var sheet = ActiveSheet;
        var def = sheet?.FindButton(style);
        float uniformW = 0f;
        if (def != null)
        {
            var measureStyle = def.GetLabelStyle();
            float padH = def.padding.PadLeft + def.padding.PadRight + 4f;
            for (int i = 0; i < labels.Length; i++)
            {
                float w = measureStyle.CalcSize(new GUIContent(labels[i])).x + padH;
                if (w > uniformW) uniformW = w;
            }
        }

        // Override options with uniform width if measured
        var uniformOpts = uniformW > 0f
            ? new[] { GUILayout.Width(uniformW) }
            : options;

        GUIStyle noMargin = shaped ? new GUIStyle { margin = new RectOffset(0,0,0,0) } : GUIStyle.none;
        float itemH = def != null ? def.GetLabelStyle().CalcSize(new GUIContent("X")).y : 18f;

        GUILayout.BeginVertical();
        for (int i = 0; i < labels.Length; i++)
        {
            bool isSel = (i == selected);
            ZUICornerMask mask = ZUICornerMask.None;
            if (shaped)
            {
                bool isFirst = (i == 0), isLast = (i == labels.Length - 1);
                var rect = GUILayoutUtility.GetRect(uniformW, itemH, noMargin, GUILayout.Width(uniformW));

                int id = GUIUtility.GetControlID(FocusType.Passive, rect);
                bool hover = rect.Contains(Event.current.mousePosition);
                TweenNotifyHover(id, hover, def);

                if (Event.current.type == EventType.MouseDown && hover && Event.current.button == 0)
                {
                    selected = i;
                    GUI.changed = true;
                    Event.current.Use();
                }

                if (Event.current.type == EventType.Repaint)
                {
                    // Pill-shaped: force radius to half-width for fully rounded ends
                    int pillR = Mathf.RoundToInt(rect.width / 2f);
                    var state = isSel ? ZUIButtonDrawState.ToggleOn
                              : hover ? ZUIButtonDrawState.Hover
                              : ZUIButtonDrawState.Normal;
                    var mask2 = isFirst && isLast ? ZUICornerMask.All
                              : isFirst          ? ZUICornerMask.Top
                              : isLast           ? ZUICornerMask.Bottom
                              :                    ZUICornerMask.Square;
                    DrawButtonCellVisual(rect, id, def, state, pillR, new GUIContent(labels[i]), cornerMask: mask2);
                }
                continue;
            }
            if (Toggle(isSel, labels[i], style, mask, uniformOpts) && !isSel)
                selected = i;
        }
        GUILayout.EndVertical();
        return selected;
    }

    // ===== MicroRadio ============================================================
    // A single button showing a sliding window of 3 labels: [prev] [current] [next].
    // Click cycles forward, right-click cycles backward (matching CycleButton).
    // Text styles: activeTextStyle for the current option, sideTextStyle for neighbors.
    // If empty, active uses the button def's text; side derives a dimmer version.
    // wrap: when true, neighbors wrap around (Option1's left = last option).
    // sideMaxChars: max characters for side labels before truncation with ellipsis.

    // Animation state for pixel-scroll transitions
    struct MicroRadioAnim
    {
        public int   prevSelected;
        public double startTime;
        public int   direction; // +1 forward, -1 backward
    }
    static readonly Dictionary<int, MicroRadioAnim> _microRadioAnims = new Dictionary<int, MicroRadioAnim>();
    public static float MicroRadioAnimDuration = 0.24f;

    /// <summary>Returns true if any MicroRadio scroll animation is still active.</summary>
    internal static bool HasActiveMicroRadioAnim()
    {
        if (_microRadioAnims.Count == 0) return false;
        double now = EditorApplication.timeSinceStartup;
        // Clean up expired entries
        var expired = new System.Collections.Generic.List<int>();
        foreach (var kv in _microRadioAnims)
            if (now - kv.Value.startTime > MicroRadioAnimDuration) expired.Add(kv.Key);
        foreach (var k in expired) _microRadioAnims.Remove(k);
        return _microRadioAnims.Count > 0;
    }

    public static int MicroRadio(int selected, string[] labels,
                                  string style = Style.Default,
                                  string activeTextStyle = "",
                                  string sideTextStyle = "",
                                  bool wrap = false,
                                  int sideMaxChars = 5,
                                  params GUILayoutOption[] options)
    {
        var sheet = ActiveSheet;
        var def   = sheet?.FindButton(style);
        if (def == null)
            return EditorGUILayout.Popup(selected, labels, options);

        int maxChars = Mathf.Max(1, sideMaxChars);

        // Measure widest center label (any label can be the current one)
        var labelStyle = def.GetLabelStyle();
        float maxCenterW = 0f;
        for (int i = 0; i < labels.Length; i++)
        {
            float w = labelStyle.CalcSize(new GUIContent(labels[i])).x;
            if (w > maxCenterW) maxCenterW = w;
        }

        // Side labels are truncated, so measure the widest truncated side
        float maxSideW = 0f;
        for (int i = 0; i < labels.Length; i++)
        {
            string t = TruncateMicroLabel(labels[i], maxChars, true);
            float w = labelStyle.CalcSize(new GUIContent(t)).x;
            if (w > maxSideW) maxSideW = w;
        }

        float gap = 4f;
        float padH = def.padding.PadLeft + def.padding.PadRight;
        float fixedW = maxCenterW + (maxSideW + gap) * 2f + padH;
        float h = labelStyle.CalcSize(new GUIContent("X")).y;

        var rect = GUILayoutUtility.GetRect(fixedW, h, GUILayout.Width(fixedW));
        return DrawMicroRadio(rect, selected, labels, def, sheet, activeTextStyle, sideTextStyle, style, wrap, maxChars);
    }

    public static int MicroRadio(Rect rect, int selected, string[] labels,
                                  string style = Style.Default,
                                  string activeTextStyle = "",
                                  string sideTextStyle = "",
                                  bool wrap = false,
                                  int sideMaxChars = 5)
    {
        var sheet = ActiveSheet;
        var def   = sheet?.FindButton(style);
        if (def == null)
            return EditorGUI.Popup(rect, selected, labels);
        return DrawMicroRadio(rect, selected, labels, def, sheet, activeTextStyle, sideTextStyle, style, wrap, Mathf.Max(1, sideMaxChars));
    }

    static int DrawMicroRadio(Rect rect, int selected, string[] labels,
                               ZUIButtonDef def, ZUIStyleSheetAsset sheet,
                               string activeTextStyleName, string sideTextStyleName,
                               string debugStyle, bool wrap, int maxChars)
    {
        if (CheckDebugContextClick(rect))
        {
            CollectButtonDebugInfo(def, debugStyle, rect);
            return selected;
        }

        int id = GUIUtility.GetControlID(FocusType.Passive, rect);
        var ev = Event.current;
        bool isHover  = rect.Contains(ev.mousePosition);
        bool isActive = GUIUtility.hotControl == id;
        int  prevSelected = selected;

        TweenNotifyHover(id, isHover, def);
        TweenNotifyActive(id, isActive, def);

        switch (ev.type)
        {
            case EventType.MouseDown:
                if (isHover && ev.button == 0)
                {
                    GUIUtility.hotControl = id;
                    ev.Use();
                }
                break;
            case EventType.MouseUp:
                if (isActive)
                {
                    GUIUtility.hotControl = 0;
                    ev.Use();
                    if (isHover)
                    {
                        // Click left half = backward, right half = forward
                        int dir = ev.mousePosition.x < rect.center.x ? -1 : 1;
                        selected = (selected + dir + labels.Length) % labels.Length;
                        GUI.changed = true;

                        // Start scroll animation
                        _microRadioAnims[id] = new MicroRadioAnim
                        {
                            prevSelected = prevSelected,
                            startTime = EditorApplication.timeSinceStartup,
                            direction = dir
                        };
                        EnsureAnimUpdateRunning();
                    }
                }
                break;
        }

        if (ev.type == EventType.Repaint)
        {
            int r = SimulateLegacyCorners ? 0 : def.GetResolvedCornerRadius();
            var state = isActive ? ZUIButtonDrawState.Active
                      : isHover ? ZUIButtonDrawState.Hover
                      : ZUIButtonDrawState.Normal;

            // Draw button background
            def.DrawVisual(rect, state, r);

            // Resolve text styles
            GUIStyle activeStyle, sideStyle;

            if (!string.IsNullOrEmpty(activeTextStyleName))
            {
                var tsd = sheet.FindText(activeTextStyleName);
                activeStyle = tsd != null ? tsd.GetStyle(sheet) : def.GetLabelStyle(state);
            }
            else
                activeStyle = def.GetLabelStyle(state);

            if (!string.IsNullOrEmpty(sideTextStyleName))
            {
                var tsd = sheet.FindText(sideTextStyleName);
                sideStyle = tsd != null ? tsd.GetStyle(sheet) : new GUIStyle(activeStyle);
            }
            else
            {
                sideStyle = new GUIStyle(activeStyle);
                var c = sideStyle.normal.textColor;
                c.a *= 0.45f;
                sideStyle.normal.textColor = c;
                sideStyle.hover.textColor  = c;
                sideStyle.active.textColor = c;
            }

            // Fixed 3-zone layout: [left side] [center] [right side]
            activeStyle.alignment = TextAnchor.MiddleCenter;
            activeStyle.clipping  = TextClipping.Clip;
            sideStyle.clipping    = TextClipping.Clip;

            float padL = def.padding.PadLeft;
            float padR = def.padding.PadRight;
            float innerW = rect.width - padL - padR;

            // Measure widest center label for fixed center zone
            float centerW = 0f;
            for (int li = 0; li < labels.Length; li++)
            {
                float w = activeStyle.CalcSize(new GUIContent(labels[li])).x;
                if (w > centerW) centerW = w;
            }

            float gap = 4f;
            float sideW = (innerW - centerW - gap * 2f) / 2f;
            float x0 = rect.x + padL;

            // Compute animation offset
            float animOffset = 0f;
            if (_microRadioAnims.TryGetValue(id, out var anim))
            {
                float elapsed = (float)(EditorApplication.timeSinceStartup - anim.startTime);
                float t = Mathf.Clamp01(elapsed / MicroRadioAnimDuration);
                // Ease out
                t = 1f - (1f - t) * (1f - t);
                // Scroll distance = one "slot" width (sideW + gap)
                float fullSlide = (sideW + gap) * anim.direction;
                animOffset = fullSlide * (1f - t); // starts offset, ends at 0
            }

            // Resolve neighbor indices (with optional wrap)
            int n = labels.Length;
            int leftIdx  = wrap ? ((selected - 1 + n) % n) : (selected - 1);
            int rightIdx = wrap ? ((selected + 1) % n)     : (selected + 1);
            bool hasLeft  = wrap || leftIdx >= 0;
            bool hasRight = wrap || rightIdx < n;
            if (leftIdx < 0) leftIdx = 0;
            if (rightIdx >= n) rightIdx = n - 1;

            // Clip to button interior
            GUI.BeginClip(new Rect(rect.x + padL, rect.y, innerW, rect.height));
            float cx = animOffset; // local coords, 0 = left edge of inner area

            // Left side
            if (hasLeft && sideW > 0f)
            {
                sideStyle.alignment = TextAnchor.MiddleRight;
                string txt = TruncateMicroLabel(labels[leftIdx], maxChars, false);
                GUI.Label(new Rect(cx, 0f, sideW, rect.height), txt, sideStyle);
            }

            // Center
            {
                var centerRect = new Rect(cx + sideW + gap, 0f, centerW, rect.height);
                GUI.Label(centerRect, labels[selected], activeStyle);
            }

            // Right side
            if (hasRight && sideW > 0f)
            {
                sideStyle.alignment = TextAnchor.MiddleLeft;
                string txt = TruncateMicroLabel(labels[rightIdx], maxChars, true);
                GUI.Label(new Rect(cx + sideW + gap + centerW + gap, 0f, sideW, rect.height), txt, sideStyle);
            }

            GUI.EndClip();
        }

        DrawFlashOverlayIfNeeded(rect, def.name, def.GetResolvedCornerRadius(), FlashDefType.Button);
        return selected;
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    /// <summary>Truncates a label for MicroRadio side display.
    /// suffixEllipsis=true: "Option..." (right side), false: "...tion" (left side).</summary>
    static string TruncateMicroLabel(string label, int maxChars, bool suffixEllipsis)
    {
        if (label.Length <= maxChars) return label;
        if (suffixEllipsis)
            return label.Substring(0, maxChars) + "\u2026";
        else
            return "\u2026" + label.Substring(label.Length - maxChars);
    }

    static GUILayoutOption[] AppendMinWidth(GUILayoutOption[] options, float minW)
    {
        var result = new GUILayoutOption[options.Length + 1];
        options.CopyTo(result, 0);
        result[options.Length] = GUILayout.MinWidth(minW);
        return result;
    }
}

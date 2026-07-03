// ZUIAutoColorEditor.cs
// Editor UI for creating and viewing autocolors on palette entries.
// Provides gradient-painted mod sliders and the inline creation panel.

using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

public static class ZUIAutoColorEditor
{
    // ── Gradient texture cache ───────────────────────────────────────────────
    // Keyed by (baseColor, channel, otherMods) so textures rebuild when inputs change.

    enum ModChannel { Hue, Sat, Val }

    static readonly Dictionary<int, Texture2D> s_gradientCache = new Dictionary<int, Texture2D>();

    const int k_GradientWidth = 128;
    const int k_GradientHeight = 1;

    static int GradientHash(Color baseColor, ModChannel channel, float hueMod, float satMod, float valMod)
    {
        int h = baseColor.GetHashCode();
        h = h * 397 ^ (int)channel;
        h = h * 397 ^ hueMod.GetHashCode();
        h = h * 397 ^ satMod.GetHashCode();
        h = h * 397 ^ valMod.GetHashCode();
        return h;
    }

    static Texture2D GetOrBuildGradient(Color baseColor, ModChannel channel, float hueMod, float satMod, float valMod)
    {
        int hash = GradientHash(baseColor, channel, hueMod, satMod, valMod);
        if (s_gradientCache.TryGetValue(hash, out var existing) && existing != null)
            return existing;

        var tex = new Texture2D(k_GradientWidth, k_GradientHeight, TextureFormat.RGBA32, false)
        {
            filterMode = FilterMode.Bilinear,
            wrapMode   = TextureWrapMode.Clamp,
        };

        for (int x = 0; x < k_GradientWidth; x++)
        {
            float t = (float)x / (k_GradientWidth - 1);  // 0..1
            float mod = t * 2f - 1f;                       // -1..+1

            float h = hueMod, s = satMod, v = valMod;
            switch (channel)
            {
                case ModChannel.Hue: h = mod; break;
                case ModChannel.Sat: s = mod; break;
                case ModChannel.Val: v = mod; break;
            }

            Color c = ZUIColorHSL.ApplyAutoColorMod(baseColor, h, s, v);
            tex.SetPixel(x, 0, c);
        }

        tex.Apply();

        // Keep cache bounded
        if (s_gradientCache.Count > 64)
            s_gradientCache.Clear();

        s_gradientCache[hash] = tex;
        return tex;
    }

    // ── Mod slider ───────────────────────────────────────────────────────────

    const float k_SliderH      = 14f;
    const float k_LabelW       = 28f;
    const float k_ValueW       = 40f;
    const float k_ThumbW       = 4f;
    const float k_SliderMargin = 2f;

    /// <summary>
    /// Draws a single mod slider with a gradient-painted track showing resolved colors.
    /// Returns the new mod value.
    /// </summary>
    static float DrawModSlider(string label, float modValue, Color baseColor, ModChannel channel,
                                float hueMod, float satMod, float valMod, float sliderWidth)
    {
        GUILayout.BeginHorizontal();

        // Label
        EditorGUILayout.LabelField(label, EditorStyles.miniLabel, GUILayout.Width(k_LabelW), GUILayout.Height(k_SliderH));

        // Track rect
        float trackW = sliderWidth - k_LabelW - k_ValueW - k_SliderMargin * 2f;
        if (trackW < 40f) trackW = 40f;
        var trackRect = GUILayoutUtility.GetRect(trackW, k_SliderH, GUILayout.Width(trackW), GUILayout.Height(k_SliderH));

        // Draw gradient texture as track background
        if (Event.current.type == EventType.Repaint)
        {
            var tex = GetOrBuildGradient(baseColor, channel, hueMod, satMod, valMod);
            GUI.DrawTexture(trackRect, tex, ScaleMode.StretchToFill);

            // Border
            Color borderColor = new Color(0f, 0f, 0f, 0.4f);
            EditorGUI.DrawRect(new Rect(trackRect.x, trackRect.y, trackRect.width, 1f), borderColor);
            EditorGUI.DrawRect(new Rect(trackRect.x, trackRect.yMax - 1f, trackRect.width, 1f), borderColor);
            EditorGUI.DrawRect(new Rect(trackRect.x, trackRect.y, 1f, trackRect.height), borderColor);
            EditorGUI.DrawRect(new Rect(trackRect.xMax - 1f, trackRect.y, 1f, trackRect.height), borderColor);

            // Center mark (where mod = 0, i.e. base color)
            float centerX = trackRect.x + trackRect.width * 0.5f;
            EditorGUI.DrawRect(new Rect(centerX - 0.5f, trackRect.y, 1f, trackRect.height), new Color(1f, 1f, 1f, 0.3f));

            // Thumb at current position
            float thumbT = (modValue + 1f) * 0.5f;  // -1..+1 → 0..1
            float thumbX = trackRect.x + thumbT * trackRect.width - k_ThumbW * 0.5f;
            var thumbRect = new Rect(thumbX, trackRect.y, k_ThumbW, trackRect.height);
            EditorGUI.DrawRect(thumbRect, Color.white);
            // Inner dark line for contrast
            EditorGUI.DrawRect(new Rect(thumbX + 1f, trackRect.y + 1f, k_ThumbW - 2f, trackRect.height - 2f),
                               new Color(0f, 0f, 0f, 0.5f));
        }

        // Input: drag on the track
        int controlId = GUIUtility.GetControlID(FocusType.Passive);
        var evt = Event.current;
        switch (evt.type)
        {
            case EventType.MouseDown:
                if (trackRect.Contains(evt.mousePosition))
                {
                    GUIUtility.hotControl = controlId;
                    modValue = MouseToMod(evt.mousePosition.x, trackRect);
                    evt.Use();
                    GUI.changed = true;
                }
                break;

            case EventType.MouseDrag:
                if (GUIUtility.hotControl == controlId)
                {
                    modValue = MouseToMod(evt.mousePosition.x, trackRect);
                    evt.Use();
                    GUI.changed = true;
                }
                break;

            case EventType.MouseUp:
                if (GUIUtility.hotControl == controlId)
                {
                    GUIUtility.hotControl = 0;
                    evt.Use();
                }
                break;
        }

        // Value readout
        GUILayout.Space(k_SliderMargin);
        string display = channel == ModChannel.Hue
            ? $"{modValue * 60f:+0;-0;0}°"
            : $"{modValue * 100f:+0;-0;0}%";
        EditorGUILayout.LabelField(display, EditorStyles.miniLabel, GUILayout.Width(k_ValueW), GUILayout.Height(k_SliderH));

        GUILayout.EndHorizontal();
        return Mathf.Clamp(modValue, -1f, 1f);
    }

    static float MouseToMod(float mouseX, Rect trackRect)
    {
        float t = Mathf.Clamp01((mouseX - trackRect.x) / trackRect.width);
        return t * 2f - 1f;  // 0..1 → -1..+1
    }

    // ── Autocolor swatch row ─────────────────────────────────────────────────

    const float k_SwatchSize   = 24f;
    const float k_SwatchGap    = 2f;
    const float k_NameTagW     = 50f;
    const float k_NameTagH     = 12f;

    // Set by context menu callbacks (which run deferred)
    static bool s_pendingRemoval;

    /// <summary>Returns and clears the pending removal flag. Caller should mark sheet dirty when true.</summary>
    public static bool ConsumePendingRemoval()
    {
        if (!s_pendingRemoval) return false;
        s_pendingRemoval = false;
        return true;
    }

    /// <summary>
    /// Draws autocolor swatches inline (no BeginHorizontal / FlexibleSpace).
    /// Caller is responsible for surrounding horizontal group.
    /// Returns the index of a swatch that was clicked for editing, or -1.
    /// </summary>
    public static int DrawAutoColorSwatchesInline(ZUIPaletteColor entry)
    {
        if (entry.autoColors == null || entry.autoColors.Count == 0)
            return -1;

        int clickedIndex = -1;

        for (int i = 0; i < entry.autoColors.Count; i++)
        {
            var ac = entry.autoColors[i];
            Color resolved = ac.Resolve(entry.color);

            GUILayout.BeginVertical(GUILayout.Width(k_SwatchSize));

            var swRect = GUILayoutUtility.GetRect(k_SwatchSize, k_SwatchSize,
                GUILayout.Width(k_SwatchSize), GUILayout.Height(k_SwatchSize));

            if (Event.current.type == EventType.Repaint)
            {
                EditorGUI.DrawRect(swRect, resolved);
                Color borderColor = new Color(0f, 0f, 0f, 0.5f);
                EditorGUI.DrawRect(new Rect(swRect.x, swRect.y, swRect.width, 1f), borderColor);
                EditorGUI.DrawRect(new Rect(swRect.x, swRect.yMax - 1f, swRect.width, 1f), borderColor);
                EditorGUI.DrawRect(new Rect(swRect.x, swRect.y, 1f, swRect.height), borderColor);
                EditorGUI.DrawRect(new Rect(swRect.xMax - 1f, swRect.y, 1f, swRect.height), borderColor);
            }

            var nameStyle = new GUIStyle(EditorStyles.miniLabel)
            {
                fontSize  = 8,
                alignment = TextAnchor.UpperCenter,
                clipping  = TextClipping.Clip,
            };
            EditorGUILayout.LabelField(ac.name, nameStyle,
                GUILayout.Width(k_SwatchSize), GUILayout.Height(k_NameTagH));

            GUILayout.EndVertical();
            GUILayout.Space(k_SwatchGap);

            if (Event.current.type == EventType.MouseDown && swRect.Contains(Event.current.mousePosition))
            {
                if (Event.current.button == 1)
                {
                    int capturedIndex = i;
                    var menu = new GenericMenu();
                    menu.AddItem(new GUIContent($"Delete \"{ac.name}\""), false, () =>
                    {
                        entry.autoColors.RemoveAt(capturedIndex);
                        s_pendingRemoval = true;
                    });
                    menu.ShowAsContext();
                }
                else
                {
                    clickedIndex = i;
                }
                Event.current.Use();
            }
        }

        return clickedIndex;
    }

    /// <summary>
    /// Draws the autocolor swatches for a palette entry.
    /// Returns the index of a swatch that was clicked for editing, or -1.
    /// </summary>
    public static int DrawAutoColorSwatches(ZUIPaletteColor entry, float indent)
    {
        if (entry.autoColors == null || entry.autoColors.Count == 0)
            return -1;

        int clickedIndex = -1;

        GUILayout.BeginHorizontal();
        GUILayout.Space(indent);

        for (int i = 0; i < entry.autoColors.Count; i++)
        {
            var ac = entry.autoColors[i];
            Color resolved = ac.Resolve(entry.color);

            GUILayout.BeginVertical(GUILayout.Width(k_SwatchSize));

            var swRect = GUILayoutUtility.GetRect(k_SwatchSize, k_SwatchSize,
                GUILayout.Width(k_SwatchSize), GUILayout.Height(k_SwatchSize));

            if (Event.current.type == EventType.Repaint)
            {
                EditorGUI.DrawRect(swRect, resolved);
                // Border
                Color borderColor = new Color(0f, 0f, 0f, 0.5f);
                EditorGUI.DrawRect(new Rect(swRect.x, swRect.y, swRect.width, 1f), borderColor);
                EditorGUI.DrawRect(new Rect(swRect.x, swRect.yMax - 1f, swRect.width, 1f), borderColor);
                EditorGUI.DrawRect(new Rect(swRect.x, swRect.y, 1f, swRect.height), borderColor);
                EditorGUI.DrawRect(new Rect(swRect.xMax - 1f, swRect.y, 1f, swRect.height), borderColor);
            }

            // Name tag below swatch
            var nameStyle = new GUIStyle(EditorStyles.miniLabel)
            {
                fontSize  = 8,
                alignment = TextAnchor.UpperCenter,
                clipping  = TextClipping.Clip,
            };
            EditorGUILayout.LabelField(ac.name, nameStyle,
                GUILayout.Width(k_SwatchSize), GUILayout.Height(k_NameTagH));

            GUILayout.EndVertical();
            GUILayout.Space(k_SwatchGap);

            // Click detection on swatch
            if (Event.current.type == EventType.MouseDown && swRect.Contains(Event.current.mousePosition))
            {
                if (Event.current.button == 1)
                {
                    // Right-click: context menu
                    int capturedIndex = i;
                    var menu = new GenericMenu();
                    menu.AddItem(new GUIContent($"Delete \"{ac.name}\""), false, () =>
                    {
                        entry.autoColors.RemoveAt(capturedIndex);
                        s_pendingRemoval = true;
                    });
                    menu.ShowAsContext();
                }
                else
                {
                    clickedIndex = i;
                }
                Event.current.Use();
            }
        }

        GUILayout.FlexibleSpace();
        GUILayout.EndHorizontal();

        return clickedIndex;
    }

    // ── Autocolor create/edit panel ──────────────────────────────────────────

    struct EditState
    {
        public ZUIAutoColor draft;
        public ZUIAutoColor original;  // snapshot for revert on cancel (edit mode only)
        public int editIndex;          // -1 = creating new, >= 0 = editing existing
    }

    // Per-palette-entry edit state (keyed by palette entry name)
    static readonly Dictionary<string, EditState> s_editing = new Dictionary<string, EditState>();

    /// <summary>Returns true if we're currently creating or editing an autocolor for this entry.</summary>
    public static bool IsCreating(string paletteName) => s_editing.ContainsKey(paletteName);

    /// <summary>Starts creating a new autocolor for the given palette entry.</summary>
    public static void BeginCreate(string paletteName)
    {
        s_editing[paletteName] = new EditState { draft = new ZUIAutoColor { name = "New Color" }, editIndex = -1 };
    }

    /// <summary>Starts editing an existing autocolor by index.</summary>
    public static void BeginEdit(string paletteName, int index, ZUIAutoColor source)
    {
        s_editing[paletteName] = new EditState
        {
            draft = new ZUIAutoColor { name = source.name, hueMod = source.hueMod, satMod = source.satMod, valMod = source.valMod },
            original = new ZUIAutoColor { name = source.name, hueMod = source.hueMod, satMod = source.satMod, valMod = source.valMod },
            editIndex = index,
        };
    }

    /// <summary>Cancels creation/editing for the given palette entry.</summary>
    public static void CancelCreate(string paletteName)
    {
        s_editing.Remove(paletteName);
    }

    /// <summary>
    /// Draws the inline create/edit panel. Returns the finished ZUIAutoColor when confirmed, null otherwise.
    /// editedIndex is set to the index being edited (-1 for new).
    /// Pass autoColors list for live preview during editing.
    /// </summary>
    public static ZUIAutoColor DrawCreationPanel(string paletteName, Color baseColor, float indent, float availableWidth,
                                                  out int editedIndex, List<ZUIAutoColor> autoColors = null)
    {
        editedIndex = -1;
        if (!s_editing.TryGetValue(paletteName, out var state))
            return null;
        var draft = state.draft;
        editedIndex = state.editIndex;
        bool isEdit = state.editIndex >= 0;

        float panelWidth = availableWidth - indent;
        if (panelWidth < 160f) panelWidth = 160f;

        GUILayout.BeginHorizontal();
        GUILayout.Space(indent);

        // Panel background
        var panelRect = EditorGUILayout.BeginVertical(GUILayout.Width(panelWidth));
        if (Event.current.type == EventType.Repaint)
        {
            EditorGUI.DrawRect(panelRect, new Color(0.15f, 0.15f, 0.18f, 0.95f));
            // Subtle border
            Color bc = new Color(1f, 1f, 1f, 0.1f);
            EditorGUI.DrawRect(new Rect(panelRect.x, panelRect.y, panelRect.width, 1f), bc);
            EditorGUI.DrawRect(new Rect(panelRect.x, panelRect.yMax - 1f, panelRect.width, 1f), bc);
            EditorGUI.DrawRect(new Rect(panelRect.x, panelRect.y, 1f, panelRect.height), bc);
            EditorGUI.DrawRect(new Rect(panelRect.xMax - 1f, panelRect.y, 1f, panelRect.height), bc);
        }

        GUILayout.Space(4f);

        // Name field
        GUILayout.BeginHorizontal();
        GUILayout.Space(6f);
        EditorGUILayout.LabelField("Name", EditorStyles.miniLabel, GUILayout.Width(k_LabelW));
        draft.name = EditorGUILayout.TextField(draft.name, GUILayout.Width(panelWidth - k_LabelW - 16f));
        GUILayout.EndHorizontal();

        GUILayout.Space(3f);

        // Preview swatch (base → result)
        GUILayout.BeginHorizontal();
        GUILayout.Space(6f);
        Color resolved = draft.Resolve(baseColor);

        // Base color swatch
        var baseSwRect = GUILayoutUtility.GetRect(24f, 18f, GUILayout.Width(24f), GUILayout.Height(18f));
        // Arrow
        EditorGUILayout.LabelField("→", EditorStyles.miniLabel, GUILayout.Width(14f), GUILayout.Height(18f));
        // Result swatch
        var resultSwRect = GUILayoutUtility.GetRect(24f, 18f, GUILayout.Width(24f), GUILayout.Height(18f));

        if (Event.current.type == EventType.Repaint)
        {
            EditorGUI.DrawRect(baseSwRect, baseColor);
            EditorGUI.DrawRect(resultSwRect, resolved);

            Color brd = new Color(0f, 0f, 0f, 0.4f);
            DrawSwatchBorder(baseSwRect, brd);
            DrawSwatchBorder(resultSwRect, brd);
        }

        GUILayout.FlexibleSpace();
        GUILayout.EndHorizontal();

        GUILayout.Space(3f);

        // Mod sliders
        float sliderW = panelWidth - 12f;
        GUILayout.BeginVertical();
        GUILayout.Space(1f);

        // Each slider needs current values of all three mods for gradient context
        GUILayout.BeginHorizontal();
        GUILayout.Space(6f);
        GUILayout.BeginVertical();
        EditorGUI.BeginChangeCheck();
        draft.hueMod = DrawModSlider("Hue", draft.hueMod, baseColor, ModChannel.Hue, draft.hueMod, draft.satMod, draft.valMod, sliderW);
        draft.satMod = DrawModSlider("Sat", draft.satMod, baseColor, ModChannel.Sat, draft.hueMod, draft.satMod, draft.valMod, sliderW);
        draft.valMod = DrawModSlider("Val", draft.valMod, baseColor, ModChannel.Val, draft.hueMod, draft.satMod, draft.valMod, sliderW);
        if (EditorGUI.EndChangeCheck())
        {
            GUI.changed = true;
            // Live preview: write draft values to the actual autocolor during editing
            if (isEdit && autoColors != null && state.editIndex >= 0 && state.editIndex < autoColors.Count)
            {
                var live = autoColors[state.editIndex];
                live.name   = draft.name;
                live.hueMod = draft.hueMod;
                live.satMod = draft.satMod;
                live.valMod = draft.valMod;
            }
        }
        GUILayout.EndVertical();
        GUILayout.EndHorizontal();

        GUILayout.Space(4f);

        // Confirm / Cancel buttons
        GUILayout.BeginHorizontal();
        GUILayout.Space(6f);
        ZUIAutoColor result = null;
        if (GUILayout.Button(isEdit ? "Save" : "Add", EditorStyles.miniButton, GUILayout.Width(50f)))
        {
            result = draft;
            s_editing.Remove(paletteName);
        }
        if (GUILayout.Button("Cancel", EditorStyles.miniButton, GUILayout.Width(50f)))
        {
            // Revert live changes in edit mode
            if (isEdit && autoColors != null && state.editIndex >= 0 && state.editIndex < autoColors.Count && state.original != null)
            {
                var live = autoColors[state.editIndex];
                live.name   = state.original.name;
                live.hueMod = state.original.hueMod;
                live.satMod = state.original.satMod;
                live.valMod = state.original.valMod;
            }
            s_editing.Remove(paletteName);
        }
        GUILayout.EndHorizontal();

        GUILayout.Space(4f);

        EditorGUILayout.EndVertical();
        GUILayout.EndHorizontal();

        return result;
    }

    static void DrawSwatchBorder(Rect r, Color c)
    {
        EditorGUI.DrawRect(new Rect(r.x, r.y, r.width, 1f), c);
        EditorGUI.DrawRect(new Rect(r.x, r.yMax - 1f, r.width, 1f), c);
        EditorGUI.DrawRect(new Rect(r.x, r.y, 1f, r.height), c);
        EditorGUI.DrawRect(new Rect(r.xMax - 1f, r.y, 1f, r.height), c);
    }
}

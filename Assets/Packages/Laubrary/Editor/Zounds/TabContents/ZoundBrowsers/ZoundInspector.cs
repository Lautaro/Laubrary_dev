using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using UnityEngine.Audio;

namespace Laubrary.Zounds {

    /// <summary>
    /// Draws the detail fields and action buttons for a single zound.
    /// Used by BrowserTab in both singlecolumn and multicolumn layouts.
    ///
    /// Three public draw methods exist depending on context:
    ///   DrawMulticolumn   — full inspector panel that expands below a multicolumn row.
    ///                       Lays out 5 columns: [Edit+MS] [Fields A] [Fields B] [Tags] [Route/Conv/Dup/Del]
    ///   DrawSinglecolumn  — draws into pre-computed rects passed by DrawSinglecolumnRow.
    ///                       Edit and M/S rects come from the left; fields fill the middle;
    ///                       Route/Conv/Dup/Del sit in the right-group rect.
    ///   DrawSimple        — a compact read-only strip used inside Zequence editor sub-rows.
    /// </summary>
    public class ZoundBrowserEditor<TZound> where TZound : Zound {

        private BrowserTab parentTab;

        private GUIContent label_volume = new GUIContent("V", "Volume");
        private GUIContent label_pitch = new GUIContent("P", "Pitch");
        private GUIContent label_chance = new GUIContent("C", "Chance");
        private GUIContent icon_openEditor;
        private GUIContent icon_openEditorKlip;
        private GUIContent icon_openEditorZequence;
        private GUIContent icon_addMissing;
        private GUIContent icon_routingOn;
        private GUIContent icon_routingOff;
        private GUIContent icon_convert;
        private GUIContent icon_remove;
        private GUIContent icon_convertToZequence;
        private GUIContent icon_duplicate;
        private GUIContent muteLabel;
        private GUIContent soloLabel;
        private GUIStyle tagsLabelStyle;
        private GUIStyle tagsLabelStyleInline;

        //private bool nameHasDrawn; // Not needed since this will be drawn first anyway.
        private bool volumeHasDrawn;
        private bool pitchHasDrawn;
        private bool chanceHasDrawn;

        private float lastTagsWidth = 0f;

        public GUIStyle GetTagsLabelStyle() => tagsLabelStyle;
        public GUIStyle GetTagsLabelStyleInline() => tagsLabelStyleInline;
        public float GetLastTagsWidth() => lastTagsWidth;

        public ZoundBrowserEditor(BrowserTab parentTab) {
            this.parentTab = parentTab;
            icon_openEditor = new GUIContent(ZUI.FindIcon("open-editor") ?? Resources.Load<Texture>("ZoundsWindowIcons/open-editor"), "Open editor.");
            icon_openEditorKlip = new GUIContent(ZUI.FindIcon("open-editor-klip") ?? Resources.Load<Texture>("ZoundsWindowIcons/open-editor-klip"), "Open Klip editor.");
            icon_openEditorZequence = new GUIContent(ZUI.FindIcon("open-editor-zequence") ?? Resources.Load<Texture>("ZoundsWindowIcons/open-editor-zequence"), "Open Zequence editor.");
            icon_addMissing = new GUIContent(ZUI.FindIcon("add-new") ?? Resources.Load<Texture>("ZoundsWindowIcons/add-new"), "Add as a new zound.");
            icon_routingOn = new GUIContent(ZUI.FindIcon("routing-on") ?? Resources.Load<Texture>("ZoundsWindowIcons/routing-on"), "Set manual routing.");
            icon_routingOff = new GUIContent(ZUI.FindIcon("routing-off") ?? Resources.Load<Texture>("ZoundsWindowIcons/routing-off"), "Set manual routing.");
            icon_convert = new GUIContent(ZUI.FindIcon("convert") ?? Resources.Load<Texture>("ZoundsWindowIcons/convert"), "Convert to Klip.");
            icon_remove = new GUIContent(ZUI.FindIcon("remove") ?? Resources.Load<Texture>("ZoundsWindowIcons/remove"), "Remove this zound.");
            icon_convertToZequence = new GUIContent(ZUI.FindIcon("convert-zequence") ?? Resources.Load<Texture>("ZoundsWindowIcons/convert-zequence"), "Convert this Klip to Zequence.");
            icon_duplicate = new GUIContent(ZUI.FindIcon("duplicate") ?? Resources.Load<Texture>("ZoundsWindowIcons/duplicate"), "Duplicate this zound.");
            muteLabel = new GUIContent("M", "Mute/Unmute");
            soloLabel = new GUIContent("S", "Toggle Solo");
            tagsLabelStyle = new GUIStyle();
            tagsLabelStyle.normal.textColor = new Color32(163, 198, 255, 255);
            tagsLabelStyle.wordWrap = true;
            tagsLabelStyle.clipping = TextClipping.Clip;

            // Inline variant: wraps within the fixed-width inline area (so 2 lines can fit at
            // smaller font sizes). UpperLeft anchors the first line at the top so the second
            // line falls naturally below it. Font size is driven by the "Zounds Tags" ZUI text
            // style when present (applied per-draw in DrawTagsField).
            tagsLabelStyleInline = new GUIStyle(tagsLabelStyle);
            tagsLabelStyleInline.wordWrap = true;
            tagsLabelStyleInline.alignment = TextAnchor.UpperLeft;
        }

        // ──────────────────────────────────────────────────────────────────────────
        // MULTICOLUMN INSPECTOR
        // Drawn inside a GUILayout HelpBox block that expands below the selected row.
        // Height is animated via BrowserTab.inspectorAnimFloat (smooth open/close).
        // ──────────────────────────────────────────────────────────────────────────
        public void DrawMulticolumn(Zound zoundToInspect, float inspectorHeight, float animProgress = 1f) {
            var guiEnabled = GUI.enabled;
            GUI.enabled = guiEnabled && !(zoundToInspect.IsClipOrLocalZound());

            var browserSettings = ZoundsProject.Instance.browserSettings;
            int fieldCount = 0;
            if (browserSettings.showNameField) fieldCount++;
            if (browserSettings.showVolume) fieldCount++;
            if (browserSettings.showPitch) fieldCount++;
            if (browserSettings.showChance) fieldCount++;

            GUILayout.BeginHorizontal(EditorStyles.helpBox, GUILayout.Height(inspectorHeight), GUILayout.ExpandWidth(true));
            {
                var inspectorRect = GUILayoutUtility.GetRect(1, inspectorHeight, GUILayout.ExpandWidth(true));

                float fieldWidthMultiplier;
                float tagsWidthMultiplier;

                if (browserSettings.showTags) {
                    if (fieldCount > 2) { fieldWidthMultiplier = 0.4f; tagsWidthMultiplier = 0.2f; }
                    else if (fieldCount > 0) { fieldWidthMultiplier = 0.5f; tagsWidthMultiplier = 0.5f; }
                    else { fieldWidthMultiplier = 0f; tagsWidthMultiplier = 1f; }
                }
                else {
                    fieldWidthMultiplier = fieldCount > 2 ? 0.5f : (fieldCount > 0 ? 1f : 0f);
                    tagsWidthMultiplier = 0f;
                }

                float buttonWidth = 30f;
                float leftButtonsWidth = (browserSettings.showOpenEditor ? buttonWidth : 0f);
                if (browserSettings.showMute || browserSettings.showSolo) leftButtonsWidth += 24f;

                float baseRemoveRectWidth = 0f;
                if (browserSettings.showRouting)   baseRemoveRectWidth += buttonWidth;
                if (browserSettings.showConvertToZequence) baseRemoveRectWidth += buttonWidth;
                if (browserSettings.showDuplicate) baseRemoveRectWidth += buttonWidth;
                if (browserSettings.showRemove)    baseRemoveRectWidth += buttonWidth;

                float mcLeftGap  = leftButtonsWidth > 0 ? BrowserTab.LEFT_BUTTONS_TO_NAME_GAP : 0f;
                float mcRightGap = baseRemoveRectWidth > 0 ? BrowserTab.INSPECTOR_TO_REMOVE_GAP : 0f;

                // Button zones (edit, M/S, remove) and fields zone use base inspector height —
                // they should not grow with tags. Tags zone uses full inspectorHeight.
                // fieldsRect is top-aligned at inspectorRect.y with base height so that
                // bottom-anchoring in DrawZoundFields places rows just below the button row.
                float baseHeight = Mathf.Min(inspectorHeight, BrowserTab.inspectorHeight);
                float x = inspectorRect.x;
                Rect editRect    = new Rect(x, inspectorRect.y, browserSettings.showOpenEditor ? buttonWidth : 0f, baseHeight);
                x += editRect.width;
                Rect msRect      = new Rect(x, inspectorRect.y, (browserSettings.showMute || browserSettings.showSolo) ? 24f : 0f, baseHeight);
                x += msRect.width + mcLeftGap;
                float remainingWidth = inspectorRect.xMax - x - baseRemoveRectWidth - mcRightGap;
                Rect fieldsRect  = new Rect(x, inspectorRect.y, remainingWidth * (1f - tagsWidthMultiplier), baseHeight);
                Rect tagsRect    = new Rect(fieldsRect.xMax, inspectorRect.y, remainingWidth * tagsWidthMultiplier, inspectorHeight);
                Rect removeRect  = new Rect(tagsRect.xMax + mcRightGap, inspectorRect.y, baseRemoveRectWidth, baseHeight);

                var prevLabelWidth = EditorGUIUtility.labelWidth;
                EditorGUIUtility.labelWidth = 12f;

                if (s_debugBorders) {
                    DebugRect(inspectorRect, Color.white, $"panel h={inspectorRect.height:0} bh={baseHeight:0}");
                }

                // Fade in fields/sliders during the last portion of the open animation.
                // Buttons scale naturally, but sliders and inputs look odd popping in at full size.
                float fieldAlpha = Mathf.Clamp01((animProgress - 0.8f) / 0.2f);
                var prevColor = GUI.color;
                if (fieldAlpha < 1f)
                    GUI.color = new Color(prevColor.r, prevColor.g, prevColor.b, prevColor.a * fieldAlpha);

                DrawZoundFields(editRect, msRect, fieldsRect, tagsRect, removeRect, zoundToInspect, fillButtonHeight: true, twoRowFields: true);

                GUI.color = prevColor;
                EditorGUIUtility.labelWidth = prevLabelWidth;
            }
            GUILayout.EndHorizontal();

            GUI.enabled = guiEnabled;
        }

        // ──────────────────────────────────────────────────────────────────────────
        // SINGLECOLUMN INSPECTOR
        // Row-1 order (left → right):
        //   [Edit] [Mute|Solo] [ZoundBtn] [NameInput] [V] [P] [C] [Route|Conv|Dup|Del] [Tags if fits]
        // Tags overflow to a dedicated row 2 when they cannot fit on row 1.
        // All rects are pre-computed by BrowserTab.DrawSinglecolumnRow.
        // ──────────────────────────────────────────────────────────────────────────
        internal void DrawZoundSinglecolumn(ref BrowserTab.ZoundListRowLayout layout, Zound zoundToInspect) {
            var guiEnabled = GUI.enabled;
            GUI.enabled = guiEnabled && !(zoundToInspect.IsClipOrLocalZound());

            var browserSettings = ZoundsProject.Instance.browserSettings;
            bool isMissingZound = !(zoundToInspect is ClipZound) && zoundToInspect.id == 0;

            var prevLabelWidth = EditorGUIUtility.labelWidth;
            EditorGUIUtility.labelWidth = 12f;

            if (browserSettings.showOpenEditor && layout.editButtonRect.width > 0f) {
                DrawOpenEditorButton(layout.editButtonRect, zoundToInspect, isMissingZound);
            }

            if (!isMissingZound && (browserSettings.showMute || browserSettings.showSolo) && layout.muteSoloRect.width > 0f) {
                DrawMuteSoloButtonsHorizontal(layout.muteSoloRect, zoundToInspect);
            }

            if (!isMissingZound) {
                if (browserSettings.showNameField && layout.nameInputRect.width > 1f)
                    DrawNameField(layout.nameInputRect, zoundToInspect);
                if (browserSettings.showVolume && layout.volumeRect.width > 1f)
                    DrawVolumeField(layout.volumeRect, zoundToInspect);
                if (browserSettings.showPitch && layout.pitchRect.width > 1f)
                    DrawPitchField(layout.pitchRect, zoundToInspect);
                if (browserSettings.showChance && layout.chanceRect.width > 1f)
                    DrawChanceField(layout.chanceRect, zoundToInspect);
            }

            if ((browserSettings.showRouting || browserSettings.showConvertToZequence || browserSettings.showDuplicate || browserSettings.showRemove)
                && layout.rightGroupRect.width > 0f) {
                DrawRemoveButton(layout.rightGroupRect, zoundToInspect, isMissingZound);
            }

            if (!isMissingZound && browserSettings.showTags) {
                if (layout.tagsOnSeparateRow && layout.tagsRowRect.width > 1f)
                    DrawTagsField(layout.tagsRowRect, zoundToInspect);
                else if (layout.tagsInlineRect.width > 1f)
                    DrawTagsField(layout.tagsInlineRect, zoundToInspect);
            }

            EditorGUIUtility.labelWidth = prevLabelWidth;
            GUI.enabled = guiEnabled;
        }

        // ──────────────────────────────────────────────────────────────────────────
        // SHARED FIELD DRAWING
        // Single method called by both DrawMulticolumn and DrawZoundSinglecolumn.
        // All rects are absolute screen coordinates. Tags rect may be Rect.zero (hidden).
        // Controls are vertically centred to natural height so they stay compact when
        // the row is taller than ROW_HEIGHT (e.g. tags wrapping into multiple lines).
        // Tags are exempt — they fill whatever height they are given.
        //
        // fillButtonHeight: when true, edit/M/S/remove buttons fill the full zone height
        //                   (grid inspector panel, or list two-row mode where buttons span both rows).
        //                   When false, buttons are clamped to singleLineHeight and vertically centred
        //                   (list single-row mode, where the row may be tall from tag wrapping).
        // twoRowFields:     when true, fields are distributed across two rows within fieldsRect.
        //                   Used by the multicolumn inspector which has limited horizontal space.
        // ──────────────────────────────────────────────────────────────────────────
        // DEBUG: set to true to draw colored borders around every zone rect in DrawZoundFields.
        private static bool s_debugBorders = false;
        private static void DebugRect(Rect r, Color c, string label = "") {
            if (Event.current.type != EventType.Repaint) return;
            var prev = GUI.color;
            GUI.color = c;
            GUI.DrawTexture(new Rect(r.x,           r.y,            r.width, 1f), EditorGUIUtility.whiteTexture);
            GUI.DrawTexture(new Rect(r.x,           r.yMax - 1f,    r.width, 1f), EditorGUIUtility.whiteTexture);
            GUI.DrawTexture(new Rect(r.x,           r.y,            1f, r.height), EditorGUIUtility.whiteTexture);
            GUI.DrawTexture(new Rect(r.xMax - 1f,   r.y,            1f, r.height), EditorGUIUtility.whiteTexture);
            GUI.color = prev;
            if (!string.IsNullOrEmpty(label)) {
                var s = new GUIStyle(EditorStyles.label);
                s.normal.textColor = c;
                s.fontSize = 8;
                GUI.Label(new Rect(r.x + 2f, r.y, r.width, 12f), label, s);
            }
        }

        private void DrawZoundFields(Rect editRect, Rect msRect, Rect fieldsRect, Rect tagsRect, Rect removeRect, Zound zoundToInspect, bool fillButtonHeight = false, bool twoRowFields = false) {
            ResetState();
            var browserSettings = ZoundsProject.Instance.browserSettings;
            bool isMissingZound = !(zoundToInspect is ClipZound) && zoundToInspect.id == 0;
            var guiEnabled = GUI.enabled;

            if (s_debugBorders) {
                DebugRect(editRect,   Color.cyan,    "edit");
                DebugRect(msRect,     Color.green,   "ms");
                DebugRect(fieldsRect, Color.yellow,  "fields");
                DebugRect(tagsRect,   Color.magenta, "tags");
                DebugRect(removeRect, Color.red,     "remove");
            }

            // fillButtonHeight: buttons span their full zone (grid inspector, list two-row mode).
            // Otherwise clamp to singleLineHeight so buttons stay compact when the row grows tall
            // from tag wrapping in single-row list mode.
            Rect drawEditRect   = fillButtonHeight ? editRect   : NaturalHeight(editRect);
            Rect drawMsRect     = fillButtonHeight ? msRect     : NaturalHeight(msRect);
            Rect drawRemoveRect = fillButtonHeight ? removeRect : NaturalHeight(removeRect);

            if (browserSettings.showOpenEditor && editRect.width > 0f) {
                DrawOpenEditorButton(drawEditRect, zoundToInspect, isMissingZound);
            }

            if (!isMissingZound && (browserSettings.showMute || browserSettings.showSolo) && msRect.width > 0f) {
                DrawMuteSoloButtonsVertical(drawMsRect, zoundToInspect);
            }

            if ((browserSettings.showRouting || browserSettings.showConvertToZequence || browserSettings.showDuplicate || browserSettings.showRemove) && removeRect.width > 0f) {
                DrawRemoveButton(drawRemoveRect, zoundToInspect, isMissingZound);
            }

            if (!isMissingZound && fieldsRect.width > 1f) {
                bool tagsInSeparateZone = tagsRect.width > 1f;

                int fieldCount = 0;
                if (browserSettings.showNameField) fieldCount++;
                if (browserSettings.showVolume)    fieldCount++;
                if (browserSettings.showPitch)     fieldCount++;
                if (browserSettings.showChance)    fieldCount++;
                if (browserSettings.showTags && !tagsInSeparateZone) fieldCount++;

                if (fieldCount > 0) {
                    float lh = EditorGUIUtility.singleLineHeight;
                    float rowGap = 2f;

                    if (twoRowFields) {
                        // Split fields into two rows: ceil(n/2) on row 0, floor(n/2) on row 1.
                        // Both rows are bottom-anchored so they align with the last tag line when
                        // the inspector panel is taller than the base height.
                        int row0Count = Mathf.CeilToInt(fieldCount / 2f);
                        int row1Count = fieldCount - row0Count;
                        float fw0 = row0Count > 0 ? (fieldsRect.width - 4f) / row0Count : 0f;
                        float fw1 = row1Count > 0 ? (fieldsRect.width - 4f) / row1Count : 0f;
                        float bottomY = fieldsRect.yMax - lh;
                        float row1Y   = row1Count > 0 ? bottomY : fieldsRect.y;
                        float row0Y   = row1Count > 0 ? row1Y - lh - rowGap : bottomY;
                        Rect r0 = new Rect(fieldsRect.x, row0Y, fw0 - 4f, lh);
                        Rect r1 = new Rect(fieldsRect.x, row1Y, fw1 - 4f, lh);

                        // Row 0
                        if (browserSettings.showNameField) { DrawNameField(r0, zoundToInspect); r0.x += fw0; }
                        else if (browserSettings.showVolume) { DrawVolumeField(r0, zoundToInspect); r0.x += fw0; }
                        else if (browserSettings.showPitch)  { DrawPitchField(r0, zoundToInspect); r0.x += fw0; }
                        else if (browserSettings.showChance) { DrawChanceField(r0, zoundToInspect); r0.x += fw0; }

                        if (row0Count > 1) {
                            if (browserSettings.showVolume && !volumeHasDrawn)  { DrawVolumeField(r0, zoundToInspect); r0.x += fw0; }
                            else if (browserSettings.showPitch && !pitchHasDrawn)  { DrawPitchField(r0, zoundToInspect); r0.x += fw0; }
                            else if (browserSettings.showChance && !chanceHasDrawn) { DrawChanceField(r0, zoundToInspect); r0.x += fw0; }
                        }

                        // Row 1
                        if (row1Count > 0) {
                            if (browserSettings.showVolume && !volumeHasDrawn)   { DrawVolumeField(r1, zoundToInspect); r1.x += fw1; }
                            else if (browserSettings.showPitch && !pitchHasDrawn)  { DrawPitchField(r1, zoundToInspect); r1.x += fw1; }
                            else if (browserSettings.showChance && !chanceHasDrawn) { DrawChanceField(r1, zoundToInspect); r1.x += fw1; }

                            if (row1Count > 1) {
                                if (browserSettings.showChance && !chanceHasDrawn) { DrawChanceField(r1, zoundToInspect); r1.x += fw1; }
                            }
                        }

                        // Tags on row 1 if inline
                        if (browserSettings.showTags && !tagsInSeparateZone) DrawTagsField(r1, zoundToInspect);
                    }
                    else {
                        // Single horizontal row of fields.
                        float fieldWidth = (fieldsRect.width - 4f) / fieldCount;
                        Rect fieldRect   = NaturalHeight(fieldsRect);
                        fieldRect.width  = fieldWidth - 4f;

                        if (browserSettings.showNameField) { DrawNameField(fieldRect, zoundToInspect); fieldRect.x += fieldWidth; }
                        if (browserSettings.showVolume)    { DrawVolumeField(fieldRect, zoundToInspect); fieldRect.x += fieldWidth; }
                        if (browserSettings.showPitch)     { DrawPitchField(fieldRect, zoundToInspect); fieldRect.x += fieldWidth; }
                        if (browserSettings.showChance)    { DrawChanceField(fieldRect, zoundToInspect); fieldRect.x += fieldWidth; }
                        if (browserSettings.showTags && !tagsInSeparateZone) { DrawTagsField(fieldRect, zoundToInspect); fieldRect.x += fieldWidth; }
                    }
                }

                if (browserSettings.showTags && tagsInSeparateZone) DrawTagsField(tagsRect, zoundToInspect);
            }

            GUI.enabled = guiEnabled;
        }

        // Returns a copy of rect with height clamped to singleLineHeight, top-aligned.
        // Used so buttons and fields stay compact when the row is taller than normal (tag wrapping).
        private static Rect NaturalHeight(Rect rect) {
            float h = EditorGUIUtility.singleLineHeight;
            if (rect.height <= h) return rect;
            return new Rect(rect.x, rect.y, rect.width, h);
        }

        // ──────────────────────────────────────────────────────────────────────────
        // SIMPLE INSPECTOR
        // Used by the Zequence editor to draw compact sub-rows for each nested zound.
        // No edit/right-group buttons; just M/S (unless isLocalZound) + Name + Vol + Pitch + Chance + Tags.
        // ──────────────────────────────────────────────────────────────────────────
        public void DrawSimple(Rect fieldsRect, Zound zoundToInspect, bool isLocalZound, bool drawName = true, bool drawTags = true) {
            if (zoundToInspect == null) return;
            var guiEnabled = GUI.enabled;
            GUI.enabled = guiEnabled && !(zoundToInspect is ClipZound);

            ResetState();
            var browserSettings = ZoundsProject.Instance.browserSettings;
            int fieldCount = 3;
            if (drawName) fieldCount++;
            if (drawTags) fieldCount++;
            float muteSoloWidth = isLocalZound? 0f : 44f;
            float fieldWidth = (fieldsRect.width - muteSoloWidth) / fieldCount;
            Rect fieldRect = fieldsRect;

            if (!isLocalZound) {
                DrawMuteSoloButtonsHorizontal(new Rect(fieldRect.x, fieldRect.y, muteSoloWidth, fieldRect.height), zoundToInspect, forceBoth: true);
            }

            fieldRect.x += muteSoloWidth;
            fieldRect.width = fieldWidth - 4f;

            var prevLabelWidth = EditorGUIUtility.labelWidth;
            EditorGUIUtility.labelWidth = 12f;

            if (drawName) {
                DrawNameField(fieldRect, zoundToInspect);
                fieldRect.x += fieldWidth;
            }

            DrawVolumeField(fieldRect, zoundToInspect);
            fieldRect.x += fieldWidth;

            DrawPitchField(fieldRect, zoundToInspect);
            fieldRect.x += fieldWidth;

            DrawChanceField(fieldRect, zoundToInspect);
            fieldRect.x += fieldWidth;

            if (drawTags) {
                DrawTagsField(fieldRect, zoundToInspect, true);
                fieldRect.x += fieldWidth;
            }

            EditorGUIUtility.labelWidth = prevLabelWidth;
            GUI.enabled = guiEnabled;
        }

        // Resets the "has this field been drawn yet?" flags before each draw call.
        // These flags prevent the same field from being drawn twice when the inspector
        // has fewer columns than fields (overflow logic in DrawMulticolumn column 2).
        private void ResetState() {
            //nameHasDrawn = false;
            volumeHasDrawn = false;
            pitchHasDrawn = false;
            chanceHasDrawn = false;
        }

        // ── Left-side edit / convert / add-missing button ────────────────────────
        // The same rect is reused for three different actions depending on zound type:
        //   Missing zound  → "Add as new" button (green confirm style)
        //   ClipZound      → "Convert to Klip" button (wraps the raw AudioClip into a Klip)
        //   Klip/Zequence  → "Open editor" button (opens the waveform / sequence editor)
        private void DrawOpenEditorButton(Rect rect, Zound zoundToInspect, bool isMissingZound) {
            if (isMissingZound) {
                if (ZUI.Button(rect, icon_addMissing, ZUI.Style.ZoundBtnFlat, ZUI.Tint.Confirm)) {  // green-tinted to stand out for missing-zound action
                    RemoveMissingZound(zoundToInspect);
                    BrowserTab.OpenAddNewZoundMenu(zoundToInspect.name);
                }
            }
            else if (zoundToInspect is ClipZound clipZound) {
                if (ZUI.Button(rect, icon_convert, ZUI.Style.ZoundBtnFlat)) {
                    if (EditorUtility.DisplayDialog("Convert to Klip: " + clipZound.name, "Convert this into audio clip a Klip?\n" + clipZound.name, "Convert", "Cancel")) {
                        if (parentTab is BrowserTab browserTab) {
                            browserTab.ConvertClipToKlip(clipZound);
                        }
                    }
                }
            }
            else {
                GUIContent icon = (zoundToInspect is Klip) ? icon_openEditorKlip :
                                  (zoundToInspect is Zequence) ? icon_openEditorZequence :
                                  icon_openEditor;
                if (ZUI.Button(rect, icon, ZUI.Style.ZoundBtnFlat)) {
                    parentTab.OpenZoundEditor(zoundToInspect);
                }
            }
        }

        // ── Mute / Solo buttons ───────────────────────────────────────────────────
        // Vertical variant: if both M and S are shown and the rect is narrow (≤24px wide),
        // M is on top and S is below (used in singlecolumn multipleRows mode and multicolumn).
        // If the rect is wide enough, M and S are placed side by side horizontally.
        // Horizontal variant (DrawMuteSoloButtonsHorizontal): always side-by-side, used in DrawSimple.
        private void DrawMuteSoloButtonsVertical(Rect muteSoloRect, Zound zoundToInspect) {
            var browserSettings = ZoundsProject.Instance.browserSettings;

            Rect muteRect = muteSoloRect;
            Rect soloRect = muteSoloRect;

            float msGap = BrowserTab.MUTE_SOLO_GAP;
            bool bothVisible = browserSettings.showMute && browserSettings.showSolo;
            ZUICornerMask muteMask  = ZUICornerMask.None;
            ZUICornerMask soloMask  = ZUICornerMask.None;
            if (bothVisible) {
                if (muteSoloRect.width > 24f) {
                    muteRect.width = (muteSoloRect.width - msGap) / 2f;
                    soloRect = muteRect;
                    soloRect.x = muteRect.xMax + msGap;
                    muteMask = ZUICornerMask.Left;
                    soloMask = ZUICornerMask.Right;
                }
                else {
                    muteRect.height = (muteSoloRect.height - msGap) / 2f;
                    soloRect = muteRect;
                    soloRect.y = muteRect.yMax + msGap;
                    muteMask = ZUICornerMask.Top;
                    soloMask = ZUICornerMask.Bottom;
                }
            }

            if (browserSettings.showMute) {
                if (ZUI.Toggle(muteRect, zoundToInspect.mute, muteLabel, ZUI.Style.ZoundBtnFlatToggle, ZUI.PaletteColor("Warning", new Color(.70f, .42f, .08f, 1f)), muteMask) != zoundToInspect.mute) {
                    ToggleMute(zoundToInspect);
                }
            }
            if (browserSettings.showSolo) {
                if (ZUI.Toggle(soloRect, zoundToInspect.solo, soloLabel, ZUI.Style.ZoundBtnFlatToggle, ZUI.PaletteColor("Confirm", new Color(.14f, .34f, .14f, 1f)), soloMask) != zoundToInspect.solo) {
                    ToggleSolo(zoundToInspect);
                }
            }
        }

        // Horizontal Mute/Solo pair with shared (rounded-outer, square-inner) corners.
        // Honors browserSettings.showMute/showSolo — either button alone uses ZUICornerMask.All.
        private void DrawMuteSoloButtonsHorizontal(Rect muteSoloRect, Zound zoundToInspect, bool forceBoth = false) {
            var browserSettings = ZoundsProject.Instance.browserSettings;
            bool showM = forceBoth || browserSettings.showMute;
            bool showS = forceBoth || browserSettings.showSolo;
            if (!showM && !showS) return;

            float gap = 1f;
            var muteRect = muteSoloRect;
            var soloRect = muteSoloRect;
            if (showM && showS) {
                float half = (muteSoloRect.width - gap) * 0.5f;
                muteRect.width = half;
                soloRect.x     = muteRect.xMax + gap;
                soloRect.width = muteSoloRect.width - half - gap;
            }

            ZUICornerMask muteMask = (showM && showS) ? ZUICornerMask.Left  : ZUICornerMask.All;
            ZUICornerMask soloMask = (showM && showS) ? ZUICornerMask.Right : ZUICornerMask.All;

            if (showM) {
                if (ZUI.Toggle(muteRect, zoundToInspect.mute, muteLabel, ZUI.Style.ZoundBtnFlatToggle, ZUI.PaletteColor("Warning", new Color(.70f, .42f, .08f, 1f)), muteMask) != zoundToInspect.mute) {
                    ToggleMute(zoundToInspect);
                }
            }
            if (showS) {
                if (ZUI.Toggle(soloRect, zoundToInspect.solo, soloLabel, ZUI.Style.ZoundBtnFlatToggle, ZUI.PaletteColor("Confirm", new Color(.14f, .34f, .14f, 1f)), soloMask) != zoundToInspect.solo) {
                    ToggleSolo(zoundToInspect);
                }
            }
        }

        internal static void ToggleSolo(Zound zoundToInspect) {
            ZoundsWindow.ModifyZoundsProject("solo zound", () => {
                zoundToInspect.solo = !zoundToInspect.solo;
                if (zoundToInspect.solo) zoundToInspect.mute = false;
                ZoundsProject.Instance.zoundLibrary.soloStatusNeedsUpdate = true;
            });
        }

        internal static void ToggleMute(Zound zoundToInspect) {
            ZoundsWindow.ModifyZoundsProject("mute zound", () => {
                zoundToInspect.mute = !zoundToInspect.mute;
                if (zoundToInspect.mute) zoundToInspect.solo = false;
                ZoundsProject.Instance.zoundLibrary.soloStatusNeedsUpdate = true;
            });
        }

        // ── Right action button group: Route / Convert / Duplicate / Remove ──
        // All buttons share the same rect, divided equally with ZoundItem_spacing gaps.
        // Button count is calculated first so each slot gets the same width.
        // isMissingZound suppresses all buttons except Remove (which clears the missing entry).
        // Buttons are disabled during play mode (removing/duplicating at runtime is unsafe).
        // Convert-to-Zequence always reserves its slot (so every row's buttons stay the same
        // width whether or not this particular zound can use it — see the large-list UI rule
        // against uneven button widths), but only Klips get a live button; it is greyed out
        // for Zequences (a Zequence can't itself be "converted to a Zequence").
        private void DrawRemoveButton(Rect rect, Zound zoundToInspect, bool isMissingZound) {
            var browserSettings = ZoundsProject.Instance.browserSettings;
            bool guiEnabled = GUI.enabled;
            if (isMissingZound) GUI.enabled = true;
            else GUI.enabled = guiEnabled && !Application.isPlaying;

            bool canConvertToZequence = !isMissingZound && zoundToInspect is Klip klipToConvert && klipToConvert.parentId == 0;

            int buttonCount = 0;
            if (!isMissingZound && browserSettings.showRouting) buttonCount++;
            if (!isMissingZound && browserSettings.showConvertToZequence) buttonCount++;
            if (!isMissingZound && browserSettings.showDuplicate) buttonCount++;
            if (browserSettings.showRemove) buttonCount++;

            if (buttonCount == 0) return;

            float gap = BrowserTab.ZoundItem_spacing;
            float buttonWidth = (rect.width - gap * (buttonCount - 1)) / buttonCount;
            float currentX = rect.x;
            int buttonIndex = 0;

            ZUICornerMask MaskFor(int idx) =>
                buttonCount == 1 ? ZUICornerMask.All :
                idx == 0              ? ZUICornerMask.Left :
                idx == buttonCount - 1 ? ZUICornerMask.Right :
                ZUICornerMask.None;

            if (!isMissingZound) {
                if (browserSettings.showRouting) {
                    if (ZUI.Button(new Rect(currentX, rect.y, buttonWidth, rect.height), zoundToInspect.editor_hasManuallySetRouting ? icon_routingOn : icon_routingOff, ZUI.Style.ZoundBtnFlat, MaskFor(buttonIndex))) {
                        OpenManualRoutingDropdown(zoundToInspect);
                    }
                    currentX += buttonWidth + gap; buttonIndex++;
                }
                if (browserSettings.showConvertToZequence) {
                    bool prevEnabledForConvert = GUI.enabled;
                    GUI.enabled = prevEnabledForConvert && canConvertToZequence;
                    if (ZUI.Button(new Rect(currentX, rect.y, buttonWidth, rect.height), icon_convertToZequence, ZUI.Style.ZoundBtnFlat, MaskFor(buttonIndex))) {
                        if (EditorUtility.DisplayDialog("Convert to Zequence: " + zoundToInspect.name,
                            "Convert this Klip into a Zequence containing it as a local klip?\n" + zoundToInspect.name, "Convert", "Cancel")) {
                            // Deferred (like Remove/Duplicate below) — mutating zoundLibrary.klips/
                            // zequences immediately here would modify the list this row is being
                            // drawn from mid-iteration.
                            parentTab.zoundToConvertToZequence = (Klip)zoundToInspect;
                        }
                    }
                    GUI.enabled = prevEnabledForConvert;
                    currentX += buttonWidth + gap; buttonIndex++;
                }
                if (browserSettings.showDuplicate) {
                    if (ZUI.Button(new Rect(currentX, rect.y, buttonWidth, rect.height), icon_duplicate, ZUI.Style.ZoundBtnFlat, MaskFor(buttonIndex))) {
                        parentTab.zoundToDuplicate = zoundToInspect;
                    }
                    currentX += buttonWidth + gap; buttonIndex++;
                }
            }
            if (browserSettings.showRemove) {
                if (ZUI.Button(new Rect(currentX, rect.y, buttonWidth, rect.height), icon_remove, ZUI.Style.ZoundBtnFlat, ZUI.Tint.Danger, MaskFor(buttonIndex))) {
                    if (isMissingZound) {
                        RemoveMissingZound(zoundToInspect);
                    }
                    else {
                        if (AudioAssetUtility.DisplayZoundRemoveDialog(zoundToInspect)) {
                            parentTab.zoundToRemove = zoundToInspect;
                        }
                    }
                }
            }
            GUI.enabled = guiEnabled;
        }

        internal static void RemoveMissingZound(Zound zoundToInspect) {
            string keyToDelete = null;
            foreach (var kvp in ZoundEngine.MissingZounds) {
                if (kvp.Value == zoundToInspect) {
                    keyToDelete = kvp.Key;
                    break;
                }
            }
            if (keyToDelete != null) {
                ZoundEngine.RemovePersistedMissingZound(keyToDelete);
            }
        }

        private void DrawNameField(Rect rect, Zound zoundToInspect) {
            bool guiEnabled = GUI.enabled;
            EditorGUI.BeginChangeCheck();
            string controlName = "rename-" + zoundToInspect.id;
            GUI.SetNextControlName(controlName);
            string newName = EditorGUI.DelayedTextField(rect, GUIContent.none, zoundToInspect.name);
            if (EditorGUI.EndChangeCheck()) {
                newName = ZoundDictionary.EnsureUniqueZoundName(newName, zoundToInspect);
                ZoundsWindow.ModifyZoundsProject("rename zound", () => {
                    zoundToInspect.name = newName;
                    //if (Application.isPlaying) {
                        if (ZoundEngine.IsInitialized()) {
                            ZoundDictionary.ValidateZoundRuntime(zoundToInspect);
                        }
                    //}
                    var zoundLibrary = ZoundsProject.Instance.zoundLibrary;
                    if (zoundToInspect is Klip klip && zoundLibrary.klips.Contains(klip)) zoundLibrary.klips = zoundLibrary.klips.OrderBy(it => it.name).ToList();
                    else if (zoundToInspect is Zequence zequence && zoundLibrary.zequences.Contains(zequence)) zoundLibrary.zequences = zoundLibrary.zequences.OrderBy(it => it.name).ToList();
                });
                if (parentTab != null) parentTab.filterCache = null;

                ZoundsWindow.setFocusNextFrame = controlName;
            }
            GUI.enabled = guiEnabled;
            //nameHasDrawn = true;
        }

        public static float RoundTo3DecimalPlaces(float original) {
            return Mathf.Round(original * 1000f) / 1000f;
        }

        // Display-scale: values are shown as percentages in the slider track. Underlying zound
        // fields stay on their native scale (Volume 0-1, Pitch 0.1-2, Chance 0-1).
        private void DrawVolumeField(Rect rect, Zound zoundToInspect) {
            var bs = ZoundsProject.Instance.browserSettings;
            float newMin = zoundToInspect.minVolume * 100f;
            float newMax = zoundToInspect.maxVolume * 100f;
            EditorGUI.BeginChangeCheck();
            ZUI.MicroMinMax(rect, ref newMin, ref newMax,
                             Zound.MinVolumeRange * 100f, Zound.MaxVolumeRange * 100f,
                             label_volume.tooltip, ZUI.SliderStyle.MinMax,
                             showInputFields: bs.vpcShowInputBoxes,
                             labelMode: bs.vpcShowSliderType
                                 ? ZUI.MicroMinMaxLabelMode.LabelAndValues
                                 : ZUI.MicroMinMaxLabelMode.ValuesOnly);
            if (EditorGUI.EndChangeCheck()) {
                ZoundsWindow.ModifyZoundsProject("change zound volume", () => {
                    zoundToInspect.minVolume = RoundTo3DecimalPlaces(newMin / 100f);
                    zoundToInspect.maxVolume = RoundTo3DecimalPlaces(newMax / 100f);
                });
            }
            volumeHasDrawn = true;
        }

        private void DrawPitchField(Rect rect, Zound zoundToInspect) {
            var bs = ZoundsProject.Instance.browserSettings;
            float newMin = zoundToInspect.minPitch * 100f;
            float newMax = zoundToInspect.maxPitch * 100f;
            EditorGUI.BeginChangeCheck();
            ZUI.MicroMinMax(rect, ref newMin, ref newMax,
                             Zound.MinPitchRange * 100f, Zound.MaxPitchRange * 100f,
                             label_pitch.tooltip, ZUI.SliderStyle.MinMaxPitch,
                             showInputFields: bs.vpcShowInputBoxes,
                             labelMode: bs.vpcShowSliderType
                                 ? ZUI.MicroMinMaxLabelMode.LabelAndValues
                                 : ZUI.MicroMinMaxLabelMode.ValuesOnly);
            if (EditorGUI.EndChangeCheck()) {
                ZoundsWindow.ModifyZoundsProject("change zound pitch", () => {
                    zoundToInspect.minPitch = RoundTo3DecimalPlaces(newMin / 100f);
                    zoundToInspect.maxPitch = RoundTo3DecimalPlaces(newMax / 100f);
                });
            }
            pitchHasDrawn = true;
        }

        private void DrawChanceField(Rect rect, Zound zoundToInspect) {
            // Chance is a single value — use MicroSlider to match V/P visually.
            var bs = ZoundsProject.Instance.browserSettings;
            float current = zoundToInspect.chance * 100f;
            EditorGUI.BeginChangeCheck();
            float next = ZUI.MicroSlider(rect, current,
                                          Zound.MinChanceRange * 100f, Zound.MaxChanceRange * 100f,
                                          label_chance.tooltip, ZUI.SliderStyle.Chance,
                                          showInputField: bs.vpcShowInputBoxes,
                                          labelMode: bs.vpcShowSliderType
                                              ? ZUI.MicroSliderLabelMode.LabelAndValue
                                              : ZUI.MicroSliderLabelMode.ValueOnly);
            if (EditorGUI.EndChangeCheck()) {
                ZoundsWindow.ModifyZoundsProject("change zound chance", () => {
                    zoundToInspect.chance = RoundTo3DecimalPlaces(next / 100f);
                });
            }
            chanceHasDrawn = true;
        }

        private GUIContent tempContent = new GUIContent();
        private void DrawTagsField(Rect rect, Zound zoundToInspect, bool drawSimple = false) {
            string tagsString = BrowserTab.GetZoundTagsString(zoundToInspect);
            // Multi-line mode when the caller has already given us a rect taller than a single row
            // (grid inspector, or list tags-on-own-row mode). Single-line otherwise.
            bool multiLine = !drawSimple && rect.height > BrowserTab.ROW_HEIGHT + 1f;
            GUIStyle style = multiLine ? tagsLabelStyle : tagsLabelStyleInline;

            // Apply the "Zounds Tags" ZUI text style (font, color, size) if the user has defined
            // one in the active sheet. Smaller font sizes allow the fixed inline area to fit 2 lines.
            var sheet = ZUI.ActiveSheet;
            if (sheet != null) {
                var tsd = sheet.FindText("Zounds Tags");
                if (tsd != null) tsd.text.Apply(style, sheet);
            }

            if (!drawSimple && rect.width > 0) {
                lastTagsWidth = rect.width;
                tempContent.text = tagsString;
                if (multiLine) {
                    rect.height = tagsLabelStyle.CalcHeight(tempContent, lastTagsWidth);
                }
            }
            if (GUI.Button(rect, tagsString, style)) {
                TagsEditorWindow.OpenWindow(zoundToInspect);
            }
        }


        internal static void OpenManualRoutingDropdown(Zound zoundToInspect) {
#if ADDRESSABLES_INSTALLED
            List<AudioMixerGroup> allMixerGroups = new List<AudioMixerGroup>();
            RoutingTab.GetAllAddresableMixerGroups(ref allMixerGroups);
            if (allMixerGroups.Count == 0) {
                Debug.LogWarning("There is no MixerGroup found that is set as Addressable.");
            }
            else {
                var currentMixer = zoundToInspect.manuallySetMixerGroupRef != null ? zoundToInspect.manuallySetMixerGroupRef.editorAsset : null;
                var mixerGroupMenu = new GenericMenu();

                mixerGroupMenu.AddItem(new GUIContent("-None-"), currentMixer == null, () => {
                    ZoundsWindow.ModifyZoundsProject("unset manual routing", () => {
                        zoundToInspect.manuallySetMixerGroupRef = null;
                    });
                    RoutingTab.reorderableListNeedsUpdate = true;
                });
                foreach (var mixerGroup in allMixerGroups) {
                    var mg = mixerGroup;
                    bool selected = currentMixer == mixerGroup.audioMixer && mg.name == zoundToInspect.manuallySetMixerGroupRef.SubObjectName;
                    mixerGroupMenu.AddItem(new GUIContent(mixerGroup.name), selected, () => {
                        var audioMixerPath = AssetDatabase.GetAssetPath(mg.audioMixer);
                        var audioMixerGUID = AssetDatabase.GUIDFromAssetPath(audioMixerPath);
                        var mixerGroupRef = new UnityEngine.AddressableAssets.AssetReference(audioMixerGUID.ToString());
                        mixerGroupRef.SubObjectName = mg.name;
                        mixerGroupRef.SetEditorSubObject(mg);
                        ZoundsWindow.ModifyZoundsProject("set manual routing", () => {
                            zoundToInspect.manuallySetMixerGroupRef = mixerGroupRef;
                        });
                        RoutingTab.reorderableListNeedsUpdate = true;
                    });
                }

                mixerGroupMenu.ShowAsContext();
            }
#else
            Debug.LogError("Please import Addressables package.");
#endif
        }

    }

}

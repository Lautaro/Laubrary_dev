using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Laubrary.Zounds {

    /// <summary>
    /// Draws the Zound Browser grid (multicolumn) mode.
    /// Includes the fixed-column row renderer, the flow-layout row renderer,
    /// and the single button cell drawer used by both.
    ///
    /// Color tinting for selected/playing state is applied by DrawFixedRow / DrawFlowRow
    /// before delegating to DrawButton; DrawButton handles pulse overlay, highlight texture,
    /// missing-zound box, input, and mute/solo indicator.
    /// </summary>
    internal static class ZoundGridItemView {

        private static GUIContent s_btnContent = new GUIContent();

        // ── Fixed-column row ──────────────────────────────────────────────────

        /// <summary>
        /// Draws one horizontal grid row in Fixed multicolumn mode.
        /// Iterates columnCount slots; empty trailing slots are drawn as invisible placeholders
        /// so the grid stays aligned when the last row isn't full.
        /// currentIndex is passed by ref and advances by columnCount on return.
        /// FlexibleSpace is added at both ends to horizontally centre the grid.
        /// </summary>
        public static void DrawFixedRow(
            List<Zound> filteredList,
            int selectedIndex,
            ref int currentIndex,
            int columnCount,
            float itemWidth,
            BrowserTab tab) {

            GUILayout.BeginHorizontal();

            var col = GUI.color;
            var evt = Event.current;

            GUILayout.FlexibleSpace();
            {
                for (int i = 0; i < columnCount; i++) {
                    if (i > 0) GUILayout.Space(BrowserTab.MULTICOLUMN_H_GAP);
                    if (currentIndex >= filteredList.Count) {
                        GUILayoutUtility.GetRect(itemWidth, 24f, GUIStyle.none,
                            GUILayout.MinWidth(itemWidth), GUILayout.MaxWidth(itemWidth), GUILayout.Height(24f));
                    }
                    else {
                        bool hasAnyInstancePlaying = ZoundBrowserPlaybackVisuals.TryGetAnyInstanceToken(filteredList[currentIndex], out var token);

                        bool isClipZound  = filteredList[currentIndex].IsClipOrLocalZound();
                        bool isKlipIssue  = filteredList[currentIndex] is Klip klip &&
                                            string.IsNullOrEmpty(klip.externalSourcePath) &&
                                            (klip.audioClipRef == null || !klip.audioClipRef.RuntimeKeyIsValid() || klip.audioClipRef.editorAsset == null);

                        if (!isClipZound && filteredList[currentIndex].id == 0) {
                            // missing zound — box draws its own style, no color tint needed
                        }
                        else if (isKlipIssue) {
                            GUI.color = new Color(1f, 0.4f, 0f, 1f);
                        }
                        else {
                            var zound = filteredList[currentIndex];
                            ZoundBrowserPlaybackVisuals.UpdateZoundButtonPulse(zound, isClipZound, hasAnyInstancePlaying, token);

                            if (selectedIndex == currentIndex) {
                                if (hasAnyInstancePlaying) {
                                    if      (token.state == ZoundToken.State.Paused)     GUI.color = new Color(0.9f, 0.5f, 0.9f, 1f);
                                    else if (token.audioSource.volume < Mathf.Epsilon)   GUI.color = new Color(0.9f, 0.5f, 0.1f, 1f);
                                    else GUI.color = isClipZound ? ZoundsEditorColors.clipFlashColorStartSelected : ZoundsEditorColors.flashColorStartSelected;
                                }
                                else {
                                    GUI.color = isClipZound ? Color.cyan : ZoundsEditorColors.flashColorStartSelected;
                                }
                            }
                            else {
                                if (hasAnyInstancePlaying) {
                                    if      (token.state == ZoundToken.State.Paused)     GUI.color = new Color(0.9f, 0.5f, 0.9f, 1f);
                                    else if (token.audioSource.volume < Mathf.Epsilon)   GUI.color = new Color(0.9f, 0.5f, 0.1f, 1f);
                                    else if (isClipZound)                                GUI.color = ZoundsEditorColors.clipFlashColorStart;
                                }
                                else if (isClipZound) GUI.color = Color.cyan;
                            }
                        }
                        DrawButton(filteredList[currentIndex], selectedIndex, currentIndex, itemWidth, token, evt, tab);
                        GUI.color = col;
                    }
                    currentIndex++;
                }
            }
            GUILayout.FlexibleSpace();

            GUILayout.EndHorizontal();
        }

        // ── Flow-layout row ───────────────────────────────────────────────────

        /// <summary>
        /// Draws a wrapping flow-layout row for Auto/Min button size modes.
        /// Buttons are laid out left-to-right; when the next button would overflow maxWidth
        /// a new GUILayout horizontal row is started. The inspector panel is drawn after
        /// the row that contains the selected zound.
        /// totalIndex is passed by ref so the caller's global index stays in sync.
        /// </summary>
        public static void DrawFlowRow(
            List<Zound> zounds,
            int selectedIndex,
            ref int totalIndex,
            float maxWidth,
            BrowserTab tab) {

            var browserSettings = ZoundsProject.Instance.browserSettings;
            var sizeMode  = browserSettings.buttonSizeMode;
            float minWidth = browserSettings.itemWidth;
            var btnStyle   = ZUI.GetButtonStyle(ZUI.Style.ZoundBtn);

            float currentX = 0;
            bool rowStarted = false;
            bool inspectorPending = false;

            for (int i = 0; i < zounds.Count; i++) {
                var zound = zounds[i];
                s_btnContent.text = zound.name;
                float requiredWidth = btnStyle.CalcSize(s_btnContent).x;

                if (sizeMode == ZoundsProject.BrowserSettings.ButtonSizeMode.Min) {
                    requiredWidth = Mathf.Max(requiredWidth, minWidth);
                }

                if (!rowStarted) {
                    GUILayout.BeginHorizontal();
                    rowStarted = true;
                    currentX = 0;
                }

                if (currentX + requiredWidth > maxWidth - 40f && currentX > 0) {
                    GUILayout.EndHorizontal();
                    rowStarted = false;
                    if (inspectorPending) {
                        int localIdx = selectedIndex - (totalIndex - i);
                        if (localIdx >= 0 && localIdx < zounds.Count) {
                            tab.UpdateInspectorHeight(zounds[localIdx]);
                            tab.zoundBrowserEditor.DrawMulticolumn(zounds[localIdx], tab.inspectorAnimFloat.value, tab.inspectorAnimFloat.progress);
                        }
                        inspectorPending = false;
                    }
                    GUILayout.Space(BrowserTab.MULTICOLUMN_V_GAP);
                    GUILayout.BeginHorizontal();
                    rowStarted = true;
                    currentX = 0;
                }

                int currentIndex = totalIndex;
                bool hasAnyInstancePlaying = ZoundBrowserPlaybackVisuals.TryGetAnyInstanceToken(zound, out var token);
                bool isClipZoundG = zound.IsClipOrLocalZound();
                ZoundBrowserPlaybackVisuals.UpdateZoundButtonPulse(zound, isClipZoundG, hasAnyInstancePlaying, token);

                if (!isClipZoundG && zound.id == 0) { /* missing zound — box handles its own style */ }
                else if (zound is Klip klipG && string.IsNullOrEmpty(klipG.externalSourcePath) && (klipG.audioClipRef == null || !klipG.audioClipRef.RuntimeKeyIsValid() || klipG.audioClipRef.editorAsset == null)) GUI.color = new Color(1f, 0.4f, 0f, 1f);
                else if (hasAnyInstancePlaying) {
                    if      (token.state == ZoundToken.State.Paused)   GUI.color = new Color(0.9f, 0.5f, 0.9f, 1f);
                    else if (token.audioSource.volume < Mathf.Epsilon) GUI.color = new Color(0.9f, 0.5f, 0.1f, 1f);
                    else if (selectedIndex == currentIndex) GUI.color = isClipZoundG ? ZoundsEditorColors.clipFlashColorStartSelected : ZoundsEditorColors.flashColorStartSelected;
                    else if (isClipZoundG)                             GUI.color = ZoundsEditorColors.clipFlashColorStart;
                }
                else if (selectedIndex == currentIndex) GUI.color = isClipZoundG ? Color.cyan : ZoundsEditorColors.flashColorStartSelected;
                else if (isClipZoundG)                  GUI.color = Color.cyan;

                if (currentX > 0) GUILayout.Space(BrowserTab.MULTICOLUMN_H_GAP);
                DrawButton(zound, selectedIndex, currentIndex, requiredWidth, token, Event.current, tab);
                GUI.color = Color.white;

                if (selectedIndex == currentIndex) inspectorPending = true;

                currentX += requiredWidth + BrowserTab.MULTICOLUMN_H_GAP;
                totalIndex++;
            }

            if (rowStarted) {
                GUILayout.EndHorizontal();
                if (inspectorPending && selectedIndex >= 0 && selectedIndex < totalIndex) {
                    int localIdx = selectedIndex - (totalIndex - zounds.Count);
                    if (localIdx >= 0 && localIdx < zounds.Count) {
                        tab.UpdateInspectorHeight(zounds[localIdx]);
                        tab.zoundBrowserEditor.DrawMulticolumn(zounds[localIdx], tab.inspectorAnimFloat.value, tab.inspectorAnimFloat.progress);
                    }
                }
            }
        }

        // ── Single button cell ────────────────────────────────────────────────

        public static void DrawButton(
            Zound currentZound,
            int selectedIndex,
            int currentIndex,
            float itemWidth,
            ZoundToken token,
            Event evt,
            BrowserTab tab) {

            var zoundName = currentZound.name;
            s_btnContent.text    = zoundName;
            s_btnContent.tooltip = zoundName + ": Left click to play. Right click to open configuration panel. Middle click or Alt left click to copy the name to clipboard.";

            var nameRect = GUILayoutUtility.GetRect(itemWidth, 24f, ZUI.GetButtonStyle(ZUI.Style.ZoundBtn),
                GUILayout.MinWidth(itemWidth), GUILayout.MaxWidth(itemWidth));

            ZUI.DrawPulse(ZoundBrowserPlaybackVisuals.ZoundPulseKey(currentZound), nameRect);
            ZoundBrowserPlaybackVisuals.DrawMuteSoloBackground(nameRect, currentZound);

            if (token != null) {
                if (token.isChildZound) {
                    if (!token.isDelayFinished) {
                        var highlightRect = new Rect(nameRect.x - 1f, nameRect.y - 1f, nameRect.width + 2.5f, nameRect.height + 2f);
                        var guiColor = GUI.color;
                        GUI.color = Color.yellow;
                        GUI.DrawTexture(highlightRect, EditorGUIUtility.whiteTexture);
                        GUI.color = guiColor;
                    }
                }
                else {
                    var highlightRect = new Rect(nameRect.x - 1f, nameRect.y - 1f, nameRect.width + 2.5f, nameRect.height + 2f);
                    GUI.DrawTexture(highlightRect, EditorGUIUtility.whiteTexture);
                }
            }

            bool isMissingZound = !(currentZound is ClipZound) && currentZound.id == 0;

            float pulseI    = ZUI.GetPulseIntensity(ZoundBrowserPlaybackVisuals.ZoundPulseKey(currentZound));
            var   prevColor = GUI.color;
            if (pulseI > 0f) GUI.color = Color.Lerp(GUI.color, Color.black, pulseI * 0.6f);

            if (isMissingZound) {
                GUI.color = prevColor;
                var missingBoxDef = ZUI.ActiveSheet?.FindBox("MissingZound");
                if (missingBoxDef != null) {
                    missingBoxDef.DrawBackground(nameRect);
                    if (evt.type == EventType.Repaint) {
                        var labelStyle = new GUIStyle(EditorStyles.label);
                        missingBoxDef.GetResolvedTitleText().Apply(labelStyle, missingBoxDef.ownerSheet);
                        labelStyle.alignment = TextAnchor.MiddleCenter;
                        labelStyle.clipping  = TextClipping.Clip;
                        GUI.Label(nameRect, zoundName, labelStyle);
                    }
                }
                else {
                    if (evt.type == EventType.Repaint) {
                        var fallbackStyle = new GUIStyle(EditorStyles.label);
                        fallbackStyle.normal.textColor = new Color(0.8f, 0.4f, 0.4f, 1f);
                        fallbackStyle.alignment = TextAnchor.MiddleCenter;
                        GUI.Label(nameRect, zoundName, fallbackStyle);
                    }
                }
                int missingId = GUIUtility.GetControlID(FocusType.Passive, nameRect);
                if (evt.type == EventType.MouseDown && (evt.button == 0 || evt.button == 1) && nameRect.Contains(evt.mousePosition)) {
                    GUIUtility.hotControl = missingId;
                    evt.Use();
                }
                if (evt.type == EventType.MouseUp && GUIUtility.hotControl == missingId) {
                    GUIUtility.hotControl = 0;
                    evt.Use();
                    if (nameRect.Contains(evt.mousePosition)) {
                        if (selectedIndex == currentIndex) tab.SelectZound(null);
                        else tab.SelectZound(currentZound);
                        GUI.FocusControl(null);
                    }
                }
            }
            else {
                if (ZUI.Button(nameRect, s_btnContent, ZUI.Style.ZoundBtn)) {
                    if (evt.button == 0) {
                        if (evt.alt) {
                            ZoundBrowserPlaybackVisuals.CopyToClipboard(zoundName);
                        }
                        else {
                            if (evt.control) {
                                InfoViewWindow.OpenWindow(currentZound);
                            }
                            else {
                                var browserSettings = ZoundsProject.Instance.browserSettings;
                                if (browserSettings.killOnPlay) ZoundEngine.StopAllZounds();
                                ZoundEngine.PlayZound(currentZound);
                            }
                        }
                    }
                    else if (evt.button == 1) {
                        if (selectedIndex == currentIndex) tab.SelectZound(null);
                        else tab.SelectZound(currentZound);
                    }
                    else if (evt.button == 2) {
                        ZoundBrowserPlaybackVisuals.CopyToClipboard(zoundName);
                    }
                    GUI.FocusControl(null);
                }
            }

            GUI.color = prevColor;

            ZoundBrowserPlaybackVisuals.DrawMuteSoloIndicator(nameRect, currentZound);
        }
    }
}

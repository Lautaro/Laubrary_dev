using UnityEditor;
using UnityEngine;

namespace Laubrary.Zounds {

    /// <summary>
    /// Draws a single row in the Zound Browser single-column list.
    /// All geometry is pre-computed in <see cref="BrowserTab.ZoundListRowLayout"/>
    /// before this class is called; Draw() only handles input and rendering.
    /// </summary>
    internal static class ZoundListItemView {

        private static GUIContent s_btnContent = new GUIContent();

        public static void Draw(
            Zound currentZound,
            ref BrowserTab.ZoundListRowLayout layout,
            ZoundBrowserEditor<Zound> editor,
            BrowserTab tab) {

            var evt             = Event.current;
            var browserSettings = ZoundsProject.Instance.browserSettings;

            bool isClipZound    = currentZound.IsClipOrLocalZound();
            bool isMissingZound = !isClipZound && currentZound.id == 0;

            if (!isMissingZound) {
                ZoundBrowserPlaybackVisuals.TryGetAnyInstanceToken(currentZound, out var tokenPre);
                ZoundBrowserPlaybackVisuals.UpdateZoundButtonPulse(currentZound, isClipZound, tokenPre != null, tokenPre);
                ZUI.DrawPulse(ZoundBrowserPlaybackVisuals.ZoundPulseKey(currentZound), layout.itemAreaRect);
            }
            ZoundBrowserPlaybackVisuals.DrawMuteSoloBackground(layout.itemAreaRect, currentZound);

            var guiColor = GUI.color;

            if (!isMissingZound) {
                ZoundBrowserPlaybackVisuals.TryGetAnyInstanceToken(currentZound, out var token);
                bool hasToken = token != null;
                if (hasToken) {
                    if      (token.state == ZoundToken.State.Paused)   GUI.color = new Color(0.9f, 0.5f, 0.9f, 1f);
                    else if (token.audioSource.volume < Mathf.Epsilon) GUI.color = new Color(0.9f, 0.5f, 0.1f, 1f);
                    else    GUI.color = isClipZound ? ZoundsEditorColors.clipFlashColorStart : ZoundsEditorColors.flashColorStart;
                }
                else if (isClipZound) {
                    GUI.color = Color.cyan;
                }
            }

            var zoundName = currentZound.name;

            if (isMissingZound) {
                var missingBoxRect = layout.nameButtonRect;
                var missingBoxDef  = ZUI.ActiveSheet?.FindBox("MissingZound");
                if (missingBoxDef != null) {
                    missingBoxDef.DrawBackground(missingBoxRect);
                    if (Event.current.type == EventType.Repaint) {
                        var labelStyle = new GUIStyle(EditorStyles.label);
                        missingBoxDef.GetResolvedTitleText().Apply(labelStyle, missingBoxDef.ownerSheet);
                        labelStyle.alignment = TextAnchor.MiddleCenter;
                        labelStyle.clipping  = TextClipping.Clip;
                        GUI.Label(missingBoxRect, zoundName, labelStyle);
                    }
                }
                else {
                    if (Event.current.type == EventType.Repaint) {
                        var fallbackStyle = new GUIStyle(EditorStyles.label);
                        fallbackStyle.normal.textColor = new Color(0.8f, 0.4f, 0.4f, 1f);
                        fallbackStyle.alignment = TextAnchor.MiddleCenter;
                        GUI.Label(missingBoxRect, zoundName, fallbackStyle);
                    }
                }
            }
            else {
                s_btnContent.text    = zoundName;
                s_btnContent.tooltip = zoundName + ": Left click to play. Right click to open edit mode. Middle click or Alt left click to copy the name to clipboard.";

                float pulseI = ZUI.GetPulseIntensity(ZoundBrowserPlaybackVisuals.ZoundPulseKey(currentZound));
                if (pulseI > 0f) GUI.color = Color.Lerp(guiColor, Color.black, pulseI * 0.6f);

                if (ZUI.Button(layout.nameButtonRect, s_btnContent, ZUI.Style.ZoundBtn)) {
                    if (evt.button == 0) {
                        if (evt.alt) {
                            ZoundBrowserPlaybackVisuals.CopyToClipboard(zoundName);
                        }
                        else {
                            if (evt.control) { InfoViewWindow.OpenWindow(currentZound); }
                            else {
                                if (browserSettings.killOnPlay) ZoundEngine.StopAllZounds();
                                ZoundEngine.PlayZound(currentZound);
                            }
                        }
                    }
                    else if (evt.button == 1) { tab.OpenZoundEditor(currentZound); }
                    else if (evt.button == 2) { ZoundBrowserPlaybackVisuals.CopyToClipboard(zoundName); }
                    GUI.FocusControl(null);
                }
            }

            GUI.color = guiColor;

            editor.DrawZoundSinglecolumn(ref layout, currentZound);

            ZoundBrowserPlaybackVisuals.DrawMuteSoloIndicator(layout.itemAreaRect, currentZound);
        }
    }
}

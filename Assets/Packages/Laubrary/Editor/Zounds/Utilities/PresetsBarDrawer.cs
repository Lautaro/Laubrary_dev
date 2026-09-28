using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Laubrary.Zounds {

    public static class PresetsBarDrawer {

        public const float presetsLabelWidth = 50f;
        public const float savePresetButtonWidth = 80f;
        public const float savePresetButtonWidthMinimal = 40f;

        private static GUIContent tempGUIContent = new GUIContent();
        private static GUIContent button_savePreset = new GUIContent("Save Preset", "Save as a new preset, or use an existing preset name to override it.");
        private static GUIContent button_savePresetMinimal = new GUIContent("Save", "Save as a new preset, or use an existing preset name to override it.");

        public static Vector2 DrawPresets<TPreset>(Vector2 scrollPos, Rect presetsRect, List<TPreset> presetList, float totalWidth, string defaultName, System.Action onClickSavePreset, System.Action<string> onHandlePresetSave, System.Action<string> elementClickHandler) where TPreset : ZoundsEditorPresets.IPreset {
            return DrawPresets(scrollPos, false, presetsRect, presetList, totalWidth, defaultName, onClickSavePreset, onHandlePresetSave, elementClickHandler);
        }

        public static Vector2 DrawPresetsMinimal<TPreset>(Vector2 scrollPos, Rect presetsRect, List<TPreset> presetList, float totalWidth, string defaultName, System.Action onClickSavePreset, System.Action<string> onHandlePresetSave, System.Action<string> elementClickHandler) where TPreset : ZoundsEditorPresets.IPreset {
            return DrawPresets(scrollPos, true, presetsRect, presetList, totalWidth, defaultName, onClickSavePreset, onHandlePresetSave, elementClickHandler);
        }

        private static Vector2 DrawPresets<TPreset>(Vector2 scrollPos, bool drawMinimal, Rect presetsRect, List<TPreset> presetList, float totalWidth, string defaultName, System.Action onClickSavePreset, System.Action<string> onHandlePresetSave, System.Action<string> elementClickHandler) where TPreset : ZoundsEditorPresets.IPreset {
            if (!drawMinimal) {
                var labelRect = new Rect(presetsRect.x, presetsRect.y, presetsLabelWidth, EditorGUIUtility.singleLineHeight);
                EditorGUI.LabelField(labelRect, "Presets:");
            }

            float saveWidth = drawMinimal ? savePresetButtonWidthMinimal : savePresetButtonWidth;
            var saveRect = new Rect(presetsRect.xMax - saveWidth - 2f, presetsRect.y + 2, saveWidth, presetsRect.height - 4f);

            float labelWidth = drawMinimal ? 0 : presetsLabelWidth;
            var viewportRect = new Rect(presetsRect.x + labelWidth, presetsRect.y, presetsRect.width - labelWidth, presetsRect.height);
            var contentRect = new Rect(viewportRect.x, viewportRect.y, totalWidth, 18f);

            var guiColor = GUI.color;
            GUI.color = new Color(0.2f, 0.2f, 0.2f, 1f);
            GUI.DrawTexture(viewportRect, EditorGUIUtility.whiteTexture);
            GUI.color = guiColor;

            viewportRect.width -= (saveWidth + 4f);

            scrollPos = GUI.BeginScrollView(viewportRect, scrollPos, contentRect);
            {
                var evt = Event.current;
                float currentX = viewportRect.x;
                DrawPresetElement(evt, contentRect, ref currentX, null, elementClickHandler);
                foreach (var preset in presetList) {
                    DrawPresetElement(evt, contentRect, ref currentX, preset, elementClickHandler);
                }
            }
            GUI.EndScrollView();

            if (GUI.Button(saveRect, drawMinimal ? button_savePresetMinimal : button_savePreset)) {
                onClickSavePreset?.Invoke();
                SavePresetPopup.Show(Event.current.mousePosition, defaultName, onHandlePresetSave);
            }

            return scrollPos;
        }

        public static void DrawPresetElement(Event evt, Rect contentRect, ref float currentX, ZoundsEditorPresets.IPreset preset, System.Action<string> elementClickHandler) {
            string presetName = preset == null ? "Default" : preset.name;
            var guiColor = GUI.color;
            GUI.color = GenerateRandomColor(presetName);
            tempGUIContent.text = presetName;
            var btnStyle = ZUI.GetButtonStyle(ZUI.Style.Default);
            float width = btnStyle.CalcSize(tempGUIContent).x + 8f;
            var elmRect = new Rect(currentX, contentRect.y, width, 22f);
            if (ZUI.Button(elmRect, tempGUIContent, ZUI.Style.Default)) {
                elementClickHandler?.Invoke(presetName);
            }
            GUI.color = guiColor;
            currentX += width;
        }

        /// <summary>A preset button's tint, derived from its name (shared with the UI Toolkit twin).</summary>
        internal static Color GenerateRandomColor(string input) {
            if (string.IsNullOrEmpty(input))
                return Color.white;

            int hash = input.GetHashCode();
            if (hash < 0) hash = -hash;

            System.Random rand = new System.Random(hash);

            // Range limits
            const float min = 0.75f;
            const float max = 1f;
            const float range = max - min;

            float r = min + (float)rand.NextDouble() * range;
            float g = min + (float)rand.NextDouble() * range;
            float b = min + (float)rand.NextDouble() * range;

            return new Color(r, g, b, 1f);
        }

    }

}

using System;
using Laubrary.Zui;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Laubrary.Zounds.Uitk {

    /// <summary>
    /// The Zounds UI Toolkit port's controls (T-0456): ZUI's own UI Toolkit controls wearing the COPIED Zounds look
    /// (Skin/ZoundsSkin.uss, generated once by <see cref="ZoundsSkinExtractor"/>). Each factory mirrors the IMGUI call
    /// the old windows make — style name, corner mask, size — so a port reads line for line like the original.
    /// </summary>
    public static class ZS {

        const string SkinPath = ZoundsSkinExtractor.SkinFolder + "/ZoundsSkin.uss";
        static StyleSheet s_skin;

        static StyleSheet Skin {
            get {
                if (s_skin == null) s_skin = AssetDatabase.LoadAssetAtPath<StyleSheet>(SkinPath);
                return s_skin;
            }
        }

        /// <summary>Makes <paramref name="root"/> a Zounds UI Toolkit root: ZUI's stylesheet, then the Zounds skin on top.</summary>
        public static void Attach(VisualElement root) {
            Z.Attach(root);
            if (Skin != null && !root.styleSheets.Contains(Skin)) root.styleSheets.Add(Skin);
            root.AddToClassList("zs-root");
        }

        /// <summary>ZUI.Button(label, style, cornerMask, width, height) — a sheet-styled button.</summary>
        public static Button Button(string label, string tooltip, string style, Action onClick,
                                    ZUICornerMask corners = ZUICornerMask.None, float width = -1f, float height = 20f) {
            var b = Z.Button(label, tooltip, onClick);
            b.AddToClassList("zs-" + style);
            b.Corners(corners);
            Size(b, width, height);
            return b;
        }

        /// <summary>ZUI.Toggle(value, content, style, cornerMask, width, height) — a latched, sheet-styled toggle.</summary>
        public static ZuiToggleButton Toggle(string label, string tooltip, bool value, Action<bool> onChanged, string style = "RichToggle",
                                             ZUICornerMask corners = ZUICornerMask.None, float width = -1f, float height = 20f) {
            var t = Z.ToggleButton(label, tooltip, value, onChanged);
            t.AddToClassList("zs-" + style);
            t.Corners(corners);
            Size(t, width, height);
            return t;
        }

        /// <summary>ZUI.MicroSlider(rect, value, min, max, label, style, ..., labelMode, default) on a Zounds slider style.</summary>
        public static ZuiSkinSlider Slider(string text, float value, float min, float max, string tooltip, Action<float> onChanged,
                                           ZuiSkinSlider.LabelMode mode = ZuiSkinSlider.LabelMode.LabelOnly, float? defaultValue = null,
                                           string style = "Default", float width = 150f, float height = 18f) {
            var s = new ZuiSkinSlider(text, value, min, max, tooltip, onChanged, mode, defaultValue);
            s.AddToClassList("zs-slider-" + style.ToLowerInvariant());
            Size(s, width, height);
            return s;
        }

        static void Size(VisualElement e, float width, float height) {
            if (width > 0f) e.style.width = width;
            if (height > 0f) e.style.height = height;
            e.style.flexShrink = 0;
            e.style.flexGrow = 0;
        }

        /// <summary>Places <paramref name="e"/> at an exact position, for pixel-for-pixel comparisons with an IMGUI rect.</summary>
        public static T At<T>(this T e, float x, float y) where T : VisualElement {
            e.style.position = Position.Absolute;
            e.style.left = x; e.style.top = y;
            return e;
        }
    }
}

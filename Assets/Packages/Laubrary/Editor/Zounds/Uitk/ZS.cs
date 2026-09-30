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

        /// <summary>
        /// Whether Unity's built-in editor styles exist yet. Straight after a script reload the UI Toolkit windows rebuild
        /// before any window has drawn, and until one has, reading those styles throws inside Unity's own getter. Several
        /// shared measurements the Zounds windows borrow from the old ones read them, so a window that builds then must
        /// wait for this (found 2026-09-28: the main window came back empty after every reload).
        /// </summary>
        public static bool EditorStylesReady {
            get { try { return EditorStyles.label != null && EditorStyles.helpBox != null; } catch (NullReferenceException) { return false; } }
        }

        /// <summary>The sheet's "H Btns Big" and "H Btns Medium" spacings (horizontal spacing 5 × scale 2), copied once:
        /// the Browser's gap between a row's controls, and between Mute/Solo cells and toolbar buttons.</summary>
        public const float ItemSpacing = 10f, MediumSpacing = 10f;
        /// <summary>The sheet's vertical spacing: ZUI.RowSpace().</summary>
        public const float RowSpace = 10f;

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
            if (Layout != null && !root.styleSheets.Contains(Layout)) root.styleSheets.Add(Layout);
            root.AddToClassList("zs-root");
        }

        static StyleSheet s_layout;
        static StyleSheet Layout {
            get {
                if (s_layout == null) s_layout = AssetDatabase.LoadAssetAtPath<StyleSheet>("Assets/Packages/Laubrary/Editor/Zounds/Uitk/ZoundsUitk.uss");
                return s_layout;
            }
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

        /// <summary>
        /// ZUI.Toggle(value, content, style, onColor, cornerMask, width, height) — a latched, sheet-styled toggle. With
        /// <paramref name="onColor"/>, the ON state is a flat fill of that colour with no border (the old ZUI's on-colour
        /// override, e.g. Mute's warning colour), keeping the corner shape.
        /// </summary>
        /// <summary>
        /// An eye toggle (T-0494, the owner's "eye box"): open and latched while the thing is shown, closed while hidden.
        /// Uses ZUI's own eye icons. <paramref name="tooltip"/> is given the current state and should say what clicking does.
        /// </summary>
        public static ZuiToggleButton Eye(bool visible, Func<bool, string> tooltip, Action<bool> onChanged,
                                          ZUICornerMask corners = ZUICornerMask.All, float width = 22f, float height = 20f) {
            ZuiToggleButton t = null;
            VisualElement icon = null;
            void SetIcon(bool v) {
                if (icon != null) t.Remove(icon);
                icon = Z.Icon(v ? "eye" : "eye-closed", 13f);
                if (icon != null) {
                    icon.style.position = Position.Absolute; icon.style.left = (width - 13f) * 0.5f; icon.style.top = (height - 13f) * 0.5f;
                    t.Add(icon);
                }
                t.tooltip = tooltip(v);
            }
            t = Toggle("", tooltip(visible), visible, v => { SetIcon(v); onChanged(v); }, "RichToggle", corners, width, height);
            t.markWhenOn = false;
            SetIcon(visible);
            t.userData = (Action<bool>)SetIcon;   // for SetEye
            return t;
        }

        /// <summary>
        /// Shows <paramref name="visible"/> on an eye made by <see cref="Eye"/> without firing its change: the icon and the
        /// hover text follow. For an eye that mirrors state another control can change too (T-0519: the waveform's curve eye
        /// and the same modifier's eye on its card are one setting).
        /// </summary>
        public static void SetEye(ZuiToggleButton eye, bool visible) {
            if (eye == null || eye.value == visible) return;
            eye.SetValueWithoutNotify(visible);
            (eye.userData as Action<bool>)?.Invoke(visible);
        }

        public static ZuiToggleButton Toggle(string label, string tooltip, bool value, Action<bool> onChanged, string style = "RichToggle",
                                             ZUICornerMask corners = ZUICornerMask.None, float width = -1f, float height = 20f,
                                             Color? onColor = null) {
            ZuiToggleButton t = null;
            t = Z.ToggleButton(label, tooltip, value, v => { ApplyOnColor(t, onColor); onChanged?.Invoke(v); });
            t.AddToClassList("zs-" + style);
            t.Corners(corners);
            Size(t, width, height);
            ApplyOnColor(t, onColor);
            return t;
        }

        /// <summary>Re-applies a toggle's on-colour override after its value was set from code.</summary>
        public static void ApplyOnColor(ZuiToggleButton t, Color? onColor) {
            if (t == null || !onColor.HasValue) return;
            if (t.value) {
                t.style.backgroundImage = StyleKeyword.None;
                t.style.backgroundColor = onColor.Value;
                t.style.borderTopWidth = t.style.borderRightWidth = t.style.borderBottomWidth = t.style.borderLeftWidth = 0f;
            }
            else {
                t.style.backgroundImage = StyleKeyword.Null;
                t.style.backgroundColor = StyleKeyword.Null;
                t.style.borderTopWidth = t.style.borderRightWidth = t.style.borderBottomWidth = t.style.borderLeftWidth = StyleKeyword.Null;
            }
        }

        /// <summary>ZUI.MicroMinMax(rect, ref min, ref max, absMin, absMax, label, style, showInputFields, labelMode).</summary>
        public static ZuiSkinMinMax MinMax(string text, float lo, float hi, float absMin, float absMax, string tooltip,
                                           Action<float, float> onChanged, string style, ZuiSkinMinMax.LabelMode mode,
                                           bool showFields = false, float width = 150f, float height = 18f) {
            var info = SliderInfo(style);
            var m = new ZuiSkinMinMax(text, lo, hi, absMin, absMax, tooltip, onChanged, mode, info.bipolar, info.bipolarCenter,
                                      showFields, info.valueWidth, info.format);
            m.AddToClassList("zs-slider-" + style.ToLowerInvariant());
            Size(m, width, height);
            return m;
        }

        /// <summary>
        /// What a Zounds slider style says about BEHAVIOUR rather than looks (the look is in the USS): copied, like the
        /// look, from the old sheet (2026-09-28). All six styles use the automatic number format.
        /// </summary>
        public static (bool bipolar, float bipolarCenter, float valueWidth, string format) SliderInfo(string style) {
            switch (style) {
                case "MinMaxPitch": return (true, float.NaN, 40f, null);
                case "SmallSlider": case "BigSlider": return (false, float.NaN, 65.6f, null);
                default: return (false, float.NaN, 40f, null);
            }
        }

        /// <summary>ZUI.MicroSlider(rect, value, min, max, label, style, ..., labelMode, default) on a Zounds slider style.</summary>
        public static ZuiSkinSlider Slider(string text, float value, float min, float max, string tooltip, Action<float> onChanged,
                                           ZuiSkinSlider.LabelMode mode = ZuiSkinSlider.LabelMode.LabelOnly, float? defaultValue = null,
                                           string style = "Default", float width = 150f, float height = 18f,
                                           Func<float, string> format = null) {
            var s = new ZuiSkinSlider(text, value, min, max, tooltip, onChanged, mode, defaultValue, format);
            s.AddToClassList("zs-slider-" + style.ToLowerInvariant());
            Size(s, width, height);
            return s;
        }

        /// <summary>
        /// One Undo step per drag, as the old windows do it (ZoundsWindow.BeginDragUndo on the first change of a press,
        /// EndDragUndo on release): returns the <c>onBeforeMutate</c> to hand the control, and closes the step when the
        /// pointer is released anywhere under <paramref name="e"/>.
        /// </summary>
        public static Action DragUndo(VisualElement e, string undoName, Action onEnd = null) {
            bool open = false;
            void End() { if (!open) return; open = false; ZoundsWindow.EndDragUndo(onEnd); }
            e.RegisterCallback<PointerUpEvent>(_ => End(), TrickleDown.TrickleDown);
            e.RegisterCallback<PointerCaptureOutEvent>(_ => End(), TrickleDown.TrickleDown);
            return () => { if (open) return; open = true; ZoundsWindow.BeginDragUndo(undoName); };
        }

        /// <summary>Sizes a text button to its text plus <paramref name="extra"/> px, as the old code sizes it with
        /// CalcSize(label) + extra — measured by UI Toolkit, once the element has its font.</summary>
        public static T FitToText<T>(this T e, float extra) where T : TextElement {
            void Fit() {
                var sz = e.MeasureTextSize(e.text, 0, VisualElement.MeasureMode.Undefined, 0, VisualElement.MeasureMode.Undefined);
                if (sz.x > 0f) e.style.width = Mathf.Ceil(sz.x) + extra;
            }
            e.RegisterCallback<AttachToPanelEvent>(_ => e.schedule.Execute(Fit));
            return e;
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

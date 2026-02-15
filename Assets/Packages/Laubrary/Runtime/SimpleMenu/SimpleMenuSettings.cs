using UnityEngine;
using UnityEngine.Serialization;

namespace Laubrary.SimpleMenu
{
    [CreateAssetMenu(fileName = "SimpleMenuSettings", menuName = "Simple Menu/Settings")]
    public class SimpleMenuSettings : ScriptableObject
    {
        [Header("Control Prefabs")]
        [Tooltip("Prefab for button controls")]
        public GameObject buttonPrefab;

        [Tooltip("Prefab for slider controls")]
        public GameObject sliderPrefab;

        [Tooltip("Prefab for toggle controls")]
        public GameObject togglePrefab;

        [Tooltip("Prefab for dropdown controls")]
        public GameObject dropdownPrefab;

        [Tooltip("Prefab for input field controls")]
        public GameObject inputFieldPrefab;

        [Tooltip("Prefab for labels used with sliders and input fields")]
        public GameObject labelPrefab;

        [Tooltip("Prefab for text box controls (info box style)")]
        public GameObject textBoxPrefab;

        [Tooltip("Prefab for menu header displayed at the top of each menu")]
        public GameObject headerPrefab;

        [Header("Layout Settings")]
        [Tooltip("Padding on the left side of menu controls in pixels")]
        public int paddingLeft = 0;

        [Tooltip("Padding on the right side of menu controls in pixels")]
        public int paddingRight = 0;

        [Tooltip("Padding on the top of menu controls in pixels")]
        public int paddingTop = 0;

        [Tooltip("Padding on the bottom of menu controls in pixels")]
        public int paddingBottom = 0;

        [Header("Optional Visual Settings")]
        [Tooltip("Tint color for all controls. Alpha = 0 means no tint.")]
        public Color controlTintColor = new Color(1f, 1f, 1f, 0f);

        [Tooltip("Font size for all text. Set to -1 to use default.")]
        public float fontSize = -1f;

        [Tooltip("Enable auto-size for fonts")]
        public bool autoSizeFont = false;

        [Tooltip("Width in pixels for slider value labels when ShowValueLabel is enabled.")]
        public float sliderValueLabelWidth = 60f;

        [Tooltip("Use TextMeshPro style override for control text (buttons, toggles, sliders, etc). Does not affect headers or textboxes.")]
        [FormerlySerializedAs("useTextStyleOverride")]
        public bool useControlTextStyleOverride = false;

        [Tooltip("TextMeshPro prefab to copy style from (font, size, material, color mode, gradients, etc).")]
        [FormerlySerializedAs("textStyleOverridePrefab")]
        [FormerlySerializedAs("controlTestStyle")]
        public GameObject controlTextStylePrefab;

        public bool HasControlTint => controlTintColor.a > 0f;
        public bool HasFontSize => fontSize > 0f;
    }
}

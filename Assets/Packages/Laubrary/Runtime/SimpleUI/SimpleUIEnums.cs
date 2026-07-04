namespace Laubrary.SimpleUI
{
    public enum NameMatchMode
    {
        Exact,
        CaseInsensitive,
        Loose
    }

    public enum MultiMatchMode
    {
        Fail,
        UseFirst
    }

    public enum BindingStatus
    {
        Success,
        Error,
        Skipped
    }

    public enum BindingMode
    {
        OneWay,
        TwoWay
    }

    public enum ConfidenceLevel
    {
        Explicit,
        Strong,
        Weak
    }

    /// <summary>
    /// Global settings for SimpleUI generation.
    /// Maps C# types to UI prefabs.
    /// </summary>
    [UnityEngine.CreateAssetMenu(fileName = "SimpleUISettings", menuName = "Laubrary/SimpleUI/Settings")]
    public class SimpleUISettings : UnityEngine.ScriptableObject
    {
        [UnityEngine.Header("Default Prefabs")]
        [UnityEngine.Tooltip("Read-only display row. Used for OneWay bindings.")]
        public UnityEngine.GameObject displayPrefab;

        [UnityEngine.Tooltip("Editable text input row. Used for TwoWay bindings on string, int, float.")]
        public UnityEngine.GameObject inputPrefab;

        [UnityEngine.Tooltip("Toggle row. Used for any bool field.")]
        public UnityEngine.GameObject togglePrefab;

        [UnityEngine.Tooltip("Slider row. Used for numeric fields decorated with [SimpleUISlider].")]
        public UnityEngine.GameObject sliderPrefab;

        [UnityEngine.Tooltip("Dropdown row. Used for any enum field.")]
        public UnityEngine.GameObject dropdownPrefab;

        [UnityEngine.Header("Text Style")]
        [UnityEngine.Tooltip("Shared font applied to all TMP components in generated UI. Leave empty to use TMP defaults.")]
        public TMPro.TMP_FontAsset font;

        [UnityEngine.Tooltip("Font size applied to all label and value text. Set to 0 to leave unchanged.")]
        public float fontSize = 0f;

        [UnityEngine.Tooltip("Color applied to label text (child named 'label').")]
        public UnityEngine.Color labelColor = UnityEngine.Color.white;

        [UnityEngine.Tooltip("Color applied to value text (child named 'value').")]
        public UnityEngine.Color valueColor = UnityEngine.Color.white;

        /// <summary>
        /// Applies the configured text style to all TextMeshProUGUI components found under the given root.
        /// Distinguishes label vs value by GameObject name.
        /// </summary>
        public void ApplyTextStyle(UnityEngine.Transform root)
        {
            foreach (var tmp in root.GetComponentsInChildren<TMPro.TextMeshProUGUI>(includeInactive: true))
            {
                if (font != null)
                    tmp.font = font;

                if (fontSize > 0f)
                    tmp.fontSize = fontSize;

                bool isLabel = tmp.gameObject.name.Equals("label", System.StringComparison.OrdinalIgnoreCase);
                bool isValue = tmp.gameObject.name.Equals("value", System.StringComparison.OrdinalIgnoreCase);

                if (isLabel) tmp.color = labelColor;
                else if (isValue) tmp.color = valueColor;
            }
        }

        /// <summary>
        /// Resolves the correct prefab based on binding mode, type, and attributes on the member.
        /// Resolution order: attributes → type → binding mode.
        /// </summary>
        public UnityEngine.GameObject ResolvePrefab(System.Type memberType, BindingMode bindingMode, System.Reflection.MemberInfo memberInfo)
        {
            // 1. Attribute overrides
            if (memberInfo != null && memberInfo.GetCustomAttributes(typeof(SimpleUISliderAttribute), false).Length > 0)
                return sliderPrefab;

            // 2. Type-specific
            if (memberType == typeof(bool))
                return togglePrefab;

            if (memberType.IsEnum)
                return dropdownPrefab;

            // 3. Binding mode
            if (bindingMode == BindingMode.TwoWay)
                return inputPrefab;

            return displayPrefab;
        }
    }
}
